Shader "KingOfGraphic/Own/WaterSprayF46" {
 Properties {_BaseColor("Water mist",Color)=(.83,.96,1,.18)}
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 Pass {HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
 struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float eye:TEXCOORD1;half fog:TEXCOORD2;};
 V Vert(A a){V o;float3 w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(w);o.uv=a.uv;o.color=a.color*_BaseColor;o.eye=-TransformWorldToView(w).z;o.fog=ComputeFogFactor(o.p.z);return o;}
 float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
 half4 Frag(V i):SV_Target {float2 q=i.uv*2-1;float shape=1-smoothstep(.15,.96,dot(q,q));shape*=lerp(.65,1,Noise(i.uv*5+float2(_Time.y*.13,0)));float depth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.p)),_ZBufferParams);float soft=saturate((depth-i.eye)/.65);return half4(MixFog(i.color.rgb,i.fog),i.color.a*shape*soft);}
 ENDHLSL}
 }}
