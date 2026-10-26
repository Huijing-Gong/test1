Shader "KingOfGraphic/Own/ShoreContactFoam" {
Properties { _BaseColor("Owned shore foam",Color)=(.83,.94,1,.62) }
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+16" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float2 uv:TEXCOORD1;half fog:TEXCOORD2;};
V Vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target {float2 p=i.w.xz;float drift=sin(p.x*2.9+p.y*3.7+_Time.y*.9)+sin(p.x*6.3-p.y*4.1-_Time.y*1.2)*.45;float broken=smoothstep(-.38,.72,drift);float ridge=pow(saturate(.5+.5*sin(i.uv.y*22-_Time.y*1.45+sin(p.x*1.9+p.y*2.3))),5);float fade=pow(saturate(1-i.uv.y),1.4);return half4(MixFog(_BaseColor.rgb,i.fog),fade*broken*(.36+ridge*.64)*_BaseColor.a);}
ENDHLSL }
}}
