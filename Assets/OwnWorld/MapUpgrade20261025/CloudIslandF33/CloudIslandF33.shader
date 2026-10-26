Shader "Own/MapUpgrade20261025/CloudIslandF33" {
Properties {
 _EdgeField("Boundary and top height",2D)="black"{}
 _CloudHeightField("Smoothed lower shelf height",2D)="black"{}
 _RouteClearance("Route clearance",2D)="black"{}
 _VolumeNoise("Own volume noise",3D)="white"{}
 _InlandWidth("Maximum inland wisps metres",Float)=2.8
 _NearHeight("Near-ground wisp top metres",Float)=.42
 _CloudDrop("Cloud centre below rim metres",Float)=6
 _CloudThickness("Cloud half thickness",Float)=2.4
 _CloudDensity("Lower cloud density",Float)=.8
 _WispDensity("Thin edge wisp density",Float)=.18
 _OuterReach("Cloud reach outside island",Float)=38
 _DriftSpeed("Slow drift",Float)=.12
}
SubShader {Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-1" "RenderType"="Transparent"} Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
Pass {HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 3.5
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
TEXTURE2D(_EdgeField);SAMPLER(sampler_EdgeField);
TEXTURE2D(_CloudHeightField);SAMPLER(sampler_CloudHeightField);
TEXTURE2D(_RouteClearance);SAMPLER(sampler_RouteClearance);
TEXTURE3D(_VolumeNoise);SAMPLER(sampler_VolumeNoise);
CBUFFER_START(UnityPerMaterial)
float _InlandWidth,_NearHeight,_CloudDrop,_CloudThickness,_CloudDensity,_WispDensity,_OuterReach,_DriftSpeed;
CBUFFER_END
float _OverworldEclipseDarkness;
struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;};
V Vert(A a){V o;o.p=float4(a.p.xy,0,1);return o;}
half4 Frag(V i):SV_Target{
 float2 uv=GetNormalizedScreenSpaceUV(i.p);float raw=SampleSceneDepth(uv);
 #if UNITY_REVERSED_Z
 float nearD=1,farD=.00001;
 #else
 float nearD=UNITY_NEAR_CLIP_VALUE,farD=.99999;raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
 #endif
 float3 origin=ComputeWorldSpacePosition(uv,nearD,UNITY_MATRIX_I_VP);
 float3 farPoint=ComputeWorldSpacePosition(uv,farD,UNITY_MATRIX_I_VP);
 float3 hit=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
 float3 rd=normalize(farPoint-origin);
 float3 safeRd=lerp(-1.0,1.0,step(0,rd))*max(abs(rd),.00001);
 float3 ta=(float3(-185,-24,-185)-origin)/safeRd,tb=(float3(185,42,185)-origin)/safeRd;
 float3 lo=min(ta,tb),hi=max(ta,tb);
 float t=max(0,max(lo.x,max(lo.y,lo.z)));
 float end=min(length(hit-origin),min(hi.x,min(hi.y,hi.z)));
 if(end<=t)return 0;
 float T=1;float3 sum=0;float clock=_Time.y*_DriftSpeed;
 [loop]for(int j=0;j<160;j++){
  if(t>=end||T<.004)break;
  float3 p=origin+rd*(t+.2);
  float2 fuv=(p.xz+130)/260;
  float3 field=SAMPLE_TEXTURE2D_LOD(_EdgeField,sampler_EdgeField,fuv,0).rgb;
  float d=(field.r-.5)*64+length(max(abs(p.xz)-130,0)),g=field.g*60-15;
  float cloudG=min(g+1.2,SAMPLE_TEXTURE2D_LOD(_CloudHeightField,sampler_CloudHeightField,fuv,0).r*60-15);
  float horizontalGap=max(-d-10,d-_OuterReach);
  float shelfExtent=_CloudThickness*1.25+1.1;
  float shelfGap=max(p.y-cloudG+_CloudDrop-shelfExtent,cloudG-_CloudDrop-shelfExtent-p.y);
  float wispGap=max(p.y-g-_NearHeight,g-.7-p.y);
  float verticalGap=min(shelfGap,wispGap);
  float dt=min(end-t,clamp(max(horizontalGap,verticalGap)*.6,.4,8));
  if(max(horizontalGap,verticalGap)>.5){t+=dt;continue;}
  float shape=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,float3(p.x,0,p.z)*.018+float3(clock*.002,0,clock*.001),0).r;
  shape=saturate((shape-.25)*2);
  float wave=shape*2-1;
  float detail=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,p*.04+float3(-clock*.003,clock*.001,0),0).r;
  // A separate, finite horizontal cloud shelf. Clear air above it exposes a short rock face.
  float centreY=cloudG-_CloudDrop+1.1*wave;
  float halfHeight=_CloudThickness*(.85+.4*shape);
  float radialReach=_OuterReach*(.62+.38*shape);
  float outer=1-smoothstep(radialReach*.60,radialReach,max(d,0));
  float inner=smoothstep(-10,-1,d);
  float vertical=1-smoothstep(.25,1,abs(p.y-centreY)/halfHeight);
  float body=inner*outer*saturate((vertical-(1-detail)*.65)*1.8);
  // Thin near-ground wisps shift inward and outward independently of the lower cloud shelf.
  float wispCentre=-.3+3.1*wave;
  float wispBand=smoothstep(-_InlandWidth,-_InlandWidth+.6,d)*exp(-pow((d-wispCentre)/2.3,2));
  float wispTop=g+_NearHeight*(.45+.55*shape);
  float wispHeight=smoothstep(g-.7,g-.15,p.y)*(1-smoothstep(g+.04,wispTop,p.y));
  float patch=smoothstep(.30,.75,shape);
  float wisp=wispBand*wispHeight*patch;
  if(wisp>.001){float route=SAMPLE_TEXTURE2D_LOD(_RouteClearance,sampler_RouteClearance,fuv,0).r;wisp*=1-.65*route;}
  float sigma=body*_CloudDensity+wisp*_WispDensity;
  float alpha=1-exp(-sigma*dt);
  float light=saturate((p.y-centreY)/max(halfHeight,1)*.35+.6);
  float shadeNoise=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,(p+float3(-2,3,1))*.04+float3(-clock*.003,clock*.001,0),0).r;
  float lighting=saturate(.42+.55*light+1.1*(detail-shadeNoise));
  float3 col=lerp(float3(.62,.70,.80),float3(.94,.95,.96),lighting);
  col*=.94+.08*detail;
  col*=lerp(float3(1,1,1),float3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
  sum+=T*alpha*col;T*=1-alpha;t+=dt;
 }
 return half4(sum,1-T);
}
ENDHLSL}}}