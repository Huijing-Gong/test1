Shader "MistGarden/StoneSentinelSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("Original UV atlas",2D)="white"{}
        [MainColor] _BaseColor("Tint / hit flash",Color)=(1,1,1,1)
        _HitColor("Warden hit flash",Color)=(1,0.72,0.68,1)
        _HitStrength("Warden hit strength",Range(0,1))=0
        _StoneTex("Ivory stone surface",2D)="white"{}
        _StoneColor("Ivory limestone",Color)=(0.91,0.875,0.785,1)
        _BronzeColor("Aged bronze frame",Color)=(0.247,0.42,0.369,1)
        _JointColor("Dark bronze joints",Color)=(0.165,0.231,0.216,1)
        _MossColor("Sparse olive moss",Color)=(0.498,0.639,0.227,1)
        _StoneScale("Stone surface scale",Float)=0.75
        _ArmorBias("Stone coverage",Range(0,1))=1
        _Metallic("Bronze metallic",Range(0,1))=0.2
        _Smoothness("Bronze smoothness",Range(0,1))=0.25
        _Cull("Cull",Float)=2
        _Cutoff("Cutoff",Float)=0.5
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_StoneTex); SAMPLER(sampler_StoneTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _StoneColor, _BronzeColor, _JointColor, _MossColor;
                half4 _HitColor;half _HitStrength;
                float _StoneScale;
                half _ArmorBias, _Metallic, _Smoothness, _Cull, _Cutoff;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;
                float3 positionOS:TEXCOORD3;
                half3 normalOS:TEXCOORD4;
                half fog:TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.positionOS=v.positionOS.xyz;
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.normalOS=v.normalOS;
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 original=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half3 normalWS=NormalizeNormalPerPixel(i.normalWS);
                half3 weights=pow(abs(normalize(i.normalOS)),4);weights/=max(dot(weights,half3(1,1,1)),0.001);
                float3 p=i.positionOS*_StoneScale;
                half3 detail=SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,p.yz).rgb*weights.x
                            +SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,p.xz).rgb*weights.y
                            +SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,p.xy).rgb*weights.z;
                half luminance=dot(original,half3(.2126,.7152,.0722));
                half yellow=smoothstep(.08,.26,original.r-original.b)*smoothstep(.2,.5,original.r);
                half pale=smoothstep(.16,.38,luminance);
                half stone=saturate(max(yellow,pale)*_ArmorBias);
                half cyan=smoothstep(.05,.22,min(original.g,original.b)-original.r);
                half3 bronze=lerp(_JointColor.rgb*.65,_BronzeColor.rgb,smoothstep(.025,.3,luminance));
                half weather=lerp(.5,1.04,smoothstep(.22,.72,detail.r));
                half3 limestone=_StoneColor.rgb*weather*lerp(.82,1.02,smoothstep(.05,.5,luminance));
                half3 color=lerp(bronze,limestone,stone*(1-cyan));
                half moss=stone*(1-cyan)*smoothstep(.15,.7,normalize(i.normalOS).y)
                     *smoothstep(.94,1.08,detail.g/max(detail.r,.01));
                color=lerp(color,_MossColor.rgb,moss*.8);
                color=lerp(color,half3(.08,.6,.55),cyan);
                color*=_BaseColor.rgb;
                InputData inputData=(InputData)0;
                inputData.positionWS=i.positionWS;inputData.normalWS=normalWS;
                inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                inputData.bakedGI=SampleSH(normalWS);inputData.shadowMask=half4(1,1,1,1);
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                half4 result=UniversalFragmentPBR(inputData,color,_Metallic*(1-stone),half3(0,0,0),lerp(_Smoothness,.12,stone),1,half3(.05,.5,.44)*cyan,1);
                result.rgb=lerp(result.rgb,_HitColor.rgb*1.55,saturate(_HitStrength));
                result.rgb=MixFog(result.rgb,i.fog);return result;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
