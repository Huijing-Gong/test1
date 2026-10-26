Shader "SideA/Sunlit Vertex Stone"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Unlit ("Flat distant scenery", Range(0,1)) = 0
        _StoneTex ("Shared limestone grain", 2D) = "white" {}
        _RuinsTex ("Reused ruins stone atlas", 2D) = "white" {}
        _RuinsNormalTex ("Reused ruins atlas normal", 2D) = "bump" {}
        _NormalStrength ("Ruins normal detail strength", Range(0,1)) = 0.3
        _DetailStrength ("Stone grain strength", Range(0,1)) = 0
        _AmbientStrength ("Environment light contribution", Range(0,1.5)) = 0.65
        _SunStrength ("Main light contribution", Range(0,1.5)) = 0.65
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Keep the same material layout in the forward and shadow passes for the SRP Batcher.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Unlit;
            half _DetailStrength;
            half _AmbientStrength;
            half _SunStrength;
            half _NormalStrength;
            float4 _StoneTex_ST;
            float4 _RuinsTex_ST;
        CBUFFER_END
        TEXTURE2D(_StoneTex); SAMPLER(sampler_StoneTex);
        TEXTURE2D(_RuinsTex); SAMPLER(sampler_RuinsTex);
        TEXTURE2D(_RuinsNormalTex); SAMPLER(sampler_RuinsNormalTex);
        ENDHLSL
        Pass
        {
            Name "SunlitForward"
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half2 fogAndMask : TEXCOORD5;
                half4 tangentWS : TEXCOORD6;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz, false),
                    input.tangentOS.w * GetOddNegativeScale());
                output.color = input.color.rgb * _BaseColor.rgb;
                output.uv = input.uv;
                output.shadowCoord = GetShadowCoord(position);
                output.fogAndMask = half2(ComputeFogFactor(output.positionCS.z), input.color.a);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = input.color;
                half3 normalWS = normalize(input.normalWS);
                // Vertex alpha is a discrete asset channel, never opacity:
                // 255 = grain-mapped caps/posts, 128 = the original ruins wall UV atlas,
                // 0 = opaque ivy/banner colours. Avoid sampling a texture for decoration.
                UNITY_BRANCH
                if (input.fogAndMask.y > 0.75h)
                {
                    half3 grain = SAMPLE_TEXTURE2D(_StoneTex, sampler_StoneTex,
                        input.uv * _StoneTex_ST.xy + _StoneTex_ST.zw).rgb;
                    albedo *= lerp(half3(1,1,1), grain, _DetailStrength);
                }
                else if (input.fogAndMask.y > 0.25h)
                {
                    float2 atlasUV = input.uv * _RuinsTex_ST.xy + _RuinsTex_ST.zw;
                    albedo *= SAMPLE_TEXTURE2D(_RuinsTex, sampler_RuinsTex,
                        atlasUV).rgb;
                    // Only the reused stone atlas has this small normal-map sample. Keep the
                    // original shared grain caps and opaque foliage on their geometric normals.
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_RuinsNormalTex,
                        sampler_RuinsNormalTex, atlasUV), _NormalStrength);
                    half3 tangentWS = normalize(input.tangentWS.xyz);
                    half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                    normalWS = normalize(normalTS.x * tangentWS + normalTS.y * bitangentWS
                        + normalTS.z * normalWS);
                }
                // Calculate cascade selection per fragment, not at the corners of a long deck.
                // Screen-space shadows use the screen coordinate supplied by URP's vertex helper.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = input.shadowCoord;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                half shadow = lerp(sun.shadowAttenuation, 1.0h,
                    GetMainLightShadowFade(input.positionWS));
                half3 ambient = max(half3(0,0,0), SampleSH(normalWS)) * _AmbientStrength;
                half3 direct = sun.color * (saturate(dot(normalWS, sun.direction))
                    * sun.distanceAttenuation * shadow * _SunStrength);
                // Matte diffuse stone: no specular highlights, reflection probe, transparency
                // or extra lights. The scene controls the actual sky/sun colours.
                half3 color = albedo * lerp(ambient + direct, half3(1,1,1), _Unlit);
                return half4(MixFog(color, input.fogAndMask.x), 1);
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
            // Use the installed URP caster implementation so normal/depth bias and reverse-Z
            // clamping follow exactly the same rules as the actors' URP materials.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
