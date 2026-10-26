Shader "KingOfGraphic/Own/DockTimber"
{
 Properties{_BaseColor("Honey timber",Color)=(0.64,0.39,0.17,1) _RunAlongZ("World plank axis Z",Float)=0}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Back
 Pass{Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial) half4 _BaseColor; float _RunAlongZ; CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half fog:TEXCOORD2;};
 V Vert(A v){V o;o.w=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(v.n);o.fog=ComputeFogFactor(o.p.z);return o;}
 half4 Frag(V i):SV_Target{
 float along=lerp(i.w.x,i.w.z,_RunAlongZ);float across=lerp(i.w.z,i.w.x,_RunAlongZ);
 float plank=floor(across/.30);float h=frac(sin(plank*127.1)*43758.5453);float grain=across+sin(along*.65+h*8)*.009+sin(along*1.7+h*20)*.002;
 float stripes=sin(grain*610+sin(along*.31+h*5)*2)*.025+sin(grain*180+h*8)*.035;
 float seamDistance=min(frac(across/.30),1-frac(across/.30))*.30;
 float aa=max(fwidth(across),.0005);float seam=1-smoothstep(.002,.005+aa,seamDistance);
 float endDistance=min(frac((along+h*3.2)/3.2),1-frac((along+h*3.2)/3.2))*3.2;
 float ends=(1-smoothstep(.001,.004+max(fwidth(along),.0005),endDistance))*.38;
 float2 knotUV=float2(frac((along+h*2.7)/2.7)-.5,frac(across/.30)-.5);
 float knot=exp(-dot(knotUV*float2(16,6),knotUV*float2(16,6)))*.10;
 float surface=saturate((abs(i.n.y)-.6)*3);float variation=.86+h*.18+stripes-knot;
 half3 albedo=_BaseColor.rgb*variation;albedo*=1-(seam*.42+ends*.2)*surface;
 #ifndef UNITY_COLORSPACE_GAMMA
 albedo=SRGBToLinear(albedo);
 #endif
 half3 n=normalize(i.n);Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half3 ambient=max(SampleSH(n),half3(.12,.15,.13));
 half3 color=albedo*(ambient*.80+l.color*(.12+saturate(dot(n,l.direction))*.87)*l.shadowAttenuation);
 return half4(MixFog(color,i.fog),1);}
 ENDHLSL}
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
