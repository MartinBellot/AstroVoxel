// ============================================================
//  SunSurface.shader
//  Surface du soleil : limb darkening + granulation animée.
//  Unlit, opaque, couleurs HDR (> 1) pour le bloom URP.
// ============================================================

Shader "AstroVoxel/SunSurface"
{
    Properties
    {
        [HDR] _CoreColor         ("Core Color (HDR)",       Color)         = (4.0, 3.5, 2.0, 1)
        [HDR] _LimbColor         ("Limb Color (HDR)",       Color)         = (2.5, 0.6, 0.02, 1)
        _LimbDarkening           ("Limb Darkening Power",   Range(0.1, 2)) = 0.45
        _GranulationStrength     ("Granulation Strength",   Range(0, 0.5)) = 0.18
        _GranulationScale        ("Granulation Scale",      Range(0.001, 0.05)) = 0.012
        _PulseSpeed              ("Pulse Speed",            Range(0, 3))   = 0.45
    }

    SubShader
    {
        Tags
        {
            "Queue"      = "Geometry"
            "RenderType" = "Opaque"
        }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Structures ────────────────────────────────────

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float3 viewDir     : TEXCOORD2;
            };

            // ── Propriétés ────────────────────────────────────

            float4 _CoreColor, _LimbColor;
            float  _LimbDarkening, _GranulationStrength, _GranulationScale, _PulseSpeed;

            // ── Vertex ────────────────────────────────────────

            v2f vert(appdata v)
            {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir     = normalize(_WorldSpaceCameraPos - o.worldPos);
                return o;
            }

            // ── Fragment ─────────────────────────────────────

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 V = normalize(i.viewDir);

                // ── Limb darkening ────────────────────────────
                // ndotv ≈ 1 au centre du disque, ≈ 0 sur le bord
                float ndotv = saturate(dot(N, V));
                float limb  = pow(ndotv, _LimbDarkening);

                // ── Granulation solaire (bruit sin multi-octaves) ─
                float3 p = i.worldPos * _GranulationScale;
                float  t = _Time.y * _PulseSpeed;

                // Octave 1 : grandes cellules de convection
                float g1 = sin(p.x * 8.31 + t * 0.90)
                         * cos(p.y * 9.73 - t * 0.65)
                         * sin(p.z * 7.17 + t * 0.80);

                // Octave 2 : détail fin (filaments)
                float g2 = sin(p.x * 17.24 - t * 1.30)
                         * cos(p.z * 15.82 + t * 1.10)
                         * sin(p.y * 13.51 - t * 0.95);

                // Octave 3 : micro-granules
                float g3 = sin(p.x * 31.60 + t * 2.10)
                         * cos(p.y * 28.40 - t * 1.85);

                float gran = (g1 * 0.55 + g2 * 0.30 + g3 * 0.15) * 0.5 + 0.5;

                // ── Couleur de base ───────────────────────────
                float3 col = lerp(_LimbColor.rgb, _CoreColor.rgb, limb);

                // Ajoute la granulation sur la zone centrale (estompe sur le bord)
                col += (gran - 0.5) * _GranulationStrength * _CoreColor.rgb * limb;

                // ── Légère pulsation globale ──────────────────
                float pulse = sin(_Time.y * _PulseSpeed * 1.8) * 0.04 + 1.0;
                col *= pulse;

                return fixed4(col, 1.0);
            }

            ENDCG
        }
    }
}
