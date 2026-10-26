Shader "FoldedRealm/DriftingLeaf"
{
    Properties { _BaseColor("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; half fog:TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=i.uv; o.color=i.color*_BaseColor;
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                // A tapered small leaf silhouette, no downloaded textures.
                float width=.65*sqrt(saturate(1-p.y*p.y));
                clip(width-abs(p.x));
                half vein=1-smoothstep(.025,.09,abs(p.x+.07*sin(p.y*3)));
                half3 ambient=max(SampleSH(half3(0,1,0)),half3(.18,.20,.16));
                Light sun=GetMainLight();
                half3 color=i.color.rgb*(ambient+sun.color*.48);
                color*=lerp(1,.8,vein);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
