Shader "Enemies/LeafWispSurface"
{
    Properties
    {
        [MainColor] _BaseColor ("Ceramic / graphite", Color) = (.8,.78,.68,1)
        _Charge ("Charge edge", Range(0,1)) = 0
        _Alive ("Core active", Range(0,1)) = 1
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Charge, _Alive;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A input)
            {
                V o; o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V input):SV_Target
            {
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode==DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                half3 n=normalize(input.normalWS), v=GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse=saturate(dot(n,light.direction));
                half3 colour=_BaseColor.rgb*(SampleSH(n)*.85h+light.color*diffuse*light.shadowAttenuation*.8h+.08h);
                half spec=pow(saturate(dot(n,normalize(light.direction+v))),32.0h)*.17h*light.shadowAttenuation;
                colour+=spec*light.color;
                // Restrained upper edge; no full-body green shield or new light.
                half rim=pow(1-saturate(dot(n,v)),3.0h)*saturate(n.y+.35h);
                colour+=half3(.035h,.3h,.36h)*rim*(.12h+_Charge*.65h)*_Alive;
                return half4(MixFog(colour,input.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
