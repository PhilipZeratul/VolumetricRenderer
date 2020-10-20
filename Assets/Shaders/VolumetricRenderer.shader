Shader "Volumetric/VolumetricRenderer"
{
    SubShader
    {
        Pass
        {
            Blend One Zero
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
            
            struct Attributes
            {
                float3 vertex : POSITION;
                uint id : SV_VertexID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
            TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);

            float4 _ScreenQuadCorners[3];
            int _MaxSteps;
            float _MaxDistance;

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.position = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.uv = o.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                o.viewDir = _ScreenQuadCorners[v.id].xyz;

                return o;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float3 viewDirWS = IN.viewDir;

                float depth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, IN.uv).r;
                float viewDepth = LinearEyeDepth(depth);

                float3 currentPos = _WorldSpaceCameraPos.xyz;
                float stepDist = _MaxDistance / _MaxSteps;
                float accumDist = 0.0;
                
                for (int i = 0; i < _MaxSteps && accumDist < viewDepth; i++)
                {
                    currentPos += stepDist * viewDirWS;
                    accumDist += stepDist;
                }

                float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                float fog = exp(-accumDist * 0.3);
                float4 color = lerp(1, mainTex, fog);
                //float4 color = float4(IN.uv, 0, 1);
                return color;
            }
            ENDHLSL
        }

        Pass // Debug
        {
            Blend One Zero
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

            struct Attributes
            {
                float3 vertex : POSITION;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE3D_SAMPLER3D(_MaterialVolume_A, sampler_MaterialVolume_A);
            TEXTURE3D_SAMPLER3D(_MaterialVolume_B, sampler_MaterialVolume_B);
            TEXTURE3D_SAMPLER3D(_ScatterVolume, sampler_ScatterVolume);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.position = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.uv = o.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                return o;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE3D(_ScatterVolume, sampler_ScatterVolume, float3(IN.uv, 0));
                //color = 1;
                return color;
            }
            ENDHLSL
        }
    }
}
