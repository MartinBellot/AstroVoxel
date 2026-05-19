// ============================================================
//  SpaceShipPrefabCreator.cs   [EDITOR ONLY]
//  Génère le Prefab "SpaceShip" dans Assets/_Prefabs/
//  via le menu  AstroVoxel → Create SpaceShip Prefab
//
//  Matériaux de blocs utilisés (BlockTextureRegistry) :
//    iron_block        → fuselage principal + nacelles
//    polished_andesite → nez avant (gris poli lisse)
//    deepslate_tiles   → section arrière (dalles sombres)
//    netherite_block   → ailes (alliage noir exotique)
//    gold_block        → liserets + bagues moteur (accents dorés)
//    obsidian          → cadre verrière (noir brillant)
//    coal_block        → tuyères (noir mat)
//
//  Hiérarchie créée :
//    SpaceShip              ← Rigidbody + BoxCollider + SpaceShipController
//    ├── Model              ← Groupe visuel
//    │   ├── Hull           ← Fuselage (iron_block)
//    │   ├── HullNose       ← Nez avant (polished_andesite)
//    │   ├── HullRear       ← Section arrière (deepslate_tiles)
//    │   ├── WingLeft       ← Aile gauche (netherite_block)
//    │   ├── WingRight      ← Aile droite (netherite_block)
//    │   ├── WingAccentLeft ← Liseret doré gauche (gold_block)
//    │   ├── WingAccentRight← Liseret doré droit (gold_block)
//    │   ├── CockpitFrame   ← Cadre verrière (obsidian)
//    │   ├── Cockpit        ← Verrière (transparent bleu)
//    │   ├── EngineMain     ← Réacteur central (coal_block)
//    │   ├── EngineRing     ← Bague moteur (gold_block)
//    │   ├── NacelleLeft    ← Nacelle gauche (iron_block)
//    │   ├── NacelleRight   ← Nacelle droite (iron_block)
//    │   ├── EnginePodLeft  ← Tuyère gauche (coal_block)
//    │   └── EnginePodRight ← Tuyère droite (coal_block)
//    ├── ShipCamera         ← Camera + SpaceShipCamera + AudioListener
//    └── ExitPoint          ← Point de sortie joueur (côté droit)
// ============================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AstroVoxel.Vehicle;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Editor
{
    public static class SpaceShipPrefabCreator
    {
        private const string PrefabPath = "Assets/_Prefabs/SpaceShip.prefab";

        [MenuItem("AstroVoxel/Create SpaceShip Prefab")]
        public static void CreatePrefab()
        {
            // ── Charge les matériaux depuis la BlockTextureRegistry ──────
            var registry = UnityEngine.Resources.Load<BlockTextureRegistry>("BlockTextureRegistry");
            Material[] regMats = registry?.materials;

            if (regMats == null)
                Debug.LogWarning("[SpaceShipPrefabCreator] BlockTextureRegistry introuvable — " +
                                 "lancez AstroVoxel → Rebuild Block Texture Registry. " +
                                 "Repli sur matériaux gris.");

            Material GetBlock(BlockType bt)
            {
                int idx = (int)bt;
                return (regMats != null && idx < regMats.Length && regMats[idx] != null)
                    ? regMats[idx]
                    : null;
            }

            // ── Racine ────────────────────────────────────────
            var root = new GameObject("SpaceShip");

            var rb                    = root.AddComponent<Rigidbody>();
            rb.useGravity             = false;
            rb.freezeRotation         = false;
            rb.mass                   = 1000f;
            rb.linearDamping          = 0f;
            rb.angularDamping         = 1.5f;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var col    = root.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size   = new Vector3(2.4f, 1.2f, 6.5f);

            // ── Modèle visuel ──────────────────────────────────
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            // ── Fuselage ───────────────────────────────────────

            // Corps principal — iron_block (métal argenté structuré)
            CreatePart(PrimitiveType.Cube, "Hull", model.transform,
                pos:   Vector3.zero,
                scale: new Vector3(2.2f, 1.0f, 5.0f),
                mat:   GetBlock(BlockType.IronBlock));

            // Nez avant — polished_andesite (gris poli, plus étroit)
            CreatePart(PrimitiveType.Cube, "HullNose", model.transform,
                pos:   new Vector3(0f, 0.05f, 3.6f),
                scale: new Vector3(1.4f, 0.8f, 2.2f),
                mat:   GetBlock(BlockType.PolishedAndesite));

            // Section arrière — deepslate_tiles (dalles sombres, moteurs)
            CreatePart(PrimitiveType.Cube, "HullRear", model.transform,
                pos:   new Vector3(0f, 0f, -2.9f),
                scale: new Vector3(2.4f, 1.1f, 1.4f),
                mat:   GetBlock(BlockType.DeepSlateTiles));

            // ── Ailes ──────────────────────────────────────────

            // Aile gauche — netherite_block (alliage noir exotique)
            CreatePart(PrimitiveType.Cube, "WingLeft", model.transform,
                pos:   new Vector3(-3.9f, -0.32f, -0.4f),
                scale: new Vector3(5.6f, 0.18f, 3.2f),
                mat:   GetBlock(BlockType.NetheriteBlock));

            // Aile droite — netherite_block
            CreatePart(PrimitiveType.Cube, "WingRight", model.transform,
                pos:   new Vector3(3.9f, -0.32f, -0.4f),
                scale: new Vector3(5.6f, 0.18f, 3.2f),
                mat:   GetBlock(BlockType.NetheriteBlock));

            // Liseret doré bord d'attaque gauche — gold_block
            CreatePart(PrimitiveType.Cube, "WingAccentLeft", model.transform,
                pos:   new Vector3(-5.85f, -0.28f, -0.4f),
                scale: new Vector3(0.38f, 0.24f, 3.2f),
                mat:   GetBlock(BlockType.GoldBlock));

            // Liseret doré bord d'attaque droit — gold_block
            CreatePart(PrimitiveType.Cube, "WingAccentRight", model.transform,
                pos:   new Vector3(5.85f, -0.28f, -0.4f),
                scale: new Vector3(0.38f, 0.24f, 3.2f),
                mat:   GetBlock(BlockType.GoldBlock));

            // ── Verrière ───────────────────────────────────────

            // Cadre en obsidienne (entourage noir brillant)
            CreatePart(PrimitiveType.Cube, "CockpitFrame", model.transform,
                pos:   new Vector3(0f, 0.64f, 1.9f),
                scale: new Vector3(1.45f, 0.72f, 1.65f),
                mat:   GetBlock(BlockType.Obsidian));

            // Verre translucide (légèrement plus petit que le cadre)
            CreateTransparentPart("Cockpit", model.transform,
                pos:   new Vector3(0f, 0.68f, 1.9f),
                scale: new Vector3(1.18f, 0.52f, 1.38f),
                color: new Color(0.35f, 0.8f, 1f, 0.38f));

            // ── Réacteur central ───────────────────────────────

            // Tuyère principale — coal_block (noir mat)
            var engineMain = CreatePart(PrimitiveType.Cylinder, "EngineMain", model.transform,
                pos:   new Vector3(0f, 0f, -3.65f),
                scale: new Vector3(0.72f, 1.05f, 0.72f),
                mat:   GetBlock(BlockType.CoalBlock));
            engineMain.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Bague dorée autour de la tuyère — gold_block
            var engineRing = CreatePart(PrimitiveType.Cylinder, "EngineRing", model.transform,
                pos:   new Vector3(0f, 0f, -3.05f),
                scale: new Vector3(0.88f, 0.13f, 0.88f),
                mat:   GetBlock(BlockType.GoldBlock));
            engineRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Nacelles latérales ─────────────────────────────

            // Nacelle gauche — iron_block (pod moteur sur l'aile)
            CreatePart(PrimitiveType.Cube, "NacelleLeft", model.transform,
                pos:   new Vector3(-2.6f, -0.35f, -1.5f),
                scale: new Vector3(0.9f, 0.7f, 2.8f),
                mat:   GetBlock(BlockType.IronBlock));

            // Nacelle droite — iron_block
            CreatePart(PrimitiveType.Cube, "NacelleRight", model.transform,
                pos:   new Vector3(2.6f, -0.35f, -1.5f),
                scale: new Vector3(0.9f, 0.7f, 2.8f),
                mat:   GetBlock(BlockType.IronBlock));

            // Tuyère nacelle gauche — coal_block
            var podLeft = CreatePart(PrimitiveType.Cylinder, "EnginePodLeft", model.transform,
                pos:   new Vector3(-2.6f, -0.35f, -3.05f),
                scale: new Vector3(0.46f, 0.55f, 0.46f),
                mat:   GetBlock(BlockType.CoalBlock));
            podLeft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Tuyère nacelle droite — coal_block
            var podRight = CreatePart(PrimitiveType.Cylinder, "EnginePodRight", model.transform,
                pos:   new Vector3(2.6f, -0.35f, -3.05f),
                scale: new Vector3(0.46f, 0.55f, 0.46f),
                mat:   GetBlock(BlockType.CoalBlock));
            podRight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Caméra 3e personne ─────────────────────────────
            var camGO = new GameObject("ShipCamera");
            camGO.transform.SetParent(root.transform, false);
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;

            var cam           = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane  = 2000f;
            cam.fieldOfView   = 65f;
            cam.clearFlags    = CameraClearFlags.Skybox;

            var urp = camGO.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;

            camGO.AddComponent<AudioListener>();

            var shipCam    = camGO.AddComponent<SpaceShipCamera>();
            shipCam.target = root.transform;

            camGO.SetActive(false);

            // ── Point de sortie ────────────────────────────────
            var exit = new GameObject("ExitPoint");
            exit.transform.SetParent(root.transform, false);
            exit.transform.localPosition = new Vector3(2.5f, 1.5f, 0f);

            // ── SpaceShipController (câblage) ──────────────────
            var ctrl       = root.AddComponent<SpaceShipController>();
            ctrl.exitPoint = exit.transform;
            ctrl.shipCamera = cam;

            // ── Sauvegarde du Prefab ───────────────────────────
            EnsurePrefabsFolder();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            if (prefab != null)
            {
                AssetDatabase.Refresh();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"[SpaceShipPrefabCreator] Prefab créé : {PrefabPath}");
            }
            else
            {
                Debug.LogError($"[SpaceShipPrefabCreator] Échec de la création du prefab à {PrefabPath}");
            }
        }

        // ── Helpers ───────────────────────────────────────────

        /// <summary>Crée une primitive avec un matériau de bloc (ou repli gris si null).</summary>
        private static GameObject CreatePart(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 pos,
            Vector3 scale,
            Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                if (mat != null)
                {
                    rend.sharedMaterial = mat;
                }
                else
                {
                    // Repli : gris métallique neutre
                    var fallback = new Material(
                        Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse"));
                    fallback.color = new Color(0.4f, 0.42f, 0.46f);
                    rend.sharedMaterial = fallback;
                }
            }

            return go;
        }

        /// <summary>Crée un cube translucide (verrière).</summary>
        private static GameObject CreateTransparentPart(
            string name,
            Transform parent,
            Vector3 pos,
            Vector3 scale,
            Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse"));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend",   0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.color = color;
                rend.sharedMaterial = mat;
            }

            return go;
        }

        private static void EnsurePrefabsFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prefabs"))
                AssetDatabase.CreateFolder("Assets", "_Prefabs");
        }
    }
}
#endif
