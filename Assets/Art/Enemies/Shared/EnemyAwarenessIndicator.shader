Shader "Enemies/AwarenessIndicator"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 vertex:POSITION; half4 color:COLOR; };
            struct V { float4 position:SV_POSITION; half4 color:COLOR; };
            V Vert(A i) { V o; o.position=TransformObjectToHClip(i.vertex.xyz); o.color=i.color; return o; }
            half4 Frag(V i):SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
