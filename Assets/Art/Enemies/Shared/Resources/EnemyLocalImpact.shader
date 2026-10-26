Shader "Enemies/LocalImpact"
{
    Properties
    {
        _ImpactPoint ("Actual contact point", Vector) = (0,0,0,0)
        _ImpactRadius ("Local patch radius (zero for chips)", Float) = 0
        _ImpactStrength ("Flash fade", Range(0,1)) = 1
        _ImpactTint ("Local energy hue", Color) = (1,.29,.025,1)
        _CueOpacity ("Small vertex cue opacity", Range(0,1)) = 1
        [HideInInspector] _LinkPhase ("Repair packet phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
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
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ImpactPoint;
                float _ImpactRadius, _ImpactStrength, _LinkPhase;
                half4 _ImpactTint;
                half _CueOpacity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; half4 color : COLOR; float2 uv : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) + TransformObjectToWorldNormal(input.normalOS) * .009;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z); output.color = input.color; output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_ImpactRadius < -.5)
                {
                    // A single narrow line, with dark edges against pale paths and
                    // a travelling packet against foliage; no second renderer/light.
                    half side = abs(input.uv.y * 2 - 1);
                    half inner = 1-smoothstep(.4,.78,side);
                    half packet = 1-smoothstep(.03,.19,abs(frac(input.uv.x*5-_LinkPhase)-.5));
                    half3 colour = lerp(half3(.015,.04,.025),input.color.rgb,inner);
                    colour = lerp(colour,half3(1,1,.62),packet * inner * .85);
                    half alpha = (1-smoothstep(.88,1,side))*input.color.a;
                    #if defined(DEBUG_DISPLAY)
                        if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                    #endif
                    return half4(MixFog(colour,input.fog),alpha);
                }
                if (_ImpactRadius <= 0)
                {
                    #if defined(DEBUG_DISPLAY)
                        if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                    #endif
                    return half4(MixFog(input.color.rgb, input.fog), input.color.a * _CueOpacity);
                }
                half radius = distance(input.positionWS,_ImpactPoint.xyz) / max(.01,_ImpactRadius);
                half mask = 1-smoothstep(.88h,1.0h,radius);
                clip(mask-.01h);
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                // Light centre, saturated amber middle, narrow dark boundary: readable
                // on both sunlit ivory and shaded graphite without a whole-body tint.
                half middle = 1-smoothstep(.65h,.84h,radius);
                half centre = 1-smoothstep(.12h,.38h,radius);
                half3 energy = lerp(half3(.035h,.07h,.08h),_ImpactTint.rgb,middle);
                energy = lerp(energy,half3(1.0h,.94h,.67h),centre);
                return half4(MixFog(energy,input.fog),mask * _ImpactStrength * .94h);
            }
            ENDHLSL
        }
    }
}
