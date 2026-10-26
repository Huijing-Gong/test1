Shader "KingOfGraphic/Own/SoftCloud"
{Properties
{_BaseColor("Color",Color)=(1,1,1,1)}
 SubShader
{Tags
{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Pass
{
HLSLPROGRAM

#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial) half4 _BaseColor;
 CBUFFER_END
struct A{float4 p:POSITION;
float2 uv:TEXCOORD0;
}
;
struct V{float4 p:SV_POSITION;
float2 uv:TEXCOORD0;
}
;

V vert(A a){V o;
o.p=TransformObjectToHClip(a.p.xyz);
o.uv=a.uv;
return o;
}

half4 frag(V i):SV_Target
{float2 p=(i.uv-.5)*float2(2.6,2);
float d=min(length(p-float2(-.55,0))-.48,min(length(p-float2(.1,.18))-.66,length(p-float2(.7,0))-.4));
float a=1-smoothstep(-.22,.05,d);
float shade=.86+.1*i.uv.y;
return half4(_BaseColor.rgb*shade,a*_BaseColor.a);
}


ENDHLSL

}

}

}

