Shader "Own/Summit Transfer Arena Sky" {
Properties { _TransferBlend("Transfer veil",Range(0,1))=0
 _Assembly("Black sun assembly",Range(0,1))=1
 _SkyTop("Upper sky",Color)=(.018,.026,.049,1)
 _SkyHorizon("Horizon",Color)=(.16,.19,.25,1)
 _CloudLight("Cloud light",Color)=(.45,.47,.51,1)
 _CloudShadow("Cloud shadow",Color)=(.055,.069,.105,1)
 _CloudCoverage("Cloud coverage",Range(0,1))=.66
 _CloudContrast("Cloud contrast",Range(.1,3))=1.35
 _CloudScale("Cloud field scale",Float)=4.4
 _EclipseDirection("Eclipse world direction",Vector)=(0,.24,1,0)
 _EclipseSize("Eclipse angular radius",Range(.015,.25))=.108
 _CoronaWidth("Corona width",Range(.001,.04))=.006
 _CoronaColor("Ivory gold corona",Color)=(1,.91,.75,1)
 _CoronaStrength("Corona strength",Range(0,4))=2
}
SubShader { Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"} Cull Off ZWrite Off
Pass { HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A { float4 vertex:POSITION; }; struct V { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
CBUFFER_START(UnityPerMaterial)
float4 _SkyTop,_SkyHorizon,_CloudLight,_CloudShadow,_EclipseDirection,_CoronaColor;
float _CloudCoverage,_CloudContrast,_CloudScale,_EclipseSize,_CoronaWidth,_CoronaStrength;
CBUFFER_END
// Driven only by the arena simulation; no wall-clock animation during pause.
float _BlackSunAtmosphere;
float hash21(float2 p) { p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y); }
float noise(float2 p) {float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
float cloudField(float2 p) {float n=0,a=.52;[unroll]for(int k=0;k<5;k++){n+=a*noise(p);p=mul(float2x2(.8,-.6,.6,.8),p)*2.07+7.1;a*=.49;}return n;}
V vert(A i) {V o;o.pos=TransformObjectToHClip(i.vertex.xyz);o.dir=i.vertex.xyz;return o;}
float _TransferBlend, _Assembly;
 half4 frag(V i):SV_Target {
 float3 d=normalize(i.dir);float altitude=saturate(d.y);
 float3 sky=lerp(_SkyHorizon.rgb,_SkyTop.rgb,pow(altitude,.65));
 float2 p=d.xz/max(.20,d.y+.58)*_CloudScale;
 p+=float2(noise(p*.7+18),noise(p*.7-12))*.8;
 float n=cloudField(p),detail=noise(p*10.7)*.8+noise(p*21.1)*.2;
 float edge=(n-(1-_CloudCoverage))*(2.3*_CloudContrast);
 float density=smoothstep(.025,.78,edge+(.5-detail)*.09);
 float contour=saturate(1-abs(edge-.26)*3.0);
 float lighting=saturate(.20+n*.42+detail*.28+contour*.24);
 float3 cloud=lerp(_CloudShadow.rgb,_CloudLight.rgb,lighting);
 float horizon=smoothstep(-.25,.18,d.y);
 sky=lerp(sky,cloud,saturate(density*.94+(1-horizon)*.65));
 sky=lerp(sky,_SkyHorizon.rgb*.78,1-smoothstep(-.45,.015,d.y));
 float3 ed=normalize(_EclipseDirection.xyz);float angle=acos(clamp(dot(d,ed),-1,1));
 float aa=max(fwidth(angle),.0002),disk=1-smoothstep(_EclipseSize-aa,_EclipseSize+aa,angle);
 float3 tangent=normalize(cross(ed,float3(0,1,0))),bitangent=cross(ed,tangent);
 float a=atan2(dot(d,bitangent),dot(d,tangent));
 float rays=.70+.16*sin(a*11.0+1.2)+.09*sin(a*29.0)+.05*sin(a*53.0+2.3);
 float width=_CoronaWidth*(.7+rays*.6);
 float rim=exp(-abs(angle-_EclipseSize)/max(width,.0005));
 float outer=exp(-max(angle-_EclipseSize,0)/max(width*(2.2+rays*2),.001))*(1-disk);
 // The disc stays at its final sky direction. Gold fragments gather into its rim;
 // the dark core fills in sectors rather than translating up from the horizon.
 float assembly=saturate(_Assembly);
 float sector=hash21(float2(floor((a+PI)*19),floor(angle/max(_EclipseSize,.001)*20)));
 float assembled=smoothstep(sector*.72,sector*.72+.24,assembly);
 float ringWeight=smoothstep(.03,.88,assembly);
 float fragmentLight=0;
 if(assembly>0 && assembly<1 && angle<_EclipseSize*4.5)
 {
     float2 local=float2(dot(d,tangent),dot(d,bitangent));
     [unroll]for(int k=0;k<28;k++)
     {
         float seed=hash21(float2(k,17));float az=k*2.399963+seed*.7;
         float travel=smoothstep(seed*.22,.80+seed*.15,assembly);
         float radius=lerp(_EclipseSize*(2.0+seed*2),_EclipseSize,travel);
         float2 motePoint=float2(cos(az),sin(az))*radius;
         float size=lerp(.0015,.004,seed);
         fragmentLight+=exp(-dot(local-motePoint,local-motePoint)/(size*size))*sin(PI*assembly)*1.9;
     }
 }
 float veil=saturate(_BlackSunAtmosphere)*.96;
 sky=lerp(sky,float3(.003,.004,.008),disk*(1-veil)*assembled);
 sky+=_CoronaColor.rgb*((rim*.68+outer*.15)*_CoronaStrength*(1-disk*.88)*ringWeight+fragmentLight)*(1-veil);
 sky*=1-saturate(_BlackSunAtmosphere)*.18;
 return half4(lerp(sky,float3(.055,.065,.085),saturate(_TransferBlend)),1);
}
ENDHLSL }
} Fallback Off
}
