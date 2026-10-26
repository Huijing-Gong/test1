Shader "Own/MapUpgrade20261025/CourtPavingF12"
{
 Properties{
 _BaseColor("Ivory grey stone",Color)=(.80,.79,.74,1)
 _JointColor("Quiet warm grey joint",Color)=(.68,.67,.61,1)
 _GoldColor("Muted hairline gold",Color)=(.67,.57,.36,1)
 _PaverSize("Square side in metres",Range(2,3))=2.5
 _JointMetres("Full joint width metres",Range(.004,.03))=.012
 _GoldMetres("Full gold hairline metres",Range(.001,.008))=.003
 _GoldStrength("Gold blend",Range(0,.4))=.16
 _Variation("Tile colour variation",Range(0,.1))=.018
 _Smoothness("Smoothness",Range(0,1))=.18
 _PavingOrigin("World XZ origin (xy)",Vector)=(74,84.5,0,0)
 }
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass{Name "ForwardLit" Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor,_JointColor,_GoldColor;float4 _PavingOrigin;
 float _PaverSize,_JointMetres,_GoldMetres,_GoldStrength,_Variation,_Smoothness;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
 Varyings Vert(Attributes a){Varyings o;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);o.positionWS=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(a.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 half4 Frag(Varyings i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);half3 n=normalize(i.normalWS);float size=max(2,_PaverSize);float2 q=(i.positionWS.xz-_PavingOrigin.xy)/size;float2 f=frac(q);float2 dist=min(f,1-f)*size;float edge=min(dist.x,dist.y);float aa=max(fwidth(edge),.0005);float joint=1-smoothstep(_JointMetres*.5,_JointMetres*.5+aa,edge);float gold=1-smoothstep(_GoldMetres*.5,_GoldMetres*.5+aa,edge);
 half3 albedo=_BaseColor.rgb*(1+(Hash(floor(q))-.5)*_Variation);float grain=Hash(floor(i.positionWS.xz*55))-.5;albedo*=1+grain*.008;albedo=lerp(albedo,_JointColor.rgb,joint*.25);albedo=lerp(albedo,_GoldColor.rgb,gold*_GoldStrength); // no heavy bevel or baked black outline
 Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));half3 color=albedo*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.distanceAttenuation*light.shadowAttenuation);half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS),h=normalize(light.direction+v);color+=light.color*pow(saturate(dot(n,h)),lerp(8,96,_Smoothness))*.012*light.shadowAttenuation;return half4(MixFog(color,i.fog),1);}
 ENDHLSL}
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}
