Shader "KingOfGraphic/Own/CloudSeaLayer" {
Properties { _Tint("Cloud tint",Color)=(.83,.91,1,.78) }
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-80"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
Pass { HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_fog
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _Tint;CBUFFER_END
struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half fog:TEXCOORD1;};
V Vert(A v){V o;o.w=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.w);o.fog=ComputeFogFactor(o.p.z);return o;}
float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
half4 Frag(V i):SV_Target{
float2 p=i.w.xz*.025+float2(_Time.y*.0011,-_Time.y*.0007);
float n=Noise(p)*.55+Noise(p*2.07+7.3)*.28+Noise(p*4.19-4.1)*.17;
float cloud=smoothstep(.27,.69,n);
float distance=length(i.w.xz);float outer=1-smoothstep(275,355,distance);
half3 c=lerp(half3(.55,.70,.87),_Tint.rgb,saturate(n*1.45));
return half4(MixFog(c,i.fog),cloud*outer*_Tint.a);
}
ENDHLSL
}
}
}