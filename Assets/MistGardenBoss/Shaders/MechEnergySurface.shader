Shader "MistGarden/MechEnergySurface"
{
    Properties
    {
        [HDR] _Color("Color",Color)=(1,0.65,0.1,0.5)
        _Mode("0 Shell 1 Glow 2 Trail 3 Shield 4 Ground",Float)=0
        _Progress("Ground fill",Range(0,1))=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float3 positionWS:TEXCOORD1;float2 uv:TEXCOORD2;float4 color:COLOR;};
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;float _Mode;float _Progress;
            CBUFFER_END
            V vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.uv=i.uv;o.color=i.color;return o;}
            half4 frag(V i):SV_Target
            {
                float alpha=1;float r=length(i.uv*2-1);
                if(_Mode<.5){float f=pow(1-saturate(abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)))),2.6);alpha=.15+.65*f;}
                else if(_Mode<1.5){alpha=pow(saturate(1-r*r),3);}
                else if(_Mode<2.5){alpha=pow(saturate(1-abs(i.uv.y*2-1)),1.6);}
                else if(_Mode<3.5){float f=pow(1-saturate(abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)))),3);float bands=pow(saturate(.5+.5*cos(i.positionWS.y*17)),24);alpha=.045+f*.55+bands*.025;}
                else{float ring=smoothstep(.94,.975,r)*(1-smoothstep(.975,1,r));float fill=(1-smoothstep(_Progress-.02,_Progress+.02,r))*.10;alpha=ring+fill;}
                return half4(_Color.rgb*i.color.rgb,alpha*_Color.a*i.color.a);
            }
            ENDHLSL
        }
    }
}
