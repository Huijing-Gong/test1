// OUTPUT ONLY. Compatible with current F5 ribbon: uv.x=width0..1,
// uv.y=authored along-distance (NOT normalized progress); COLOR.r=each fall progress0..1.
Shader "KingOfGraphic/Own/Candidate/FlowingFallsF9"
{
 Properties
 {
  _BaseColor("Clear water",Color)=(.12,.43,.51,1)
  _FoamColor("Aerated water",Color)=(.78,.92,.95,1)
  _FlowSpeed("Flow speed",Float)=1.2
  _DetailStrength("Layered thread strength",Range(0,1))=.72
  _LateralStrength("Cross-thread wandering",Range(0,1))=.65
  _FootFoam("Broken foot churn",Range(0,1))=.68
  _Opacity("Body opacity",Range(.3,1))=.80
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+7" "RenderType"="Transparent" }
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
   half4 _BaseColor,_FoamColor;
   float _FlowSpeed,_DetailStrength,_LateralStrength,_FootFoam,_Opacity;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half progress:TEXCOORD1;half fog:TEXCOORD2;};
   V Vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.progress=saturate(a.color.r);o.fog=ComputeFogFactor(o.p.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
   float Thread(float phase,float thinness)
   {
    float d=abs(sin(phase));float aa=max(.015,fwidth(phase)*.55);
    return 1-smoothstep(thinness,thinness+aa,d);
   }
   half4 Frag(V i):SV_Target
   {
    float t=_Time.y*_FlowSpeed;
    // Features advect toward INCREASING authored uv.y, i.e. down the actual path.
    float along=i.uv.y-t*.72;
    float warpA=Noise(float2(i.uv.x*5.3+1.7,along*2.8));
    float warpB=Noise(float2(i.uv.x*11.1-4.2,along*5.7+9.1));
    float wander=((warpA-.5)*.038+sin(i.uv.y*5.1-t*2.2+warpB*2.0)*.012)*_LateralStrength;
    float x=i.uv.x+wander;
    float breakup=Noise(float2(x*17.7,along*7.1));
    float phaseA=x*86.0+(warpA-.5)*2.5+sin(along*3.4)*.72;
    float phaseB=x*181.0+(warpB-.5)*3.2+along*2.3;
    float phaseC=x*347.0+(warpA-warpB)*4.1-along*4.7;
    float fadeB=1-smoothstep(.55,1.6,fwidth(phaseB));
    float fadeC=1-smoothstep(.50,1.5,fwidth(phaseC));
    float threads=Thread(phaseA,.22)*(.32+.68*breakup);
    threads+=Thread(phaseB,.16)*fadeB*.52*smoothstep(.24,.78,warpB);
    threads+=Thread(phaseC,.13)*fadeC*.23*smoothstep(.38,.82,breakup);
    threads=saturate(threads)*_DetailStrength;
    float fallingPatch=smoothstep(.28,.78,Noise(float2(x*9.2,along*11.4+3.0)));
    float foot=smoothstep(.74,.98,i.progress)*(.35+.65*fallingPatch)*_FootFoam;
    float lip=(1-smoothstep(.01,.13,i.progress))*fallingPatch*.18;
    // Width-edge fade uses ORIGINAL uv.x: no geometry displacement or footprint widening.
    float edge=smoothstep(.002,.042,i.uv.x)*smoothstep(.002,.042,1-i.uv.x);
    float bodyPatch=.90+.17*warpA+.06*warpB;
    half3 body=_BaseColor.rgb*bodyPatch;
    float aeration=saturate(threads*.70+foot+lip);
    half3 color=lerp(body,_FoamColor.rgb,aeration);
    float glint=Thread(phaseB+1.2,.10)*fadeB*pow(saturate(breakup),5)*.045;
    color=saturate(color+half3(.45,.58,.60)*glint);
    float alpha=edge*lerp(_Opacity,.96,saturate(aeration));
    return half4(MixFog(color,i.fog),alpha);
   }
   ENDHLSL
  }
 }
}
