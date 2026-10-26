Shader "Boss/DeathDissolve" {
Properties {_MainTex("Texture",2D)="white"{} _Progress("Progress",Range(0,1))=0}
SubShader { Tags {"RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline"} Cull Off
Pass { Tags {"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float _Progress;
struct App {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};
struct Vary {float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;};
Vary vert(App a){Vary o;o.world=TransformObjectToWorld(a.vertex.xyz);o.pos=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normal);o.uv=a.uv;return o;}
float hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
float noise(float3 p){float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);}

half4 frag(Vary i):SV_Target {float n=noise(i.world*19)*.72+noise(i.world*47)*.28;float d=n-lerp(-.12,1.12,_Progress);clip(d);float edge=(1-smoothstep(.015,.085,d))*step(.001,_Progress);Light l=GetMainLight();float3 normal=normalize(i.normal);float3 lighting=SampleSH(normal)+l.color*saturate(dot(normal,l.direction));return half4(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb*lighting+float3(5,2.1,.3)*edge,1);}
ENDHLSL
}}}
