Shader "KingOfGraphic/Own/TwoSidedPlantDiffuseQA"
{
    Properties
    {
        _BaseMap("Own generated base color", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "OwnTwoSidedDiffuse"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
                return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE frontFace:FRONT_FACE_SEMANTIC):SV_Target
            {
                half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(frontFace,1.0h,-1.0h);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 light=max(SampleSH(n),0.0h)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation;
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
                return half4(color.rgb*light,color.a);
            }
            ENDHLSL
        }
    }
}
