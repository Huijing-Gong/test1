Shader "Combat/Focus Spectral" {
Properties { _Tint("Tint",Color)=(.2,.8,1,.5) }
SubShader { Tags {"RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline"}
Pass { Blend SrcAlpha One ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _Tint;
CBUFFER_END
struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};
struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;half4 c:COLOR;};
V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.n);o.c=a.c;return o;}
half4 frag(V i):SV_Target {half rim=pow(1-saturate(abs(dot(normalize(i.normal),normalize(GetWorldSpaceViewDir(i.world))))),2);return half4(_Tint.rgb*(.55+rim*1.4),_Tint.a*(.18+rim*.82));}
ENDHLSL
}}}
