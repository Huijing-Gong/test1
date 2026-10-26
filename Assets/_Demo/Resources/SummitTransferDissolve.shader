Shader "Own/Summit Transfer Dissolve"
{
 Properties {
  _BaseMap("Surface",2D)="white" {} _BaseColor("Tint",Color)=(1,1,1,1)
  _Cutoff("Cutout",Range(0,1))=.1 _Progress("Removed",Float)=0
  _Centre("Centre and radius",Vector)=(0,0,0,10000)
  _LocalMask("Local mask",Float)=0 _Floor("Floor",Float)=0
  _EdgeColor("Edge",Color)=(1,.65,.2,1)
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
  Cull Off
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
  CBUFFER_START(UnityPerMaterial)
  float4 _BaseMap_ST,_BaseColor,_Centre,_EdgeColor;
  float _Cutoff,_Progress,_LocalMask,_Floor;
  CBUFFER_END
  struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
  struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;};
  V vert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.fog=ComputeFogFactor(o.p.z);return o;}
  float hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
  float removal(float3 w){
   float radius=length(w.xz-_Centre.xz);
   float cell=hash(floor(w*5));
   float order=lerp(cell, .15*cell+.85*(1-saturate(radius/max(_Centre.w,1))),_Floor);
   float progress=_Progress;
   if(_LocalMask>.5 && (radius>_Centre.w || w.y<_Centre.y-6))progress=-1;
   return order-progress;
  }
  ENDHLSL
  Pass {Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   half4 frag(V i):SV_Target {
    half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;clip(tex.a-_Cutoff);
    float edge=removal(i.w);clip(edge);
    Light light=GetMainLight();float3 n=normalize(i.n);
    float3 col=tex.rgb*(SampleSH(n)+light.color*(.12+.88*saturate(dot(n,light.direction))));
    col+=_EdgeColor.rgb*(1-smoothstep(0,.07,edge))*2.4*step(.001,_Progress)*step(_Progress,1);
    return half4(MixFog(col,i.fog),1);
   }
   ENDHLSL
  }
  Pass {Tags {"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment depth
   #pragma multi_compile_instancing
   half4 depth(V i):SV_Target {clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a-_Cutoff);clip(removal(i.w));return 0;}
   ENDHLSL
  }
 } Fallback Off
}
