Shader "Own/MapUpgrade20261025/CarvedLimestone"
{
 Properties
 {
  _BaseColor("Stone colour",Color)=(0.72,0.64,0.46,1)
  _GroutColor("Joint colour",Color)=(0.30,0.27,0.20,1)
  _TileScale("Tiles per metre",Float)=0.65
  _JointWidth("Joint width",Range(0.002,0.05))=0.012
  _Variation("Tile variation",Range(0,0.3))=0.10
  _Smoothness("Smoothness",Range(0,1))=0.25
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass
  {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor,_GroutColor;
    float _TileScale,_JointWidth,_Variation,_Smoothness;
   CBUFFER_END
   struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
   Varyings Vert(Attributes a){Varyings o;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);o.positionWS=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(a.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 Frag(Varyings i):SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(i);
    half3 n=normalize(i.normalWS);
    float2 p=i.positionWS.xz*_TileScale;
    if(abs(n.y)<0.5)p=float2(abs(n.x)>abs(n.z)?i.positionWS.z:i.positionWS.x,i.positionWS.y)*_TileScale;
    p.x+=step(0.5,frac(floor(p.y)*0.5))*0.5;
    float2 c=floor(p),f=frac(p),d=min(f,1-f);
    float e=min(d.x,d.y),aa=max(fwidth(e),0.001);
    float joint=1-smoothstep(_JointWidth,_JointWidth+aa,e);
    float bevel=smoothstep(_JointWidth,_JointWidth+0.027,e);
    half3 albedo=_BaseColor.rgb*(1+(Hash(c)-.5)*_Variation);
    albedo*=lerp(.86,1,bevel);
    albedo=lerp(albedo,_GroutColor.rgb,joint*.70);
    float grain=Hash(floor(i.positionWS.xz*70))-.5;
    albedo*=1+grain*.025;
    Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
    half3 color=albedo*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.distanceAttenuation*light.shadowAttenuation);
    half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS),h=normalize(light.direction+v);
    color+=light.color*pow(saturate(dot(n,h)),lerp(8,96,_Smoothness))*.025*light.shadowAttenuation;
    return half4(MixFog(color,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}
