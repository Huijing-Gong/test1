Shader "OverworldV2/ReliefWater"
{
 Properties
 {
  _Shallow("Shallow",Color)=(0.10,0.60,0.56,1)
  _Deep("Deep",Color)=(0.025,0.25,0.37,1)
  _DepthStart("Depth colour start (metres)",Float)=0.15
  _DepthEnd("Depth colour end (metres)",Float)=3.5
  _FoamColor("Shoreline colour",Color)=(0.64,0.83,0.77,1)
  _FoamStart("Shoreline depth start (metres)",Float)=0.035
  _FoamEnd("Shoreline depth end (metres)",Float)=0.35
  _FoamStrength("Shoreline strength",Range(0,1))=0.5
  _RippleStrength("Ripple brightness",Range(0,0.3))=0.08
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
   half4 _Shallow,_Deep,_FoamColor;
   float _DepthStart,_DepthEnd,_FoamStart,_FoamEnd,_FoamStrength,_RippleStrength;
   CBUFFER_END
   struct A{float4 pos:POSITION;half4 color:COLOR;};
   struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;half depth:TEXCOORD1;float4 shadow:TEXCOORD2;half fog:TEXCOORD3;};
   // The existing mesh stores lake depth / 6 in vertex red. Positions and
   // shoreline clipping stay identical to StudyWater; no scene-depth texture.
   V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.pos.xyz);o.pos=p.positionCS;o.world=p.positionWS;o.depth=v.color.r*6;o.shadow=GetShadowCoord(p);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target
   {
    clip(i.depth-.025);
    float depthBlend=smoothstep(_DepthStart,max(_DepthStart+.001,_DepthEnd),i.depth);
    float t=_Time.y;float2 p=i.world.xz;
    float wave=sin(p.x*1.6+p.y*.58+t*.4)*sin(p.x*.6-p.y*1.4-t*.3);
    float ripple=pow(saturate(wave),7)*_RippleStrength;
    float foam=(1-smoothstep(_FoamStart,max(_FoamStart+.001,_FoamEnd),i.depth))*(.45+.55*saturate(sin(p.x*4+p.y*6+t*1.4)));
    half3 n=normalize(half3(cos(p.x*.75+t*.5)*.035,1,sin(p.y*.8-t*.35)*.045));
    Light l=GetMainLight(TransformWorldToShadowCoord(i.world));half3 view=GetWorldSpaceNormalizeViewDir(i.world);
    half fresnel=pow(1-saturate(dot(n,view)),4);
    half shine=pow(saturate(dot(n,normalize(view+l.direction))),180)*.32;
    half3 water=lerp(_Shallow.rgb,_Deep.rgb,depthBlend);
    water=water*(SampleSH(n)+l.color*.65*lerp(.65,1,l.shadowAttenuation));
    water+=ripple*lerp(.9,.12,depthBlend);
    water=lerp(water,half3(.61,.79,.88),fresnel*.4);
    water=lerp(water,_FoamColor.rgb,foam*saturate(_FoamStrength))+shine*l.color;
    return half4(MixFog(water,i.fog),1);
   }
   ENDHLSL
  }
 }
}
