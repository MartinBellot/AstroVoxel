// ============================================================
//  ThrusterParticle.shader
//  Shader additif pour les particules de tuyère du vaisseau.
//  • Blend One One = additif pur (glow sans obscurcissement).
//  • Couleur vertex transmise par le ParticleSystem.
//  • Compatible URP et Built-in via CGPROGRAM/UnityCG.cginc.
//  • Garanti inclus dans le build (référencé par le vaisseau).
// ============================================================
Shader "AstroVoxel/ThrusterParticle"
{
    Properties
    {
        _MainTex   ("Particle Texture", 2D)    = "white" {}
        [HDR]
        _TintColor ("Tint Color",       Color) = (1, 0.55, 0.1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
        }

        ZWrite Off
        Cull   Off
        Blend  One One   // additif pur — s'additionne sans obscurcir

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   2.0
            #pragma multi_compile_particles
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _TintColor;

            struct appdata_t
            {
                float4 vertex   : POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // Couleur particule × teinte globale (× 2 pour la plage HDR)
                o.color    = v.color * _TintColor * 2.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                return tex * i.color;
            }
            ENDCG
        }
    }

    // ── Fallback minimal si le Pass CGPROGRAM échoue ──────────
    Fallback Off
}
