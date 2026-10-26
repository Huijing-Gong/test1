Shader "OverworldV2/StudyGround"
{
 Properties
 {
  _GrassTex("Grass",2D)="white"{}
  _PathTex("Path",2D)="white"{}
  _StoneTex("Stone",2D)="white"{}
  _GrassTint("Grass tint",Color)=(0.85,0.94,0.67,1)
  _SandTint("Shore tint",Color)=(0.87,0.79,0.57,1)
  _Tiling("World tile size",Float)=5
  [HideInInspector] _BaseMap("Shadow alpha",2D)="white"{}
  [HideInInspector] _BaseColor("Shadow tint",Color)=(1,1,1,1)
  [HideInInspector] _Cull("Cull",Float)=2
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass
  {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_GrassTex);SAMPLER(sampler_GrassTex);
   TEXTURE2D(_PathTex);SAMPLER(sampler_PathTex);
   TEXTURE2D(_StoneTex);SAMPLER(sampler_StoneTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _GrassTint,_SandTint;float _Tiling;
   CBUFFER_END
   struct A{float4 pos:POSITION;float3 normal:NORMAL;half4 color:COLOR;};
   struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;half4 color:COLOR;float4 shadow:TEXCOORD2;half fog:TEXCOORD3;};
   V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.pos.xyz);o.pos=p.positionCS;o.world=p.positionWS;o.normal=TransformObjectToWorldNormal(v.normal);o.color=v.color;o.shadow=GetShadowCoord(p);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target
   {
    float2 uv=i.world.xz/max(.1,_Tiling);
    half3 grass=SAMPLE_TEXTURE2D(_GrassTex,sampler_GrassTex,uv).rgb;
    grass=lerp(dot(grass,half3(.2126,.7152,.0722)).xxx,grass,.66)*_GrassTint.rgb;
    half3 path=SAMPLE_TEXTURE2D(_PathTex,sampler_PathTex,uv*.85).rgb;
    half3 stone=SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,uv).rgb;
    half3 ground=lerp(grass,path,saturate(i.color.r));
    ground=lerp(ground,lerp(_SandTint.rgb,stone*_SandTint.rgb,.22),saturate(i.color.g));
    ground*=lerp(.85,1.12,i.color.b);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 n=normalize(i.normal);
    half3 lit=SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation;
    return half4(MixFog(ground*lit,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
