Shader "Enemies/AttackAfterimage"
{
    Properties
    {
        _Tint ("Mint silhouette", Color) = (.2,.87,.78,1)
        _Fade ("Lifetime opacity", Range(0,1)) = 0
        _Clock ("Snapshot clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Fade, _Clock;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A i) { V o; o.positionWS=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(i.normalOS); o.fog=ComputeFogFactor(o.positionCS.z); return o; }
            half4 Frag(V i):SV_Target
            {
                half facing=saturate(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)));
                half rim=pow(1-facing,1.7);
                // Filled silhouette + bright edge: recognisable even with bloom disabled.
                half alpha=_Fade*(.22+.58*rim);
                half stripe=.90+.10*sin(i.positionWS.y*35-_Clock*9);
                half3 c=lerp(_Tint.rgb*.48,half3(.77,1,.90),rim)*stripe;
                clip(alpha-.012);
                return half4(MixFog(c,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
