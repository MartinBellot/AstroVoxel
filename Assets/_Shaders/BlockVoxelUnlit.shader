// ============================================================
//  BlockVoxelUnlit.shader
//  Shader minimaliste pour les blocs voxel.
//  • Rendu plat (Unlit) — texture seule, sans calcul de lumière PBR.
//  • Compatible URP (SubShader 0) et Built-In RP (SubShader 1).
//  • Passes ShadowCaster + DepthOnly pour URP inclus.
// ============================================================
Shader "AstroVoxel/BlockUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap  ("Texture", 2D)      = "white" {}
        [MainColor]   _BaseColor("Color", Color)      = (1,1,1,1)
        // Propriétés cachées requises par UnlitInput.hlsl / les passes URP
        [HideInInspector] _Cutoff  ("AlphaCutout", Range(0,1)) = 0.5
        [HideInInspector] _Surface ("__surface",   Float)      = 0.0
        [HideInInspector] _Cull   ("__cull",       Float)      = 2.0
    }

    // ─── SubShader URP ────────────────────────────────────────
    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── Pass principal : Forward + réception des ombres ────
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // Variantes d'ombres URP (cascades, screen-space)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW

            // UnlitInput.hlsl → CBUFFER + TEXTURE2D(_BaseMap) + SAMPLER
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            // RealtimeLights.hlsl → GetMainLight(shadowCoord) + TransformWorldToShadowCoord
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;   // position monde pour le calcul d'ombres
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                // ── Réception des ombres de la lumière principale ──
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light  mainLight   = GetMainLight(shadowCoord);
                    // 0.35 = luminosité minimum dans l'ombre (ambiance)
                    col.rgb *= lerp(0.35h, 1.0h, mainLight.shadowAttenuation);
                #endif

                return col * _BaseColor;
            }
            ENDHLSL
        }

        // ── ShadowCaster ───────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing

            // UnlitInput.hlsl déclare le CBUFFER UnityPerMaterial complet
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ── DepthOnly ──────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    // ─── SubShader Built-In RP (repli) ────────────────────────
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            fixed4    _BaseColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_BaseMap, i.uv) * _BaseColor;
            }
            ENDCG
        }
    }
}
