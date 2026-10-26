Shader "Own/Black Sun" {
Properties {
 _Tint("Tint",Color)=(1,.77,.40,1)
 _Mode("0 sun, 1 wave, 2 motes",Float)=0
 _Energy("Energy",Float)=1
 _PhaseTime("Simulation time",Float)=0
 _Wave("Wave fraction",Float)=0
 _CourtBounds("Court centre XZ and half size",Vector)=(0,0,10000,10000)
}
SubShader { Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" } Blend One OneMinusSrcAlpha ZWrite Off Cull Off
Pass { HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float2 worldXZ:TEXCOORD1;};
CBUFFER_START(UnityPerMaterial)
float4 _Tint,_CourtBounds;float _Mode,_Energy,_PhaseTime,_Wave;
CBUFFER_END
V vert(A i){V o;
 if(_Mode<.5){
  // Camera-facing disk per render: round from gameplay, recording and spectator cameras.
  float3 centre=TransformObjectToWorld(float3(0,0,0));
  float sx=length(TransformObjectToWorldDir(float3(1,0,0),false));
  float sy=length(TransformObjectToWorldDir(float3(0,1,0),false));
  float3 right=UNITY_MATRIX_I_V._m00_m10_m20;
  float3 up=UNITY_MATRIX_I_V._m01_m11_m21;
  o.pos=TransformWorldToHClip(centre+right*i.vertex.x*sx+up*i.vertex.y*sy);
 }else o.pos=TransformObjectToHClip(i.vertex.xyz);
 o.worldXZ=TransformObjectToWorld(i.vertex.xyz).xz;o.uv=i.uv;o.color=i.color;return o;}
float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
half4 frag(V i):SV_Target {
 if(_Mode>2.5){float a=i.color.a*.88;return half4(i.color.rgb*_Tint.rgb*a,a);}
 if(_Mode>1.5){float edge=1-abs(i.uv.y*2-1);float a=saturate(edge*2)*i.color.a*_Energy;return half4(i.color.rgb*_Tint.rgb*a,a*.32);}
 float2 p=(i.uv*2-1)*1.4;float r=length(p),a=atan2(p.y,p.x);
 if(_Mode>.5){
  clip(min(_CourtBounds.z-abs(i.worldXZ.x-_CourtBounds.x),_CourtBounds.w-abs(i.worldXZ.y-_CourtBounds.y)));
  float edge=exp(-abs(r-_Wave)*68);float trail=exp(-abs(r-(_Wave-.045))*29)*.2;
  float rays=pow(saturate(cos(a*12+sin(a*5)*.4)),18)*saturate(1-r)*.14;
  float cut=(1-smoothstep(.98,1.04,r));float gold=(edge+trail+rays)*_Energy*cut;
  float dust=exp(-abs(r-min(.98,_Wave*.92))*13)*noise(p*22+_PhaseTime*2.1);
  dust*=saturate(_PhaseTime*12)*(1-smoothstep(.20,.85,_PhaseTime))*cut*.30;
  return half4(_Tint.rgb*gold*2.1+float3(.32,.29,.24)*dust,saturate(gold*.20+dust));
 }
 float n=noise(float2(a*5.8,_PhaseTime*.35+r*8));
 float edgeRadius=.985+(n-.5)*.018;
 float disk=1-smoothstep(edgeRadius-.003,edgeRadius+.004,r);
 float rim=exp(-abs(r-edgeRadius)*115);
 float filament=pow(saturate(.5+.5*sin(a*19+noise(p*7)*4-_PhaseTime*.6)),6);
 float outer=exp(-max(r-edgeRadius,0)*15)*(1-disk);
 float jets=exp(-max(r-edgeRadius,0)*(10+14*(1-filament)))*(1-disk)*filament;
 float inner=pow(saturate(r),11)*.028;
 float3 dark=float3(.0015,.002,.004)+float3(.07,.035,.012)*inner;
 float3 corona=_Tint.rgb*(rim*2.5+outer*.55+jets*.48)*_Energy;
 float edgeFade=1-smoothstep(1.22,1.4,r);
 return half4((dark*disk+corona)*edgeFade,saturate(disk+outer*.12)*edgeFade);
}
ENDHLSL }
} Fallback Off
}
