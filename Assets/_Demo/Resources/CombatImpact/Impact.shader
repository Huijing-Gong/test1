Shader "Combat/Player Contact" {
SubShader { Tags {"RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline"}
Pass { Blend SrcAlpha One ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 c:COLOR;};
V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.c=a.c;return o;}
half4 frag(V i):SV_Target {float2 p=abs(i.uv*2-1);float core=exp(-dot(p,p)*20);float rays=pow(saturate(1-p.x),32)*pow(saturate(1-p.y),2)+pow(saturate(1-p.y),32)*pow(saturate(1-p.x),2);return half4(i.c.rgb*1.7,i.c.a*saturate(core+rays*.7));}
ENDHLSL
}}}
