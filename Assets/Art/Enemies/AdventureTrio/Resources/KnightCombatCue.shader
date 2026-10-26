Shader "Enemies/KnightCombatCue"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 vertex:POSITION; half4 color:COLOR; };
            struct V { float4 position:SV_POSITION; half4 color:COLOR; half fog:TEXCOORD0; };
            V Vert(A i) { V o; o.position=TransformObjectToHClip(i.vertex.xyz); o.color=i.color; o.fog=ComputeFogFactor(o.position.z); return o; }
            half4 Frag(V i):SV_Target { return half4(MixFog(i.color.rgb,i.fog),i.color.a); }
            ENDHLSL
        }
    }
}
