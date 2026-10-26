// OUTPUT ONLY. Compatible with current F5 ribbon: uv.x=width0..1,
// uv.y=authored along-distance (NOT normalized progress); COLOR.r=each fall progress0..1.
Shader "KingOfGraphic/Own/Candidate/FlowingFallsF14"
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
    // Broad advected sheets: low cross-width frequency, varying thickness; no periodic sine comb.
    float warpA=Noise(float2(i.uv.x*3.1,along*.55));
    float warpB=Noise(float2(i.uv.x*7.7+4.2,along*1.4+9.1));
    float x=i.uv.x+(warpA-.5)*.075*_LateralStrength;
    float sheetField=Noise(float2(x*5.2+(warpB-.5)*1.6,along*.82));
    float sheets=smoothstep(.27,.73,sheetField);
    // Fine longitudinal fibres ride inside broad clear sheets. Noise, never equal-spaced comb lines.
    float fibreWarp=Noise(float2(x*6.3,along*.33));
    float fibres=Noise(float2(x*22.5+(fibreWarp-.5)*2.4,along*.78));
    float fine=Noise(float2(x*47.7+warpA*3.1,along*1.35+7.1));
    float lengthGate=Noise(float2(x*10.1,along*.48+4.2));
    float fibreFoam=smoothstep(.42,.74,fibres)*smoothstep(.27,.65,lengthGate);
    float fineFoam=smoothstep(.56,.80,fine)*smoothstep(.35,.70,fibres);
    float aeration=saturate((fibreFoam*.72+fineFoam*.25+sheets*.06)*_DetailStrength);
    float brokenFoot=smoothstep(.42,.76,Noise(float2(x*21.2,along*2.7)));
    float foot=smoothstep(.80,.99,i.progress)*brokenFoot*_FootFoam*.65;
    float lip=(1-smoothstep(.01,.10,i.progress))*fibreFoam*.15;
    aeration=saturate(aeration+foot+lip);
    float edgeWidth=.018+(Noise(float2(along*1.8,3.7))-.5)*.012;
    float edge=smoothstep(0,edgeWidth,i.uv.x)*smoothstep(0,edgeWidth,1-i.uv.x);
    half3 body=_BaseColor.rgb*lerp(.88,1.13,sheets);
    half3 color=lerp(body,_FoamColor.rgb,aeration);
    float alpha=edge*lerp(_Opacity*.88,.97,saturate(aeration+sheets*.2));
    return half4(MixFog(color,i.fog),alpha);
   }
   ENDHLSL
  }
 }
}
