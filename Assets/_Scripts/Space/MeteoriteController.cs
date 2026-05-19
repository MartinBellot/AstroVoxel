// ============================================================
//  MeteoriteController.cs
//  Contrôle un météorite individuel (pool-managed).
//
//  Cycle de vie d'un météorite :
//    1. Launch()  → vitesse + mesh + traînée de particules
//    2. Fly       → mouvement par Rigidbody + traînée visible
//    3. Impact    → OnCollisionEnter : cratère, dépôt de blocs, effets
//    4. Deactivate → retour au pool (IsAvailable = true)
//
//  Optim : utilise AddForce(Impulse) au lancement plutôt que Rigidbody.velocity
//  pour compatibilité avec Unity Physics (pas de modification directe de velocity
//  dans les versions récentes).
// ============================================================

using System.Collections;
using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Météorite physique, géré par pool dans <see cref="MeteoriteSpawner"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MeteoriteController : MonoBehaviour
    {
        // ── Constantes ────────────────────────────────────────
        private const float MaxLifeTime     = 30f;
        private const float CraterRadiusMin = 1.5f;
        private const float CraterRadiusMax = 4.5f;

        // ── État pool ─────────────────────────────────────────
        public bool IsAvailable { get; private set; } = true;

        // ── Refs ──────────────────────────────────────────────
        private Rigidbody         _rb;
        private MeshFilter        _mf;
        private MeshRenderer      _mr;
        private ParticleSystem    _trail;
        private PlanetWorld       _planet;          // fallback pour cratering planétaire
        private Material[]        _blockMaterials;
        private float             _radius;
        private Coroutine         _lifeCoroutine;

        // ── Initialisation (Awake) ────────────────────────────

        private void Awake()
        {
            _rb           = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation          = RigidbodyInterpolation.Interpolate;

            _mf = gameObject.AddComponent<MeshFilter>();
            _mr = gameObject.AddComponent<MeshRenderer>();

            // Collider sphérique (sera redimensionné dans Launch)
            gameObject.AddComponent<SphereCollider>();

            BuildTrailParticles();
        }

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Lance le météorite depuis une position dans une direction à une vitesse donnée.
        /// </summary>
        public void Launch(
            Vector3    startPos,
            Vector3    direction,
            float      speed,
            float      radius,
            PlanetWorld planet,
            Material[]  materials)
        {
            IsAvailable     = false;
            _planet         = planet;
            _blockMaterials = materials;
            _radius         = Mathf.Clamp(radius, 0.3f, 3.0f);

            transform.position = startPos;
            transform.rotation = Quaternion.LookRotation(direction);
            gameObject.SetActive(true);

            // Mesh procédural
            _mf.sharedMesh = GenerateMeteoriteMesh(_radius);

            // Matériau
            int bsIdx = (int)BlockType.Blackstone;
            _mr.sharedMaterial = (_blockMaterials != null && _blockMaterials.Length > bsIdx && _blockMaterials[bsIdx] != null)
                ? _blockMaterials[bsIdx]
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse"));

            // Redimensionne le collider
            var sc = GetComponent<SphereCollider>();
            sc.radius = _radius;

            // Vitesse
            _rb.linearVelocity = direction.normalized * speed;

            // Traînée
            if (_trail != null) _trail.Play();

            // Vie maximale
            if (_lifeCoroutine != null) StopCoroutine(_lifeCoroutine);
            _lifeCoroutine = StartCoroutine(LifeTimer());
        }

        // ── Impact ────────────────────────────────────────────

        private void OnCollisionEnter(Collision col)
        {
            if (!gameObject.activeSelf || IsAvailable) return;

            Vector3 impactPoint = col.contacts.Length > 0
                ? col.contacts[0].point
                : transform.position;

            // Détecte le monde voxel touché : astéroïde en priorité, puis planète
            IVoxelWorld targetWorld = null;
            var asteroid = col.gameObject.GetComponentInParent<AsteroidWorld>();
            if (asteroid != null)
                targetWorld = asteroid;
            else if (_planet != null)
                targetWorld = _planet;

            CreateCrater(impactPoint, targetWorld);
            DepositBlocks(impactPoint, targetWorld);
            CreateImpactEffect(impactPoint);

            Deactivate();
        }

        // ── Effets d'impact ───────────────────────────────────

        private void CreateCrater(Vector3 impactPos, IVoxelWorld world)
        {
            if (world == null) return;

            float craterR = Mathf.Lerp(CraterRadiusMin, CraterRadiusMax,
                (_radius - 0.3f) / 2.7f);

            int ri = Mathf.CeilToInt(craterR);
            for (int z = -ri; z <= ri; z++)
            for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
            {
                if (x * x + y * y + z * z <= craterR * craterR)
                    world.BreakBlock(impactPos + new Vector3(x, y, z));
            }
        }

        private void DepositBlocks(Vector3 center, IVoxelWorld world)
        {
            if (world == null) return;

            int count = Random.Range(4, 10);
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Random.insideUnitSphere * (_radius * 2f + 2f);
                Vector3 pos    = center + offset;
                BlockType bt   = (Random.value > 0.55f) ? BlockType.Obsidian : BlockType.Blackstone;
                world.PlaceBlock(pos, bt);
            }
        }

        private void CreateImpactEffect(Vector3 pos)
        {
            var go  = new GameObject("MeteorImpact");
            go.transform.position = pos;
            var ps  = go.AddComponent<ParticleSystem>();
            // Stop immédiatement (playOnAwake = true par défaut) avant de modifier duration
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration        = 0.6f;
            main.loop            = false;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(5f, 22f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.1f), new Color(0.5f, 0.2f, 0.05f));
            main.gravityModifier = 0.3f;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

            ps.Play();
            Destroy(go, 2f);
        }

        // ── Trail de particules ───────────────────────────────

        private void BuildTrailParticles()
        {
            var go = new GameObject("MeteorTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _trail = go.AddComponent<ParticleSystem>();
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _trail.main;
            main.loop            = true;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.5f, 2.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.15f, 0.50f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Dégradé blanc → orange → gris → transparent
            var grad    = new Gradient();
            var colKeys = new GradientColorKey[]
            {
                new GradientColorKey(Color.white,            0.00f),
                new GradientColorKey(new Color(1f,0.55f,0f), 0.30f),
                new GradientColorKey(new Color(0.5f,0.5f,0.5f), 0.70f),
                new GradientColorKey(Color.gray,             1.00f),
            };
            var alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0.00f),
                new GradientAlphaKey(0.7f, 0.40f),
                new GradientAlphaKey(0f, 1.00f),
            };
            grad.SetKeys(colKeys, alphaKeys);
            main.startColor = new ParticleSystem.MinMaxGradient(grad);

            var em = _trail.emission;
            em.rateOverTime = 80f;

            var shape = _trail.shape;
            shape.enabled    = true;
            shape.shapeType  = ParticleSystemShapeType.Cone;
            shape.angle      = 12f;
            shape.radius     = 0.1f;
            shape.rotation   = new Vector3(180f, 0f, 0f); // émis vers l'arrière

            var sol = _trail.sizeOverLifetime;
            sol.enabled = true;
            sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            _trail.Stop();
        }

        // ── Mesh procédural ───────────────────────────────────

        private static Mesh GenerateMeteoriteMesh(float radius)
        {
            var mesh = new Mesh { name = "Meteorite" };

            const int latSegs = 6;
            const int lonSegs = 8;
            const int stride  = lonSegs + 1;

            // Seed visuel basé sur le hash du temps (variation entre météorites)
            var rng   = new System.Random((int)(Time.time * 100));
            float freq = 0.45f;
            float so   = (float)(rng.NextDouble() * 100.0);

            var verts = new System.Collections.Generic.List<Vector3>((latSegs + 1) * stride);
            var tris  = new System.Collections.Generic.List<int>(latSegs * lonSegs * 6);

            for (int lat = 0; lat <= latSegs; lat++)
            {
                float theta = Mathf.PI * lat / latSegs;
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / lonSegs;
                    float x   = Mathf.Sin(theta) * Mathf.Cos(phi);
                    float y   = Mathf.Cos(theta);
                    float z   = Mathf.Sin(theta) * Mathf.Sin(phi);

                    // Déformation per-vertex
                    float n = Mathf.PerlinNoise(x * freq + so, z * freq + so)
                            + Mathf.PerlinNoise(y * freq + so + 7f, x * freq + so + 7f) * 0.5f;
                    n /= 1.5f;
                    float r = radius * (0.60f + n * 0.80f);
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

        // ── Gestion pool ──────────────────────────────────────

        private IEnumerator LifeTimer()
        {
            yield return new WaitForSeconds(MaxLifeTime);
            Deactivate();
        }

        private void Deactivate()
        {
            if (_lifeCoroutine != null) { StopCoroutine(_lifeCoroutine); _lifeCoroutine = null; }
            if (_trail != null) _trail.Stop();
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            gameObject.SetActive(false);
            IsAvailable = true;
        }
    }
}
