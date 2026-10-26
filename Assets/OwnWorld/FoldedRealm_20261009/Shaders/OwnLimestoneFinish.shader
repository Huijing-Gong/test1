Shader "KingOfGraphic/Own/LimestoneFinish"
{
    Properties
    {
        _BaseColor("Pale limestone", Color) = (0.84,0.82,0.69,1)
        _Smoothness("Stone polish", Range(0,1)) = 0.24
        _GrainStrength("Fine grain", Range(0,0.1)) = 0.018
        _VeinStrength("Quiet veining", Range(0,0.1)) = 0.035
        [HideInInspector] _BaseMap("Default white", 2D) = "white" {}
        [HideInInspector] _Cutoff("Opaque", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Back
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _GrainStrength;
                half _VeinStrength;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; half3 n:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A a)
            {
                V o;
                o.w=TransformObjectToWorld(a.p.xyz);
                o.p=TransformWorldToHClip(o.w);
                o.n=TransformObjectToWorldNormal(a.n);
                o.fog=ComputeFogFactor(o.p.z);
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                float grain=frac(sin(dot(floor(i.w*75),float3(127.1,311.7,74.7)))*43758.5453)*2-1;
                float footprint=length(fwidth(i.w));
                grain*=1-smoothstep(0.004,0.012,footprint);
                float drift=dot(i.w,float3(2.3,0.8,1.3))+sin(i.w.y*1.7+i.w.z*0.8)*0.8;
                float wave=sin(drift);
                float vein=1-smoothstep(0.026,0.075+fwidth(wave)*1.3,abs(wave));
                half3 albedo=_BaseColor.rgb*(1+grain*_GrainStrength-vein*_VeinStrength);
                half3 normal=normalize(i.n);
                InputData input=(InputData)0;
                input.positionWS=i.w;
                input.normalWS=normal;
                input.viewDirectionWS=SafeNormalize(GetWorldSpaceViewDir(i.w));
                input.shadowCoord=TransformWorldToShadowCoord(i.w);
                input.bakedGI=SampleSH(normal);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
                input.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=albedo;
                surface.alpha=1;
                surface.metallic=0;
                surface.smoothness=_Smoothness;
                surface.occlusion=1;
                surface.normalTS=half3(0,0,1);
                half4 color=UniversalFragmentPBR(input,surface);
                color.rgb=MixFog(color.rgb,i.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "OwnOpaqueShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
