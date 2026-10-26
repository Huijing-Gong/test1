Shader "KingOfGraphic/Own/ApricotTree" {
Properties {_BaseMap("Own color texture",2D)="white"{} _BaseColor("Color",Color)=(1,1,1,1) _Cull("Cull",Float)=2}
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull [_Cull]
Pass {Name "Forward" Tags {"LightMode"="UniversalForward"} HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; half4 _BaseColor; CBUFFER_END
struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;half4 c:COLOR;};
struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;half4 c:COLOR;half fog:TEXCOORD3;};
V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.c=a.c;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {half3 n=normalize(i.n)*IS_FRONT_VFACE(face,1,-1);Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half3 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb*SRGBToLinear(i.c.rgb);half leaf=(c.g>c.r*1.08&&c.g>c.b*1.3)?1:0;if(leaf>0)c=lerp(half3(.96,.46,.50),half3(1.0,.77,.73),saturate((c.g-.15)*1.6));half3 lit=SampleSH(n)*.8+l.color*(saturate(dot(n,l.direction))*.70+.08)*l.shadowAttenuation;lit+=leaf*half3(.06,.045,.055);return half4(MixFog(c*lit,i.fog),1);}
ENDHLSL}
UsePass "Universal Render Pipeline/Lit/ShadowCaster"
UsePass "Universal Render Pipeline/Lit/DepthOnly"
UsePass "Universal Render Pipeline/Lit/DepthNormals"
}}

