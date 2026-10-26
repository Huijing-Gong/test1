Shader "KingOfGraphic/Own/FoldedSky" {Properties {} SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background"} Cull Off ZWrite Off
Pass {HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 d:TEXCOORD0;};
V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.d=a.p.xyz;return o;}
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
half4 frag(V i):SV_Target {float3 d=normalize(i.d);half3 c=lerp(half3(.74,.86,.90),half3(.22,.56,.79),saturate(d.y*.95+.12));float2 p=d.xz/max(.12,d.y+.24)*2.8;float n=noise(p)*.55+noise(p*2.1)*.3+noise(p*4.4)*.15;float cloud=smoothstep(.51,.7,n)*smoothstep(-.05,.16,d.y);return half4(lerp(c,half3(.98,.97,.93),cloud*.82),1);}
ENDHLSL}}}
