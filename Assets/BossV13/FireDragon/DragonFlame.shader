Shader "Boss/DragonFlame" {
 Properties { _Color("Color",Color)=(1,.25,.02,1) _Phase("Phase",Float)=0 _Soft("Soft",Float)=1 }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend SrcAlpha One ZWrite Off Cull Off
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
 struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
 float4 _Color;float _Phase,_Soft;
 v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 fixed4 frag(v2f i):SV_Target {float n=noise(i.uv*float2(12,5)-float2(_Phase*5,_Phase)); float edge=pow(saturate(1-abs(i.uv.y*2-1)),.8);float a=lerp(1,edge*smoothstep(.12,.62,n+.2),_Soft);return float4(_Color.rgb*i.color.rgb,_Color.a*i.color.a*a);}
 ENDCG }
 }
}
