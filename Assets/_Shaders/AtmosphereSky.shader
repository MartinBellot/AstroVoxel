// ============================================================
//  AtmosphereSky.shader
//  Sphère atmosphérique entourant la planète.
//
//  Depuis la surface (caméra à l'intérieur) : ciel bleu gradient
//  avec diffusion de Rayleigh simulée et lueur solaire à l'horizon.
//
//  Depuis l'espace (caméra à l'extérieur) : couronne bleue/blanche
//  visible sur le limbe de la planète.
//
//  Technique : Cull Off + VFACE pour détecter quelle face est rendue.
// ============================================================

Shader "AstroVoxel/AtmosphereSky"
{
    Properties
    {
        [HDR] _ZenithColor  ("Sky Zenith Color",  Color) = (0.04, 0.14, 0.52, 1)
        [HDR] _HorizonColor ("Sky Horizon Color", Color) = (0.46, 0.68, 0.98, 1)
        _NightFactor  ("Night Factor",   Range(0, 1)) = 0
        _SunDir       ("Sun Direction",  Vector)     = (1, 0, 0, 0)
        _Intensity    ("Intensity",      Range(0, 2)) = 1.0
        _PlanetCenter ("Planet Center",  Vector)     = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue"      = "Transparent+1"
            "RenderType" = "Transparent"
        }
        ZWrite  Off
        ZTest   LEqual
        Cull    Off
        Blend   SrcAlpha OneMinusSrcAlpha

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
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            // ── Propriétés ────────────────────────────────────

            float4 _ZenithColor, _HorizonColor;
            float  _NightFactor, _Intensity;
            float4 _SunDir;
            float4 _PlanetCenter;

            // ── Vertex ────────────────────────────────────────

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // ── Fragment ─────────────────────────────────────

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                // ── Repères ───────────────────────────────────────────────
                // camUp = "haut" planétaire depuis la caméra.
                // _PlanetCenter permet de gérer les planètes hors de l'origine.
                float3 camPos  = _WorldSpaceCameraPos;
                float3 camUp   = normalize(camPos - _PlanetCenter.xyz);
                // viewDir : du fragment vers la caméra
                float3 viewDir = normalize(camPos - i.worldPos);

                // facing > 0 → face avant (caméra OUTSIDE) → vue spatiale
                // facing < 0 → face arrière (caméra INSIDE) → vue surface
                bool insideAtmosphere = facing < 0.0;

                if (insideAtmosphere)
                {
                    // ── Ciel depuis la surface planétaire ────────────────────

                    // zenithDot : -1 quand viewDir pointe vers le bas de la sphère (zénith),
                    //              0 à l'horizon (tangente à la sphère).
                    float zenithDot = dot(viewDir, camUp);
                    // zenith01 : 0 = horizon, 1 = zénith (pour lerp)
                    float zenith01  = saturate(-zenithDot);

                    // Gradient bleu : clair (horizon) → foncé (zénith)
                    float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(zenith01, 0.55));

                    // Lueur solaire à l'horizon (diffusion de Rayleigh)
                    // Plus forte quand le soleil est proche de l'horizon (zenith01 faible)
                    float sunAlign      = max(0.0, dot(viewDir, _SunDir.xyz));
                    float horizonFactor = saturate(1.0 - zenith01 * 2.5);
                    // Aureole orange autour du soleil sur l'horizon
                    sky += float3(0.70, 0.28, 0.02) * pow(sunAlign, 6.0) * horizonFactor;
                    // Rougeur du coucher au niveau de l'horizon entier
                    sky += float3(0.40, 0.12, 0.01) * (1.0 - zenith01) * (1.0 - _NightFactor) * 0.18;

                    // Nuit : fondu vers noir quasi-total
                    float3 nightSky = float3(0.002, 0.003, 0.010);
                    sky = lerp(sky, nightSky, _NightFactor);

                    // Opacité : forte au zénith pour couvrir les étoiles le jour,
                    // très réduite la nuit pour les laisser apparaître.
                    float alpha = lerp(0.50, 0.96, pow(zenith01, 0.35)) * _Intensity;
                    alpha = lerp(alpha, alpha * 0.08, _NightFactor);

                    return fixed4(sky, alpha);
                }
                else
                {
                    // ── Vue depuis l'espace : couronne atmosphérique ──────────
                    float3 N      = normalize(i.worldPos - _PlanetCenter.xyz); // normal outward
                    float  ndotv  = abs(dot(N, viewDir));
                    float  fresnel = pow(1.0 - ndotv, 4.5);

                    float3 col   = _HorizonColor.rgb * 0.70;
                    float  alpha = fresnel * 0.55 * _Intensity * (1.0 - _NightFactor * 0.55);
                    return fixed4(col, alpha);
                }
            }

            ENDCG
        }
    }
}
