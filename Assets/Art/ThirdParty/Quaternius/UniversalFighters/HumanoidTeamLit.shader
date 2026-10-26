Shader "SideA/HumanoidTeamLit"
{
    Properties
    {
        _BaseMap("Ranger Color",2D)="white"{}
        _BaseColor("Base Color",Color)=(1,1,1,1)
        _TeamColor("Cloth Team Color",Color)=(.1,.55,1,1)
        _EclipseAuraColor("Eclipse team energy",Color)=(0,0,0,0)
        _EclipseAuraStrength("Eclipse energy (stage scoped)",Float)=0
        _EclipseAuraClock("Eclipse energy clock",Float)=0
        _EclipseTopLight("Eclipse overhead lighting",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Share the exact same material layout between character colour and shadow rendering.
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half4 _TeamColor;
            half4 _EclipseAuraColor; float _EclipseAuraStrength, _EclipseAuraClock;
            float _EclipseTopLight;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            // URP reserves bits 0..3 for users. Bit 3 marks visible fighter
            // pixels so the external aura never paints over a face or armour.
            Stencil { Ref 8 WriteMask 8 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; };
            V vert(A i) { V o; o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o; }
            half4 frag(V i):SV_Target
            {
                half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                // Recolor only green cloth; leather, silver fasteners and bare
                // skin keep their authored color. No extra texture/mask/light.
                half green=(color.g-max(color.r,color.b))/max(.015h,color.g);
                half mask=smoothstep(.10h,.28h,green);
                half luminance=dot(color,half3(.2126h,.7152h,.0722h));
                color=lerp(color,_TeamColor.rgb*(.16h+luminance*2.6h),mask)*_BaseColor.rgb;
                half3 normal=normalize(i.normalWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 ambient=max(half3(.20h,.20h,.20h),SampleSH(normal));
                half eclipse=saturate(_EclipseTopLight);
                half3 nightAmbient=max(half3(.055h,.065h,.090h),SampleSH(normal)*.55h);
                half3 lighting=lerp(ambient*.65h,nightAmbient,eclipse)+light.color*saturate(dot(normal,light.direction))*
                    lerp(.45h,1.h,light.shadowAttenuation)*.75h;
                half rim=pow(1-saturate(dot(normal,GetWorldSpaceNormalizeViewDir(i.positionWS))),3.2);
                half flow=.88h+.12h*sin(i.positionWS.y*8-_EclipseAuraClock*3);
                // A small neutral lift retains skin/leather detail; team energy
                // stays at the edge rather than painting over face or clothing.
                half3 energy=(color*lerp(.19h,.065h,eclipse)+_EclipseAuraColor.rgb*rim*flow*.66h)*_EclipseAuraStrength;
                return half4(color*lighting+energy,1);
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
            // Casting is controlled by the BattleScene body renderer, not a global prefab edit.
            // URP applies its normal/depth bias and reverse-Z clamping to the deformed mesh.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
