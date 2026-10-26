Shader "OverworldV2/DistantPanorama"
{
    Properties
    {
        _MainTex("Generated distant landscape",2D)="white"{}
        _Rotation("Horizon rotation",Range(0,360))=155
        _HorizonOffset("Raise painted horizon",Range(-.2,.2))=.06
        _Exposure("Exposure",Range(0,2))=1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _Rotation,_HorizonOffset,_Exposure;
            CBUFFER_END
            struct A {float4 vertex:POSITION;};
            struct V {float4 vertex:SV_POSITION;float3 direction:TEXCOORD0;};
            V vert(A v){V o;o.vertex=TransformObjectToHClip(v.vertex.xyz);o.direction=v.vertex.xyz;return o;}
            half4 frag(V i):SV_Target
            {
                float3 direction=normalize(i.direction);
                float2 uv=float2(frac(.5-atan2(direction.z,direction.x)/(2*PI)+_Rotation/360),saturate(.5+asin(direction.y)/PI-_HorizonOffset));
                half3 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv).rgb;
                // Artwork is not a calibrated seamless HDRI. Blend its narrow join,
                // preserving the useful painted landscape without a hard vertical seam.
                float seam=.5*(1-smoothstep(0,.018,min(uv.x,1-uv.x)));
                half3 opposite=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,float2(1-uv.x,uv.y)).rgb;
                return half4(lerp(color,opposite,seam)*_Exposure,1);
            }
            ENDHLSL
        }
    }
}
