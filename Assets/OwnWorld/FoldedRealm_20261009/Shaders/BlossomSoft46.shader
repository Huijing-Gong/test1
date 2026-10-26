Shader "KingOfGraphic/Own/BlossomSoft46" {Properties {_BaseColor("Tint",Color)=(1,1,1,1)} SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Off
Pass{Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;half fog:TEXCOORD2;};
V Vert(A v){UNITY_SETUP_INSTANCE_ID(v);V o;o.w=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(v.n);o.c=v.c*_BaseColor;o.fog=ComputeFogFactor(o.p.z);return o;}
half4 Frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target{half3 n=normalize(i.n)*IS_FRONT_VFACE(face,1,-1);Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half3 ambient=max(SampleSH(n),half3(.12,.15,.13));half3 albedo=i.c.rgb;
#ifndef UNITY_COLORSPACE_GAMMA
albedo=SRGBToLinear(albedo);
#endif
half foliage=step(.42,i.c.g)*step(i.c.g+.09,i.c.r);
half ndl=dot(n,l.direction);
half3 bark=albedo*(ambient*.85+l.color*(.12+saturate(ndl)*.87)*l.shadowAttenuation);
half3 sky=max(ambient,half3(.30,.28,.26));
half diffuse=saturate((ndl+.45)/1.45);
half transmission=saturate(-ndl)*.22;
half3 petals=albedo*(sky*.95+l.color*(.22+diffuse*.70+transmission)*lerp(.68,1,l.shadowAttenuation));
half3 color=lerp(bark,petals,foliage);return half4(MixFog(color,i.fog),1);}
ENDHLSL}
Pass{Tags{"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0 Cull Back
HLSLPROGRAM
#pragma vertex ShadowPassVertex
#pragma fragment ShadowPassFragment
#pragma multi_compile_instancing
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
#include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
ENDHLSL}
UsePass "Universal Render Pipeline/Lit/DepthOnly"
}}