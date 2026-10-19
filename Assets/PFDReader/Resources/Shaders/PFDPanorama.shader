// 360°-Panorama (equirektangular), mono oder Stereo Top/Bottom (oben = linkes Auge). Innen auf einer Kugel um den Kopf.
Shader "PFD/Panorama"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "black" {}
        _Stereo ("Stereo Top/Bottom", Float) = 0
        _Swap ("Augen tauschen", Float) = 0
        _Yaw ("Drehung (Grad)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial) float4 _MainTex_ST; float _Stereo; float _Swap; float _Yaw; CBUFFER_END
            V vert (A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p = TransformObjectToHClip(v.p.xyz);
                o.dir = v.p.xyz;
                return o;
            }
            half4 frag (V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float lon = atan2(d.x, d.z) + radians(_Yaw);
                float lat = asin(clamp(d.y, -1, 1));
                float2 uv = float2(frac(lon / (2 * PI) + 0.5), lat / PI + 0.5);
                if (_Stereo > 0.5)
                {
                    float eye = unity_StereoEyeIndex;
                    if (_Swap > 0.5) eye = 1 - eye;
                    uv.y = uv.y * 0.5 + (eye < 0.5 ? 0.5 : 0.0);
                }
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
            }
            ENDHLSL
        }
    }
}
