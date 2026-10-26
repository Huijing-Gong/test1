Shader "KingOfGraphic/Hologram Points"
{
 Properties { _Tint("Tint",Color)=(0.35,1,0.85,1) _Intensity("Intensity",Float)=1.5 _Reveal("Reveal",Range(0,1))=1 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off
 Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _Tint;float _Intensity,_Reveal;
 CBUFFER_END
 struct A {float4 p:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float2 data:TEXCOORD1;float4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float height:TEXCOORD1;};
 V vert(A a){
 V o;o.height=a.p.y;float3 center=TransformObjectToWorld(a.p.xyz);
 float scale=length(float3(unity_ObjectToWorld._m00,unity_ObjectToWorld._m10,unity_ObjectToWorld._m20));
 float3 view=TransformWorldToView(center);
 view.xy+=a.uv*a.data.x*scale*2.7;
 o.p=mul(UNITY_MATRIX_P,float4(view,1));o.uv=a.uv;
 float flicker=.65+.35*sin(_Time.y*2.1+a.data.y*45);
 float depthLight=1;
 if(dot(a.normal,a.normal)>.01){
 float3 normalWS=TransformObjectToWorldNormal(a.normal);
 float3 viewDirection=GetWorldSpaceNormalizeViewDir(center);
 depthLight=.3+.7*saturate(dot(normalWS,viewDirection)*.75+.25);
 float viewAcrossBlade=abs(dot(TransformObjectToWorldDir(float3(0,0,1)),viewDirection));
 depthLight*=.25+.75*sqrt(viewAcrossBlade);
 }
 o.color=float4(a.color.rgb*_Tint.rgb*_Intensity,a.color.a*flicker*_Tint.a*depthLight);
 return o;}
 half4 frag(V i):SV_Target{
 clip(_Reveal-.00001);clip(lerp(.205,1.05,_Reveal)-i.height);
 float r=dot(i.uv,i.uv);clip(1-r);
 float soft=(exp(-r*32)+.045*exp(-r*3))*(1-smoothstep(.6,1,r));
 return half4(i.color.rgb,i.color.a*soft);
 }
 ENDHLSL
 }
 }
}
