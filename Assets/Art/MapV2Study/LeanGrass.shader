Shader "OverworldV2/LeanGrass"
{
 Properties { _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct A { float4 pos:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 pos:SV_POSITION; half4 color:COLOR; float3 world:TEXCOORD0; half fog:TEXCOORD1; };
   V vert(A i)
   {
    UNITY_SETUP_INSTANCE_ID(i); V o;
    float3 p=TransformObjectToWorld(i.pos.xyz);
    p.x+=sin(_Time.y*1.7+p.x*.8+p.z*.45)*.08*i.uv.y;
    o.world=p;o.pos=TransformWorldToHClip(p);o.fog=ComputeFogFactor(o.pos.z);
    o.color=i.color*_BaseColor;return o;
   }
   half4 frag(V i):SV_Target
   {
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    half3 lit=SampleSH(half3(0,1,0))+sun.color*(.35+.45*saturate(sun.direction.y))*sun.shadowAttenuation;
    return half4(MixFog(i.color.rgb*lit,i.fog),1);
   }
   ENDHLSL
  }
 }
}
