Shader "KingOfGraphic/Own/GoldenGuidance"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.color=v.color;o.uv=v.uv;return o; }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                float r=length(p);
                float core=pow(saturate(1-r),3);
                float halo=pow(saturate(1-r),1.5)*.28;
                float rays=pow(saturate(1-abs(p.x)*14),3)*pow(saturate(1-abs(p.y)),3)
                         +pow(saturate(1-abs(p.y)*14),3)*pow(saturate(1-abs(p.x)),3);
                return half4(i.color.rgb*1.5,i.color.a*saturate(core+halo+rays*.23));
            }
            ENDHLSL
        }
    }
}
