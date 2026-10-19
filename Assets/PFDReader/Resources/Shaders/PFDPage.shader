// Buchseite: unbeleuchtete Textur (lesbar, farbtreu) mit sanfter Schattierung aus Woelbung und Rueckseiten-Durchschein.
Shader "PFD/Page"
{
    Properties
    {
        _MainTex ("Seite", 2D) = "white" {}
        _Tint ("Papier-Tint", Color) = (1,1,1,1)
        _Shade ("Schattierung", Range(0,1)) = 0.35
        _EdgeDark ("Bundsteg-Schatten", Range(0,1)) = 0.25
        _SpineSide ("Bund links(0)/rechts(1)", Float) = 0
        _Mirror ("U spiegeln", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _ContentRect ("Inhaltsbereich (u0,v0,u1,v1)", Vector) = (0,0,1,1)
        _Paper ("Papierfarbe Rand", Color) = (0.97,0.95,0.9,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 viewWS : TEXCOORD2; float2 raw : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _Tint; float _Shade; float _EdgeDark; float _SpineSide; float _Mirror; float4 _ContentRect; float4 _Paper;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                float2 uv = v.uv; if (_Mirror > 0.5) uv.x = 1 - uv.x;
                o.raw = uv;
                o.uv = uv;
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                // Inhalt in den Seitenbereich einpassen (Rand/Bundsteg bleiben Papier), danach Zoom-Ausschnitt
                float2 cuv = (i.raw - _ContentRect.xy) / max(_ContentRect.zw - _ContentRect.xy, 1e-4);
                float2 tuv = cuv * _MainTex_ST.xy + _MainTex_ST.zw;
                half4 c = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, tuv, ddx(tuv), ddy(tuv)) * _Tint;
                if (any(cuv < 0) || any(cuv > 1)) c = _Paper;
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                float3 vdir = normalize(i.viewWS);
                // Licht schraeg von oben/vorne: gibt der gewoelbten Seite Form, ohne die Lesbarkeit zu beeinflussen
                float3 l = normalize(float3(0.2, 0.8, 0.0) + vdir);
                float lambert = saturate(dot(n, l)) * 0.5 + 0.5;
                float shade = lerp(1, lambert, _Shade);
                // dezenter Schatten am Bund
                float u = _SpineSide > 0.5 ? 1 - i.raw.x : i.raw.x;
                if (_Mirror > 0.5) u = 1 - u;
                shade *= 1 - _EdgeDark * pow(saturate(1 - u * 12), 2);
                c.rgb *= shade;
                return c;
            }
            ENDHLSL
        }
    }
}
