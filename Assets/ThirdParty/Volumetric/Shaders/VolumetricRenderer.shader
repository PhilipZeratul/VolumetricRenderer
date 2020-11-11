Shader "Hidden/Volumetric/VolumetricRenderer"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
    }
    SubShader
    {
        CGINCLUDE

        #pragma enable_d3d11_debug_symbols

        struct appdata
        {
            uint id : SV_VertexID;
        };

        struct v2f
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 viewDir : TEXCOORD1;
        };

        float4 _ScreenQuadCorners[3];

        // Postprocessing https://www.reddit.com/r/gamedev/comments/2j17wk/a_slightly_faster_bufferless_vertex_shader_trick/
        v2f vert(appdata v)
        {
            v2f o;
            UNITY_INITIALIZE_OUTPUT(v2f, o);

            o.uv.x = (v.id == 2) ? 2.0 : 0.0;
            o.uv.y = (v.id == 1) ? 2.0 : 0.0;

            o.position = float4(o.uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 1.0, 1.0);

            #if UNITY_UV_STARTS_AT_TOP
                o.uv = o.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
            #endif

            o.viewDir = _ScreenQuadCorners[v.id].xyz;

            return o;
        }

        ENDCG

        Pass
        {
            Blend One Zero
            Cull Off
            ZTest Always
            ZWrite Off

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "VolumetricHelper.hlsl"

            UNITY_DECLARE_TEX2D(_MainTex);
            Texture3D<float4> _AccumulationVolumeSrv;

            int _MaxSteps;

            float4 frag(v2f IN) : SV_Target
            {
                float3 viewDirWS = IN.viewDir / IN.viewDir.z;

                float depth = _CameraDepthTexture.SampleLevel(sampler_bilinear_clamp, IN.uv, 0).r;
                float viewDepth = LinearEyeDepth(depth);

                float3 worldPos = viewDirWS * viewDepth + _WorldSpaceCameraPos.xyz;
                float3 froxelPos = WorldPos2FroxelPos(worldPos);

                float3 uvw = FroxelPos2FroxelUvw(froxelPos);
                float4 accumulationVolume = _AccumulationVolumeSrv.Sample(sampler_bilinear_clamp, uvw);

                float3 accumuLight = accumulationVolume.rgb;
                float totalTransmittance = accumulationVolume.a;

                float4 mainTex = UNITY_SAMPLE_TEX2D(_MainTex, IN.uv);
                float4 color = 1;
                color.rgb = mainTex * totalTransmittance + accumuLight;

                //color.rgb = accumuLight;

                return color;
            }
            ENDCG
        }

        Pass // Debug
        {
            Blend One Zero
            Cull Off
            ZTest Always
            ZWrite Off

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2D(_MainTex);

            UNITY_DECLARE_TEX3D(_ShadowVolume);
            UNITY_DECLARE_TEX3D(_MaterialVolume_A);
            UNITY_DECLARE_TEX3D(_MaterialVolume_B);
            UNITY_DECLARE_TEX3D(_ScatterVolume);
            UNITY_DECLARE_TEX2D(_AccumulationVolume);

            float4 frag(v2f IN) : SV_Target
            {
                float4 mainTex = UNITY_SAMPLE_TEX2D(_MainTex, IN.uv);
                float4 color = UNITY_SAMPLE_TEX2D(_AccumulationVolume, IN.uv);
                //color = lerp(mainTex, color, color.a);
                color.rgb = mainTex * color.a + color.rgb;
                //color.rgb = color.aaa;
                color.a = 1;
                return color;
            }
            ENDCG
        }
    }
}
