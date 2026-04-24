// ============================================================
//  SpaceSkybox.shader
//  Skybox procédurale "espace" : fond noir + milliers d'étoiles
//  générées par bruit de hachage en 4 couches de densité.
//  Compatible Built-in RP et URP (CGPROGRAM + UnityCG.cginc).
// ============================================================

Shader "AstroVoxel/SpaceSkybox"
{
    Properties
    {
        // Propriétés exposées dans l'Inspector (modifiables à chaud)
        _StarBrightness  ("Star Brightness",   Range(0.5, 8.0))       = 4.5
        _NebulaIntensity ("Nebula Intensity",  Range(0.0, 1.0))        = 0.05
        _NebulaColor     ("Nebula Base Color", Color)                  = (0.001, 0.0005, 0.002, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Background"
            "RenderType"     = "Background"
            "PreviewType"    = "Skybox"
        }
        Cull  Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Structures ────────────────────────────────────

            struct appdata { float4 vertex : POSITION; };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            // ── Propriétés ────────────────────────────────────

            float  _StarBrightness;
            float  _NebulaIntensity;
            float4 _NebulaColor;

            // ── Hash utilitaires ─────────────────────────────

            /// Hash 2D → float2, distribution quasi-uniforme [0,1]².
            float2 hash2(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            /// Hash 2D → float, distribution [0,1].
            float hash1(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // ── Couche d'étoiles ─────────────────────────────

            /// Génère un champ d'étoiles sur une grille UV de densité `scale`.
            /// `offset` décale les coordonnées pour que chaque couche soit indépendante.
            float starLayer(float2 uv, float scale, float2 offset)
            {
                float2 scaled = uv * scale + offset;
                float2 cellId = floor(scaled);
                float2 cellF  = frac(scaled);

                float result = 0.0;

                // Vérifie les 9 cellules voisines pour éviter les artefacts aux bords
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 n   = float2(i, j);
                    float2 rng = hash2(cellId + n);

                    // ~35 % des cellules contiennent une étoile
                    float present = step(0.65, rng.x);

                    // Vecteur de la position courante vers le centre de l'étoile
                    float2 toStar = n + rng - cellF;
                    float  dist   = length(toStar);

                    // Rayon très petit : étoiles en pointes acérées (10× plus petit qu'avant)
                    float radius = lerp(0.003, 0.012, hash1(cellId + n + float2(0.5, 0.5)));

                    // Gaussienne serrée : luminosité maximale au centre, chute TRÈS rapide.
                    // k élevé (9) = étoile quasi-ponctuelle, pas de halo flou.
                    float k      = 9.0;
                    float bright = (0.55 + 0.45 * rng.y)
                                 * exp(-dist * dist * k / (radius * radius));

                    result += present * bright;
                }

                return result;
            }

            // ── Vertex ───────────────────────────────────────

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;   // direction non normalisée — normalisée en frag
                return o;
            }

            // ── Fragment ──────────────────────────────────────

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // ── Projection sphérique → UV [0,1]² ─────────
                const float PI = 3.14159265359;
                float phi   = atan2(d.z, d.x);                    // [-π, π]
                float theta = asin(clamp(d.y, -0.9999, 0.9999));  // [-π/2, π/2]

                float2 uv = float2(phi * (0.5 / PI) + 0.5,        // [0, 1]
                                   theta / PI + 0.5);              // [0, 1]

                // ── 4 couches d'étoiles (densités et offsets différents) ──

                // Couche 1 : grandes étoiles lumineuses (rare)
                float stars  = starLayer(uv, 22.0, float2( 0.00,  0.00));
                // Couche 2 : étoiles moyennes
                stars       += starLayer(uv, 42.0, float2( 3.71,  1.93)) * 0.65;
                // Couche 3 : petites étoiles (très nombreuses)
                stars       += starLayer(uv, 68.0, float2( 8.32,  5.17)) * 0.45;
                // Couche 4 : poussière stellaire (quasi-points)
                stars       += starLayer(uv, 98.0, float2(14.70,  9.82)) * 0.28;

                stars = saturate(stars * _StarBrightness);

                // ── Légère nébuleuse en arrière-plan ─────────
                // Bande douce simulant la Voie Lactée
                float y     = uv.y - 0.5;
                float band  = exp(-y * y * 60.0);
                float n1    = sin(uv.x * 12.7 + 1.3) * 0.5 + 0.5;
                float n2    = sin(uv.x * 27.4 - 0.8) * 0.5 + 0.5;
                float nebula = band * (0.5 * n1 + 0.3 * n2 + 0.2) * _NebulaIntensity;

                // ── Assemblage de la couleur finale ──────────
                float3 skyCol   = _NebulaColor.rgb + float3(0.03, 0.015, 0.06) * nebula;
                // Légère variation de teinte : étoiles chaudes (jaune) vs froides (bleu)
                float  warmCold = hash1(floor(uv * 98.0 + float2(14.70, 9.82)));
                float3 starTint = lerp(float3(1.0, 0.92, 0.78), float3(0.80, 0.90, 1.00), warmCold);
                float3 col      = skyCol + starTint * stars;

                return fixed4(col, 1.0);
            }

            ENDCG
        }
    }
}
