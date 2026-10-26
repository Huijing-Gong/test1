// OUTPUT-ONLY own candidate; not Unity compiled. Existing shaders untouched.
Shader "Own/MapUpgrade20261025/IvoryFoldCliffF1"
{
 Properties {

  _BaseColor("Warm ivory rock",Color)=(.84,.81,.70,1)
  _CoolColor("Quiet mineral tint",Color)=(.74,.77,.73,1)
  _GoldColor("Matte pale gold fissure",Color)=(.56,.45,.23,1)
  _FoldScale("Large colour patch per metre",Float)=.07
  _Variation("Quiet stone variation",Range(0,.25))=.065
  _FacetStrength("Actual mesh face normal",Range(0,1))=.7
  _CrackScale("Crack cells per metre",Float)=.12
  _CrackWidth("Crack width in cell units",Range(.002,.05))=.008
  _GoldCoverage("Sparse selected crack cells",Range(0,.4))=.12
  _GoldStrength("Mineral seam colour weight",Range(0,1))=.72
  _AmbientFloor("Minimum ambient light",Range(0,.2))=.06
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Back ZWrite On
  HLSLINCLUDE

   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor,_CoolColor,_GoldColor;
    float _FoldScale,_Variation,_FacetStrength,_CrackScale,_CrackWidth,_GoldCoverage,_GoldStrength,_AmbientFloor;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 n:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   struct D {float4 p:SV_POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.fog=ComputeFogFactor(o.p.z);return o;}
   D DepthVert(A a){D o=(D)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.p=TransformObjectToHClip(a.p.xyz);return o;}
   float3 _LightDirection;
   float3 _LightPosition;
   D ShadowVert(A a){D o=(D)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);float3 w=TransformObjectToWorld(a.p.xyz);float3 n=TransformObjectToWorldNormal(a.n);
   #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 dir=normalize(_LightPosition-w);
   #else
    float3 dir=_LightDirection;
   #endif
    o.p=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(w,n,dir)));return o;}
   half DepthFrag(D i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return i.p.z;}
   half4 ShadowFrag(D i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);return 0;}
   float OwnSurfaceHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float OwnSurfaceNoise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(OwnSurfaceHash(a),OwnSurfaceHash(a+float2(1,0)),f.x),lerp(OwnSurfaceHash(a+float2(0,1)),OwnSurfaceHash(a+1),f.x),f.y);}
   half3 OwnSurfaceDiffuse(half3 albedo,half3 n,float3 w){
   #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 sc=ComputeScreenPos(TransformWorldToHClip(w));
   #else
    float4 sc=TransformWorldToShadowCoord(w);
   #endif
    Light sun=GetMainLight(sc);
    half3 ambient=max(SampleSH(n),half3(_AmbientFloor,_AmbientFloor,_AmbientFloor));
    return albedo*(ambient+sun.color*saturate(dot(n,sun.direction))*sun.distanceAttenuation*sun.shadowAttenuation);
   }

   float2 CellOffset(float2 p){return float2(OwnSurfaceHash(p),OwnSurfaceHash(p+37.17));}
   // Nine 2D cells on the dominant rock plane: large irregular seams, no brick rows.
   float2 CrackField(float2 p){float2 cell=floor(p),f=frac(p),chosen=0;float first=100,second=100;
    [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){
     float2 c=float2(x,y),q=c+lerp(.18,.82,CellOffset(cell+c))-f;float dist=dot(q,q);
     if(dist<first){second=first;first=dist;chosen=cell+c;}else second=min(second,dist);
    }
    return float2(sqrt(second)-sqrt(first),OwnSurfaceHash(chosen+91.73));
   }
   half4 Frag(V i):SV_Target{
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 smoothN=normalize(i.n);float3 face=cross(ddy(i.w),ddx(i.w));face*=rsqrt(max(dot(face,face),1e-12));if(dot(face,smoothN)<0)face=-face;
    half3 n=normalize(lerp(smoothN,face,_FacetStrength));
    half3 axis=abs(smoothN);float2 p=axis.y>max(axis.x,axis.z)?i.w.xz:(axis.x>axis.z?i.w.zy:i.w.xy);
    float broad=OwnSurfaceNoise(p*_FoldScale+float2(5.1,17.3));
    half3 albedo=_BaseColor.rgb*(1+(broad-.5)*_Variation);
    albedo=lerp(albedo,_CoolColor.rgb,smoothstep(.65,.92,OwnSurfaceNoise(p*_FoldScale*.57-8.2))*_Variation);
    float2 field=CrackField(p*_CrackScale);float aa=max(fwidth(field.x),.001);
    float crackLine=1-smoothstep(_CrackWidth,_CrackWidth+aa,field.x);
    float selected=smoothstep(1-_GoldCoverage-.015,1-_GoldCoverage+.015,field.y)*step(.001,_GoldCoverage);
    float breakup=smoothstep(.26,.47,OwnSurfaceNoise(p*.49+float2(17.2,-9.1)));
    float distantFade=1-smoothstep(.12,.38,length(fwidth(p*_CrackScale)));
    float seam=crackLine*selected*breakup*distantFade;
    albedo=lerp(albedo,_GoldColor.rgb,seam*_GoldStrength);
    // Matte mineral inlay: no emission, metallic response or polished gold highlight.
    return half4(MixFog(OwnSurfaceDiffuse(albedo,n,i.w),i.fog),1);
   }

  ENDHLSL

  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   ENDHLSL
  }
  Pass {
   Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
   ZWrite On ZTest LEqual ColorMask 0
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ShadowVert
   #pragma fragment ShadowFrag
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   ENDHLSL
  }
  Pass {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
   ZWrite On ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex DepthVert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   ENDHLSL
  }
 }
 FallBack Off
}
