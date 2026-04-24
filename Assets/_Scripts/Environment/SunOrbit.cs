// ============================================================
//  SunOrbit.cs
//  Soleil qui tourne autour de la planète.
//  Crée automatiquement :
//    - Une grande sphère avec SunSurface (limb darkening + granulation)
//    - Une sphère de corona additive (halo Fresnel)
//    - Une DirectionalLight calée sur la direction solaire
// ============================================================

using UnityEngine;
using UnityEngine.Rendering;

namespace AstroVoxel.Environment
{
    /// <summary>
    /// Soleil orbital.
    /// Placez ce composant sur un GameObject vide créé par
    /// <see cref="Bootstrap.GameBootstrap"/>. La planète doit être
    /// à l'origine (Vector3.zero) du monde.
    /// </summary>
    public sealed class SunOrbit : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Orbite")]
        [Tooltip("Distance du centre de la planète (unités). Doit être < Camera.farClipPlane.")]
        [SerializeField] private float orbitRadius = 800f;

        [Tooltip("Vitesse de rotation orbitale (degrés / seconde).")]
        [SerializeField] private float orbitSpeed  = 2.5f;

        [Tooltip("Inclinaison de l'axe d'orbite (comme une inclinaison axiale).")]
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
        [SerializeField] private float lightIntensity  = 1.8f;
        [SerializeField] private Color lightColor      = new Color(1f, 0.96f, 0.82f);
        [SerializeField] private float lightIndirect   = 1.0f;

        // ── État interne ──────────────────────────────────────

        private Transform _sunBody;
        private Light     _sunLight;
        private float     _angle;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            // Inclinaison de l'axe d'orbite (dans le repère parent = monde)
            transform.localRotation = Quaternion.AngleAxis(orbitTilt, Vector3.forward);

            // Pivot mobile qui se déplace sur l'orbite
            var bodyGO = new GameObject("SunBody");
            bodyGO.transform.SetParent(transform, false);
            _sunBody = bodyGO.transform;

            CreateSunSphere();
            CreateCorona();
            CreateDirectionalLight();

            // Positionne dès le premier frame
            ApplyOrbit(_angle);
        }

        private void Update()
        {
            _angle = (_angle + orbitSpeed * Time.deltaTime) % 360f;
            ApplyOrbit(_angle);
        }

        // ── Mouvement orbital ─────────────────────────────────

        private void ApplyOrbit(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            _sunBody.localPosition = new Vector3(
                Mathf.Cos(rad) * orbitRadius,
                0f,
                Mathf.Sin(rad) * orbitRadius);

            // La lumière directionnelle pointe du soleil vers le centre planétaire (origine)
            Vector3 toCenter = Vector3.zero - _sunBody.position; // planète à l'origine
            if (toCenter.sqrMagnitude > 0.01f)
                _sunLight.transform.rotation = Quaternion.LookRotation(toCenter.normalized);
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
            // La lumière est enfant du root Sun (pas de SunBody) pour
            // que sa position ne bouge pas mais que sa rotation soit mise à jour.
            var lightGO = new GameObject("SunDirectionalLight");
            lightGO.transform.SetParent(transform, false);

            _sunLight = lightGO.AddComponent<Light>();
            _sunLight.type             = LightType.Directional;
            _sunLight.color            = lightColor;
            _sunLight.intensity        = lightIntensity;
            _sunLight.bounceIntensity  = lightIndirect;
            _sunLight.shadows          = LightShadows.Soft;
            _sunLight.shadowStrength   = 0.85f;
            _sunLight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High;
        }

        // ── Gizmos ────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Orbite
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            DrawGizmoCircle(transform.position, transform.up, orbitRadius, 64);

            // Soleil (position actuelle)
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
            Vector3 fwd = Vector3.Cross(right, normal).normalized;

            Vector3 prev = center + right * radius;
            for (int s = 1; s <= segments; s++)
            {
                float a = s * (2f * Mathf.PI / segments);
                Vector3 next = center + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
