Shader "SamProduction/StageSky"
{
    Properties
    {
        _Zenith("Zenith",Color)=(.012,.018,.09,1)
        _Horizon("Horizon",Color)=(.025,.045,.24,1)
        _Filament("Distant blue filaments",Color)=(.025,.13,.38,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith,_Horizon,_Filament;
            CBUFFER_END
            struct A {float4 vertex:POSITION;};
            struct V {float4 position:SV_POSITION;float3 direction:TEXCOORD0;};
            V vert(A i){V o;o.position=TransformObjectToHClip(i.vertex.xyz);o.direction=i.vertex.xyz;return o;}
            half4 frag(V i):SV_Target
            {
                float3 d=normalize(i.direction);
                float h=saturate(d.y*1.8+.18);
                float3 base=lerp(_Horizon.rgb,_Zenith.rgb,h);
                float a=atan2(d.x,d.z),e=asin(d.y);
                float ridge=sin(a*7.1+e*18+sin(a*3-e*5)*1.2);
                float filament=pow(saturate(.5+.5*ridge),24)*.18;
                float fine=pow(saturate(.5+.5*sin(a*24+e*31)),48)*.05;
                return half4(base+_Filament.rgb*(filament+fine),1);
            }
            ENDHLSL
        }
    }
}
