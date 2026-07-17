// URP-compatible rewrite — was a surface shader (#pragma surface) which does not work in URP
// Transparent variant: unlit emission, alpha from _Color.a, Cull Off
// Stereo macros included for Quest Single Pass Instanced VR

Shader "Custom/SurfaceVertexColorTransparent" {
    Properties {
        _Color ("Color", Color) = (1, 1, 1, 0.1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0.0

        [Toggle] _UseFog ("Enable fog", Float) = 0.0
        _FogStart ("Fog start", Float) = 0.0
        _FogDensity ("Fog density", Float) = 0.5
        [Toggle] _LimitedView ("Enable limited view", Float) = 0.0
        _LimitedViewRadius ("Limited view Radius", Float) = 10.0
        _LimitedViewCenter ("Limited view Center", Vector) = (0.0, 0.0, 0.0)
    }

    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float  _Glossiness;
            float  _Metallic;
            float  _UseFog;
            float  _FogStart;
            float  _FogDensity;
            float  _LimitedView;
            float  _LimitedViewRadius;
            float4 _LimitedViewCenter;
        CBUFFER_END
        ENDHLSL

        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 objPos      : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v) {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionHCS = posInputs.positionCS;
                o.uv          = TRANSFORM_TEX(v.uv, _MainTex);
                o.objPos      = v.positionOS.xyz;
                o.positionWS  = posInputs.positionWS;
                o.color       = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                UNITY_SETUP_INSTANCE_ID(i);

                if (_LimitedView > 0) {
                    if (distance(i.objPos, _LimitedViewCenter.xyz) > _LimitedViewRadius)
                        clip(-1);
                }

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 emission = texColor.rgb * _Color.rgb * i.color.rgb;

                if (_UseFog > 0) {
                    float fogFactor = exp(_FogStart - i.positionWS.z / max(0.0001, _FogDensity));
                    emission = lerp(unity_FogColor.rgb, emission, saturate(fogFactor));
                }

                return half4(emission, _Color.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
