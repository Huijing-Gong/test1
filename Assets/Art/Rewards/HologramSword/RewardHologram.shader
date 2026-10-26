Shader "SideGameA/Rewards/Sword Hologram URP"
{
    Properties
    {
        [HDR] _BodyColor ("Body / cyan", Color) = (0.08, 0.65, 0.85, 1)
        [HDR] _AccentColor ("Guard / warm gold", Color) = (1, 0.65, 0.16, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.38
        _ScanDensity ("Scan line count", Range(2, 120)) = 42
        _ScanWidth ("Scan line width", Range(0.02, 0.8)) = 0.16
        _ScanSpeed ("Scan speed / cycles per second", Range(-4, 4)) = 0.7
        _ScanIntensity ("Scan brightness", Range(0, 3)) = 0.9
        _PulseAmount ("Pulse amount", Range(0, 0.5)) = 0.08
        _PulseSpeed ("Pulse cycles per second", Range(0, 3)) = 0.55
        [HDR] _RimColor ("Rim / blue", Color) = (0.025, 0.22, 1, 1)
        _RimStrength ("Rim brightness", Range(0, 5)) = 2.5
        _RimPower ("Rim power / higher is narrower", Range(1, 8)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Names match Properties so Unity can supply the material values.
            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor;
                float4 _AccentColor;
                float _Opacity;
                float _ScanDensity;
                float _ScanWidth;
                float _ScanSpeed;
                float _ScanIntensity;
                float _PulseAmount;
                float _PulseSpeed;
                float4 _RimColor;
                float _RimStrength;
                float _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float accent : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.accent = input.color.r;
                // Position and normal must use the same space as the view direction.
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float3 color = _BodyColor.rgb;
                if (input.accent > 0.5)
                {
                    color = _AccentColor.rgb;
                }
                // Repeat bright bands along the sword's shared vertical UV.
                float scanPosition = input.uv.y * _ScanDensity - _Time.y * _ScanSpeed;
                float scanPhase = frac(scanPosition);
                float brightness = 1.0;
                if (scanPhase < _ScanWidth)
                {
                    brightness = brightness + _ScanIntensity;
                }
                float pulseTime = _Time.y * _PulseSpeed * 6.283185;
                float pulse = 1.0 + sin(pulseTime) * _PulseAmount;
                color = color * brightness * pulse;

                // Interpolation changes normal length, so normalise it again.
                float3 normal = normalize(input.normalWS);
                // URP handles both perspective and orthographic cameras here.
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Facing the camera: dot is near 1. Grazing angles: near 0.
                float facing = saturate(dot(normal, viewDirection));
                float rim = pow(1.0 - facing, _RimPower);
                // Higher power narrows the rim; strength zero disables it.
                color = color + _RimColor.rgb * rim * _RimStrength;
                return float4(color, _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
