// OUTPUT ONLY. Existing Disc UV is centred[-1,1], NOT texture UV[0,1]. No new mesh/texture.
Shader "KingOfGraphic/Own/Candidate/WaterfallImpactFoamF9"
{
 Properties
 {
  _BaseColor("Foam",Color)=(.89,.97,1,.72)
  _FlowSpeed("Churn speed",Range(.1,2))=.8
  _FoamDensity("Foam density",Range(0,1))=.62
  _RippleStrength("Broken outward ripple",Range(0,1))=.35
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;float _FlowSpeed,_FoamDensity,_RippleStrength;
   CBUFFER_END
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half fog:TEXCOORD1;};
   V Vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
   half4 Frag(V i):SV_Target
   {
    float2 p=i.uv;float r=length(p),t=_Time.y*_FlowSpeed;
    float2 radial=p/max(.08,r);
    float broad=Noise(p*3.8-float2(t*.21,t*.13));
    float warp=(broad-.5)*.17;
    float2 advected=p-radial*t*.10+float2(sin(t*.5),cos(t*.41))*.07;
    float grain=Noise((advected+warp)*13.3);
    float aa=max(.025,fwidth(grain)*.65);
    float lace=1-smoothstep(.055,.10+aa,abs(grain-.53));
    float churn=smoothstep(.28,.73,grain);
    float core=1-smoothstep(.12,.55,r+warp*.35);
    float edge=1-smoothstep(.67+warp,.99,r);
    float outwardPhase=(r+warp)*27-t*2.1;
    float wave=pow(saturate(.5+.5*sin(outwardPhase)),5);
    float broken=wave*smoothstep(.48,.78,Noise(p*7.2+float2(-t*.06,t*.09)))*_RippleStrength;
    float corePulse=.52+.20*broad+.12*sin(t*2.5+grain*2);
    float alpha=edge*(core*(corePulse+lace*.15)+(1-core)*(lace*.35+churn*.10+broken*.5));
    alpha*=lerp(.45,1,_FoamDensity)*_BaseColor.a;
    half3 color=_BaseColor.rgb*lerp(.94,1,saturate(lace+core*.4));
    return half4(MixFog(color,i.fog),saturate(alpha));
   }
   ENDHLSL
  }
 }
}
