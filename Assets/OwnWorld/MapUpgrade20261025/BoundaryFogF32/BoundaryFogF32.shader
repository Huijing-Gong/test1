Shader "Own/MapUpgrade20261025/BoundaryFogF32" {
Properties {
 _EdgeField("Walkable boundary data",2D)="black"{}
 _RouteClearance("Route top clearance",2D)="black"{}
 _VolumeNoise("Own volume noise",3D)="white"{}
 _Debug("Debug",Float)=0
 _Density("Extinction per metre",Float)=9
 _InlandWidth("Inland fade metres",Float)=2.8
 _OutsideWidth("Outer fade metres",Float)=9
 _TopHeight("Low fog top",Float)=1.25
 _DriftSpeed("Slow drift",Float)=.12
}
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-1" "RenderType"="Transparent"} Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 3.5
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
TEXTURE2D(_EdgeField);SAMPLER(sampler_EdgeField);
TEXTURE2D(_RouteClearance);SAMPLER(sampler_RouteClearance);
TEXTURE3D(_VolumeNoise);SAMPLER(sampler_VolumeNoise);
CBUFFER_START(UnityPerMaterial)
float _Debug,_Density,_InlandWidth,_OutsideWidth,_TopHeight,_DriftSpeed;
CBUFFER_END
float _OverworldEclipseDarkness;
struct A {float4 p:POSITION;};struct V {float4 p:SV_POSITION;};
V Vert(A a){V o;o.p=float4(a.p.xy,0,1);return o;}
half4 Frag(V i):SV_Target {
 float2 uv=GetNormalizedScreenSpaceUV(i.p);
 float raw=SampleSceneDepth(uv);
 #if UNITY_REVERSED_Z
 float nearD=1;float farD=.00001;
 #else
 float nearD=UNITY_NEAR_CLIP_VALUE;float farD=.99999;raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
 #endif
 float3 origin=ComputeWorldSpacePosition(uv,nearD,UNITY_MATRIX_I_VP);
 float3 farPoint=ComputeWorldSpacePosition(uv,farD,UNITY_MATRIX_I_VP);
 float3 hit=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
 if(_Debug>.5)return half4((hit.x+130)/260,(hit.y+15)/60,(hit.z+130)/260,1);
 float3 rd=normalize(farPoint-origin);
 float3 safeRd=lerp(-1.0,1.0,step(0,rd))*max(abs(rd),.00001);
 float3 ta=(float3(-145,-18,-145)-origin)/safeRd;
 float3 tb=(float3(145,42,145)-origin)/safeRd;
 float3 lo=min(ta,tb),hi=max(ta,tb);
 float start=max(0,max(lo.x,max(lo.y,lo.z)));
 float end=min(length(hit-origin),min(hi.x,min(hi.y,hi.z)));
 if(end<=start)return 0;
 float t=start,T=1;float3 sum=0;
 [loop] for(int j=0;j<192;j++){
  if(t>=end||T<.002)break;
  float3 p=origin+rd*(t+.16);
  float3 field=SAMPLE_TEXTURE2D_LOD(_EdgeField,sampler_EdgeField,(p.xz+130)/260,0).rgb;
  float d=(field.r-.5)*64+length(max(abs(p.xz)-130,0)),g=field.g*60-15;
  float horizontalGap=max(-d-_InlandWidth,d-_OutsideWidth);
  float verticalGap=max(p.y-g-_TopHeight-.4,-18-p.y);
  float dt=min(end-t,clamp(max(horizontalGap,verticalGap)*.6,.32,6.0));
  if(max(horizontalGap,verticalGap)>.6){t+=dt;continue;}
  float clock=_Time.y*_DriftSpeed;
  float cloud=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,p*.025+float3(clock*.004,0,clock*.002),0).r;
  float wave=cloud*2-1;
  float ripple=SAMPLE_TEXTURE3D_LOD(_VolumeNoise,sampler_VolumeNoise,p*.075+float3(-clock*.003,clock*.002,0),0).r*2-1;
  float edgeD=d+.12*wave;
  float band=smoothstep(-_InlandWidth,-.55,edgeD)*exp(-pow(max(edgeD,0)/(2.8+1.2*cloud),2))*(1-smoothstep(_OutsideWidth*.6,_OutsideWidth,max(edgeD,0)));
  float top=1-smoothstep(g+.05,g+_TopHeight+.5*wave+.12*ripple,p.y);
  float bottom=smoothstep(-13+2*wave,-4+2*wave,p.y);
  float den=band*top*bottom*(.75+.21*wave+.04*ripple);
  float2 route=SAMPLE_TEXTURE2D_LOD(_RouteClearance,sampler_RouteClearance,(p.xz+130)/260,0).rg;
  den*=1-route.r*smoothstep(route.g*60-15-.5,route.g*60-15-.15,p.y);
  float alpha=1-exp(-den*_Density*dt);
  float light=saturate((p.y-g+6)/7);
  float3 col=lerp(float3(.64,.72,.80),float3(.89,.91,.91),light);
  col*=.91+.09*wave+.02*ripple;
  col*=lerp(float3(1,1,1),float3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
  sum+=T*alpha*col;T*=1-alpha;t+=dt;
 }
 float2 hitRoute=SAMPLE_TEXTURE2D_LOD(_RouteClearance,sampler_RouteClearance,(hit.xz+130)/260,0).rg;
 float clearTop=hitRoute.r*smoothstep(hitRoute.g*60-15-.45,hitRoute.g*60-15-.10,hit.y);
 float keep=1-.985*clearTop;
 return half4(sum*keep,(1-T)*keep);
}
ENDHLSL } } }
