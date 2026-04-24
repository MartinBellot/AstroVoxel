// ============================================================
//  SunCorona.shader
//  Halo de couronne solaire : sphère transparente additive.
//  Effet Fresnel = brillant sur les bords, invisible au centre.
//  Compatible URP via CGPROGRAM/UnityCG.cginc.
// ============================================================

Shader "AstroVoxel/SunCorona"
{
    Properties
    {
        [HDR] _CoronaColor  ("Corona Color (HDR)",  Color)         = (2.2, 0.55, 0.03, 1)
        [HDR] _InnerColor   ("Inner Glow (HDR)",    Color)         = (1.5, 1.2, 0.4, 1)
        _FresnelPower        ("Fresnel Power",       Range(1, 10))  = 4.0
        _Intensity           ("Intensity",           Range(0, 6))   = 2.5
        _PulseSpeed          ("Pulse Speed",         Range(0, 3))   = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue"      = "Transparent"
            "RenderType" = "Transparent"
        }
        ZWrite  Off
        Cull    Off          // visible depuis l'intérieur ET l'extérieur
        Blend   One One      // additif : s'additionne à la couleur de fond

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
                float4 pos      : SV_POSITION;
                float3 worldN   : TEXCOORD0;
                float3 viewDir  : TEXCOORD1;
            };

            // ── Propriétés ────────────────────────────────────

            float4 _CoronaColor, _InnerColor;
            float  _FresnelPower, _Intensity, _PulseSpeed;

            // ── Vertex ────────────────────────────────────────

            v2f vert(appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.worldN  = UnityObjectToWorldNormal(v.normal);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - wp);
                return o;
            }

            // ── Fragment ─────────────────────────────────────

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.viewDir);

                // Fresnel : 0 au centre du disque, 1 sur les bords
                float ndotv   = abs(dot(N, V));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                // Inner glow : lueur légère même au centre du disque
                float inner = pow(1.0 - ndotv, _FresnelPower * 0.3) * 0.25;

                // Légère pulsation
                float pulse = sin(_Time.y * _PulseSpeed) * 0.07 + 1.0;

                float3 col = (_CoronaColor.rgb * fresnel + _InnerColor.rgb * inner)
                           * _Intensity * pulse;

                return fixed4(col, saturate(fresnel + inner));
            }

            ENDCG
        }
    }
}
