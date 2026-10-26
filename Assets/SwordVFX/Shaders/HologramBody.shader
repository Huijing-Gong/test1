Shader "KingOfGraphic/Hologram Body"
{
 Properties { _Tint("Body tint",Color)=(.08,.65,.52,1) _Opacity("Transparency density",Range(0,1))=.24 _Reveal("Reveal",Range(0,1))=1 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off
 Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _Tint;float _Opacity,_Reveal;
 CBUFFER_END
 struct A {float4 p:POSITION;float3 n:NORMAL;};
 struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 local:TEXCOORD2;};
 V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.n);o.local=a.p.xyz;return o;}
 half4 frag(V i):SV_Target{
 clip(_Reveal-.00001);clip(lerp(.205,1.05,_Reveal)-i.local.y);
 float3 n=normalize(i.normal),v=GetWorldSpaceNormalizeViewDir(i.world);
 Light light=GetMainLight();
 float facing=saturate(dot(n,v));float rim=pow(1-facing,2.5);
 float spec=pow(saturate(dot(n,normalize(light.direction+v))),45);
 float slope=saturate(dot(n,normalize(float3(-.5,.8,.6))))*.5+.5;
 float mist=.5+.25*sin(i.local.y*19+i.local.x*32)+.25*sin(i.local.y*43-i.local.z*51);
 float3 color=_Tint.rgb*(.5+slope*.6)+float3(.35,1,.86)*(rim*1.5+spec*1.6);
 float ridge=exp(-abs(i.local.x+.01)*180);
 color+=float3(.16,.65,.5)*ridge*.25;
 float alpha=_Opacity*(.65+.45*mist)+rim*.22+spec*.18;
 return half4(color,saturate(alpha));
 }
 ENDHLSL
 }
 }
}
