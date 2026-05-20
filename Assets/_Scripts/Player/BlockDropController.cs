// ============================================================
//  BlockDropController.cs
//  En mode Survie, quand un bloc est cassé, un "drop" flotte à
//  la position du bloc, avec une animation de rebond/rotation,
//  et est aspiré vers le joueur dès qu'il s'approche à < 2u.
//
//  Utilisation :
//    BlockDropController.SpawnDrop(blockType, worldPos, playerTransform);
// ============================================================

using System.Collections;
using UnityEngine;

namespace AstroVoxel.Player
{
    public sealed class BlockDropController : MonoBehaviour
    {
        // ── Config ────────────────────────────────────────────
        private const float PickupRadius    = 4.0f;   // distance de pickup auto
        private const float PickupDelay     = 0.5f;   // délai avant que le drop soit collectible
        private const float BobAmplitude    = 0.15f;  // amplitude du flottement
        private const float BobSpeed        = 2.0f;   // vitesse du flottement
        private const float RotationSpeed   = 90f;    // degrés/s
        private const float LifeTime        = 300f;   // auto-dépop après 5 min

        // ── État instance ─────────────────────────────────────
        private Transform _player;
        private ItemType  _itemType;
        private Vector3   _basePos;
        private float     _bobPhase;
        private bool      _collectible;

        // ── Factory statique ──────────────────────────────────

        /// <summary>
        /// Crée un drop à la position world-space du bloc cassé.
        /// </summary>
        public static void SpawnDrop(
            AstroVoxel.VoxelEngine.BlockType block,
            Vector3 worldPos,
            Transform player,
            Material[] blockMaterials = null)
        {
            if (!GameModeManager.IsSurvival) return;

            ItemType itype = ItemTypeHelper.FromBlockType(block);
            if (itype == ItemType.None) return;

            // Mini-cube visuel
            var go     = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name    = $"Drop_{block}";
            go.transform.position   = worldPos + Vector3.up * 0.5f;
            go.transform.localScale = Vector3.one * 0.35f;

            // Couleur du drop = couleur du bloc
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                byte iconId = AstroVoxel.VoxelEngine.BlockFaceData.GetIconRenderingId((byte)block);
                Material mat = (blockMaterials != null && iconId < blockMaterials.Length && blockMaterials[iconId] != null)
                    ? blockMaterials[iconId]
                    : null;

                if (mat != null)
                {
                    renderer.sharedMaterial = mat;
                }
                else
                {
                    renderer.material.color = AstroVoxel.VoxelEngine.BlockFaceData.GetFallbackColor(block);
                }
            }

            // Pas de physique : on gère le flottement nous-mêmes
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            // Trigger pour ne pas bloquer le joueur
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // Controller
            var ctrl       = go.AddComponent<BlockDropController>();
            ctrl._player   = player;
            ctrl._itemType = itype;
            ctrl._basePos  = go.transform.position;
            ctrl._bobPhase = Random.Range(0f, Mathf.PI * 2f); // phase aléatoire pour éviter la sync

            // Auto-dépop
            Destroy(go, LifeTime);

            // Délai avant collectible
            ctrl.StartCoroutine(ctrl.CoEnablePickup());
        }

        /// <summary>
        /// Crée un drop d'item (non-bloc, ex. graines) à la position world-space indiquée.
        /// </summary>
        public static void SpawnItemDrop(
            ItemType itemType,
            Vector3 worldPos,
            Transform player,
            Material[] itemMaterials = null)
        {
            if (!GameModeManager.IsSurvival) return;
            if (itemType == ItemType.None) return;

            // Quad visuel (ressemble à une icône 2D)
            var go     = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name    = $"Drop_{itemType}";
            go.transform.position   = worldPos + Vector3.up * 0.5f;
            go.transform.localScale = Vector3.one * 0.45f;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Les items non-blocs commencent à ID 300 ; l'index dans le tableau est ID-300.
                int idx = (int)itemType - 300;

                // Si le tableau de matériaux n'est pas fourni, tenter de le charger depuis le registry.
                if (itemMaterials == null)
                {
                    var reg = Resources.Load<AstroVoxel.VoxelEngine.BlockTextureRegistry>("BlockTextureRegistry");
                    if (reg != null) itemMaterials = reg.itemMaterials;
                }

                Material mat = (itemMaterials != null && idx >= 0 && idx < itemMaterials.Length && itemMaterials[idx] != null)
                    ? itemMaterials[idx]
                    : null;

                if (mat != null)
                    renderer.sharedMaterial = mat;
                else
                    renderer.material.color = AstroVoxel.VoxelEngine.BlockFaceData.GetFallbackColor(
                        AstroVoxel.VoxelEngine.BlockType.Grass); // couleur herbe comme placeholder
            }

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var ctrl       = go.AddComponent<BlockDropController>();
            ctrl._player   = player;
            ctrl._itemType = itemType;
            ctrl._basePos  = go.transform.position;
            ctrl._bobPhase = Random.Range(0f, Mathf.PI * 2f);

            Destroy(go, LifeTime);
            ctrl.StartCoroutine(ctrl.CoEnablePickup());
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            // Flottement + rotation
            _bobPhase += BobSpeed * Time.deltaTime;
            float y = _basePos.y + Mathf.Sin(_bobPhase) * BobAmplitude;
            transform.position = new Vector3(_basePos.x, y, _basePos.z);
            transform.Rotate(Vector3.up, RotationSpeed * Time.deltaTime, UnityEngine.Space.World);

            // Vérification distance joueur
            if (_collectible && _player != null)
            {
                float dist = Vector3.Distance(transform.position, _player.position);
                if (dist <= PickupRadius)
                    Collect();
            }
        }

        private IEnumerator CoEnablePickup()
        {
            yield return new WaitForSeconds(PickupDelay);
            _collectible = true;
        }

        private void Collect()
        {
            SurvivalInventoryData.Instance.AddItem(_itemType, 1);
            Destroy(gameObject);
        }
    }
}
