Shader "Own/MapUpgrade20261025/CloudIslandLightingF34"
{
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
 _WispDensity("Thin edge wisp density",Float)=1.3
 _OuterReach("Cloud reach outside island",Float)=38
 _Wind("World wind direction XZ",Vector)=(.8,0,.35,0)
 _WindSpeed("Wind speed metres per second",Range(0,.5))=.16
 _DetailStrength("Close cloud detail",Range(0,1))=.35
 _LightAbsorption("Cloud self shading",Range(0,3))=1.35
 _SunColor("Sunlit cloud tint",Color)=(.97,.98,1,1)
 _ShadowColor("Cloud shadow tint",Color)=(.57,.67,.80,1)
 _SurfaceFade("Opaque surface softness metres",Range(.05,1))=.4
 [HideInInspector]_DebugStepBudget("Inspection only",Float)=0
 [HideInInspector]_InspectionTime("Inspection time; negative uses game time",Float)=-1
}
SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-1" "RenderType"="Transparent"}
 Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 Pass {
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 TEXTURE2D(_EdgeField);SAMPLER(sampler_EdgeField);
 TEXTURE2D(_CloudHeightField);SAMPLER(sampler_CloudHeightField);
 TEXTURE2D(_RouteClearance);SAMPLER(sampler_RouteClearance);
 TEXTURE3D(_VolumeNoise);SAMPLER(sampler_VolumeNoise);
 CBUFFER_START(UnityPerMaterial)
 float _InlandWidth,_NearHeight,_CloudDrop,_CloudThickness,_CloudDensity,_WispDensity,_OuterReach;
 float4 _Wind,_SunColor,_ShadowColor;
 float _WindSpeed,_DetailStrength,_LightAbsorption,_SurfaceFade,_InspectionTime,_DebugStepBudget;
 CBUFFER_END
 float _OverworldEclipseDarkness;
 struct A { float4 p:POSITION; };
 struct V { float4 p:SV_POSITION; };
 V Vert(A a) { V o;o.p=float4(a.p.xy,0,1);return o; }
 float VolumeDetail(float3 p,float3 drift,float fineWeight) {
  float broad=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,(p-drift)*.045,0).r;
  if(fineWeight>.001) {
   float fine=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,(p-drift*.65)*.13+float3(.19,.31,.07),0).r;
   broad=lerp(broad,saturate(broad+(fine-.5)*.7),fineWeight);
  }
  return broad;
 }
 float Interior(float3 p,float centre,float halfHeight,float detail) {
  float v=1-smoothstep(.25,1,abs(p.y-centre)/max(halfHeight,.1));
  return saturate((v-(1-detail)*.65)*1.8);
 }
 half4 Frag(V i):SV_Target {
  float2 uv=GetNormalizedScreenSpaceUV(i.p);
  float raw=SampleSceneDepth(uv);
  #if UNITY_REVERSED_Z
  float nearD=1,farD=.00001;
  #else
  float nearD=UNITY_NEAR_CLIP_VALUE,farD=.99999;
  raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
  #endif
  float3 origin=ComputeWorldSpacePosition(uv,nearD,UNITY_MATRIX_I_VP);
  float3 farPoint=ComputeWorldSpacePosition(uv,farD,UNITY_MATRIX_I_VP);
  float3 hit=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
  float3 rd=normalize(farPoint-origin);
  float3 safeRd=lerp(-1.0,1.0,step(0,rd))*max(abs(rd),.00001);
  float3 ta=(float3(-185,-24,-185)-origin)/safeRd;
  float3 tb=(float3(185,42,185)-origin)/safeRd;
  float3 lo=min(ta,tb),hi=max(ta,tb);
  float t=max(0,max(lo.x,max(lo.y,lo.z)));
  float hitDistance=length(hit-origin);
  float end=min(hitDistance,min(hi.x,min(hi.y,hi.z)));
  if(end<=t)return 0;
  Light sun=GetMainLight();
  float3 lightDir=normalize(sun.direction+float3(0,.00001,0));
  float3 lightTint=lerp(float3(1,1,1),min(sun.color,float3(1.5,1.5,1.5)),.25);
  float phase=pow(saturate(dot(rd,lightDir)),8)*.08;
  float2 wind=_Wind.xz/max(length(_Wind.xz),.001);
  float seconds=_InspectionTime>=0?_InspectionTime:_Time.y;
  float3 drift=float3(wind.x,0,wind.y)*seconds*_WindSpeed;
  float T=1;float3 sum=0;
  [loop]for(int j=0;j<192;j++) {
   if(t>=end||T<.004)break;
   float minimumStep=lerp(.4,1.1,smoothstep(25,110,t));
   float3 p=origin+rd*(t+min(minimumStep*.5,(end-t)*.5));
   float2 fuv=(p.xz+130)/260;
   float3 field=SAMPLE_TEXTURE2D_LOD(_EdgeField,sampler_EdgeField,fuv,0).rgb;
   float d=(field.r-.5)*64+length(max(abs(p.xz)-130,0));
   float g=field.g*60-15;
   float cloudG=min(g+1.2,SAMPLE_TEXTURE2D_LOD(_CloudHeightField,sampler_CloudHeightField,fuv,0).r*60-15);
   float horizontalGap=max(-d-10,d-_OuterReach);
   float shelfExtent=_CloudThickness*1.25+1.1;
   float shelfGap=max(p.y-cloudG+_CloudDrop-shelfExtent,cloudG-_CloudDrop-shelfExtent-p.y);
   float wispGap=max(p.y-g-_NearHeight,g-.7-p.y);
   float verticalGap=min(shelfGap,wispGap);
   float gap=max(horizontalGap,verticalGap);
   float dt=min(end-t,clamp(gap*.6,minimumStep,8));
   if(gap>.5){t+=dt;continue;}
   float shape=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,(float3(p.x,0,p.z)-drift*.55)*.018,0).r;
   shape=saturate((shape-.25)*2);
   float wave=shape*2-1;
   float centreY=cloudG-_CloudDrop+1.1*wave;
   float halfHeight=_CloudThickness*(.85+.4*shape);
   float fineWeight=_DetailStrength*(1-smoothstep(35,80,t));
   float detail=VolumeDetail(p,drift,fineWeight);
   float radialReach=_OuterReach*(.62+.38*shape);
   float outer=1-smoothstep(radialReach*.60,radialReach,max(d,0));
   float inner=smoothstep(-10,-1,d);
   float body=inner*outer*Interior(p,centreY,halfHeight,detail);
   float wispCentre=-.3+3.1*wave;
   float wispBand=smoothstep(-_InlandWidth,-_InlandWidth+.6,d)*exp(-pow((d-wispCentre)/2.3,2));
   float wispTop=g+_NearHeight*(.45+.55*shape);
   float wispHeight=smoothstep(g-.7,g-.15,p.y)*(1-smoothstep(g+.04,wispTop,p.y));
   float wisp=wispBand*wispHeight*smoothstep(.30,.75,shape);
   if(wisp>.001)wisp*=1-.65*SAMPLE_TEXTURE2D_LOD(_RouteClearance,sampler_RouteClearance,fuv,0).r;
   float contact=saturate((hitDistance-t)/max(_SurfaceFade,.05));
   float sigma=(body*_CloudDensity+wisp*_WispDensity)*contact;
   if(sigma<.0001){t+=dt;continue;}
   // A single sunward density probe supplies soft self shading, without a second light ray march.
   float3 probe=p+lightDir*2.8;
   float probeDetail=VolumeDetail(probe,drift,0);
   float obstruction=Interior(probe,centreY,halfHeight,probeDetail)*inner*outer;
   float sunTransmission=exp(-obstruction*_LightAbsorption);
   float height=saturate((p.y-centreY)/max(halfHeight,1)*.5+.5);
   float directionalShape=2.1*(detail-probeDetail);
   float lit=saturate(.30+.32*height+.12*sunTransmission+directionalShape+phase);
   float3 col=lerp(_ShadowColor.rgb,_SunColor.rgb,lit)*lightTint;
   col=lerp(col,_SunColor.rgb*lightTint,saturate(wisp*_WispDensity/max(sigma,.001))*.45);
   col*=lerp(float3(1,1,1),float3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
   float alpha=1-exp(-sigma*dt);
   sum+=T*alpha*col;T*=1-alpha;t+=dt;
  }
  if(_DebugStepBudget>.5 && t<end && T>=.004)return half4(1,0,1,1);
  return half4(sum,1-T);
 }
 ENDHLSL
 }
}
}