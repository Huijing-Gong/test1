Shader "RepairWisp/SupportShell"
{
    Properties
    {
        [HDR] _SupportColor ("Repair energy", Color) = (0.65, 0.95, 0.15, 1)
        _SupportStrength ("Support visibility", Range(0, 2)) = 0
        _RepairProgress ("Repair progress", Range(0, 1)) = 0
        _SupportOrigin ("Recipient origin", Vector) = (0, 0, 0, 0)
        _SupportRadius ("Local torso repair zone", Float) = 0.65
        _RepairScanPhase ("Travelling scan phase", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "RepairShell"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _SupportColor;
                float4 _SupportOrigin;
                half _SupportStrength, _RepairProgress, _RepairScanPhase;
                float _SupportRadius;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // A millimetre-scale shell, not a larger collision body or shield bubble.
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) + output.normalWS * .009;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 p = input.positionWS - _SupportOrigin.xyz;
                float radius = max(.05, _SupportRadius);
                half zone = 1 - smoothstep(radius * .65, radius, length(p));
                // One thin travelling scan, without rim, grid or base tint. It reads as a
                // repair passing over a panel, NOT a surrounding invulnerability shield.
                float scanHeight = (_RepairScanPhase * 2 - 1) * radius;
                float distanceToScan = abs(p.y-scanHeight);
                half scan = 1-smoothstep(.09,.16,distanceToScan);
                half inner = 1-smoothstep(.045,.1,distanceToScan);
                half alpha = zone * scan * _SupportStrength * .93;
                clip(alpha - .008);
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                // A dark thin border survives sunlit ceramic; a yellow-green interior
                // survives shadow. Still one local stripe, not a glowing body shield.
                half3 energy = lerp(half3(.025,.20,.055),half3(.48,1,.60),inner);
                return half4(MixFog(energy, input.fog), alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
