Shader "Companions/Paimon Soft Toon"
{
 Properties
 {
  [MainTexture] _BaseMap("Original albedo",2D)="white"{}
  [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
  _CapeBase("Cape blue gradient",2D)="black"{}
  _Cape("Cape layering",Float)=0
  _AlphaClip("Alpha clip",Float)=0
  _Cutoff("Cutoff",Range(0,1))=0.3
  _Cull("Cull",Float)=0
 }
 SubShader
 {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
  Cull [_Cull]
  Pass
  {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   TEXTURE2D(_CapeBase); SAMPLER(sampler_CapeBase);
   CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST; half4 _BaseColor; float _Cape; float _AlphaClip; float _Cutoff; float _Cull;
   CBUFFER_END
   struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0;};
   struct V {float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; float3 positionWS:TEXCOORD2;};
   V vert(A i) {V o; VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz); o.positionCS=p.positionCS; o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
   half4 frag(V i):SV_Target
   {
    half4 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
    if(_AlphaClip>.5)clip(albedo.a-_Cutoff);
    if(_Cape>.5)albedo.rgb=saturate(SAMPLE_TEXTURE2D(_CapeBase,sampler_CapeBase,i.uv).rgb+albedo.rgb);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
    half ndl=dot(normalize(i.normalWS),light.direction);
    half lit=smoothstep(-.18,.35,ndl)*lerp(.55,1,light.shadowAttenuation);
    // Bounded diffuse preserves painted colors under bright scene lights.
    half3 shade=lerp(half3(.74,.73,.82),half3(1,1,1),lit);
    return half4(albedo.rgb*shade,albedo.a);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
