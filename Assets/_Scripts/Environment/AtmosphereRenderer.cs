// ============================================================
//  AtmosphereRenderer.cs
//  Pilote l'aspect visuel de l'atmosphère planétaire :
//    • Sphère de ciel bleu (AtmosphereSky)
//    • Anneau de la couche d'ozone (OzoneRing)
//    • Ambiance lumineuse (RenderSettings.ambientLight)
//    • Brouillard atmosphérique (RenderSettings.fog)
//  Met à jour tous ces éléments chaque frame selon le cycle jour/nuit.
// ============================================================

using UnityEngine;
using UnityEngine.Rendering;
using AstroVoxel.Physics;

namespace AstroVoxel.Environment
{
    /// <summary>
    /// À ajouter sur un GameObject vide centré sur la planète (origine).
    /// Appelez <see cref="Init"/> depuis <see cref="Bootstrap.GameBootstrap"/>
    /// une fois le joueur créé.
    /// </summary>
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Couleurs du ciel")]
        [Tooltip("Bleu foncé au zénith (haut du ciel).")]
        [SerializeField] private Color zenithColor   = new Color(0.04f, 0.14f, 0.52f, 1f);
        [Tooltip("Bleu clair à l'horizon.")]
        [SerializeField] private Color horizonColor  = new Color(0.46f, 0.68f, 0.98f, 1f);

        [Header("Ambiance lumineuse")]
        [Tooltip("Ambiance en plein jour (bleu-blanc doux).")]
        [SerializeField] private Color ambientDay    = new Color(0.18f, 0.22f, 0.32f, 1f);
        [Tooltip("Ambiance au coucher/lever (ambre chaud).")]
        [SerializeField] private Color ambientGolden = new Color(0.18f, 0.10f, 0.04f, 1f);
        [Tooltip("Ambiance de nuit (quasi-noire, légère teinte bleue).")]
        [SerializeField] private Color ambientNight  = new Color(0.018f, 0.018f, 0.035f, 1f);
        [Tooltip("Ambiance dans l'espace (très sombre).")]
        [SerializeField] private Color ambientSpace  = new Color(0.028f, 0.028f, 0.045f, 1f);

        [Header("Brouillard")]
        [Tooltip("Couleur du brouillard le jour (ciel bleu clair).")]
        [SerializeField] private Color fogDayColor   = new Color(0.52f, 0.70f, 0.95f, 1f);
        [Tooltip("Couleur du brouillard la nuit (noir-bleu profond).")]
        [SerializeField] private Color fogNightColor = new Color(0.008f, 0.010f, 0.025f, 1f);
        [Tooltip("Densité du brouillard atmosphérique (exponentiel).")]
        [SerializeField] private float fogDensity    = 0.0010f;

        // ── Références (cachées à l'Init) ─────────────────────

        private Transform   _player;
        private SunOrbit    _sunOrbit;
        private OzoneLayer  _ozoneLayer;

        // ── Matériaux ─────────────────────────────────────────

        private Material _skyMat;
        private Material _ozoneMat;

        // ── Initialisation publique ───────────────────────────

        /// <summary>
        /// Initialise le rendu atmosphérique.
        /// Doit être appelé depuis GameBootstrap après la création du joueur.
        /// </summary>
        public void Init(Transform player)
        {
            _player    = player;
            _sunOrbit  = FindAnyObjectByType<SunOrbit>();
            _ozoneLayer = FindAnyObjectByType<OzoneLayer>();

            float atmR   = _ozoneLayer != null ? _ozoneLayer.AtmosphereRadius - 1.5f : 78.5f;
            float ozoneR = _ozoneLayer != null ? _ozoneLayer.AtmosphereRadius         : 80.0f;

            CreateAtmosphereSphere(atmR);
            CreateOzoneSphere(ozoneR);

            // Brouillard : activé par défaut ; Update() ajuste chaque frame.
            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.Exponential;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor   = fogDayColor;
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            if (_player == null) return;

            bool  inAtmosphere = _ozoneLayer == null
                              || _ozoneLayer.IsInsideAtmosphere(_player.position);
            float sunDot       = _sunOrbit != null ? _sunOrbit.SunDot : 1f;
            Vector3 sunDir     = _sunOrbit != null ? _sunOrbit.SunDirection : Vector3.up;

            // ── Facteur nuit [0,1] ────────────────────────────────────
            // sunDot :  1 = plein midi,  0 = horizon,  -1 = minuit
            // nightFactor : 0 = plein jour,  1 = pleine nuit
            float nightFactor = Mathf.Clamp01((-sunDot + 0.15f) / 0.55f);

            // ── Facteur coucher / lever de soleil ─────────────────────
            // Maximum quand sunDot ≈ 0 (soleil à l'horizon)
            float sunsetFactor = Mathf.Clamp01(1f - Mathf.Abs(sunDot) / 0.30f);

            // ── Mise à jour du matériau de ciel ───────────────────────
            if (_skyMat != null)
            {
                _skyMat.SetFloat("_NightFactor", nightFactor);
                _skyMat.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            }

            if (inAtmosphere)
            {
                // ── Ambiance planétaire ───────────────────────────────
                // Jour → Coucher → Nuit
                Color amb = Color.Lerp(ambientDay, ambientGolden, sunsetFactor);
                amb = Color.Lerp(amb, ambientNight, nightFactor);
                RenderSettings.ambientMode  = AmbientMode.Flat;
                RenderSettings.ambientLight = amb;

                // ── Brouillard atmosphérique ──────────────────────────
                Color fogCol = Color.Lerp(fogDayColor, fogNightColor, nightFactor);
                // Teinte orangée au coucher de soleil
                fogCol = Color.Lerp(fogCol, new Color(0.85f, 0.40f, 0.08f, 1f),
                                    sunsetFactor * 0.45f * (1f - nightFactor));
                RenderSettings.fogColor   = fogCol;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fog        = true;
            }
            else
            {
                // ── Espace : ambiance quasi-nulle, pas de brouillard ──
                RenderSettings.ambientMode  = AmbientMode.Flat;
                RenderSettings.ambientLight = ambientSpace;
                RenderSettings.fog          = false;
            }
        }

        // ── Construction des sphères ──────────────────────────

        private void CreateAtmosphereSphere(float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AtmosphereSphere";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.one * radius * 2f;

            Destroy(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var shader = Shader.Find("AstroVoxel/AtmosphereSky");
            if (shader == null)
            {
                Debug.LogError("[AtmosphereRenderer] Shader 'AstroVoxel/AtmosphereSky' introuvable !", this);
                return;
            }

            _skyMat = new Material(shader);
            _skyMat.SetColor("_ZenithColor",  zenithColor);
            _skyMat.SetColor("_HorizonColor", horizonColor);
            mr.sharedMaterial = _skyMat;
        }

        private void CreateOzoneSphere(float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "OzoneSphere";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.one * radius * 2f;

            Destroy(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var shader = Shader.Find("AstroVoxel/OzoneRing");
            if (shader == null)
            {
                Debug.LogError("[AtmosphereRenderer] Shader 'AstroVoxel/OzoneRing' introuvable !", this);
                return;
            }

            _ozoneMat = new Material(shader);
            mr.sharedMaterial = _ozoneMat;
        }

        // ── Nettoyage ─────────────────────────────────────────

        private void OnDestroy()
        {
            if (_skyMat   != null) Destroy(_skyMat);
            if (_ozoneMat != null) Destroy(_ozoneMat);
        }
    }
}
