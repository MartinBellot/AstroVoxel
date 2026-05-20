// ============================================================
//  SunOrbit.cs
//  Soleil qui tourne autour de la planète.
//  Crée automatiquement :
//    - Une grande sphère avec SunSurface (limb darkening + granulation)
//    - Une sphère de corona additive (halo Fresnel)
//    - Une DirectionalLight calée sur la direction solaire
//
//  Cycle jour/nuit :
//    SunDot = dot(dir_soleil, dir_joueur) depuis le centre planète.
//    +1 = plein midi  |  0 = coucher/lever  |  -1 = minuit.
//    La lumière s'éteint quand le soleil passe derrière la planète.
// ============================================================

using UnityEngine;
using UnityEngine.Rendering;
using AstroVoxel.Player;
using AstroVoxel.Vehicle;

namespace AstroVoxel.Environment
{
    /// <summary>
    /// Soleil orbital avec cycle jour/nuit complet.
    /// </summary>
    public sealed class SunOrbit : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Orbite")]
        [Tooltip("Distance du centre de la planète (unités).")]
        [SerializeField] private float orbitRadius = 800f;

        [Tooltip("Vitesse de rotation orbitale (degrés / seconde). 0.25 = ~24 min par jour.")]
        [SerializeField] private float orbitSpeed  = 0.25f;

        [Tooltip("Inclinaison de l'axe d'orbite (inclinaison axiale).")]
        [SerializeField, Range(-90f, 90f)] private float orbitTilt = 23.5f;

        [Header("Visuel")]
        [Tooltip("Rayon de la sphère solaire en unités.")]
        [SerializeField] private float sunRadius = 70f;

        [Tooltip("Couleur HDR du cœur (blanc-jaune très chaud).")]
        [SerializeField, ColorUsage(true, true)]
        private Color coreColor = new Color(4.5f, 4.0f, 2.5f, 1f);

        [Tooltip("Couleur HDR du bord (orange / rouge).")]
        [SerializeField, ColorUsage(true, true)]
        private Color limbColor = new Color(3.0f, 0.7f, 0.02f, 1f);

        [Tooltip("Couleur HDR de la corona (halo).")]
        [SerializeField, ColorUsage(true, true)]
        private Color coronaColor = new Color(2.5f, 0.55f, 0.03f, 1f);

        [Tooltip("Ratio entre le rayon de la corona et le rayon du soleil.")]
        [SerializeField, Range(1.5f, 5f)] private float coronaRatio = 2.8f;

        [Header("Lumière directionnelle")]
        [Tooltip("Intensité maximale en plein jour.")]
        [SerializeField] private float lightIntensity = 1.8f;
        [Tooltip("Couleur de la lumière à midi (blanc-jaune légèrement chaud).")]
        [SerializeField] private Color lightColor     = new Color(1.0f, 0.97f, 0.85f, 1f);
        [SerializeField] private float lightIndirect  = 1.0f;

        // ── État interne ──────────────────────────────────────

        private Transform _sunBody;
        private Light     _sunLight;
        private float     _angle;
        private Transform _player;

        /// <summary>Direction monde normalisée du centre planétaire vers le soleil (orbitale).
        /// Indépendante de la position visuelle de _sunBody → correcte même à l'infini.</summary>
        private Vector3   _sunOrbitDir = Vector3.right;

        // ── Propriétés publiques ──────────────────────────────

        /// <summary>
        /// Produit scalaire entre la direction planète→soleil et planète→joueur.
        /// +1 = plein midi  |  0 = coucher  |  -1 = minuit.
        /// </summary>
        public float SunDot
        {
            get
            {
                if (_player == null) return 1f;
                // Utilise la direction orbitale réelle — indépendante de la position visuelle
                Vector3 playerDir = _player.position.normalized;
                return Vector3.Dot(_sunOrbitDir, playerDir);
            }
        }

        /// <summary>Direction normalisée du centre planétaire vers le soleil.</summary>
        public Vector3 SunDirection => _sunOrbitDir;

        /// <summary>
        /// Position réelle du soleil dans le monde (avant repositionnement visuel LateUpdate).
        /// Utilisée pour la détection de proximité / mort.
        /// </summary>
        public Vector3 SunRealWorldPosition => transform.position + _sunOrbitDir * orbitRadius;

        // ── API publique ──────────────────────────────────────

        /// <summary>Injecte le Transform du joueur (appelé depuis GameBootstrap).</summary>
        public void SetPlayer(Transform player) => _player = player;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            transform.localRotation = Quaternion.AngleAxis(orbitTilt, Vector3.forward);

            var bodyGO = new GameObject("SunBody");
            bodyGO.transform.SetParent(transform, false);
            _sunBody = bodyGO.transform;

            CreateSunSphere();
            CreateCorona();
            CreateDirectionalLight();

            ApplyOrbit(_angle);
        }

        private void Update()
        {
            _angle = (_angle + orbitSpeed * Time.deltaTime) % 360f;
            ApplyOrbit(_angle);
            CheckSunProximityDeath();
        }

        // ── Mort par proximité solaire ───────────────────────

        // Rayon de la zone de danger (légèrement plus grand que sunRadius)
        private const float SunDeathRadius = 80f;
        private bool _sunKillPending;

        private static readonly string[] SunDeathMessages = new[]
        {
            "Vous avez cherché le soleil. Vous l'avez trouvé.",
            "SPF 1 000 000 aurait peut-être suffi.",
            "Température de surface : 5 500 °C. Vous : bien cuit.",
            "Le soleil : 1  —  Vous : fondu.",
            "Il paraît que s'approcher du soleil c'est dangereux. Il paraît.",
            "Votre vaisseau a été upgrade en cendre.",
            "Trop chaud pour vous ? Vraiment ?",
        };

        private void CheckSunProximityDeath()
        {
            if (!GameModeManager.IsSurvival) return;
            if (_sunKillPending) return;

            Vector3 sunPos = SunRealWorldPosition;

            // Vérifie d'abord le vaisseau piloté (prioritaire)
            var ship = SpaceShipController.ActiveShip;
            if (ship != null)
            {
                float shipDist = Vector3.Distance(ship.transform.position, sunPos);
                if (shipDist < SunDeathRadius)
                {
                    _sunKillPending = true;
                    string msg = SunDeathMessages[UnityEngine.Random.Range(0, SunDeathMessages.Length)];
                    ship.TriggerCrashExplosion(msg);
                    _sunKillPending = false;
                    return;
                }
            }

            // Vérifie ensuite le joueur à pied
            if (_player == null) return;
            float playerDist = Vector3.Distance(_player.position, sunPos);
            if (playerDist < SunDeathRadius)
            {
                _sunKillPending = true;
                var health = _player.GetComponent<PlayerHealth>();
                string msg = SunDeathMessages[UnityEngine.Random.Range(0, SunDeathMessages.Length)];
                health?.KillWithMessage(msg);
                _sunKillPending = false;
            }
        }

        // ── Mouvement orbital ─────────────────────────────────

        private void ApplyOrbit(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            _sunBody.localPosition = new Vector3(
                Mathf.Cos(rad) * orbitRadius,
                0f,
                Mathf.Sin(rad) * orbitRadius);

            // Stocker la direction orbitale AVANT de déplacer le corps visuel dans LateUpdate
            _sunOrbitDir = (_sunBody.position - transform.position).normalized;
            if (_sunOrbitDir.sqrMagnitude < 0.01f) _sunOrbitDir = Vector3.right;

            // Lumière directionnelle : utilise la direction orbitale réelle
            _sunLight.transform.rotation = Quaternion.LookRotation(-_sunOrbitDir);

            UpdateDayNightCycle();
        }

        // ── Positionnement caméra-relatif (rendu à l'infini) ──

        /// <summary>
        /// Replace le corps visuel du soleil proche de la caméra active tout en
        /// conservant la direction orbitale. Garantit la visibilité depuis n'importe
        /// quelle distance sans modifier la lumière directionnelle ni le cycle j/n.
        /// </summary>
        private void LateUpdate()
        {
            // Cherche la caméra active (joueur ou vaisseau)
            Camera cam = null;
            foreach (var c in Camera.allCameras)
            {
                if (c != null && c.isActiveAndEnabled) { cam = c; break; }
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            // Place le soleil à mi-chemin du far clip dans la direction orbitale
            float renderDist = cam.farClipPlane * 0.5f;
            _sunBody.position = cam.transform.position + _sunOrbitDir * renderDist;

            // Échelle proportionnelle pour conserver la taille angulaire d'origine
            float s = renderDist / orbitRadius;
            _sunBody.localScale = new Vector3(s, s, s);
        }

        // ── Cycle jour / nuit ─────────────────────────────────

        /// <summary>
        /// Ajuste l'intensité et la couleur de la lumière selon l'angle solaire.
        /// Transitions :
        ///   SunDot ≥ 0.18 → blanc-jaune (midi)
        ///   SunDot ≈ 0    → orange brûlant (coucher/lever)
        ///   SunDot ≈ -0.2 → rouge sombre (crépuscule)
        ///   SunDot &lt; -0.35 → lumière éteinte (nuit complète)
        /// </summary>
        private void UpdateDayNightCycle()
        {
            float sd = SunDot;

            // ── Intensité : SmoothStep pour une extinction naturelle ──
            // Fondu entre -0.12 (sous l'horizon) et +0.22 (juste au-dessus)
            float t = Mathf.SmoothStep(-0.12f, 0.22f, sd);
            _sunLight.intensity = Mathf.Lerp(0f, lightIntensity, t);

            // ── Couleur de la lumière ─────────────────────────────────
            Color midday   = lightColor;                            // blanc-jaune (midi)
            Color sunset   = new Color(1.0f, 0.42f, 0.04f, 1f);   // orange vif (coucher)
            Color twilight = new Color(0.28f, 0.09f, 0.01f, 1f);  // rouge sombre (crépuscule)

            Color col;
            if (sd >= 0.18f)
            {
                col = midday;
            }
            else if (sd >= 0f)
            {
                // Transition midi → coucher : blanc-jaune → orange
                col = Color.Lerp(sunset, midday, sd / 0.18f);
            }
            else if (sd >= -0.20f)
            {
                // Coucher → crépuscule : orange → rouge sombre
                col = Color.Lerp(twilight, sunset, (sd + 0.20f) / 0.20f);
            }
            else
            {
                // Crépuscule → nuit : fondu vers noir complet
                col = twilight * Mathf.Clamp01((sd + 0.36f) / 0.16f);
            }

            _sunLight.color = col;
        }

        // ── Construction des objets visuels ───────────────────

        private void CreateSunSphere()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SunSphere";
            go.transform.SetParent(_sunBody, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.one * sunRadius * 2f;

            Destroy(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var shader = Shader.Find("AstroVoxel/SunSurface");
            if (shader == null)
            {
                Debug.LogError("[SunOrbit] Shader 'AstroVoxel/SunSurface' introuvable !", this);
                return;
            }

            var mat = new Material(shader);
            mat.SetColor("_CoreColor", coreColor);
            mat.SetColor("_LimbColor", limbColor);
            mr.sharedMaterial = mat;
        }

        private void CreateCorona()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SunCorona";
            go.transform.SetParent(_sunBody, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.one * sunRadius * 2f * coronaRatio;

            Destroy(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var shader = Shader.Find("AstroVoxel/SunCorona");
            if (shader == null)
            {
                Debug.LogError("[SunOrbit] Shader 'AstroVoxel/SunCorona' introuvable !", this);
                return;
            }

            var mat = new Material(shader);
            mat.SetColor("_CoronaColor", coronaColor);
            mr.sharedMaterial = mat;
        }

        private void CreateDirectionalLight()
        {
            var lightGO = new GameObject("SunDirectionalLight");
            lightGO.transform.SetParent(transform, false);

            _sunLight = lightGO.AddComponent<Light>();
            _sunLight.type             = LightType.Directional;
            _sunLight.color            = lightColor;
            _sunLight.intensity        = lightIntensity;
            _sunLight.bounceIntensity  = lightIndirect;
            _sunLight.shadows          = LightShadows.Soft;
            _sunLight.shadowStrength   = 0.90f;
            _sunLight.shadowResolution = LightShadowResolution.High;
        }

        // ── Gizmos ────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            DrawGizmoCircle(transform.position, transform.up, orbitRadius, 64);

            if (_sunBody != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0f, 0.8f);
                Gizmos.DrawWireSphere(_sunBody.position, sunRadius);
            }
        }

        private static void DrawGizmoCircle(Vector3 center, Vector3 normal, float radius, int segments)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 fwd  = Vector3.Cross(right, normal).normalized;
            Vector3 prev = center + right * radius;
            for (int s = 1; s <= segments; s++)
            {
                float a    = s * (2f * Mathf.PI / segments);
                Vector3 next = center + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
