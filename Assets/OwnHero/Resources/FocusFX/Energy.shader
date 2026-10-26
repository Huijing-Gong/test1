Shader "Hero/Link Time Energy" {
Properties { _Tint("Tint",Color)=(.2,.8,1,1) }
SubShader {Tags{"Queue"="Transparent+30" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
Pass {Blend SrcAlpha One ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _Tint;
CBUFFER_END
struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.color=a.color;return o;}
half4 frag(V i):SV_Target{float core=pow(saturate(1-abs(i.uv.y*2-1)),1.5);return half4(_Tint.rgb*i.color.rgb*(1.8+core*2),i.color.a*_Tint.a*core);}
ENDHLSL
}}}
