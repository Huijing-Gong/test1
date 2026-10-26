Shader "SideA/CounterShield"
{
    Properties
    {
        [MainColor] _Tint ("Tint", Color) = (.05,.8,.95,.17)
        _Window ("Counter window", Range(0,1)) = 0
        _Pulse ("Scaled-time scan", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "CounterOnly"
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
                half _Window;
                float _Pulse;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half scan = pow(saturate(.5h+.5h*sin(input.uv.y*29.0h-_Pulse*15.0h)),12.0h);
                half edge = smoothstep(.85h,1.0h,input.uv.x);
                half diamond=abs(frac((input.uv.x+input.uv.y)*5)-.5h)+abs(frac((input.uv.x-input.uv.y)*5)-.5h);
                half lattice=1-smoothstep(.035h,.07h,abs(diamond-.5h));
                half3 windowHue = lerp(_Tint.rgb,half3(.92h,.75h,.33h),edge*_Window);
                // Thin local surface, not an invincibility bubble. The gold edge
                // is driven by the actual valid window, never an independent timer.
                return half4(windowHue,_Tint.a*(.42h+scan*.12h+edge*.3h+lattice*.16h));
            }
            ENDHLSL
        }
    }
}
