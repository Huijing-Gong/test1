Shader "Boss/BlackGoldInk" {Properties {_Phase("Phase",Float)=0} SubShader {Tags {"Queue"="Transparent-1" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Pass {CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct app {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};float _Phase;
v2f vert(app v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
float4 frag(v2f i):SV_Target {float n=noise(i.uv*float2(8,3)-float2(_Phase*4,0));float wisps=noise(i.uv*float2(21,7)-float2(_Phase*7,0));float cross=saturate(1-abs(i.uv.y*2-1));float ends=smoothstep(0,.10,i.uv.x)*(1-smoothstep(.68,1,i.uv.x));float a=pow(cross,1.2)*smoothstep(.08,.62,n*.75+wisps*.35+cross*.25)*ends;float3 color=i.color.rgb;return float4(color,a*i.color.a);}
ENDCG
}}}