Shader "Own/MapUpgrade20261025/EdgeVolumeFogF29" {
Properties { _GroundEdgeMask("Edge field",2D)="black"{} _Density("Density",Float)=.24 }
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+80" "RenderType"="Transparent"} Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
TEXTURE2D(_GroundEdgeMask); SAMPLER(sampler_GroundEdgeMask);
CBUFFER_START(UnityPerMaterial) float _Density; CBUFFER_END
float _OverworldEclipseDarkness;
struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
V Vert(A a){V o;o.p=float4(a.p.xy,0,1);o.uv=a.p.xy*.5+.5;return o;}
half4 Frag(V i):SV_Target {
float2 uv=i.p.xy/_ScaledScreenParams.xy;
float raw=SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
float nearD=1;float farD=0.00001;
#else
float nearD=UNITY_NEAR_CLIP_VALUE;float farD=.99999;
raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
#endif
float3 a=ComputeWorldSpacePosition(uv,nearD,UNITY_MATRIX_I_VP);
float3 b=ComputeWorldSpacePosition(uv,farD,UNITY_MATRIX_I_VP);
float3 hit=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
float3 rd=normalize(b-a);
float3 safeRd=lerp(float3(-1,-1,-1),float3(1,1,1),step(0,rd))*max(abs(rd),.00001);
float3 t1=(float3(-130,-45,-130)-a)/safeRd;
float3 t2=(float3(130,52,130)-a)/safeRd;
float3 lo=min(t1,t2);float3 hi=max(t1,t2);
float entry=max(0,max(lo.x,max(lo.y,lo.z)));
float leave=min(length(hit-a),min(hi.x,min(hi.y,hi.z)));
if(leave<=entry)return 0;
float stepLen=(leave-entry)/32.0;
float optical=0;float hsum=0;
[unroll] for(int j=0;j<32;j++){
float3 p=a+rd*(entry+(j+.5)*stepLen);
float2 field=SAMPLE_TEXTURE2D_LOD(_GroundEdgeMask,sampler_GroundEdgeMask,(p.xz+130)/260,0).rg;
float d=(field.r-.5)*64;float g=field.g*50-5;
float band=smoothstep(-3.0,1.0,d)*(1-smoothstep(17,29,d));
float top=1-smoothstep(g-3,g+1.5,p.y);
float bottom=smoothstep(g-38,g-24,p.y);
float noise=.72+.16*sin(p.x*.21+p.z*.16+_Time.y*.021)*sin(p.y*.28-p.z*.09)+.12*sin(p.x*.43-p.z*.32);
float den=band*top*bottom*noise;
optical+=den*stepLen;hsum+=den*stepLen*saturate((p.y-g+24)/28);
}
float alpha=min(.62,1-exp(-optical*_Density));
float shade=optical>.001?hsum/optical:1;
float3 col=lerp(float3(.64,.74,.84),float3(.88,.91,.94),shade);
col*=lerp(float3(1,1,1),float3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
return half4(col,alpha);
}
ENDHLSL } } }