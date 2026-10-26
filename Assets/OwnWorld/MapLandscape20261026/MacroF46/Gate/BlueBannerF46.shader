Shader "KingOfGraphic/Own/BlueBannerF46" {Properties {_BaseColor("Blue cloth",Color)=(.018,.13,.36,1) _GoldColor("Gold embroidery",Color)=(.78,.57,.22,1)}
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Off
Pass {HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor,_GoldColor; CBUFFER_END
struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 w:TEXCOORD1;half fog:TEXCOORD2;};
V Vert(A a){V o;float free=pow(1-a.uv.y,2);a.p.z+=sin(a.uv.y*7.2+_Time.y*1.5)*free*.075;float3 w=TransformObjectToWorld(a.p.xyz);o.w=w;o.p=TransformWorldToHClip(w);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i):SV_Target {float2 p=i.uv;float border=step(p.x,.045)+step(.955,p.x)+step(p.y,.045);float2 q=(p-float2(.5,.56))*float2(1,2);float diamond=abs(q.x)+abs(q.y);float star=(1-smoothstep(.12,.16,diamond))*(1-smoothstep(.16,.20,length(q)));float thread=1-smoothstep(.005,.011,abs(abs(q.x)-abs(q.y)*.38));thread*=1-smoothstep(.16,.22,length(q));half3 c=lerp(_BaseColor.rgb,_GoldColor.rgb,saturate(border+star+thread*.65));c*=.93+.07*sin(p.y*75);Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));half shade=.67+.33*sun.shadowAttenuation;return half4(MixFog(c*shade,i.fog),1);}
ENDHLSL}
}}
