// ============================================================
//  BlockInteraction.cs
//  Clic gauche = casser un bloc, Clic droit = placer un bloc.
//  Utilise un Raycast depuis la caméra vers le monde.
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;
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
                if (GetMouseDown(1)) TryPlaceBlock();
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
            var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
            if (pw != null) { pw.BreakBlock(hitCr, pos); return; }

            // Planète de base (fallback si la hiérarchie ne remonte pas à PlanetWorld)
            world?.BreakBlock(hitCr, pos);
        }

        private void TryPlaceBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            Vector3 pos = hit.point + hit.normal * 0.5f;

            bool placed = false;

            // Astéroïde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null)
            {
                placed = ast.PlaceBlock(pos, blockToPlace);
            }
            else
            {
                // Planète infinie (PlanetWorld créé dynamiquement par InfinitePlanetSystem)
                var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
                if (pw != null)
                    placed = pw.PlaceBlock(pos, blockToPlace);
                else if (world != null)
                    placed = world.PlaceBlock(pos, blockToPlace);
            }

            // Empêche le joueur de tomber dans le vide quand il pose un bloc sous ses pieds
            if (placed)
                ResolveBlockPlacementOverlap(hit);
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
            Vector3 breakPos    = hit.point - hit.normal * 0.5f;
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
                if (pw != null)   pw.BreakBlock(hitCr, breakPos);
                else              world?.BreakBlock(hitCr, breakPos);
            }

            // Spawner le drop
            if (dropBlock != BlockType.Air)
            {
                Transform playerTr = playerRigidbody != null
                    ? playerRigidbody.transform
                    : transform;
                BlockDropController.SpawnDrop(dropBlock, breakPos, playerTr, _blockMaterials);
            }
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

            Vector3 placePos = hit.point + hit.normal * 0.5f;
            bool placed      = false;

            var ast2 = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast2 != null) placed = ast2.PlaceBlock(placePos, typeToPlace);
            else
            {
                var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
                if (pw != null)        placed = pw.PlaceBlock(placePos, typeToPlace);
                else if (world != null) placed = world.PlaceBlock(placePos, typeToPlace);
            }

            if (placed)
            {
                SurvivalInventoryData.Instance.RemoveItem((ItemType)(int)typeToPlace, 1);
                ResolveBlockPlacementOverlap(hit);
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
            return (BlockType)cr.GetBlock(lx, ly, lz);
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
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.y.ReadValue();
                if (scroll > 0f)  _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length;
                if (scroll < 0f)  _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length;
            }
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < _hotbar.Length; i++)
                    if (kb[(Key)(Key.Digit1 + i)].wasPressedThisFrame)
                        _hotbarIndex = i;
#else
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)  _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length;
            if (scroll < 0f)  _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length;

            for (int i = 0; i < _hotbar.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    _hotbarIndex = i;
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
                blockToPlace = _hotbar[_hotbarIndex];
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
                _hotbar[slot] = t;
        }

        /// <summary>
        /// Vide tous les slots de la hotbar (Air).
        /// Appelé par la console avec la commande /clear.
        /// </summary>
        public void ClearInventory()
        {
            for (int i = 0; i < _hotbar.Length; i++)
                _hotbar[i] = BlockType.Air;
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
