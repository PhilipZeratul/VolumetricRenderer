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
        };

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

            UNITY_DECLARE_TEX2D(_MainTex);
            UNITY_DECLARE_TEX2D(_CameraDepthTexture);

            float4 _ScreenQuadCorners[3];
            int _MaxSteps;
            float _MaxDistance;

            float4 frag(v2f IN) : SV_Target
            {
                /*float3 viewDirWS = IN.viewDir;

                float depth = UNITY_SAMPLE_TEX2D(_CameraDepthTexture, IN.uv).r;
                float viewDepth = LinearEyeDepth(depth);

                float3 currentPos = _WorldSpaceCameraPos.xyz;
                float stepDist = _MaxDistance / _MaxSteps;
                float accumDist = 0.0;
                
                for (int i = 0; i < _MaxSteps && accumDist < viewDepth; i++)
                {
                    currentPos += stepDist * viewDirWS;
                    accumDist += stepDist;
                }

                float4 mainTex = UNITY_SAMPLE_TEX2D(_MainTex, IN.uv);

                float fog = exp(-accumDist * 0.3);
                float4 color = lerp(1, mainTex, fog);*/
                float4 color = float4(IN.uv, 0, 1);
                
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
            UNITY_DECLARE_TEX2D(_AccumulationTex);

            float4 frag(v2f IN) : SV_Target
            {
                float4 mainTex = UNITY_SAMPLE_TEX2D(_MainTex, IN.uv);
                float4 color = UNITY_SAMPLE_TEX2D(_AccumulationTex, IN.uv);
                //color = lerp(mainTex, color, color.a);
                //color = mainTex;
                return color;
            }
            ENDCG
        }
    }
}
