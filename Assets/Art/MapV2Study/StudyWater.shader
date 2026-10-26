Shader "OverworldV2/StudyWater"
{
 Properties
 {
  _Shallow("Shallow",Color)=(0.19,0.72,0.65,1)
  _Deep("Deep",Color)=(0.025,0.28,0.41,1)
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10"}
  Pass
  {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Shallow,_Deep;
   CBUFFER_END
   struct A{float4 pos:POSITION;half4 color:COLOR;};
   struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;half depth:TEXCOORD1;float4 shadow:TEXCOORD2;half fog:TEXCOORD3;};
   V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.pos.xyz);o.pos=p.positionCS;o.world=p.positionWS;o.depth=v.color.r*6;o.shadow=GetShadowCoord(p);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target
   {
    clip(i.depth-.025);
    float t=_Time.y;float2 p=i.world.xz;
    float wave=sin(p.x*1.6+p.y*.58+t*.4)*sin(p.x*.6-p.y*1.4-t*.3);
    float ripple=pow(saturate(wave),7)*.15;
    float foam=(1-smoothstep(.03,.55,i.depth))*(.45+.55*saturate(sin(p.x*4+p.y*6+t*1.4)));
    half3 n=normalize(half3(cos(p.x*.75+t*.5)*.035,1,sin(p.y*.8-t*.35)*.045));
    Light l=GetMainLight(TransformWorldToShadowCoord(i.world));half3 view=GetWorldSpaceNormalizeViewDir(i.world);
    half fresnel=pow(1-saturate(dot(n,view)),4);
    half shine=pow(saturate(dot(n,normalize(view+l.direction))),180)*.32;
    half3 water=lerp(_Shallow.rgb,_Deep.rgb,saturate(i.depth/4));
    water=water*(SampleSH(n)+l.color*.65*lerp(.65,1,l.shadowAttenuation));
    water+=ripple*lerp(.9,.12,saturate(i.depth/4));
    water=lerp(water,half3(.61,.79,.88),fresnel*.4);
    water=lerp(water,half3(.64,.83,.77),foam*.25)+shine*l.color;
    return half4(MixFog(water,i.fog),1);
   }
   ENDHLSL
  }
 }
}
