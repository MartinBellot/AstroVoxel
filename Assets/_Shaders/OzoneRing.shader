// ============================================================
//  OzoneRing.shader
//  Anneau de la couche d'ozone : sphère additive cyan-bleutée.
//  Visible depuis l'espace (limbe planétaire) ET depuis l'intérieur
//  (anneau brillant quand le joueur traverse la frontière).
//
//  Blend One One = additif pur → s'additionne sans obscurcir.
// ============================================================

Shader "AstroVoxel/OzoneRing"
{
    Properties
    {
        [HDR] _GlowColor  ("Ozone Glow Color", Color)        = (0.15, 0.55, 1.8, 1)
        _FresnelPower      ("Fresnel Power",    Range(1, 10)) = 3.5
        _Intensity         ("Intensity",        Range(0, 4))  = 1.6
    }

    SubShader
    {
        Tags
        {
            "Queue"      = "Transparent+2"
            "RenderType" = "Transparent"
        }
        ZWrite  Off
        Cull    Off
        Blend   One One      // additif pur

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

            float4 _GlowColor;
            float  _FresnelPower, _Intensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.worldN  = UnityObjectToWorldNormal(v.normal);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.viewDir);

                // abs(dot) pour que la couche soit visible des deux côtés (Cull Off)
                float ndotv  = abs(dot(N, V));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                float3 col = _GlowColor.rgb * fresnel * _Intensity;
                return fixed4(col, 1.0);
            }

            ENDCG
        }
    }
}
