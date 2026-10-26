// OUTPUT-ONLY CANDIDATE. Not compiled or installed. Existing shaders remain untouched.
Shader "KingOfGraphic/Own/Candidate/LayeredCloud20261025"
{
 Properties {
  _Tint("Lit tint",Color)=(.89,.94,1,.42)
  _Shade("Deep tint",Color)=(.47,.63,.80,1)
  _Scale("World noise scale",Float)=.024
  _Drift("Drift XZ",Vector)=(.0008,-.0005,0,0)
  _Offset("Noise offset",Vector)=(0,0,0,0)
  _Threshold("Cloud threshold",Range(0,1))=.43
  _Softness("Edge softness",Range(.01,.5))=.22
  _OuterRadius("Outer radius",Float)=345
  _OuterFade("Outer fade",Float)=70
  _NearFade("Camera near fade",Float)=3
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-80" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Tint,_Shade; float4 _Drift,_Offset;
    float _Scale,_Threshold,_Softness,_OuterRadius,_OuterFade,_NearFade;
   CBUFFER_END
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half fog:TEXCOORD1;};
   V Vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.fog=ComputeFogFactor(o.p.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
   half4 Frag(V i):SV_Target{
    float2 p=i.w.xz*_Scale+_Offset.xy+_Time.y*_Drift.xy;
    float n=Noise(p)*.62+Noise(p*2.07+7.3)*.27+Noise(p*4.19-4.1)*.11;
    float alpha=smoothstep(_Threshold-_Softness,_Threshold+_Softness,n);
    alpha*=1-smoothstep(max(0,_OuterRadius-_OuterFade),_OuterRadius,length(i.w.xz));
    // Avoid a hard plane flashing through the free camera. No depth-texture dependency.
    alpha*=smoothstep(0,max(.01,_NearFade),distance(i.w,_WorldSpaceCameraPos));
    half3 c=lerp(_Shade.rgb,_Tint.rgb,saturate(n*1.45));
    return half4(MixFog(c,ComputeFogFactor(TransformWorldToHClip(i.w).z)),alpha*_Tint.a);
   }
   ENDHLSL
  }
 }
}
