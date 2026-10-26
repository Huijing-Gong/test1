Shader "StoneScout/PaletteLit"
{
    Properties
    {
        _StoneColor ("Stone", Color) = (0.84, 0.80, 0.70, 1)
        _ShadowColor ("Stone shadow", Color) = (0.66, 0.62, 0.53, 1)
        _JointColor ("Joints", Color) = (0.27, 0.26, 0.23, 1)
        _MossColor ("Moss", Color) = (0.41, 0.47, 0.26, 1)
        _RuneColor ("Runes", Color) = (0.30, 0.86, 0.88, 1)
        _EyeColor ("Eye", Color) = (0.95, 0.63, 0.19, 1)
        _WearColor ("Weathered stone", Color) = (0.70, 0.68, 0.59, 1)
        _Weathering ("Weathering", Range(0, 1)) = 0.2
        _Emission ("Rune and eye glow", Range(0, 3)) = 0.15
        _CorePower ("Living core power", Range(0, 1)) = 1
        _ChargeStrength ("Attack charge", Range(0, 1)) = 0
        _ChargeVeins ("Right arm charge seams", Range(0, 1)) = 0
        [HideInInspector] _BaseColor ("Hit flash tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _BaseMap ("Shadow alpha", 2D) = "white" {}
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _StoneColor, _ShadowColor, _JointColor, _MossColor;
            half4 _RuneColor, _EyeColor, _WearColor, _BaseColor;
            half _Weathering, _Emission, _CorePower, _ChargeStrength, _ChargeVeins;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Each mesh face points to the centre of one of six existing palette tiles.
            // No UVs, meshes or texture assets need to change for a new appearance.
            half Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            half WearNoise(float3 p)
            {
                p *= 8.0;
                float3 cell = floor(p);
                float3 t = frac(p);
                t = t * t * (3.0 - 2.0 * t);
                half a = lerp(Hash(cell), Hash(cell + float3(1, 0, 0)), t.x);
                half b = lerp(Hash(cell + float3(0, 1, 0)), Hash(cell + float3(1, 1, 0)), t.x);
                half c = lerp(Hash(cell + float3(0, 0, 1)), Hash(cell + float3(1, 0, 1)), t.x);
                half d = lerp(Hash(cell + float3(0, 1, 1)), Hash(cell + 1.0), t.x);
                return lerp(lerp(a, b, t.y), lerp(c, d, t.y), t.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                UNITY_SETUP_INSTANCE_ID(input);
                int column = min((int)(input.uv.x * 3.0), 2);
                int row = input.uv.y < 0.5 ? 1 : 0;
                int tile = row * 3 + column;
                half3 albedo = _StoneColor.rgb;
                if (tile == 1) albedo = _ShadowColor.rgb;
                else if (tile == 2) albedo = _JointColor.rgb;
                else if (tile == 3) albedo = _MossColor.rgb;
                else if (tile == 4) albedo = _RuneColor.rgb;
                else if (tile == 5) albedo = _EyeColor.rgb;
                if (tile >= 4) albedo *= lerp(0.1h, 1.0h, _CorePower);

                if (tile < 2)
                {
                    half wear = smoothstep(0.38, 0.72, WearNoise(input.positionOS));
                    albedo = lerp(albedo, _WearColor.rgb, wear * _Weathering);
                }

                half3 normal = normalize(input.normalWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting = SampleSH(normal) +
                    sun.color * saturate(dot(normal, sun.direction)) * sun.shadowAttenuation;
                half3 glow = tile == 4 ? _RuneColor.rgb : (tile == 5 ? _EyeColor.rgb : half3(0, 0, 0));
                half3 color = albedo * lighting * _BaseColor.rgb;
                if (tile < 2 && _ChargeStrength > .01 && _CorePower > .01 && _ChargeVeins > .01)
                {
                    // Sparse angular seams reveal charge without bleaching the stone.
                    float3 p = input.positionOS * 7;
                    float seam = abs(frac(p.y + abs(frac(p.x * .7) - .5) * .7 + p.z * .19) - .5);
                    half fissure = 1-smoothstep(.012,.040,seam);
                    half gate = smoothstep(.35,.9,_ChargeStrength) * _CorePower * _ChargeVeins;
                    half3 vein = half3(1,.40,.035);
                    color = lerp(color,vein,fissure*gate*.80);
                }
                if (tile >= 4)
                {
                    // Sunlit white stone used to swallow low-emission variants' cues.
                    // Core tiles get their own bounded energy colour; other tiles keep
                    // the actual scene's light/shadow. No rim around the whole creature.
                    half3 hue = glow / max(.01h,max(glow.r,max(glow.g,glow.b)));
                    half3 charged = tile == 5 ? half3(1.0h,.32h,.025h) : half3(1.0h,.58h,.045h);
                    hue = lerp(hue,charged,_ChargeStrength * .9h);
                    half facing = saturate(dot(normal,GetWorldSpaceNormalizeViewDir(input.positionWS)));
                    half brightness = .48h + saturate(_Emission) * .14h + _ChargeStrength * .32h;
                    color = lerp(albedo * .1h,hue * brightness * lerp(.45h,1.0h,facing),_CorePower);
                }
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
