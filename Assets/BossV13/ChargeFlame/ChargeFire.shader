Shader "Boss/ChargeFire" {
 Properties {_Color("Color",Color)=(1,1,1,1) _Phase("Time",Float)=0 _Flat("Ground",Float)=0}
 SubShader {Tags {"Queue"="Transparent+10" "RenderType"="Transparent"} Blend SrcAlpha One ZWrite Off Cull Off
 Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};float4 _Color;float _Phase,_Flat;
 v2f vert(appdata a){v2f o;o.pos=UnityObjectToClipPos(a.vertex);o.uv=a.uv;o.color=a.color;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 float4 frag(v2f i):SV_Target{float n=noise(i.uv*float2(5,9)-float2(_Phase*.7,_Phase*5));n+=noise(i.uv*float2(12,19)-float2(0,_Phase*8))*.35;float centre=1-abs(i.uv.x*2-1);float shape=saturate(centre*(1-i.uv.y*.65));float a=smoothstep(.22,.60,shape+n*.38-i.uv.y*.40)*smoothstep(0,.12,i.uv.y)*(1-smoothstep(.65,1,i.uv.y));float heat=saturate(shape*.85+n*.35-i.uv.y*.35);float3 color=lerp(float3(1,.035,.001),float3(1.7,.48,.015),heat);color=lerp(color,float3(2.3,1.5,.45),smoothstep(.76,.96,heat));if(_Flat>.5){float r=length((i.uv-.5)*2);a=pow(saturate(1-r),1.5)*(.65+n*.35);color=lerp(float3(1,.07,.001),float3(2,.75,.1),saturate(1-r*1.5));}return float4(color*_Color.rgb*i.color.rgb,a*_Color.a*i.color.a);}
 ENDCG}
 }}
