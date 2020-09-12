Shader "Volumetric/VolumetricRenderer"
{
    SubShader
    {
        Blend One Zero
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
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

            float4 _ScreenQuadCorners[3];

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

                float4 color = float4(viewDirWS, 1);
                //float4 color = float4(IN.uv, 0, 1);
                return color;
            }
            ENDHLSL
        }
    }
}
