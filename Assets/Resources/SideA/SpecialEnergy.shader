Shader "SideA/SpecialEnergy"
{
    Properties
    {
        [MainColor] _Tint ("Tint", Color) = (1,1,1,1)
        _Glow ("Readable energy", Range(0,1)) = .25
        _Fresnel ("Edge lighting", Range(0,1)) = .25
        _Beam ("Energy column", Float) = 0
        _Flow ("Scaled-time energy flow", Float) = 0
        _BeamLength ("World-space flow length", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ReadableEnergy"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Glow;
                half _Fresnel;
                half _Beam;
                float _Flow;
                float _BeamLength;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color * _Tint;
                output.uv = input.uv;
                return output;
            }
            float EnergyNoise(float2 p)
            {
                float2 cell=floor(p),f=frac(p);
                f=f*f*(3.0-2.0*f);
                float4 h=frac(sin(float4(dot(cell,float2(127.1,311.7)),
                    dot(cell+float2(1,0),float2(127.1,311.7)),
                    dot(cell+float2(0,1),float2(127.1,311.7)),
                    dot(cell+1.0,float2(127.1,311.7))))*43758.5453);
                return lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Readable without Bloom or a light per effect. Energy stays
                // saturated instead of clipping every effect to solid white.
                half3 normal = input.normalWS / max(length(input.normalWS), .001h);
                half facing = abs(dot(normal, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half rim = pow(1.0h-saturate(facing),2.0h) * _Fresnel;
                half3 colour = input.color.rgb * (.78h + _Glow * .22h);
                colour = lerp(colour, half3(.97h,.93h,.80h), rim * .28h);
                half alpha = saturate(input.color.a);
                if(_Beam < .5h)
                {
                    // Rounded core with a dark coloured rim rather than a flat disc.
                    colour *= .62h + .38h*facing;
                    colour = lerp(colour,half3(.99h,.97h,.87h),pow(saturate(facing),12.0h)*.60h);
                }
                if (_Beam > 2.5h)
                {
                    // Charge/muzzle only. Smooth luminous nucleus with moving
                    // latitudinal filaments, independent of lights and Bloom.
                    float swirl = input.uv.y * 19.0 + sin(input.uv.x * 18.85 + _Flow * 3.0) * 1.8 - _Flow * 7.0;
                    half filament = pow(saturate(.5 + .5 * sin(swirl)), 9.0);
                    half nucleus = smoothstep(.32h,.92h,facing);
                    colour = lerp(input.color.rgb * .65h, half3(.96h,1.0h,.92h), nucleus * .94h);
                    colour = lerp(colour, half3(.65h,1.0h,.91h), filament * (1-nucleus) * .8h);
                    alpha *= smoothstep(.015h,.36h,facing);
                }
                else if (_Beam > .5h)
                {
                    // Travel uses metres so long beams do not stretch the
                    // filaments. Everything remains inside the collision span.
                    float x = input.uv.x * max(_BeamLength,.01);
                    float flow = x * 3.2 - _Flow * 24.0;
                    float turbulence=EnergyNoise(float2(x*2.2-_Flow*12.0,input.uv.y*4.0));
                    float ripple = sin(flow) * .025 + (turbulence-.5)*.09;
                    half across = abs((input.uv.y-.5h)*2.0h + ripple);
                    half envelope = lerp(.62h,1.0h,smoothstep(0.0h,.09h,input.uv.x))
                        * sqrt(saturate((1.0h-input.uv.x)*18.0h));
                    half edge = envelope * (.76h + turbulence*.22h);
                    alpha *= (1.0h-smoothstep(edge-.09h,edge,across))
                        * smoothstep(0.0h,.012h,input.uv.x);
                    if (_Beam < 1.5h)
                    {
                        half core = 1.0h-smoothstep(.22h,.56h+(turbulence-.5h)*.13h,across/max(envelope,.01h));
                        half braid = pow(saturate(1.0h-abs((input.uv.y-.5h)*2.0h-sin(flow*.65)*.58h)*9.0h),2.0h);
                        half streak = pow(saturate(.5h+.5h*sin(flow+across*7.0h)),12.0h);
                        colour = lerp(input.color.rgb*.78h,half3(.96h,1.0h,.91h),core*.94h);
                        colour = lerp(colour,half3(.76h,1.0h,.91h),saturate(braid*.85h+streak*.27h)*(1-core));
                    }
                }
                return half4(colour,alpha);
            }
            ENDHLSL
        }
    }
}
