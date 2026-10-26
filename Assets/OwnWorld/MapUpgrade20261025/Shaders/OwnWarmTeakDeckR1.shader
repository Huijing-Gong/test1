// OUTPUT-ONLY own candidate; not Unity compiled. Existing shaders untouched.
Shader "Own/MapUpgrade20261025/WarmTeakDeckR1"
{
 Properties {
  _BaseColor("Warm teak",Color)=(.58,.34,.17,1)
  _JointColor("Quiet plank gap",Color)=(.20,.12,.065,1)
  _BoardWidth("Board width m",Float)=.28
  _BoardLength("Board length m",Float)=2.8
  _BoardYaw("Board direction degrees",Float)=0
  _JointWidth("Gap width m",Range(.001,.02))=.005
  _Variation("Board tone variation",Range(0,.3))=.10
  _GrainStrength("Fine grain contrast",Range(0,.15))=.045
  _AmbientFloor("Minimum ambient",Range(0,.2))=.07
 }

 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Back ZWrite On
  HLSLINCLUDE

   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor,_JointColor;
    float _BoardWidth,_BoardLength,_BoardYaw,_JointWidth,_Variation,_GrainStrength,_AmbientFloor;
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

   half4 Frag(V i):SV_Target{
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float angle=radians(_BoardYaw);float2 p=float2(dot(i.w.xz,float2(cos(angle),sin(angle))),dot(i.w.xz,float2(-sin(angle),cos(angle))));
    float bw=max(.05,_BoardWidth),bl=max(.5,_BoardLength);float row=floor(p.y/bw);
    float along=p.x+OwnSurfaceHash(float2(row,4.1))*bl*.8;float board=floor(along/bl);
    float edgeAcross=min(frac(p.y/bw),1-frac(p.y/bw))*bw;
    float edgeAlong=min(frac(along/bl),1-frac(along/bl))*bl;
    float edge=min(edgeAcross,edgeAlong);float aa=max(fwidth(edge),.0007);
    float joint=1-smoothstep(_JointWidth,_JointWidth+aa,edge);
    float tone=OwnSurfaceHash(float2(board,row));half3 albedo=_BaseColor.rgb*(1+(tone-.5)*_Variation);
    float grainPhase=OwnSurfaceNoise(float2(p.x*.55,row*2.7))*5;
    float grain=sin(p.y*230+grainPhase+sin(p.x*1.7)*1.1)*.55+sin(p.y*103+grainPhase*.7)*.25;
    float fade=1-smoothstep(.035,.16,length(fwidth(p)));
    albedo*=1+grain*_GrainStrength*fade;
    albedo=lerp(albedo,_JointColor.rgb,joint*.72);
    return half4(MixFog(OwnSurfaceDiffuse(albedo,normalize(i.n),i.w),i.fog),1);
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
