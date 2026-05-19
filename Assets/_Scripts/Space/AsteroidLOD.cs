// ============================================================
//  AsteroidLOD.cs
//  Gère le niveau de détail d'un astéroïde selon la distance au joueur.
//
//  Trois niveaux :
//    > DistFrustum  : désactivé (aucun rendu, aucune physique)
//    DistLOD-Frust  : mesh LOD simplifié (icosphère déformée, ~300 tris)
//    < DistVoxel    : voxels complets chargés (AsteroidWorld actif)
//
//  Le budget global (max astéroïdes en mode voxel simultanés) est géré
//  par AsteroidSystemManager qui peut appeler ForceUnload si nécessaire.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Contrôleur LOD d'un astéroïde. Doit être ajouté APRÈS AsteroidWorld
    /// sur le même GameObject.
    /// </summary>
    [RequireComponent(typeof(AsteroidWorld))]
    public sealed class AsteroidLOD : MonoBehaviour
    {
        // ── Seuils de distance ────────────────────────────────
        private const float DistVoxel   = 300f;   // < 300u → voxels complets (atteignable depuis la planète)
        private const float DistLOD     = 800f;   // < 800u → mesh LOD
        // Au-delà de DistLOD → tout désactivé

        // ── Composants ────────────────────────────────────────
        private AsteroidWorld _world;
        private MeshFilter    _lodMF;
        private MeshRenderer  _lodMR;
        private GameObject    _lodGO;
        private Transform     _player;        private Transform     _camera;   // référence prioritaire (suit ship/joueur actif)
        // ── État ──────────────────────────────────────────────
        private enum LodState { Culled, LODMesh, Voxels }
        private LodState _state = LodState.Culled;

        /// <summary>Vrai si cet astéroïde est actuellement en mode voxel (chargé).</summary>
        public bool IsVoxels => _state == LodState.Voxels;

        // ── Initialisation publique ───────────────────────────

        /// <summary>
        /// Appeler juste après AddComponent depuis AsteroidField.
        /// </summary>
        public void Init(Transform player)
        {
            _player = player;
            _world  = GetComponent<AsteroidWorld>();

            _lodGO  = BuildLODMesh();
            _lodGO.SetActive(false);

            // Premier update forcé
            UpdateLOD(float.MaxValue);
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            // Résolution paresseuse de la caméra active (peut changer en cours de jeu :
            // ex. embarquement vaisseau désactive le joueur → caméra du ship prend le relais).
            if (_camera == null || !_camera.gameObject.activeInHierarchy)
                _camera = ResolveActiveCamera();

            Transform reference = _camera != null ? _camera : _player;
            if (reference == null) return;

            float dist = Vector3.Distance(transform.position, reference.position);
            UpdateLOD(dist);

            // Une fois les chunks voxel entièrement chargés, on peut masquer le
            // mesh LOD (qui restait visible pendant la coroutine de chargement).
            if (_state == LodState.Voxels && _lodGO.activeSelf && _world.IsLoaded)
                _lodGO.SetActive(false);
        }
        /// <summary>Distance courante au joueur (cache, calculé dans Update).</summary>
        public float DistanceToPlayer
        {
            get
            {
                Transform reference = (_camera != null && _camera.gameObject.activeInHierarchy)
                    ? _camera
                    : _player;
                if (reference == null) return float.MaxValue;
                return Vector3.Distance(transform.position, reference.position);
            }
        }

        /// <summary>
        /// Trouve une caméra active : d'abord Camera.main, sinon la première
        /// caméra activée dans la scène (couvre le cas où la shipCamera n'est
        /// pas taguée MainCamera).
        /// </summary>
        private static Transform ResolveActiveCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main.transform;

            var all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].isActiveAndEnabled)
                    return all[i].transform;

            return null;
        }
        // ── LOD ───────────────────────────────────────────────

        private void UpdateLOD(float dist)
        {
            // 1. Calcul de l'état souhaité d'après la distance.
            LodState target;
            if      (dist < DistVoxel) target = LodState.Voxels;
            else if (dist < DistLOD)   target = LodState.LODMesh;
            else                       target = LodState.Culled;

            // 2. Gate de budget : si on veut passer en Voxels mais qu'aucun slot n'est
            //    disponible, on rétrograde vers LODMesh AVANT toute transition.
            //    Le manager peut accepter en évinçant un voxel plus lointain.
            if (target == LodState.Voxels
                && AsteroidSystemManager.Instance != null
                && !AsteroidSystemManager.Instance.RequestVoxelSlot(this, dist))
            {
                target = LodState.LODMesh;
            }

            if (target == _state) return;

            // 3. Quitte l'état précédent.
            switch (_state)
            {
                case LodState.Voxels:
                    _world.UnloadChunks();
                    break;
                case LodState.LODMesh:
                    _lodGO.SetActive(false);
                    break;
            }

            // 4. Entre dans le nouvel état.
            switch (target)
            {
                case LodState.Voxels:
                    // Garde le mesh LOD visible pendant le chargement async des chunks
                    // pour éviter le "trou" visuel (la coroutine spawne 4 chunks/frame).
                    _lodGO.SetActive(true);
                    _world.LoadChunks();
                    break;
                case LodState.LODMesh:
                    _lodGO.SetActive(true);
                    break;
                case LodState.Culled:
                    break;
            }

            _state = target;
        }

        /// <summary>
        /// Forcé par AsteroidSystemManager si le budget voxel est dépassé.
        /// </summary>
        public void ForceUnloadVoxels()
        {
            if (_state != LodState.Voxels) return;
            _world.UnloadChunks();
            _lodGO.SetActive(true);
            _state = LodState.LODMesh;
        }

        // ── Construction du mesh LOD procédural ───────────────

        private GameObject BuildLODMesh()
        {
            var go = new GameObject("AsteroidLOD_Mesh");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _lodMF = go.AddComponent<MeshFilter>();
            _lodMR = go.AddComponent<MeshRenderer>();

            // Matériau Blackstone si disponible, sinon gris sombre
            Material mat = null;
            int bsIndex  = (int)BlockType.Blackstone;
            if (_world.blockMaterials != null && _world.blockMaterials.Length > bsIndex)
                mat = _world.blockMaterials[bsIndex];

            if (mat == null)
            {
                mat = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse"));
                mat.color = new Color(0.13f, 0.12f, 0.15f);
            }
            _lodMR.sharedMaterial = mat;

            _lodMF.sharedMesh = GenerateDeformedSphere(_world.coreRadius, _world.seed);

            // Collider sphérique approché
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = _world.coreRadius;

            return go;
        }

        private static Mesh GenerateDeformedSphere(float radius, int seed)
        {
            var mesh = new Mesh { name = "AsteroidLOD" };

            const int latSegs = 10;
            const int lonSegs = 14;
            const int stride  = lonSegs + 1;

            float so   = (seed * 1.31f) % 100f;
            float freq = 0.20f;

            var verts = new List<Vector3>((latSegs + 1) * stride);
            var tris  = new List<int>(latSegs * lonSegs * 6);

            for (int lat = 0; lat <= latSegs; lat++)
            {
                float theta = Mathf.PI * lat / latSegs;
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / lonSegs;
                    float x   = Mathf.Sin(theta) * Mathf.Cos(phi);
                    float y   = Mathf.Cos(theta);
                    float z   = Mathf.Sin(theta) * Mathf.Sin(phi);

                    // Déformation Perlin multi-octave pour aspect rocheux
                    float n = Mathf.PerlinNoise(x * freq + so, z * freq + so)
                            + Mathf.PerlinNoise(y * freq + so + 10f, x * freq + so + 10f) * 0.5f;
                    n /= 1.5f;  // normalise vers [0, 1]

                    float r = radius * (0.72f + n * 0.56f);
                    verts.Add(new Vector3(x * r, y * r, z * r));
                }
            }

            for (int lat = 0; lat < latSegs; lat++)
            for (int lon = 0; lon < lonSegs; lon++)
            {
                int v0 = lat * stride + lon;
                int v1 = v0 + 1;
                int v2 = v0 + stride;
                int v3 = v2 + 1;
                tris.Add(v0); tris.Add(v2); tris.Add(v1);
                tris.Add(v1); tris.Add(v2); tris.Add(v3);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
