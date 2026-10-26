Shader "KingOfGraphic/Own/FoldedWorldWindow" {
Properties { _Variant("World glimpse",Float)=0 _WorldTex("Owned distant world",2D)="white" {} }
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20"} Cull Off
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
TEXTURE2D(_WorldTex);SAMPLER(sampler_WorldTex);
CBUFFER_START(UnityPerMaterial) float _Variant; CBUFFER_END
// Driven by the host eclipse sequence; zero preserves the original daylight art.
float _OverworldEclipseDarkness;
struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 w:TEXCOORD1;};
V Vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.uv=a.uv;return o;}
half4 Frag(V i):SV_Target{
float3 view=normalize(GetWorldSpaceViewDir(i.w));float3 localView=TransformWorldToObjectDir(view);
float2 uv=i.uv+localView.xy*.018*(1-i.uv.y);
half3 col=SAMPLE_TEXTURE2D(_WorldTex,sampler_WorldTex,saturate(uv)).rgb;
float edge=1-smoothstep(.0,.018,min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y)));col=lerp(col,half3(.23,.48,.57),edge*.25);
float eclipse=saturate(_OverworldEclipseDarkness);
half luminance=dot(col,half3(.2126,.7152,.0722));
col=lerp(col,luminance.xxx,eclipse*.25);
col*=lerp(half3(1,1,1),half3(.45,.5,.6),eclipse);
return half4(col,1);
} ENDHLSL }}}
