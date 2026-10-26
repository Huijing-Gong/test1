Shader "Enemies/KnightShieldEnergy"
{
    Properties { _BoundsMin("Min",Vector)=(0,0,0,0) _BoundsSize("Size",Vector)=(1,1,1,0)
        _UAxis("U",Vector)=(1,0,0,0) _VAxis("V",Vector)=(0,1,0,0)
        _CueClock("Clock",Float)=0 _Strain("Strain",Float)=0 _HitPulse("Contact",Float)=0 _PhaseTwo("Phase",Float)=0 _Guard("Guard",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+6" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BoundsMin,_BoundsSize,_UAxis,_VAxis;
            float _CueClock,_Strain,_HitPulse,_PhaseTwo,_Guard;
            CBUFFER_END
            struct A {float4 p:POSITION;};struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V Vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);float3 n=(i.p.xyz-_BoundsMin.xyz)/_BoundsSize.xyz;o.uv=float2(dot(n,_UAxis.xyz),dot(n,_VAxis.xyz));return o;}
            float segmentDistance(float2 p,float2 a,float2 b){float2 d=b-a;return length(p-a-d*saturate(dot(p-a,d)/dot(d,d)));}
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv;float2 q=(p-.5)*float2(1,1.2);
                float diamond=abs(abs(q.x)+abs(q.y)-.22);
                float stem=segmentDistance(p,float2(.5,.12),float2(.5,.86));
                float rune=1-smoothstep(.006,.014,min(diamond,stem));
                float zig=min(segmentDistance(p,float2(.51,.56),float2(.33,.64)),segmentDistance(p,float2(.33,.64),float2(.39,.75)));
                zig=min(zig,segmentDistance(p,float2(.51,.43),float2(.67,.36)));
                zig=min(zig,segmentDistance(p,float2(.67,.36),float2(.62,.23)));
                float cracks=(1-smoothstep(.004,.012,zig))*smoothstep(.55,.95,_Strain);
                float ripple=(1-smoothstep(.012,.032,abs(length(q)-(.05+(1-_HitPulse)*.45))))*_HitPulse;
                float pulse=.7+.3*sin(_CueClock*15);
                float alpha=rune*(.16*_Guard+.24*_PhaseTwo+.3*_HitPulse)+cracks*pulse*.85+ripple*.7;
                alpha*=smoothstep(.04,.1,p.y)*(1-smoothstep(.88,.98,p.y));
                clip(alpha-.008);
                half3 tint=lerp(half3(.24,.95,.8),half3(1,.73,.22),saturate(_Strain*.7+_PhaseTwo*.4));
                return half4(tint,alpha);
            }
            ENDHLSL
        }
    }
}
