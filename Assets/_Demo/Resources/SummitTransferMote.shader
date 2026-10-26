Shader "Own/Summit Transfer Mote" {
 SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass {Blend SrcAlpha One ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A {float4 p:POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.c=i.c;o.uv=i.uv;return o;}
   half4 frag(V i):SV_Target {float2 d=abs(i.uv-.5)*2;float a=1-smoothstep(.45,1,max(d.x,d.y));return half4(i.c.rgb*1.6,i.c.a*a);}
   ENDHLSL
  }
 } Fallback Off
}
