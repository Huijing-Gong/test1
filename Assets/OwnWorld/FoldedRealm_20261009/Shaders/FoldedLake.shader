Shader "KingOfGraphic/Own/FoldedLake" {
Properties {_BaseColor("Deep turquoise",Color)=(.015,.31,.45,1)}
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Off
Pass {Tags {"LightMode"="UniversalForward"} HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
float _OverworldEclipseDarkness;
struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half f:TEXCOORD1;};
V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.f=ComputeFogFactor(o.p.z);return o;}
half4 frag(V i):SV_Target {float t=_Time.y;float2 p=i.w.xz;float waves=sin(p.x*2.2+p.y*.7+t*1.0)+sin(p.y*3.1-p.x*.3-t*.7);float2 slope=float2(cos(p.x*2.2+p.y*.7+t)*.08,cos(p.y*3.1-p.x*.3-t*.7)*.1);float3 n=normalize(float3(slope.x,1,slope.y));float3 v=normalize(_WorldSpaceCameraPos-i.w);float fres=pow(1-saturate(dot(v,n)),3);float spark=pow(saturate(dot(reflect(-GetMainLight().direction,n),v)),70);float ripple=smoothstep(1.8,1.98,waves)*.18;half3 c=lerp(_BaseColor.rgb,half3(.25,.65,.79),fres*.7)+ripple+spark*.5;
half3 night=lerp(half3(.025,.035,.055),half3(.22,.065,.075),fres*.8)+ripple*.13+spark*half3(.65,.16,.08);
c=lerp(c,night,saturate(_OverworldEclipseDarkness));return half4(MixFog(c,i.f),1);}
ENDHLSL}}}
