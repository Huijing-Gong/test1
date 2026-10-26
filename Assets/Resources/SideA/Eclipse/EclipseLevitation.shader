Shader "FoldedRealm/EclipseLevitation"
{
    Properties
    {
        _GravityClock("Atmosphere scaled clock",Float)=0
        _Motion("Motion enabled",Float)=1
        _Intensity("Crimson gravity intensity",Range(0,2))=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent"}
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "CrimsonSuspension"
            Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _GravityClock,_Motion,_Intensity;
            CBUFFER_END
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;float life:TEXCOORD2;};
            V Vert(A i)
            {
                V o;float3 world=TransformObjectToWorld(i.p.xyz);
                float t=_GravityClock*saturate(_Motion);o.life=0;
                if(i.data.x>1.5)
                {
                    o.life=frac(t*i.data.w+i.data.y);
                    world.y+=o.life*i.data.z;
                    world.x+=.055*sin(o.life*4+i.data.y*12);
                }
                else world.y+=.045*sin(i.uv.x*18.84956+t*.48+i.data.y*6.283185)*saturate(_Motion);
                o.p=TransformWorldToHClip(world);o.uv=i.uv;o.data=i.data;return o;
            }
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p)
            {
                float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float t=_GravityClock*saturate(_Motion),alpha;float3 colour;
                float seed=i.data.y;
                if(i.data.x<.5)
                {
                    float path=frac(i.uv.x-t*(.036+seed*.009)+seed);
                    float head=pow(saturate(.5+.5*cos(path*6.283185)),13);
                    float fine=pow(saturate(1-abs(i.uv.y*2-1)),2.4);
                    float core=pow(fine,5);
                    float gaps=smoothstep(.18,.40,.5+.5*sin(i.uv.x*18.84956+seed*13+t*.16));
                    alpha=fine*(.20+head*.73)*gaps;
                    colour=lerp(float3(.85,.016,.024),float3(2.3,.16,.095),core*head);
                }
                else if(i.data.x<1.5)
                {
                    // Periodic angular coordinates avoid a visible UV seam.
                    float a=i.uv.x*6.283185;
                    float2 p=float2(cos(a),sin(a))*3.7+float2(t*.10,-t*.055)+seed*7;
                    float n=Noise(p+Noise(p*1.7)*.65);
                    float weave=.5+.5*sin(i.uv.y*17+i.uv.x*37.6991-t*.7+n*5);
                    float edge=pow(saturate(1-abs(i.uv.y*2-1)),1.8);
                    float flow=pow(saturate(.5+.5*cos(a+t*.16+seed*9)),2);
                    alpha=edge*smoothstep(.22,.72,n)*(.13+.37*weave)*(.42+.58*flow);
                    colour=float3(.92,.015,.034);
                }
                else
                {
                    float2 p=(i.uv-.5)*2;float d=dot(p,p);
                    alpha=pow(saturate(1-d),2)*smoothstep(0,.18,i.life)*(1-smoothstep(.62,1,i.life))*.68*saturate(_Motion);
                    colour=float3(1.6,.055,.027);
                }
                alpha=saturate(alpha*_Intensity);
                return half4(colour*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
