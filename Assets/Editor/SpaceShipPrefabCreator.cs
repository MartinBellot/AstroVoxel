// ============================================================
//  SpaceShipPrefabCreator.cs   [EDITOR ONLY]
//  Génère le Prefab "SpaceShip" dans Assets/_Prefabs/
//  via le menu  AstroVoxel → Create SpaceShip Prefab
//
//  Hiérarchie créée :
//    SpaceShip              ← Rigidbody + BoxCollider + SpaceShipController
//    ├── Model              ← Groupe visuel
//    │   ├── Hull           ← Fuselage (capsule)
//    │   ├── WingLeft       ← Aile gauche (cube)
//    │   ├── WingRight      ← Aile droite (cube)
//    │   ├── Cockpit        ← Verrière (cube translucide)
//    │   └── Engine         ← Réacteur arrière (cylindre)
//    ├── ShipCamera         ← Camera + SpaceShipCamera + AudioListener
//    └── ExitPoint          ← Point de sortie joueur (côté droit)
// ============================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AstroVoxel.Vehicle;

namespace AstroVoxel.Editor
{
    public static class SpaceShipPrefabCreator
    {
        private const string PrefabPath = "Assets/_Prefabs/SpaceShip.prefab";

        [MenuItem("AstroVoxel/Create SpaceShip Prefab")]
        public static void CreatePrefab()
        {
            // ── Racine ────────────────────────────────────────
            var root = new GameObject("SpaceShip");

            // Rigidbody (physique spatiale)
            var rb                  = root.AddComponent<Rigidbody>();
            rb.useGravity           = false;
            rb.freezeRotation       = false;
            rb.mass                 = 1000f;
            rb.linearDamping        = 0f;
            rb.angularDamping       = 1.5f;
            rb.interpolation        = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Collider principal (boîte sur le fuselage)
            var col    = root.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size   = new Vector3(2f, 1.2f, 5f);

            // ── Modèle visuel ──────────────────────────────────
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            // Fuselage
            var hull = CreatePrimitive(PrimitiveType.Cube, "Hull", model.transform,
                pos: Vector3.zero,
                scale: new Vector3(2f, 1f, 5f),
                color: new Color(0.25f, 0.27f, 0.32f));

            // Aile gauche
            CreatePrimitive(PrimitiveType.Cube, "WingLeft", model.transform,
                pos: new Vector3(-3.5f, -0.2f, 0f),
                scale: new Vector3(5f, 0.2f, 2.5f),
                color: new Color(0.22f, 0.24f, 0.30f));

            // Aile droite
            CreatePrimitive(PrimitiveType.Cube, "WingRight", model.transform,
                pos: new Vector3(3.5f, -0.2f, 0f),
                scale: new Vector3(5f, 0.2f, 2.5f),
                color: new Color(0.22f, 0.24f, 0.30f));

            // Verrière (cockpit)
            CreatePrimitive(PrimitiveType.Cube, "Cockpit", model.transform,
                pos: new Vector3(0f, 0.65f, 1.5f),
                scale: new Vector3(1.2f, 0.6f, 1.4f),
                color: new Color(0.4f, 0.75f, 1f, 0.5f),
                transparent: true);

            // Réacteur arrière
            var engine = CreatePrimitive(PrimitiveType.Cylinder, "Engine", model.transform,
                pos: new Vector3(0f, 0f, -2.8f),
                scale: new Vector3(0.6f, 0.8f, 0.6f),
                color: new Color(0.15f, 0.15f, 0.18f));
            engine.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Caméra 3e personne ─────────────────────────────
            var camGO = new GameObject("ShipCamera");
            camGO.transform.SetParent(root.transform, false);
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;

            var cam             = camGO.AddComponent<Camera>();
            cam.nearClipPlane   = 0.5f;
            cam.farClipPlane    = 2000f;
            cam.fieldOfView     = 65f;
            cam.clearFlags      = CameraClearFlags.Skybox;

            // Post-processing URP
            var urp = camGO.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;

            // AudioListener
            camGO.AddComponent<AudioListener>();

            // SpaceShipCamera (spring-arm)
            var shipCam   = camGO.AddComponent<SpaceShipCamera>();
            shipCam.target = root.transform;

            // La caméra vaisseau commence inactive
            camGO.SetActive(false);

            // ── Point de sortie ────────────────────────────────
            var exit = new GameObject("ExitPoint");
            exit.transform.SetParent(root.transform, false);
            // Côté droit du fuselage, légèrement surélevé
            exit.transform.localPosition = new Vector3(2.5f, 1.5f, 0f);

            // ── SpaceShipController (câblage) ──────────────────
            var ctrl        = root.AddComponent<SpaceShipController>();
            ctrl.exitPoint  = exit.transform;
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

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 pos,
            Vector3 scale,
            Color color,
            bool transparent = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;

            // Supprime le collider sur les pièces visuelles (la racine a le BoxCollider)
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            // Applique le matériau
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                string shaderName = transparent
                    ? "Universal Render Pipeline/Lit"
                    : "Universal Render Pipeline/Lit";

                var mat = new Material(Shader.Find(shaderName));

                if (transparent)
                {
                    // Mode Transparent pour la verrière
                    mat.SetFloat("_Surface", 1f);          // Surface Type = Transparent
                    mat.SetFloat("_Blend", 0f);            // Blend Mode = Alpha
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }

                mat.color = color;
                renderer.sharedMaterial = mat;
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
