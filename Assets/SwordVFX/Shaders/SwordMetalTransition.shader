Shader "KingOfGraphic/Sword Metal Transition" {
 Properties { _BaseMap("Base Map",2D)="white"{} _BaseColor("Base Color",Color)=(1,1,1,1) _Reveal("Reveal",Range(0,1))=0 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
 Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST,_BaseColor;float _Reveal;
 CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 normal:TEXCOORD0;float2 uv:TEXCOORD1;float height:TEXCOORD2;};
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.normal=TransformObjectToWorldNormal(a.n);o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.height=a.p.y;return o;}
 half4 frag(V i):SV_Target{
 if(i.height>=.205 && _Reveal>0)clip(i.height-lerp(.205,1.05,_Reveal));
 Light main=GetMainLight();float3 n=normalize(i.normal);
 float3 lighting=SampleSH(n)+main.color*saturate(dot(n,main.direction));
 return half4(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb*lighting,1);
 }
 ENDHLSL
 }
 }
}
