Shader "Enemies/DemonKingMutedPalette"
{
    Properties
    {
        _BaseMap ("Source palette atlas", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _SkinColor ("Violet skin", Color) = (.48,.24,.69,1)
        _ArmourColor ("Indigo armour", Color) = (.14,.18,.29,1)
        _CopperColor ("Satin brass", Color) = (.72,.52,.26,1)
        _HornColor ("Ivory horn", Color) = (.86,.79,.65,1)
        _Charge ("Blade charge", Range(0,1)) = 0
        _IsBlade ("Blade material", Float) = 0
        _EdgeAxis ("Blade thin axis", Vector) = (1,0,0,0)
        _EdgeStart ("Cutting edge start", Float) = .6
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _SkinColor, _ArmourColor, _CopperColor, _HornColor, _EdgeAxis;
            float _Charge, _IsBlade, _EdgeStart, _Cull;
            CBUFFER_END
            struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float3 positionOS:TEXCOORD3; half3 normalOS:TEXCOORD4; half fog:TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID};
            V Vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o);
                VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(i.normalOS); o.normalOS=i.normalOS;
                o.positionOS=i.positionOS.xyz; o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 src=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half hi=max(src.r,max(src.g,src.b)),lo=min(src.r,min(src.g,src.b));
                half chroma=hi-lo;
                half gold=step(src.b*1.35,src.r)*step(src.b*1.2,src.g)*step(src.r*.2,src.g)*step(.035,chroma);
                half chromatic=step(.025,chroma);
                half3 pigment=lerp(_SkinColor.rgb,_ArmourColor.rgb,_IsBlade);
                pigment=lerp(pigment,_CopperColor.rgb,gold);
                half3 grey=lerp(half3(.026,.03,.035),_ArmourColor.rgb,smoothstep(.008,.15,hi));
                grey=lerp(grey,_HornColor.rgb,smoothstep(.18,.55,hi));
                // Preserve the atlas' value structure: bright swatches are accents,
                // not another flat coat of body paint.
                pigment*=lerp(.72,1.12,smoothstep(.035,.65,hi));
                half3 albedo=lerp(grey,pigment,chromatic)*_BaseColor.rgb;
                half3 n=normalize(i.normalWS),v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(n,sun.direction));
                half3 ambient=max(SampleSH(n),half3(.23,.28,.34));
                half wrapped=smoothstep(-.18,.82,dot(n,sun.direction));
                half3 lit=ambient+sun.color*wrapped*lerp(.25,1,sun.shadowAttenuation);
                half3 color=albedo*lit;
                // A broad, restrained highlight instead of the original shiny metallic lobe.
                half spec=pow(saturate(dot(n,normalize(sun.direction+v))),24)*lerp(.055,.15,gold);
                color+=sun.color*spec*sun.shadowAttenuation;
                // Only the real mesh bevel/cutting faces glow. The broad blade faces
                // retain their charcoal pigment and remain readable through charge.
                float bevel=smoothstep(.10,.70,1-abs(dot(normalize(i.normalOS),normalize(_EdgeAxis.xyz))));
                float edge=bevel*smoothstep(_EdgeStart,_EdgeStart+.35,i.positionOS.z)*_IsBlade;
                color+=half3(1.5,.025,.006)*edge*_Charge;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
