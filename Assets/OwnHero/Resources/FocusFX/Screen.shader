Shader "Hero/Link Time Screen" {
Properties { _MainTex("Texture",2D)="white"{} _Strength("Strength",Float)=1 _Pulse("Pulse",Float)=0 _Travel("Travel",Float)=0 _Clock("Clock",Float)=0 }
SubShader {Tags{"Queue"="Overlay"} Pass {Blend SrcAlpha OneMinusSrcAlpha ZTest Always ZWrite Off Cull Off
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float _Strength,_Pulse,_Travel,_Clock;
fixed4 frag(v2f_img i):SV_Target{
float2 p=i.uv-.5;float r=length(p*float2(1.4,1));float a=atan2(p.y,p.x);
float rays=pow(saturate(sin(a*93+sin(a*17)*3)),35)*smoothstep(.2,.72,r);
float streak=pow(saturate(sin(r*48-_Clock*18+a*4)),16)*rays;
float edge=smoothstep(.3,.78,r);float scan=.5+.5*sin(i.uv.y*900+_Clock*5);
float alpha=_Strength*(edge*.20+streak*(.16+_Travel*.35)+edge*scan*.015)+_Pulse*.23;
return fixed4(lerp(float3(.025,.13,.25),float3(.55,.94,1),saturate(streak+_Pulse)),saturate(alpha));
}
ENDCG
}}}
