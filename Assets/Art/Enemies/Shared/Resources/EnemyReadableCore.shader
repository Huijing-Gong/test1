Shader "Enemies/ReadableCore"
{
    Properties
    {
        [MainColor] _BaseColor ("Core hue", Color) = (.08,.78,.91,1)
        [HDR] _EmissionColor ("State energy", Color) = (.12,1.17,1.36,1)
        _AttackCharge ("Attack packet strength", Range(0,1)) = 0
        _AttackClock ("Gameplay packet clock", Float) = 0
        [HideInInspector] _BaseMap ("Shadow alpha", 2D) = "white" {}
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _EmissionColor;
                float _AttackCharge, _AttackClock;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fog : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                half energy = max(_EmissionColor.r,max(_EmissionColor.g,_EmissionColor.b));
                half3 hue = _EmissionColor.rgb / max(energy,.001h);
                half facing = saturate(dot(normalize(input.normalWS),GetWorldSpaceNormalizeViewDir(input.positionWS)));
                // Keep saturated colour and a dark boundary even in direct sun. HDR
                // emission on the previous Lit material clipped to a featureless white
                // disc with this map's bloom disabled, hiding the charge progression.
                half edge = smoothstep(.06h,.48h,facing);
                half brightness = .32h + .62h * saturate(energy / 4.0h);
                half living = saturate(energy * 4.0h);
                half3 colour = lerp(_BaseColor.rgb * .14h,
                    hue * brightness * lerp(.16h,1.0h,edge),living);
                // A restrained hot centre appears only at high charge, never the shell.
                half hot = saturate((energy-2.0h)/2.0h) * smoothstep(.82h,1.0h,facing) * .22h;
                colour = lerp(colour,half3(1.0h,.94h,.66h),hot);
                // A small travelling energy packet, contained in the existing core mesh.
                half packet = pow(saturate(.5 + .5 * sin(input.positionWS.y * 18 - _AttackClock * 10)),12);
                half charged = packet * _AttackCharge * smoothstep(.25,.75,facing);
                colour = lerp(colour,lerp(hue,half3(1,.97,.78),.65),charged*.65);
                return half4(MixFog(colour,input.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
