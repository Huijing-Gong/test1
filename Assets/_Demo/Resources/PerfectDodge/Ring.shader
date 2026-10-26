Shader "Combat/Focus Ring" {
SubShader { Tags {"RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline"}
Pass { Blend SrcAlpha One ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};
V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.c=a.c;return o;}
half4 frag(V i):SV_Target{return half4(i.c.rgb*1.5,i.c.a*pow(saturate(1-abs(i.uv.y*2-1)),.6));}
ENDHLSL
}}}
