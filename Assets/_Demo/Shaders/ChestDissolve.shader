Shader "ArtLab/Chest Dissolve"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap ("Surface Texture", 2D) = "white" {}
        _BaseColor ("Lit Color", Color) = (0.45,0.23,0.095,1)
        _ShadeColor ("Shadow Color", Color) = (0.12,0.085,0.075,1)
        _HighlightStrength ("Highlight Strength", Range(0,1)) = 0.12
        [Header(Digital Disintegration)]
        _Progress ("Disintegration Progress", Range(0,1)) = 0
        _GridScale ("Cells Across Longest Side", Range(3,20)) = 8
        _CellDelay ("Cell Timing Variation", Range(0,0.4)) = 0.15
        _EdgeWidth ("Transition Width", Range(0.01,0.15)) = 0.06
        [HDR] _GlowColor ("Energy Color", Color) = (0.08,0.8,1,1)
        _GlowIntensity ("Energy Intensity", Range(0,5)) = 2
        _ScanDirection ("Scan Direction in Chest Space", Vector) = (0,-1,0,0)
        _Proximity ("Interaction Cue", Range(0,1)) = 0
        [HideInInspector] _BoundsMin ("Chest Bounds Minimum", Vector) = (-1,0,-0.6,0)
        [HideInInspector] _BoundsMax ("Chest Bounds Maximum", Vector) = (1,1.4,0.6,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _ShadeColor, _GlowColor;
            float4 _BaseMap_ST;
            float4 _ScanDirection, _BoundsMin, _BoundsMax;
            float4x4 _WorldToChest;
            float _HighlightStrength, _Progress, _GridScale, _CellDelay;
            float _EdgeWidth, _GlowIntensity, _Proximity;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 positionChest : TEXCOORD2;
            float fog : TEXCOORD3;
            float2 uv : TEXCOORD4;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionChest = mul(_WorldToChest, float4(output.positionWS, 1)).xyz;
            output.fog = ComputeFogFactor(output.positionCS.z);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }

        float GridUnitsPerMeter()
        {
            float3 size = _BoundsMax.xyz - _BoundsMin.xyz;
            return max(_GridScale, 1) / max(max(size.x, max(size.y, size.z)), 0.0001);
        }

        float3 GridPosition(float3 positionChest)
        {
            return (positionChest - _BoundsMin.xyz) * GridUnitsPerMeter();
        }

        float CellNoise(float3 cell)
        {
            // A fixed arithmetic hash avoids time-dependent flickering and texture assets.
            float3 seed = frac(cell * float3(0.1031, 0.1030, 0.0973));
            seed += dot(seed, seed.yxz + 33.33);
            return frac((seed.x + seed.y) * seed.z);
        }

        float CellDistanceToScan(float3 positionChest)
        {
            float3 cell = floor(GridPosition(positionChest));
            float3 center = _BoundsMin.xyz + (cell + 0.5) / GridUnitsPerMeter();
            float3 direction = _ScanDirection.xyz;

            // Project all eight bound corners without enumerating them. This also
            // supports diagonal scans and keeps separate body/trim meshes aligned.
            float3 cornerA = _BoundsMin.xyz * direction;
            float3 cornerB = _BoundsMax.xyz * direction;
            float minimum = dot(min(cornerA, cornerB), float3(1, 1, 1));
            float maximum = dot(max(cornerA, cornerB), float3(1, 1, 1));
            float scan = saturate((dot(center, direction) - minimum) / max(maximum - minimum, 0.0001));
            float width = max(_EdgeWidth, 0.001);
            float delay = max(_CellDelay, 0);
            float front = lerp(-4 * width, 1 + delay + width, saturate(_Progress));
            return scan + CellNoise(cell) * delay - front;
        }

        float ClipChest(float3 positionChest)
        {
            float distanceToScan = CellDistanceToScan(positionChest);
            // Explicit endpoints guarantee complete geometry and complete removal
            // even at bound edges. Every visibility pass calls this same function.
            if (_Progress >= 1)
                clip(-1);
            if (_Progress > 0)
                clip(distanceToScan);
            return distanceToScan;
        }

        float SurfaceGrid(float3 positionChest)
        {
            float3 coordinate = GridPosition(positionChest);
            float3 distanceToLine = min(frac(coordinate), 1 - frac(coordinate));
            float3 antialiasing = max(fwidth(coordinate), float3(0.0001, 0.0001, 0.0001));
            float3 gridLine = 1 - smoothstep(float3(0.018, 0.018, 0.018), 0.018 + antialiasing, distanceToLine);

            // Derivatives recover a chest-space surface normal even when the root
            // has nonuniform scale. Project onto the surface instead of highlighting
            // an entire face when its constant coordinate lies on a grid boundary.
            float3 normal = abs(cross(ddx(positionChest), ddy(positionChest)));
            normal /= max(max(normal.x, max(normal.y, normal.z)), 0.000001);
            float3 weight = normal * normal * normal * normal;
            weight /= max(dot(weight, float3(1, 1, 1)), 0.000001);
            float3 projected = float3(max(gridLine.y, gridLine.z), max(gridLine.x, gridLine.z), max(gridLine.x, gridLine.y));
            return dot(projected, weight);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            half4 Frag(Varyings input) : SV_Target
            {
                float distanceToScan = ClipChest(input.positionChest);
                float3 normal = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoordinate = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoordinate = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light = GetMainLight(shadowCoordinate);
                float ndl = saturate(dot(normal, light.direction));
                float visibility = light.shadowAttenuation * light.distanceAttenuation;
                float band = smoothstep(0.18, 0.38, ndl) * 0.55 + smoothstep(0.6, 0.76, ndl) * 0.45;
                float3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, band * visibility);
                color *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                color *= lerp(float3(0.78, 0.85, 0.94), light.color, ndl * visibility);
                float3 halfDirection = SafeNormalize(view + light.direction);
                float highlight = pow(saturate(dot(normal, halfDirection)), 48) * ndl * visibility;
                color += highlight * _HighlightStrength * light.color;

                float width = max(_EdgeWidth, 0.001);
                float active = smoothstep(0, 0.025, _Progress);
                float reveal = (1 - smoothstep(width, 4 * width, distanceToScan)) * active;
                float boundary = (1 - smoothstep(0, width, distanceToScan)) * active;
                float grid = SurfaceGrid(input.positionChest);
                float rim = pow(1 - saturate(dot(normal, view)), 3);
                float cuePulse = 0.75 + 0.25 * sin(_Time.y * 2.5);
                float proximityCue = saturate(_Proximity) * cuePulse * (0.055 * grid + 0.12 * rim);

                color = lerp(color, _GlowColor.rgb * 0.1, reveal * 0.72);
                color += _GlowColor.rgb * _GlowIntensity * (reveal * grid * 0.65 + boundary * 0.75 + proximityCue);
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = Vert(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - output.positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(
                    ApplyShadowBias(output.positionWS, output.normalWS, direction)));
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                ClipChest(input.positionChest);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag

            half4 DepthFrag(Varyings input) : SV_Target
            {
                ClipChest(input.positionChest);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                ClipChest(input.positionChest);
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normal);
                    float2 remappedNormal = saturate(octNormal * 0.5 + 0.5);
                    return half4(PackFloat2To888(remappedNormal), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
