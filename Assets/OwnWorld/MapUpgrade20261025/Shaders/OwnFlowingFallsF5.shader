Shader "KingOfGraphic/Own/Candidate/FlowingFallsF5"
{
 Properties { _BaseColor("Clear water",Color)=(.12,.43,.51,1) _FoamColor("Aerated water",Color)=(.78,.92,.95,1) _FlowSpeed("Flow speed",Float)=1.2 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+7" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor,_FoamColor;float _FlowSpeed;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half progress:TEXCOORD1;half fog:TEXCOORD2;};
   V Vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.progress=a.color.r;o.fog=ComputeFogFactor(o.p.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
   half4 Frag(V i):SV_Target
   {
    float t=_Time.y*_FlowSpeed;float y=i.uv.y-t;
    float drift=(Noise(float2(i.uv.y*.8-t*.45,9.3))-.5)*.035;
    float x=i.uv.x+drift;
    float ribbon=Noise(float2(x*28,y*.65));
    float fine=Noise(float2(x*61,y*1.8));
    float streams=smoothstep(.49,.76,ribbon)*(.45+.55*fine);
    float pulse=Noise(float2(x*13,y*3.4));
    float broken=smoothstep(.39,.77,pulse);
    float foot=smoothstep(.72,.99,i.progress)*broken*.65;
    float lip=(1-smoothstep(.01,.10,i.progress))*broken*.22;
    float foam=saturate(streams*.60+foot+lip);
    float edge=smoothstep(.002,.047,x)*smoothstep(.002,.047,1-x);
    half3 body=_BaseColor.rgb*lerp(.89,1.15,ribbon);
    half3 color=lerp(body,_FoamColor.rgb,foam);
    color+=half3(.17,.26,.28)*pow(saturate(fine),8)*.35;
    return half4(MixFog(color,i.fog),edge*lerp(.68,.96,foam));
   }
   ENDHLSL
  }
 }
}
