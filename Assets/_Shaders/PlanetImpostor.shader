// ============================================================
//  PlanetImpostor.shader
//  Sphère d'impostor de planète lointaine.
//  Rendu translucide avec léger fresnel pour faire apparaître
//  un volume sans masquer le ciel/étoiles derrière.
//  Pas d'éclairage dynamique : un seul terme couleur + fresnel.
// ============================================================

Shader "AstroVoxel/PlanetImpostor"
{
    Properties
    {
        _Color        ("Tint",          Color)        = (1, 1, 1, 0.35)
        _RimColor     ("Rim Color",     Color)        = (1, 1, 1, 1)
        _RimPower     ("Rim Power",     Range(0.5, 8)) = 2.0
        _RimIntensity ("Rim Intensity", Range(0, 2))   = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue"      = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        ZWrite Off
        Cull   Back
        Blend  SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 worldN  : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            float4 _Color;
            float4 _RimColor;
            float  _RimPower;
            float  _RimIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.worldN = UnityObjectToWorldNormal(v.normal);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float ndv  = saturate(dot(normalize(i.worldN), normalize(i.viewDir)));
                float rim  = pow(1.0 - ndv, _RimPower) * _RimIntensity;
                fixed3 rgb = _Color.rgb + _RimColor.rgb * rim;
                // L'alpha augmente sur le limbe pour suggérer un volume
                float a    = saturate(_Color.a + rim * 0.5);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
