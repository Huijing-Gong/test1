Shader "Boss/DeathDust" {SubShader {Tags {"Queue"="Transparent"} Blend SrcAlpha One ZWrite Off Cull Off Pass {CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct app {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
v2f vert(app v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
fixed4 frag(v2f i):SV_Target {float r=length(i.uv*2-1);return float4(i.color.rgb,i.color.a*pow(saturate(1-r),2));}
ENDCG
}}}
