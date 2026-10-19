// Ledereinband: feine Narbung (prozedurale Normal-/Hoehentextur, Koordinaten in Metern), weicher Glanz,
// optionale Goldpraegung aus einer Maske (Titel/Autor, Ruecken) plus Zierlinien. Single-Pass-Stereo-tauglich.
Shader "PFD/Leather"
{
    Properties
    {
        _Color ("Leder", Color) = (0.1,0.18,0.22,1)
        _GrainTex ("Narbung (RG Normal, B Hoehe)", 2D) = "bump" {}
        _Tiling ("Kacheln pro Meter", Float) = 22
        _Bump ("Narbungstiefe", Range(0,2)) = 0.9
        _Ambient ("Umgebung", Range(0,1)) = 0.42
        _Sheen ("Glanz", Range(0,1)) = 0.35
        _DecalTex ("Praegung (Alpha)", 2D) = "black" {}
        _Gold ("Gold", Color) = (0.86,0.70,0.40,1)
        _DecalMode ("0 aus, 1 Deckel, 2 Ruecken", Float) = 0
        _DecalFlip ("Praegung: RT oben beginnt (D3D/Vulkan)", Float) = 0
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 p : SV_POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1;
                float3 n : TEXCOORD2; float3 t : TEXCOORD3; float3 b : TEXCOORD4; float3 wp : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            TEXTURE2D(_GrainTex); SAMPLER(sampler_GrainTex);
            TEXTURE2D(_DecalTex); SAMPLER(sampler_DecalTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float4 _GrainTex_ST; float _Tiling; float _Bump; float _Ambient; float _Sheen;
                float4 _DecalTex_ST; float4 _Gold; float _DecalMode; float _DecalFlip; float _Cull;
            CBUFFER_END
            V vert (A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(v.p.xyz);
                o.p = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(v.n);
                o.t = TransformObjectToWorldDir(v.t.xyz);
                o.b = cross(o.n, o.t) * v.t.w * GetOddNegativeScale();
                o.uv = v.uv * _Tiling; o.uv1 = v.uv1;
                return o;
            }
            float Line(float x, float at, float w) { return saturate(1 - abs(x - at) / w); }
            half4 frag (V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 g = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, i.uv);
                float3 N = normalize(i.n), T = normalize(i.t), B = normalize(i.b);
                if (!front) { N = -N; }
                // Praegemaske
                float gold = 0;
                float2 d = i.uv1;
                bool inside = d.x >= 0 && d.x <= 1 && d.y >= 0 && d.y <= 1;
                if (_DecalMode > 0.5 && inside)
                {
                    // Lage der RenderTexture haengt von der Grafik-API ab (_DecalFlip: Zeile 0 oben)
                    float2 du = float2(d.x, _DecalFlip > 0.5 ? 1 - d.y : d.y);
                    if (_DecalMode > 1.5) du = float2(d.y, _DecalFlip > 0.5 ? 1 - d.x : d.x);  // Ruecken: von unten nach oben
                    gold = saturate(SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, du).a * 1.6);
                    if (_DecalMode < 1.5)
                    {
                        // feiner Goldrahmen + blindgepraegte Innenlinie (modern, schlicht)
                        float fx = min(d.x, 1 - d.x), fy = min(d.y, 1 - d.y) * 1.33;
                        float f = min(fx, fy);
                        gold = max(gold, Line(f, 0.055, 0.0035));
                    }
                    else
                    {
                        float y = d.y;
                        gold = max(gold, max(Line(y, 0.07, 0.003), Line(y, 0.93, 0.003)));
                        gold = max(gold, max(Line(y, 0.085, 0.0015), Line(y, 0.915, 0.0015)));
                    }
                }
                // Narbung: im Gold glatter, Praegung leicht vertieft
                float bump = _Bump * (1 - gold * 0.8);
                float2 nxy = (g.rg * 2 - 1) * bump;
                float3 n = normalize(T * nxy.x + B * nxy.y + N * sqrt(saturate(1 - dot(nxy, nxy)) + 0.02));
                float3 l = normalize(float3(0.3, 0.9, -0.4));
                float3 v = normalize(GetWorldSpaceViewDir(i.wp));
                float3 h = normalize(l + v);
                float diff = saturate(dot(n, l)) * 0.85 + 0.15 * saturate(dot(n, v));
                float3 leather = _Color.rgb * (0.82 + 0.36 * g.b);
                float3 col = leather * (_Ambient + (1 - _Ambient) * diff);
                // Leder: breiter, weicher Glanz + Fresnel-Saum
                float spec = pow(saturate(dot(n, h)), 18) * _Sheen * (0.6 + 0.4 * g.b);
                float fres = pow(1 - saturate(dot(N, v)), 4) * 0.18;
                col += spec * float3(1, 0.97, 0.92) * 0.6 + fres * _Color.rgb * 2;
                // Gold: metallisch, staerkerer Glanz in Goldfarbe
                float3 gc = _Gold.rgb * (0.55 + 0.45 * diff) + pow(saturate(dot(n, h)), 40) * _Gold.rgb * 1.4 + _Gold.rgb * 0.25 * saturate(dot(n, v));
                col = lerp(col, gc, gold);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
