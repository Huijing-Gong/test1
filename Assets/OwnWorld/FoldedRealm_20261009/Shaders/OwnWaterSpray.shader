Shader "KingOfGraphic/Own/WaterSpray" {
Properties { _BaseColor("Owned foam tint",Color)=(.88,.97,1,1) }
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+25" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
struct A{float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;half fog:TEXCOORD1;};
V Vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.color=a.color*_BaseColor;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target {float2 p=(i.uv-.5)*2;float radius=dot(p,p);float soft=pow(saturate(1-radius),2.5);clip(soft*i.color.a-.001);return half4(MixFog(i.color.rgb,i.fog),soft*i.color.a);}
ENDHLSL }
}}
