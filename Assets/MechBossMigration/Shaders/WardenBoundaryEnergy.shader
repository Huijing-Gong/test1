Shader "Warden/Boundary Energy"
{
 Properties { _BaseColor("Color",Color)=(1,1,1,1) }
 SubShader {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Blend SrcAlpha One
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes {float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0;};
   struct Varyings {float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
   CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
   Varyings vert(Attributes a){Varyings o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.color=a.color*_BaseColor;o.uv=a.uv;return o;}
   half4 frag(Varyings i):SV_Target {half edge=saturate(1-abs(i.uv.y*2-1));return half4(i.color.rgb,i.color.a*pow(edge,.6));}
   ENDHLSL
  }
 }
}
