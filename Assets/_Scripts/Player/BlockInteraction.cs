// ============================================================
//  BlockInteraction.cs
//  Clic gauche = casser un bloc, Clic droit = placer un bloc.
//  Utilise un Raycast depuis la caméra vers le monde.
// ============================================================

using System;
using UnityEngine;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Network;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    /// <summary>
    /// Gère l'interaction joueur ↔ voxels :
    /// - Clic gauche  : détruit le bloc visé
    /// - Clic droit   : place un bloc sur la face visée
    /// Dépend de <see cref="PlanetWorld"/> pour modifier les chunks.
    /// </summary>
    public sealed class BlockInteraction : MonoBehaviour
    {
        /// <summary>
        /// Déclenché après chaque placement de bloc réussi (créatif et survie).
        /// Payload : position world du bloc + type de bloc.
        /// </summary>
        public static event Action<Vector3, BlockType, IVoxelWorld> OnBlockPlaced;

        // ── Inspector ─────────────────────────────────────────
        [Header("Portée d'action")]
        [SerializeField] private float reach = 6f;

        [Header("Bloc à placer")]
        [SerializeField] private BlockType blockToPlace = BlockType.Stone;

        [Header("Références")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PlanetWorld world;
        [SerializeField] private Rigidbody playerRigidbody;

        // ── Visualisation (preview du bloc sélectionné) ───────
        [Header("Highlight (optionnel)")]
        [SerializeField] private Transform blockHighlight;   // cube semi-transparent

        // (hotbar gérée par HudBuilder)

        // ── Survie : minage progressif ─────────────────────────────────────
        private bool      _isMining;
        private float     _mineTimer;
        private float     _mineRequiredTime;
        private Vector3   _mineTargetPos;
        private int       _survivalHotbarIndex;
        private SurvivalInventory _survivalInventory;
        private Material[]        _blockMaterials;
        // ── Mode Créatif : items non-blocs par slot ────────────────────────────────────────────────
        private readonly ItemType[] _creativeHotbarItemTypes = new ItemType[9];

        /// <summary>True en mode créatif quand le slot actif contient le Propulseur.</summary>
        public bool CreativePropulseurActive =>
            !GameModeManager.IsSurvival
            && _hotbarIndex >= 0 && _hotbarIndex < _creativeHotbarItemTypes.Length
            && _creativeHotbarItemTypes[_hotbarIndex] == ItemType.Propulseur;

        /// <summary>Retourne l'ItemType non-bloc stocké dans un slot créatif (None si aucun).</summary>
        public ItemType GetCreativeHotbarItem(int slot) =>
            (slot >= 0 && slot < _creativeHotbarItemTypes.Length)
                ? _creativeHotbarItemTypes[slot]
                : ItemType.None;

        /// <summary>
        /// Place un item non-bloc dans un slot de la hotbar créative.
        /// Équivalent de SetHotbarSlot pour les items (Propulseur…).
        /// </summary>
        public void SetCreativeHotbarItemSlot(int slot, ItemType item)
        {
            if (slot < 0 || slot >= _hotbar.Length) return;
            _creativeHotbarItemTypes[slot] = item;
            _hotbar[slot] = BlockType.Air;
        }
        /// <summary>Progression du minage en cours (0-1). 0 si inactif.</summary>
        public float MineProgress => _mineRequiredTime > 0f
            ? Mathf.Clamp01(_mineTimer / _mineRequiredTime)
            : 0f;

        // ── Cycle de vie ───────────────────────────────────────────────

        private void Update()
        {
            // Bloque toute interaction quand l'inventaire ou la console est ouvert(e)
            if (CreativeInventory.IsOpen || SurvivalInventory.IsOpen || GameConsole.IsOpen)
            {
                ResetMining();
                return;
            }

            if (GameModeManager.IsSurvival)
            {
                HandleMiningSurvival();
                if (GetMouseDown(1)) TryPlaceOrInteractSurvival();
            }
            else
            {
                ResetMining();
                if (GetMouseDown(0)) TryBreakBlock();
                // Clic droit : laisser le PropulseurController gérer si actif
                if (GetMouseDown(1) && !CreativePropulseurActive) TryPlaceBlock();
            }

            // Scroll ou touches pour changer le bloc actif
            HandleBlockSelection();
        }

        // Le highlight est mis à jour en LateUpdate, APRÈS que le
        // character controller planétaire a appliqué sa rotation
        // gravitationnelle. Sans ça, la rotation du joueur/caméra
        // (parent du highlight) écrase la world-rotation qu'on vient
        // de poser dans Update, causant un décalage total dès qu'on bouge.
        private void LateUpdate() => UpdateHighlight();

        // ── Actions ───────────────────────────────────────────

        private void TryBreakBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            Vector3 pos = hit.point - hit.normal * 0.5f;

            // Astéroïde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null) { ast.BreakBlock(pos); return; }

            // Planète infinie ou de base — utilise le ChunkRenderer du collider touché
            // pour éviter le bug de frontière de face sur les troncs d'arbres.
            ChunkRenderer hitCr = hit.collider?.GetComponent<ChunkRenderer>();

            // Si un cross-block (ShortGrass) occupe la case juste au-dessus du bloc solide
            // touché, casser l'herbe en créatif (exclue de la collision mesh, introuvable au rayon).
            if (hitCr != null)
            {
                Vector3 abovePos   = hit.point + hit.normal * 0.5f;
                Vector3 aboveLocal = hitCr.transform.InverseTransformPoint(abovePos);
                int ax = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.x), 0, VoxelData.ChunkWidth  - 1);
                int ay = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.y), 0, VoxelData.ChunkHeight - 1);
                int az = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.z), 0, VoxelData.ChunkDepth  - 1);
                if (BlockProperties.IsCrossBlock(hitCr.GetBlock(ax, ay, az)))
                    pos = abovePos;
            }

            var pw = hit.collider?.GetComponentInParent<PlanetWorld>();

            // Planète principale → sync réseau via BlockSyncManager (gère aussi le mode hors-ligne)
            if (pw == world || (pw == null && world != null))
            {
                var bsm = BlockSyncManager.Instance;
                if (bsm != null) { bsm.RequestBreakBlock(hitCr, pos); return; }
            }

            if (pw != null) { pw.BreakBlock(hitCr, pos); return; }
            world?.BreakBlock(hitCr, pos);
        }

        private void TryPlaceBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            Vector3 pos = hit.point + hit.normal * 0.5f;

            bool placed = false;
            IVoxelWorld usedWorld = null;

            // Astéroïde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null)
            {
                placed = ast.PlaceBlock(pos, blockToPlace);
                if (placed) usedWorld = ast;
            }
            else
            {
                // Planète infinie (PlanetWorld créé dynamiquement par InfinitePlanetSystem)
                var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
                if (pw != null && pw != world)
                {
                    placed = pw.PlaceBlock(pos, blockToPlace);
                    if (placed) usedWorld = pw;
                }
                else
                {
                    var bsm = BlockSyncManager.Instance;
                    if (bsm != null)        { bsm.RequestPlaceBlock(pos, blockToPlace); placed = true; usedWorld = world; }
                    else if (pw != null)    { placed = pw.PlaceBlock(pos, blockToPlace);  usedWorld = pw; }
                    else if (world != null) { placed = world.PlaceBlock(pos, blockToPlace); usedWorld = world; }
                }
            }

            // Empêche le joueur de tomber dans le vide quand il pose un bloc sous ses pieds
            if (placed)
            {
                ResolveBlockPlacementOverlap(hit);
                OnBlockPlaced?.Invoke(pos, blockToPlace, usedWorld);
            }
        }

        // ── Survie : minage progressif ────────────────────────

        private void HandleMiningSurvival()
        {
            bool held = GetMouseHeld(0);
            if (!held) { ResetMining(); return; }

            if (!Raycast(out RaycastHit hit)) { ResetMining(); return; }

            BlockType blockType = GetBlockAtHit(hit);
            if (blockType == BlockType.Air) { ResetMining(); return; }

            Vector3 breakPos = hit.point - hit.normal * 0.5f;
            bool posChanged  = !_isMining || Vector3.Distance(breakPos, _mineTargetPos) > 0.5f;

            if (posChanged)
            {
                _isMining         = true;
                _mineTargetPos    = breakPos;
                float mineTime    = GetMineTime(blockType);
                // Indestructible sans l'outil requis
                _mineRequiredTime = float.IsPositiveInfinity(mineTime) ? float.MaxValue : mineTime;
                _mineTimer        = 0f;
            }

            _mineTimer += Time.deltaTime;
            if (_mineTimer >= _mineRequiredTime)
            {
                BreakBlockSurvival(hit, blockType);
                ResetMining();
            }
        }

        private void ResetMining()
        {
            _isMining         = false;
            _mineTimer        = 0f;
            _mineRequiredTime = 0f;
        }

        private void BreakBlockSurvival(RaycastHit hit, BlockType blockType)
        {
            // Les cross-blocks (ShortGrass) sont au-dessus de la surface touchée,
            // pas à l'intérieur du bloc solide (à l'opposé des blocs normaux).
            Vector3 breakPos = BlockProperties.IsCrossBlock(blockType)
                ? hit.point + hit.normal * 0.5f
                : hit.point - hit.normal * 0.5f;
            ItemType activeTool = GetActiveSurvivalTool();
            bool hasPickaxe     = activeTool == ItemType.WoodenPickaxe
                                || activeTool == ItemType.StonePickaxe
                                || activeTool == ItemType.IronPickaxe;
            bool hasAxe         = activeTool == ItemType.WoodenAxe
                                || activeTool == ItemType.StoneAxe;

            // Stone drops Cobblestone (like Minecraft)
            BlockType dropBlock = blockType;
            if (blockType == BlockType.Stone)
                dropBlock = hasPickaxe ? BlockType.Cobblestone : BlockType.Air;

            // Herbe courte → pas de drop bloc ; 50% de chance d'obtenir des graines
            bool spawnMelonSeeds = false;
            if (blockType == BlockType.ShortGrass)
            {
                dropBlock = BlockType.Air;
                spawnMelonSeeds = UnityEngine.Random.Range(0, 2) == 0;
            }

            // Minerais → pas de drop sans la bonne pioche
            bool isOre = blockType == BlockType.CoalOre
                      || blockType == BlockType.IronOre
                      || blockType == BlockType.CopperOre
                      || blockType == BlockType.GoldOre;
            if (isOre && !hasPickaxe)
                dropBlock = BlockType.Air;

            // Casser le bloc dans le monde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null)      ast.BreakBlock(breakPos);
            else
            {
                // Utilise le ChunkRenderer du collider touché pour un lookup exact
                // (évite le bug de frontière de face sur les troncs d'arbres).
                ChunkRenderer hitCr = hit.collider?.GetComponent<ChunkRenderer>();
                var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
                // Planète principale → sync réseau
                if (pw == world || (pw == null && world != null))
                {
                    var bsm = BlockSyncManager.Instance;
                    if (bsm != null) bsm.RequestBreakBlock(hitCr, breakPos);
                    else if (pw != null) pw.BreakBlock(hitCr, breakPos);
                    else world?.BreakBlock(hitCr, breakPos);
                }
                else if (pw != null) pw.BreakBlock(hitCr, breakPos);
                else              world?.BreakBlock(hitCr, breakPos);
            }

            // Spawner le drop
            Transform playerTr = playerRigidbody != null
                ? playerRigidbody.transform
                : transform;

            if (dropBlock != BlockType.Air)
                BlockDropController.SpawnDrop(dropBlock, breakPos, playerTr, _blockMaterials);

            if (spawnMelonSeeds)
                BlockDropController.SpawnItemDrop(ItemType.MelonSeeds, breakPos, playerTr);
        }

        private void TryPlaceOrInteractSurvival()
        {
            if (!Raycast(out RaycastHit hit)) return;

            // Clic droit sur un établi → ouvrir l'inventaire en mode établi
            BlockType targetBlock = GetBlockAtHit(hit);
            if (targetBlock == BlockType.CraftingTable)
            {
                _survivalInventory?.OpenWithCraftingTable();
                return;
            }

            // Sinon → poser un bloc depuis la hotbar survie
            var stacks  = SurvivalInventoryData.Instance.Hotbar;
            if (_survivalHotbarIndex < 0 || _survivalHotbarIndex >= stacks.Length) return;
            ItemStack stack = stacks[_survivalHotbarIndex];
            if (stack.IsEmpty || !stack.IsBlock()) return;

            BlockType typeToPlace = stack.ToBlockType();
            if (typeToPlace == BlockType.Air) return;

            Vector3 placePos  = hit.point + hit.normal * 0.5f;
            bool placed        = false;
            IVoxelWorld usedWorld2 = null;

            var ast2 = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast2 != null)
            {
                placed = ast2.PlaceBlock(placePos, typeToPlace);
                if (placed) usedWorld2 = ast2;
            }
            else
            {
                var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
                if (pw != null && pw != world)
                {
                    placed = pw.PlaceBlock(placePos, typeToPlace);
                    if (placed) usedWorld2 = pw;
                }
                else
                {
                    var bsm = BlockSyncManager.Instance;
                    if (bsm != null)        { bsm.RequestPlaceBlock(placePos, typeToPlace); placed = true; usedWorld2 = world; }
                    else if (pw != null)    { placed = pw.PlaceBlock(placePos, typeToPlace);  usedWorld2 = pw; }
                    else if (world != null) { placed = world.PlaceBlock(placePos, typeToPlace); usedWorld2 = world; }
                }
            }

            if (placed)
            {
                SurvivalInventoryData.Instance.RemoveItem((ItemType)(int)typeToPlace, 1);
                ResolveBlockPlacementOverlap(hit);
                OnBlockPlaced?.Invoke(placePos, typeToPlace, usedWorld2);
            }
        }

        /// <summary>Lit le type de bloc à la position visée par le raycast.</summary>
        private BlockType GetBlockAtHit(RaycastHit hit)
        {
            Vector3 pos = hit.point - hit.normal * 0.5f;
            ChunkRenderer cr = hit.collider?.GetComponent<ChunkRenderer>();
            // Fallback : cherche le chunk via le world (cas de bords de chunk)
            if (cr == null && world != null) cr = world.GetChunkAt(pos);
            if (cr == null) return BlockType.Air;
            Vector3 local = cr.transform.InverseTransformPoint(pos);
            int lx = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, VoxelData.ChunkWidth  - 1);
            int ly = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, VoxelData.ChunkHeight - 1);
            int lz = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, VoxelData.ChunkDepth  - 1);
            var blockType = (BlockType)cr.GetBlock(lx, ly, lz);

            // Les cross-blocks (ShortGrass) n'ont pas de collision propre.
            // Si on touche la face supérieure d'un bloc solide, vérifier si
            // un cross-block occupe la case juste au-dessus.
            if (BlockProperties.IsSolid(blockType))
            {
                Vector3 abovePos = hit.point + hit.normal * 0.5f;
                Vector3 aboveLocal = cr.transform.InverseTransformPoint(abovePos);
                int ax = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.x), 0, VoxelData.ChunkWidth  - 1);
                int ay = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.y), 0, VoxelData.ChunkHeight - 1);
                int az = Mathf.Clamp(Mathf.FloorToInt(aboveLocal.z), 0, VoxelData.ChunkDepth  - 1);
                // Si la case au-dessus est différente et contient un cross-block, le cibler en priorité
                if ((ax != lx || ay != ly || az != lz))
                {
                    var aboveType = (BlockType)cr.GetBlock(ax, ay, az);
                    if (BlockProperties.IsCrossBlock(aboveType))
                        return aboveType;
                }
            }

            return blockType;
        }

        /// <summary>Retourne le temps en secondes pour miner <paramref name="bt"/> avec l'outil actif.</summary>
        private float GetMineTime(BlockType bt)
        {
            ItemType tool     = GetActiveSurvivalTool();
            bool hasPickaxe   = tool == ItemType.WoodenPickaxe || tool == ItemType.StonePickaxe || tool == ItemType.IronPickaxe;
            bool hasStonePick = tool == ItemType.StonePickaxe  || tool == ItemType.IronPickaxe;
            bool hasAxe       = tool == ItemType.WoodenAxe     || tool == ItemType.StoneAxe;

            switch (bt)
            {
                // Feuillages / gazon → très rapide
                case BlockType.Leaves: case BlockType.SpruceLeaves: case BlockType.BirchLeaves:
                case BlockType.JungleLeaves: case BlockType.AcaciaLeaves: case BlockType.DarkOakLeaves:
                case BlockType.MangroveLeaves: case BlockType.CherryLeaves: case BlockType.PaleOakLeaves:
                case BlockType.Grass:
                    return 0.3f;

                // Plantes instantanées
                case BlockType.ShortGrass:
                    return 0.05f;

                // Sol
                case BlockType.Dirt: case BlockType.Sand: case BlockType.RedSand:
                case BlockType.Gravel: case BlockType.Clay: case BlockType.CoarseDirt:
                case BlockType.Mud: case BlockType.Podzol: case BlockType.RootedDirt:
                    return 0.75f;

                // Troncs
                case BlockType.Wood: case BlockType.SpruceLog: case BlockType.BirchLog:
                case BlockType.JungleLog: case BlockType.AcaciaLog: case BlockType.DarkOakLog:
                case BlockType.MangroveLog: case BlockType.CherryLog: case BlockType.PaleOakLog:
                    return hasAxe ? 0.75f : 1.5f;

                // Planches / établi
                case BlockType.OakPlanks: case BlockType.SprucePlanks: case BlockType.BirchPlanks:
                case BlockType.JunglePlanks: case BlockType.AcaciaPlanks: case BlockType.DarkOakPlanks:
                case BlockType.MangrovePlanks: case BlockType.CherryPlanks:
                case BlockType.CrimsonPlanks: case BlockType.WarpedPlanks: case BlockType.BambooPlanks:
                case BlockType.CraftingTable:
                    return hasAxe ? 0.5f : 1.0f;

                // Pierre → Cobblestone en drop
                case BlockType.Stone:
                    if (hasStonePick) return 1.35f;
                    if (hasPickaxe)   return 2.0f;
                    return 7.5f;  // lent, sans drop

                case BlockType.Cobblestone: case BlockType.MossyCobblestone:
                case BlockType.Andesite: case BlockType.PolishedAndesite:
                case BlockType.Diorite: case BlockType.PolishedDiorite:
                case BlockType.Granite: case BlockType.PolishedGranite:
                case BlockType.StoneBricks: case BlockType.CrackedStoneBricks:
                case BlockType.MossyStoneBricks: case BlockType.ChiseledStoneBricks:
                case BlockType.SmoothStone: case BlockType.Deepslate:
                case BlockType.CobbledDeepslate: case BlockType.DeepSlateBricks: case BlockType.DeepSlateTiles:
                    if (hasStonePick) return 1.35f;
                    if (hasPickaxe)   return 2.0f;
                    return 7.5f;

                // Grès
                case BlockType.Sandstone: case BlockType.RedSandstone:
                    return hasPickaxe ? 1.35f : 2.0f;

                // Minerais (pioche requise)
                case BlockType.CoalOre:
                    return hasPickaxe ? 2.5f : 7.5f;
                case BlockType.IronOre: case BlockType.CopperOre: case BlockType.GoldOre:
                    return hasStonePick ? 3.0f : float.PositiveInfinity;

                // Bedrock = indestructible
                case BlockType.Bedrock:
                    return float.PositiveInfinity;

                default:
                    return 2.0f;
            }
        }

        private ItemType GetActiveSurvivalTool()
        {
            var hotbar = SurvivalInventoryData.Instance.Hotbar;
            if (_survivalHotbarIndex >= 0 && _survivalHotbarIndex < hotbar.Length)
            {
                var stack = hotbar[_survivalHotbarIndex];
                if (!stack.IsEmpty && stack.IsTool()) return stack.itemType;
            }
            return ItemType.None;
        }

        private static bool GetMouseHeld(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return false;
            return button == 0 ? mouse.leftButton.isPressed
                 : button == 1 ? mouse.rightButton.isPressed
                 : false;
#else
            return Input.GetMouseButton(button);
#endif
        }

        /// <summary>
        /// Après avoir posé un bloc, vérifie si la capsule du joueur chevauche le bloc
        /// placé et pousse le joueur vers le haut pour l'en dégager.
        /// Évite que le moteur physique résolve le chevauchement lateralement ou vers le bas.
        /// </summary>
        private void ResolveBlockPlacementOverlap(RaycastHit hit)
        {
            if (playerRigidbody == null) return;

            var capsule = playerRigidbody.GetComponent<CapsuleCollider>();
            if (capsule == null) return;

            Transform pt      = playerRigidbody.transform;
            Vector3   up      = pt.up;  // radial haut de la planète

            // Sommet du bloc placé en world-space, projeté sur l'axe "up" du joueur.
            // hit.point est sur la face de l'ancien bloc ; +hit.normal*1 = sommet du nouveau.
            float blockTopH = Vector3.Dot(hit.point + hit.normal, up);

            // Pied de la capsule en world-space (le pivot du joueur est aux pieds).
            // capsule.center.y = playerHeight/2, height = playerHeight
            // → le bas de la capsule est au pivot (Y local = 0).
            float feetH = Vector3.Dot(pt.position, up);

            float overlap = blockTopH - feetH;
            if (overlap <= 0.001f) return;  // bloc en dessous ou au ras des pieds, OK

            // Vérifie que le bloc est horizontalement sous la capsule (pas sur le côté).
            Vector3 horizOffset = (hit.point + hit.normal * 0.5f) - pt.position;
            horizOffset -= Vector3.Dot(horizOffset, up) * up;
            if (horizOffset.magnitude > capsule.radius + 0.7f) return;

            // Limite de sécurité : ne téléporte pas de plus d'un bloc.
            if (overlap > 1.5f) return;

            // Pousse le joueur vers le haut pour dégager l'overlap.
            playerRigidbody.position += up * overlap;

            // Annule la composante descendante de la vélocité pour éviter
            // que le joueur rebondit immédiatement dans le bloc.
            Vector3 vel        = playerRigidbody.linearVelocity;
            float   downSpeed  = Vector3.Dot(vel, -up);
            if (downSpeed > 0f)
                playerRigidbody.linearVelocity += up * downSpeed;
        }

        // ── Raycast ───────────────────────────────────────────

        private bool Raycast(out RaycastHit hit)
        {
            if (playerCamera == null)
            {
                hit = default;
                return false;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            return UnityEngine.Physics.Raycast(ray, out hit, reach,
                ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
        }

        // ── Highlight ───────────────────────────────────────────

        private Vector3? _targetBlockPos;

        /// <summary>Coin (0,0,0) du bloc actuellement visé, ou null si rien.</summary>
        public Vector3? TargetBlockPos => _targetBlockPos;

        private void UpdateHighlight()
        {
            if (Raycast(out RaycastHit hit))
            {
                Vector3 blockCenter = hit.point - hit.normal * 0.5f;

                // Priorité : chunk directement touché par le raycast (évite
                // l'ambiguïté GetFace aux coutures kSeamMargin).
                // Fallback : GetChunkAt si le collider n'est pas un chunk
                // (autre objet de la scène, terrain, etc.).
                ChunkRenderer cr = hit.collider != null
                    ? hit.collider.GetComponent<ChunkRenderer>()
                    : null;
                if (cr == null && world != null)
                    cr = world.GetChunkAt(hit.point - hit.normal * 0.5f);

                if (cr != null)
                {
                    // Convertit le point hit en espace LOCAL du chunk (gère
                    // rotation + échelle + hiérarchie de transforms).
                    Vector3 local = cr.transform.InverseTransformPoint(blockCenter);
                    int lx = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, VoxelData.ChunkWidth  - 1);
                    int ly = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, VoxelData.ChunkHeight - 1);
                    int lz = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, VoxelData.ChunkDepth  - 1);

                    // Coin du bloc en world-space (le pivot du highlight est au coin).
                    Vector3 corner = cr.transform.TransformPoint(lx, ly, lz);
                    _targetBlockPos = corner;
                    if (blockHighlight != null)
                    {
                        blockHighlight.position = corner;
                        blockHighlight.rotation = cr.transform.rotation;
                        blockHighlight.gameObject.SetActive(true);
                    }
                }
                else
                {
                    // Repli si le collider n'est pas un chunk (ne devrait pas arriver)
                    _targetBlockPos = new Vector3(
                        Mathf.FloorToInt(blockCenter.x),
                        Mathf.FloorToInt(blockCenter.y),
                        Mathf.FloorToInt(blockCenter.z));
                    if (blockHighlight != null)
                    {
                        blockHighlight.position = _targetBlockPos.Value;
                        blockHighlight.gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                _targetBlockPos = null;
                if (blockHighlight != null)
                    blockHighlight.gameObject.SetActive(false);
            }
        }

        // ── Sélection du bloc ─────────────────────────────────

        private static readonly BlockType[] _defaultHotbar = new BlockType[9]
        {
            BlockType.Stone, BlockType.Dirt, BlockType.Grass,
            BlockType.Sand, BlockType.Wood, BlockType.Leaves,
            BlockType.Cobblestone, BlockType.StoneBricks, BlockType.Bricks,
        };
        private BlockType[] _hotbar = (BlockType[])_defaultHotbar.Clone();
        private int _hotbarIndex;

        private void HandleBlockSelection()
        {
            bool slotChanged = false;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.y.ReadValue();
                if (scroll > 0f)  { _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length; slotChanged = true; }
                if (scroll < 0f)  { _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length; slotChanged = true; }
            }
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < _hotbar.Length; i++)
                    if (kb[(Key)(Key.Digit1 + i)].wasPressedThisFrame)
                    { _hotbarIndex = i; slotChanged = true; }
#else
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)  { _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length; slotChanged = true; }
            if (scroll < 0f)  { _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length; slotChanged = true; }

            for (int i = 0; i < _hotbar.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                { _hotbarIndex = i; slotChanged = true; }
#endif
            if (GameModeManager.IsSurvival)
            {
                // Hotbar survie : scroll/touches sur _survivalHotbarIndex
                _survivalHotbarIndex = _hotbarIndex;
                var hotbar = SurvivalInventoryData.Instance.Hotbar;
                ItemStack active = (_survivalHotbarIndex >= 0 && _survivalHotbarIndex < hotbar.Length)
                    ? hotbar[_survivalHotbarIndex]
                    : ItemStack.Empty;
                blockToPlace = active.IsBlock() ? active.ToBlockType() : BlockType.Air;
            }
            else
            {
                // Si le slot actif contient un item spécial, blockToPlace = Air (pas de bloc à poser)
                ItemType creativeItem = _creativeHotbarItemTypes[_hotbarIndex];
                blockToPlace = (creativeItem != ItemType.None) ? BlockType.Air : _hotbar[_hotbarIndex];
            }
        }

        private static bool GetMouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return false;
            return button == 0 ? mouse.leftButton.wasPressedThisFrame
                 : button == 1 ? mouse.rightButton.wasPressedThisFrame
                 : false;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        // ── Accesseurs ────────────────────────────────────────

        public BlockType   ActiveBlock          => blockToPlace;
        public int         HotbarIndex          => _hotbarIndex;
        public BlockType[] Hotbar               => _hotbar;
        public int         SurvivalHotbarIndex  => _survivalHotbarIndex;

        /// <summary>
        /// Permet à l'inventaire créatif de remplacer un bloc dans la hotbar.
        /// </summary>
        public void SetHotbarSlot(int slot, BlockType t)
        {
            if (slot >= 0 && slot < _hotbar.Length)
            {
                _hotbar[slot] = t;
                _creativeHotbarItemTypes[slot] = ItemType.None;  // efface l'item non-bloc
            }
        }

        /// <summary>
        /// Vide tous les slots de la hotbar (Air).
        /// Appelé par la console avec la commande /clear.
        /// </summary>
        public void ClearInventory()
        {
            for (int i = 0; i < _hotbar.Length; i++)
            {
                _hotbar[i] = BlockType.Air;
                _creativeHotbarItemTypes[i] = ItemType.None;
            }
            _hotbarIndex = 0;
            blockToPlace = BlockType.Air;
        }

        /// <summary>Assigne les références depuis GameBootstrap.</summary>
        public void Init(Camera cam, PlanetWorld w, Rigidbody rb = null)
        {
            playerCamera    = cam;
            world           = w;
            playerRigidbody = rb;
        }

        /// <summary>Assigne le cube de sélection 3D créé par GameBootstrap.</summary>
        public void InitHighlight(Transform highlight)
        {
            blockHighlight = highlight;
            if (blockHighlight != null)
                blockHighlight.gameObject.SetActive(false);
        }

        /// <summary>Assigne les références spécifiques au mode Survie.</summary>
        public void InitSurvival(SurvivalInventory survInv, Material[] mats)
        {
            _survivalInventory = survInv;
            _blockMaterials    = mats;
        }
    }
}
