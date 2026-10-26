Shader "RepairWisp/RecipientStatus"
{
    Properties
    {
        _Strength("Visibility",Range(0,1))=0
        _Clock("Gameplay clock",Float)=0
        _Mode("Column or plus",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength,_Clock,_Mode;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float alpha; half3 colour;
                if(_Mode<.5)
                {
                    float fade=1-smoothstep(.7,1,i.uv.y);
                    float streak=pow(saturate(sin(i.uv.x*150.8)),28)*pow(saturate(sin(i.uv.y*17-_Clock*7+i.uv.x*31)),8);
                    float rim=1-smoothstep(.008,.035,i.uv.y);
                    float scan=1-smoothstep(.012,.042,abs(i.uv.y-frac(_Clock*.85)));
                    alpha=(.055+streak*.24+rim*.75+scan*.3)*fade;
                    colour=lerp(half3(.08,.88,.23),half3(.65,1,.75),saturate(rim+scan));
                }
                else
                {
                    float2 p=abs(i.uv-.5);
                    float d=min(max(p.x-.135,p.y-.40),max(p.x-.40,p.y-.135));
                    alpha=1-smoothstep(-.006,.006,d);
                    float inside=1-smoothstep(-.045,-.032,d);
                    colour=lerp(half3(.025,.58,.055),half3(.9,1,.9),inside);
                }
                alpha*=_Strength; clip(alpha-.004);
                return half4(colour,alpha);
            }
            ENDHLSL
        }
    }
}
