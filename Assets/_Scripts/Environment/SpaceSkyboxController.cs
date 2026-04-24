// ============================================================
//  SpaceSkyboxController.cs
//  Crée au runtime le matériau "SpaceSkybox" et l'applique
//  à RenderSettings pour remplacer la skybox par défaut.
//  Règle également l'ambiance lumineuse sur un noir spatial.
// ============================================================

using UnityEngine;
using UnityEngine.Rendering;

namespace AstroVoxel.Environment
{
    /// <summary>
    /// Applique la skybox procédurale spatiale à la scène.
    /// À placer sur un GameObject dédié créé par <see cref="Bootstrap.GameBootstrap"/>.
    /// </summary>
    public sealed class SpaceSkyboxController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────

        [Header("Skybox")]
        [Tooltip("Intensité globale des étoiles.")]
        [SerializeField, Range(0.5f, 8f)] private float starBrightness  = 4.5f;

        [Tooltip("Intensité de la nébuleuse / Voie Lactée.")]
        [SerializeField, Range(0f, 1f)]   private float nebulaIntensity = 0.05f;

        [Tooltip("Couleur de fond de la nébuleuse (quasi-noir violacé).")]
        [SerializeField] private Color nebulaColor = new Color(0.001f, 0.0005f, 0.002f, 1f);

        [Header("Éclairage ambiant")]
        [Tooltip("Couleur ambiante spatiale (très sombre).")]
        [SerializeField] private Color ambientSpaceColor = new Color(0.03f, 0.03f, 0.05f, 1f);

        // ── État interne ───────────────────────────────────────

        private Material _skyboxMaterial;

        // ── Cycle de vie ───────────────────────────────────────

        private void Awake()
        {
            ApplySkybox();
        }

        private void OnDestroy()
        {
            if (_skyboxMaterial != null)
                Destroy(_skyboxMaterial);
        }

        // ── Implémentation ─────────────────────────────────────

        private void ApplySkybox()
        {
            Shader shader = Shader.Find("AstroVoxel/SpaceSkybox");
            if (shader == null)
            {
                Debug.LogError("[SpaceSkyboxController] Shader 'AstroVoxel/SpaceSkybox' introuvable. " +
                               "Vérifiez que SpaceSkybox.shader est bien dans Assets/_Shaders/.", this);
                return;
            }

            _skyboxMaterial = new Material(shader);
            _skyboxMaterial.SetFloat("_StarBrightness",  starBrightness);
            _skyboxMaterial.SetFloat("_NebulaIntensity", nebulaIntensity);
            _skyboxMaterial.SetColor("_NebulaColor",     nebulaColor);

            // Applique la skybox à toute la scène
            RenderSettings.skybox = _skyboxMaterial;

            // Éclairage ambiant plat très sombre — évite que les voxels
            // soient trop éclairés même côté "nuit" de la planète.
            RenderSettings.ambientMode  = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientSpaceColor;

            // Déclenche le recalcul du GI dynamique pour prendre en compte la nouvelle skybox
            DynamicGI.UpdateEnvironment();
        }
    }
}
