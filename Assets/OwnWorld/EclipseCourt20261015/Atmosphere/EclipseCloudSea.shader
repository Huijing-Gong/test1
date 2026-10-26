Shader "Own/Eclipse Cloud Sea"{
Properties{
 _LightColor("Bright cool cloud",Color)=(.83,.88,.92,1)
 _ShadowColor("Gray blue shadow",Color)=(.42,.54,.63,1)
 _Opacity("Opacity",Range(0,1))=.68
 _Scale("World noise scale",Float)=.022
 _Offset("Independent layer XY offset",Vector)=(0,0,0,0)
 _HoleRadius("Square central hole halfwidth",Float)=18
 _HoleFeather("Soft inside edge metres",Float)=9
 _OuterRadius("Outer cloud halfwidth",Float)=160
 _OuterFeather("Outer fade metres",Float)=35
}
SubShader{Tags{"Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
Pass{HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A{float4 vertex:POSITION;};struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;};
CBUFFER_START(UnityPerMaterial)
float4 _LightColor,_ShadowColor,_Offset;float _Opacity,_Scale,_HoleRadius,_HoleFeather,_OuterRadius,_OuterFeather;
CBUFFER_END
float _OverworldEclipseDarkness;
float hash21(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
float fbm3(float2 p){float n=0,a=.57;[unroll]for(int k=0;k<3;k++){n+=a*noise(p);p=p*2.07+17.4;a*=.5;}return n;}
V vert(A i){V o;o.world=TransformObjectToWorld(i.vertex.xyz);o.pos=TransformWorldToHClip(o.world);return o;}
half4 frag(V i):SV_Target{float2 p=i.world.xz*_Scale+_Offset.xy;float n=fbm3(p),detail=fbm3(p*2.3+11.7);float square=max(abs(i.world.x),abs(i.world.z));float inner=smoothstep(_HoleRadius,_HoleRadius+_HoleFeather+(n-.5)*4,square);float outer=1-smoothstep(_OuterRadius-_OuterFeather,_OuterRadius,square);float density=smoothstep(.17,.7,n*.72+detail*.28);float alpha=_Opacity*inner*outer*(.45+.55*density);float3 col=lerp(_ShadowColor.rgb,_LightColor.rgb,saturate(.15+detail*.9+n*.25));col*=lerp(float3(1,1,1),float3(.15,.21,.32),saturate(_OverworldEclipseDarkness));return half4(col,alpha);}
ENDHLSL}}
Fallback Off
}
