Shader "Enemies/AttackEnergy"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Fade ("Opacity", Range(0,1)) = 1
        _Mode ("0 ribbon, 1 impact, 2 spark, 3 dust, 4 repair", Float) = 0
        _Clock ("Gameplay clock", Float) = 0
        _Progress ("Lifetime", Range(0,1)) = 0
        _Repeat ("Link segments", Float) = 12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+4" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _Fade, _Mode, _Clock, _Progress, _Repeat;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; half fog:TEXCOORD1; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color*_Tint; o.fog=ComputeFogFactor(o.positionCS.z); return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                half alpha=1, hot=0, edge=1;
                if (_Mode < .5 || (_Mode > 3.5 && _Mode < 4.5))
                {
                    float width=abs(p.y);
                    alpha=1-smoothstep(.72,1,width);
                    hot=1-smoothstep(.08,.36,width);
                    edge=1-smoothstep(.61,.87,width);
                    // Thin, coherent flowing interior. The outer boundary remains readable.
                    hot*=.82+.18*sin(i.uv.x*26-_Clock*15);
                    if (_Mode > 3.5)
                    {
                        float segment=frac(i.uv.x*_Repeat-_Clock*1.4);
                        alpha*=smoothstep(.03,.13,segment)*(1-smoothstep(.68,.80,segment));
                        hot=max(hot,(1-smoothstep(.08,.20,abs(segment-.32)))*.8);
                    }
                }
                else if (_Mode < 1.5)
                {
                    float a=atan2(p.y,p.x), r=length(p);
                    float rays=pow(abs(cos(a*4)),5);
                    float boundary=.20+.72*rays;
                    alpha=1-smoothstep(boundary-.07,boundary,r);
                    hot=1-smoothstep(.08,.43,r);
                    edge=1-smoothstep(boundary-.16,boundary-.07,r);
                }
                else if (_Mode < 2.5)
                {
                    float d=abs(p.x)*1.5+abs(p.y);
                    alpha=1-smoothstep(.72,1,d);
                    hot=1-smoothstep(.12,.55,d); edge=1-smoothstep(.65,.85,d);
                }
                else if (_Mode < 3.5)
                {
                    float r=length(p*float2(.9,1.12));
                    float a=atan2(p.y,p.x);
                    float lobe=.78+.08*sin(a*5+.4)+.05*sin(a*9);
                    alpha=(1-smoothstep(lobe-.13,lobe,r))*(1-smoothstep(.35,1,_Progress));
                    hot=saturate(.6+p.y*.4); edge=.8;
                }
                else
                {
                    // Rounded wet contact lobes, rather than gem-shaped energy sparks.
                    float a=atan2(p.y,p.x), r=length(p*float2(.8,1.2));
                    float boundary=.65+.10*sin(a*5)+.05*sin(a*8);
                    alpha=1-smoothstep(boundary-.10,boundary,r);
                    hot=saturate(.5+p.y*.55); edge=1-smoothstep(boundary-.18,boundary,r);
                }
                half3 color=lerp(i.color.rgb*.14,i.color.rgb,edge);
                color=lerp(color,half3(1,.98,.84),hot*(_Mode>2.5 && _Mode<3.5 ? .28 : .78));
                alpha*=i.color.a*_Fade;
                clip(alpha-.006);
                return half4(MixFog(color,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
