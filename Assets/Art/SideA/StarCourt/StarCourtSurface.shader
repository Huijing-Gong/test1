Shader "SideA/Star Court Textured Surface"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _StoneTex ("Small ivory surface", 2D) = "white" {}
        _SurfaceMean ("Texture mean in sampling space", Vector) = (.93,.89,.80,1)
        _DetailStrength ("Stone colour detail", Range(0,1)) = .78
        _BumpStrength ("Fine surface relief in metres", Range(0,.08)) = .014
        _AmbientStrength ("Sky contribution", Range(0,1.5)) = .64
        _SunStrength ("Sun contribution", Range(0,1.5)) = .60
        _ShadowStrength ("Soft stylized shadow contrast", Range(0,1)) = .72
        _SpecStrength ("Gold inlay highlight only", Range(0,.5)) = .18
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _StoneTex_ST;
            half4 _SurfaceMean;
            half _DetailStrength, _BumpStrength, _AmbientStrength, _SunStrength, _ShadowStrength, _SpecStrength;
        CBUFFER_END
        TEXTURE2D(_StoneTex); SAMPLER(sampler_StoneTex);
        ENDHLSL
        Pass
        {
            Name "StarCourtForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1;
                half4 color:TEXCOORD2;
                float2 uv:TEXCOORD3;
                float4 shadowCoord:TEXCOORD4;
                half fog:TEXCOORD5;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.color=half4(v.color.rgb*_BaseColor.rgb,v.color.a);
                o.uv=v.uv*_StoneTex_ST.xy+_StoneTex_ST.zw;
                o.shadowCoord=GetShadowCoord(p); o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Vertex alpha is a material selector, not opacity:
                // 1=stone, .8=mint, .65=gold, .4=teal, .25=blue, 0=leaves.
                half stone=step(.90h,i.color.a);
                half gold=step(.59h,i.color.a)*(1-step(.72h,i.color.a));
                half decoration=step(.1h,i.color.a)*(1-stone);
                half3 grain=SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,i.uv).rgb;
                half luminance=dot(grain,half3(.2126h,.7152h,.0722h));
                half3 albedo=i.color.rgb;
                // Neutralize the texture's average cream; geometry supplies
                // palette. Keep its veins and local warm mineral variation.
                half3 neutral=grain/max(_SurfaceMean.rgb,half3(.1h,.1h,.1h));
                half3 detail=clamp(1+(neutral-1)*1.8h,.70h,1.12h);
                albedo*=lerp(half3(1,1,1),detail,stone*_DetailStrength);
                albedo*=lerp(1.0h,clamp(luminance*1.32h,.82h,1.07h),decoration*.22h);
                half3 n=normalize(i.normalWS);
                // Surface gradient as in Unity's Normal From Height method.
                // One texture sample, no normal-map asset or per-frame bake.
                float3 dx=ddx(i.positionWS),dy=ddy(i.positionWS);
                float3 cx=cross(n,dx),cy=cross(dy,n);
                float determinant=dot(dx,cy);
                float scale=(determinant<0?-1:1)/max(1e-8,abs(determinant));
                float3 gradient=scale*(ddx(luminance)*cy+ddy(luminance)*cx);
                float3 relief=gradient*_BumpStrength*(stone+gold*.12h);
                relief*=min(1, .32/max(.0001,length(relief)));
                n=normalize(n-relief);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 coord=i.shadowCoord;
                #else
                    float4 coord=TransformWorldToShadowCoord(i.positionWS);
                #endif
                Light sun=GetMainLight(coord);
                half shadow=lerp(sun.shadowAttenuation,1,GetMainLightShadowFade(i.positionWS));
                shadow=lerp(1,shadow,_ShadowStrength);
                half3 ambient=max(half3(0,0,0),SampleSH(n))*_AmbientStrength;
                half3 diffuse=sun.color*(saturate(dot(n,sun.direction))*sun.distanceAttenuation*shadow*_SunStrength);
                half3 h=normalize(normalize(GetWorldSpaceViewDir(i.positionWS))+sun.direction);
                half highlight=pow(saturate(dot(n,h)),32)*gold*_SpecStrength*shadow;
                half3 result=albedo*(ambient+diffuse)+sun.color*highlight*half3(1,.88h,.64h);
                return half4(MixFog(result,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
