// Einfache, billige Beleuchtung fuer Einband/Buchblock/Cursor (Lambert + Umgebung), Single-Pass-Stereo-tauglich.
Shader "PFD/SimpleLit"
{
    Properties
    {
        _Color ("Farbe", Color) = (1,1,1,1)
        _MainTex ("Textur", 2D) = "white" {}
        _Ambient ("Umgebung", Range(0,1)) = 0.55
        _Stripes ("Papierkanten-Streifen", Range(0,1)) = 0
        _StripeFreq ("Streifendichte", Float) = 400
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; float2 uv : TEXCOORD1; float3 os : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float4 _MainTex_ST; float _Ambient; float _Stripes; float _StripeFreq;
            CBUFFER_END
            V vert (A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p = TransformObjectToHClip(v.p.xyz); o.n = TransformObjectToWorldNormal(v.n);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.os = v.p.xyz; return o;
            }
            half4 frag (V i) : SV_Target
            {
                float3 l = normalize(float3(0.3, 0.9, -0.4));
                float d = saturate(dot(normalize(i.n), l));
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                if (_Stripes > 0) { float s = frac(i.os.y * _StripeFreq); c.rgb *= 1 - _Stripes * smoothstep(0.4, 0.5, s) * smoothstep(0.6, 0.5, s); }
                c.rgb *= _Ambient + (1 - _Ambient) * d;
                return c;
            }
            ENDHLSL
        }
    }
}
