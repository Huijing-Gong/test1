Shader "Own/MapLandscape20261026/WetSandF46"{
Properties{_Dry("Dry warm sand",Color)=(.64,.52,.32,1) _Wet("Wet sand",Color)=(.32,.39,.29,1)}
SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Back ZWrite On
Pass{Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _Dry,_Wet; CBUFFER_END
struct A{float4 p:POSITION;half3 n:NORMAL;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half fog:TEXCOORD2;};
V Vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.fog=ComputeFogFactor(o.p.z);return o;}
float H(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float N(float2 p){float2 q=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(H(q),H(q+float2(1,0)),f.x),lerp(H(q+float2(0,1)),H(q+1),f.x),f.y);}
half4 Frag(V i):SV_Target{half3 n=normalize(i.n);float dry=smoothstep(3.45,4.5,i.w.y);half3 albedo=lerp(_Wet.rgb,_Dry.rgb,dry);float grain=N(i.w.xz*2.2);float fade=1-smoothstep(.12,.55,length(fwidth(i.w.xz)));albedo*=.94+N(i.w.xz*.31)*.10+(grain-.5)*.045*fade;
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
float4 sc=ComputeScreenPos(TransformWorldToHClip(i.w));
#else
float4 sc=TransformWorldToShadowCoord(i.w);
#endif
Light l=GetMainLight(sc);half3 lit=albedo*(max(SampleSH(n),half3(.10,.10,.10))+l.color*saturate(dot(n,l.direction))*l.shadowAttenuation);return half4(MixFog(lit,i.fog),1);}
ENDHLSL}
Pass{Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask 0 HLSLPROGRAM
#pragma vertex DV
#pragma fragment DF
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float4 DV(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}half4 DF():SV_Target{return 0;} ENDHLSL}
}}