Shader "FoldedRealm/LivingVegetation"
{
    Properties
    {
        _BaseMap("Original color texture", 2D) = "white" {}
        _BaseColor("Original tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        _StyleMode("Original shading style", Float) = 0
        _WindAmplitude("Stem bend / plant height", Range(0,0.2)) = 0.10
        _WindDetail("Leaf flutter in metres", Range(0,0.06)) = 0.015
        _InteractionStrength("Player bending", Range(0,1)) = 1
        [HideInInspector] _VertexDataValid("Prepared wind vertex data", Float) = 0
        [Toggle] _AlphaClip("Original texture alpha clipping", Float) = 0
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5
        _Metallic("Original Lit metallic", Range(0,1)) = 0
        _Smoothness("Original Lit smoothness", Range(0,1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _StyleMode;
            float _WindAmplitude;
            float _WindDetail;
            float _InteractionStrength;
            float _VertexDataValid;
            float _AlphaClip;
            float _Cutoff;
            float _Metallic;
            float _Smoothness;
        CBUFFER_END

        #include "VegetationWind.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float4 rootAndWeight : TEXCOORD2;
            float4 plantData : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half4 color : COLOR;
            half fog : TEXCOORD3;
            half3 vertexLighting : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        void AlphaCutoff(float2 uv, half vertexAlpha)
        {
            if (_AlphaClip > 0.5)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a *
                    _BaseColor.a * vertexAlpha - _Cutoff);
        }

        Varyings VegetationVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            FRDeformVegetation(input.positionOS.xyz, input.normalOS,
                input.rootAndWeight, input.plantData, output.positionWS, output.normalWS);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.color = input.color;
            output.fog = ComputeFogFactor(output.positionCS.z);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
            output.vertexLighting = VertexLighting(output.positionWS,output.normalWS);
            #endif
            return output;
        }

        half4 VegetationFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            AlphaCutoff(input.uv, input.color.a);
            half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1, -1);
            Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            half ndl = dot(normal, sun.direction);
            half3 color;
            half4 originalTint = input.color * _BaseColor;

            if (_StyleMode < 2.5)
            {
                // Preserve each original vertex-color shader's color-space and
                // two-sided leaf/petal lighting instead of introducing a palette.
                half3 albedo = originalTint.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                albedo = SRGBToLinear(albedo);
                #endif
                half3 ambient = max(SampleSH(normal), half3(.12,.15,.13));
                if (_StyleMode < .5)
                {
                    half wrap = saturate((ndl + .22) / 1.22);
                    half back = saturate(-ndl);
                    half shadow = lerp(.35, 1, sun.shadowAttenuation);
                    color = albedo * (ambient * .90 + sun.color *
                        (.15 + wrap * .79 + back * back * .08) * shadow);
                }
                else if (_StyleMode < 1.5)
                {
                    half wrap = saturate((ndl + .32) / 1.32);
                    half back = saturate(-ndl);
                    half petal = step(albedo.g + .09, albedo.r);
                    half3 sky = max(ambient, lerp(half3(.12,.15,.13), half3(.30,.28,.26), petal));
                    half shadow = lerp(lerp(.48,.68,petal), 1, sun.shadowAttenuation);
                    color = albedo * (sky * lerp(.85,.95,petal) + sun.color *
                        (.18 + wrap * .74 + back * back * .15) * shadow);
                }
                else
                {
                    half foliage = step(.42,originalTint.g) * step(originalTint.g + .09,originalTint.r);
                    half3 bark = albedo * (ambient * .85 + sun.color *
                        (.12 + saturate(ndl) * .87) * sun.shadowAttenuation);
                    half3 sky = max(ambient,half3(.30,.28,.26));
                    half diffuse = saturate((ndl + .45) / 1.45);
                    half transmission = saturate(-ndl) * .22;
                    half3 petals = albedo * (sky * .95 + sun.color *
                        (.22 + diffuse * .70 + transmission) * lerp(.68,1,sun.shadowAttenuation));
                    color = lerp(bark,petals,foliage);
                }
            }
            else if (_StyleMode < 3.5)
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * originalTint.rgb;
                half3 lit = half3(.38,.42,.43) + sun.color * saturate(ndl) * .55 * sun.shadowAttenuation;
                color = albedo * lit;
            }
            else if (_StyleMode < 4.5)
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb *
                    _BaseColor.rgb * SRGBToLinear(input.color.rgb);
                half leaf = (albedo.g > albedo.r * 1.08 && albedo.g > albedo.b * 1.3) ? 1 : 0;
                if (leaf > 0)
                    albedo = lerp(half3(.96,.46,.50),half3(1.0,.77,.73),saturate((albedo.g-.15)*1.6));
                half3 lit = SampleSH(normal) * .8 + sun.color *
                    (saturate(ndl) * .70 + .08) * sun.shadowAttenuation;
                lit += leaf * half3(.06,.045,.055);
                color = albedo * lit;
            }
            else if (_StyleMode < 5.5)
            {
                half3 lit = SampleSH(half3(0,1,0)) + sun.color *
                    (.35 + .45 * saturate(sun.direction.y)) * sun.shadowAttenuation;
                color = originalTint.rgb * lit;
            }
            else if (_StyleMode < 6.5)
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb;
                half3 lit = max(SampleSH(normal),0.0h) + sun.color * saturate(ndl) * sun.shadowAttenuation;
                color = albedo * lit;
            }
            else
            {
                // Only the two targeted meadow materials use this branch. Their
                // source URP/Lit materials have no normal/metallic/occlusion maps.
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normal;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.bakedGI = SampleSH(normal);
                lighting.vertexLighting = input.vertexLighting;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                color = UniversalFragmentPBR(lighting,surface).rgb;
            }
            return half4(MixFog(color,input.fog),1);
        }

        float3 _LightDirection;
        float3 _LightPosition;

        Varyings VegetationShadowVertex(Attributes input)
        {
            Varyings output = VegetationVertex(input);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
            #else
            float3 lightDirectionWS = _LightDirection;
            #endif
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(
                output.positionWS, output.normalWS, lightDirectionWS));
            output.positionCS = ApplyShadowClamping(output.positionCS);
            return output;
        }

        half4 VegetationDepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            AlphaCutoff(input.uv,input.color.a);
            return 0;
        }

        half4 VegetationNormalsFragment(Varyings input,
            FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            AlphaCutoff(input.uv,input.color.a);
            float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(face,1,-1);
            #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
            half3 packedNormalWS = PackFloat2To888(saturate(octNormalWS * .5 + .5));
            return half4(packedNormalWS,0);
            #else
            return half4(normalWS,0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegetationVertex
            #pragma fragment VegetationFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegetationShadowVertex
            #pragma fragment VegetationDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegetationVertex
            #pragma fragment VegetationDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VegetationVertex
            #pragma fragment VegetationNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
