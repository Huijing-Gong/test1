Shader "Boss/DragonSurface" {
 Properties { _Color("Color",Color)=(1,.2,.01,1) _Phase("Phase",Float)=0 }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};
 struct v2f {float4 pos:SV_POSITION;float3 n:TEXCOORD0;float3 v:TEXCOORD1;float2 uv:TEXCOORD2;};
 float4 _Color;float _Phase;
 v2f vert(appdata a){v2f o;o.pos=UnityObjectToClipPos(a.vertex);o.n=UnityObjectToWorldNormal(a.normal);o.v=WorldSpaceViewDir(a.vertex);o.uv=a.uv;return o;}
 float4 frag(v2f i):SV_Target {float rim=pow(1-abs(dot(normalize(i.n),normalize(i.v))),2);float wave=sin(i.uv.x*47+sin(i.uv.y*32)*3-_Phase*8);float vein=smoothstep(.7,1,wave);float heat=.12+rim*1.4+vein*.7;float3 col=lerp(float3(.12,.006,.001),float3(2,.43,.018),saturate(heat));col+=float3(1.3,.65,.11)*rim;return float4(col,_Color.a);}
 ENDCG }
 }
}
