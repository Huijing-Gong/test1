Shader "SideA/Brief Afterimage"
{
    Properties{_Tint("Tint",Color)=(.52,.8,.77,.15)}
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END
            struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};
            struct V{float4 positionCS:SV_POSITION;half rim:TEXCOORD0;half shade:TEXCOORD1;};
            V Vert(A i){V o;float3 w=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(w);
                float3 n=TransformObjectToWorldNormal(i.normalOS);
                o.rim=1-abs(dot(n,GetWorldSpaceNormalizeViewDir(w)));
                o.shade=saturate(dot(n,normalize(float3(-.4,.8,-.6)))*.5+.5);return o;}
            half4 Frag(V i):SV_Target
            {
                half edge=smoothstep(.35h,.9h,i.rim);
                half3 body=_Tint.rgb*lerp(.65h,1.08h,i.shade);
                return half4(lerp(body,half3(.91h,1,.88h),edge*.85h),_Tint.a*lerp(.78h,1,edge));
            }
            ENDHLSL
        }
    }
}
