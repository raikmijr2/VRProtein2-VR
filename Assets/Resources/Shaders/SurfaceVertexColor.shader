// URP-compatible rewrite — was a surface shader (#pragma surface) which does not work in URP
// Stereo macros (UNITY_VERTEX_OUTPUT_STEREO etc.) are required for Quest Single Pass Instanced VR

Shader "Custom/SurfaceVertexColor" {
    Properties {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _AOIntensity ("AO Intensity", Range(0, 20.0)) = 0

        [Toggle] _UseFog ("Enable fog", Float) = 0.0
        _FogStart ("Fog start", Float) = 0.0
        _FogDensity ("Fog density", Float) = 0.5
        [Toggle] _LimitedView ("Enable limited view", Float) = 0.0
        _LimitedViewRadius ("Limited view Radius", Float) = 10.0
        _LimitedViewCenter ("Limited view Center", Vector) = (0.0, 0.0, 0.0)
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float  _Glossiness;
            float  _Metallic;
            float  _AOIntensity;
            float  _UseFog;
            float  _FogStart;
            float  _FogDensity;
            float  _LimitedView;
            float  _LimitedViewRadius;
            float4 _LimitedViewCenter;
        CBUFFER_END
        ENDHLSL

        // ── ForwardLit ────────────────────────────────────────────────────
        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 aoCoord    : TEXCOORD1;  // x = ao factor
                float2 hideFlag   : TEXCOORD2;  // x > 1.5 → clip
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  aoFactor    : TEXCOORD1;
                float3 objPos      : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
                float4 color       : COLOR;
                float  hide        : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v) {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs posInputs  = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(v.normalOS);

                o.positionHCS = posInputs.positionCS;
                o.uv          = TRANSFORM_TEX(v.uv, _MainTex);
                o.aoFactor    = v.aoCoord.x;
                o.objPos      = v.positionOS.xyz;
                o.normalWS    = normInputs.normalWS;
                o.positionWS  = posInputs.positionWS;
                o.color       = v.color;
                o.hide        = v.hideFlag.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                UNITY_SETUP_INSTANCE_ID(i);

                if (i.hide > 1.5) clip(-1);

                if (_LimitedView > 0) {
                    if (distance(i.objPos, _LimitedViewCenter.xyz) > _LimitedViewRadius)
                        clip(-1);
                }

                half ao = 1.0;
                if (_AOIntensity > 0)
                    ao = saturate(i.aoFactor * _AOIntensity);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 albedo   = texColor.rgb * _Color.rgb * i.color.rgb * ao;

                half3 normalWS = normalize(i.normalWS);

                // Obtener dirección de luz sin shadow coord para que ndotl
                // siempre contribuya (shadowAttenuation=0 no anula el shading)
                Light mainLight  = GetMainLight();
                half  ndotl      = saturate(dot(normalWS, mainLight.direction));
                // Half-Lambert: wrap suave, nunca totalmente oscuro en caras traseras
                half  lambert    = ndotl * 0.5h + 0.5h;
                half3 lighting   = mainLight.color * mainLight.distanceAttenuation * lambert;

                // Sombras como modulación opcional (no anulan el diffuse base)
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                    half   shadowAtten = GetMainLight(shadowCoord).shadowAttenuation;
                    lighting *= lerp(0.35h, 1.0h, shadowAtten);
                #endif

                lighting += SampleSH(normalWS);

                half3 finalRGB = lighting * albedo;

                if (_UseFog > 0) {
                    float fogFactor = exp(_FogStart - i.positionWS.z / max(0.0001, _FogDensity));
                    finalRGB = lerp(unity_FogColor.rgb, finalRGB, saturate(fogFactor));
                }

                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }

        // ── ShadowCaster ─────────────────────────────────────────────────
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttribs {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttribs v) {
                ShadowVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 posWS    = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float3 shadowPos = ApplyShadowBias(posWS, normalWS, lightDir);
                o.positionCS = TransformWorldToHClip(shadowPos);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 ShadowFrag(ShadowVaryings i) : SV_TARGET { return 0; }
            ENDHLSL
        }

        // ── DepthOnly ─────────────────────────────────────────────────────
        Pass {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            struct DepthAttribs {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DepthVaryings {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttribs v) {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 DepthFrag(DepthVaryings i) : SV_TARGET { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
