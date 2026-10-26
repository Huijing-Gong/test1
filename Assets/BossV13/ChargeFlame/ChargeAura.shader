Shader "Boss/ChargeAura" {Properties {_Strength("Strength",Float)=0 _Phase("Time",Float)=0} SubShader {Tags {"Queue"="Transparent+5"} Blend SrcAlpha One ZWrite Off Cull Back
 Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata{float4 vertex:POSITION;float3 normal:NORMAL;};struct v2f{float4 pos:SV_POSITION;float3 normal:TEXCOORD0;float3 view:TEXCOORD1;float3 world:TEXCOORD2;};float _Strength,_Phase;
 v2f vert(appdata a){v2f o;a.vertex.xyz+=a.normal*.000025;o.pos=UnityObjectToClipPos(a.vertex);o.normal=UnityObjectToWorldNormal(a.normal);o.view=WorldSpaceViewDir(a.vertex);o.world=mul(unity_ObjectToWorld,a.vertex).xyz;return o;}
 float4 frag(v2f i):SV_Target{float rim=pow(1-abs(dot(normalize(i.normal),normalize(i.view))),2.7);float streak=pow(saturate(sin(i.world.y*42+sin(i.world.x*30)*2-_Phase*14)),14);float a=(rim*.8+streak*.13)*_Strength;return float4(1.8,.62,.055,a);}
 ENDCG} }}
