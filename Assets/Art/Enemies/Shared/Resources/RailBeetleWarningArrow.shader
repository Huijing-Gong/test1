Shader "Enemies/RailBeetleWarningArrow"
{
    Properties { _Charge("Windup",Range(0,1))=0 _Clock("Gameplay time",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
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
                float _Charge,_Clock;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float x=abs(i.uv.x-.5), y=i.uv.y;
                float edge=smoothstep(.458,.493,x);
                edge=max(edge,1-smoothstep(.003,.01,y));
                float inwardGlow=pow(saturate(x*2),10)*.24;
                // Fixed bounds; chevrons travel inside a faded inlet/outlet, never past the tip.
                float wave=frac((y+x*.19)*3-_Clock*.7);
                float arrow=(1-smoothstep(.065,.09,abs(wave-.5)))*(1-smoothstep(.25,.31,x));
                arrow*=smoothstep(.06,.15,y)*(1-smoothstep(.83,.94,y));
                float leading=smoothstep(.43,.55,wave);
                float flow=pow(saturate(.5+.5*cos(y*6.283-_Clock*4.4)),9);
                float head=smoothstep(.89,.97,y);
                float ready=smoothstep(.76,1,_Charge);
                half3 coral=lerp(half3(.92,.24,.13),half3(1,.48,.24),y);
                half3 gold=half3(1,.85,.47);
                half3 colour=coral;
                colour=lerp(colour,lerp(half3(1,.34,.17),gold,leading),arrow);
                colour=lerp(colour,lerp(half3(1,.52,.25),half3(1,.93,.67),flow*.65+ready*.25),edge);
                colour=lerp(colour,gold,head*(.35+.3*ready));
                float alpha=lerp(.12,.21,_Charge)+inwardGlow;
                alpha=lerp(alpha,.93,arrow);
                alpha=lerp(alpha,.95,edge);
                alpha=lerp(alpha,.62,head*(.5+.5*ready));
                return half4(colour,alpha);
            }
            ENDHLSL
        }
    }
}
