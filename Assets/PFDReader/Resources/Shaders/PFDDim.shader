// Umgebungsdimmer: schwarze Flaeche vor Raum/Panorama/Passthrough, hinter Buch und Menues.
Shader "PFD/Dim"
{
    Properties { _Amount ("Abdunkelung", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+450" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial) float _Amount; CBUFFER_END
            V vert (A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.p = TransformObjectToHClip(v.p.xyz); return o; }
            half4 frag (V i) : SV_Target { return half4(0, 0, 0, _Amount); }
            ENDHLSL
        }
    }
}
