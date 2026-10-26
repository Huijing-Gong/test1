Shader "FoldedRealm/EclipseFighterAura"
{
    Properties
    {
        _EclipseAuraColor("Team energy",Color)=(.12,.64,1,1)
        _EclipseAuraStrength("Strength",Float)=1
        _EclipseAuraClock("Clock",Float)=0
        _AuraMotion("Motion",Float)=1
        _AuraMode("0 skinned edge / 1 ribbons",Float)=0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=1
        _BodyBounds("Body min / height",Vector)=(0,0,2,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+4" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off ZTest LEqual Cull [_Cull]
            Stencil { Ref 8 ReadMask 8 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _EclipseAuraColor;
                float _EclipseAuraStrength,_EclipseAuraClock,_AuraMotion,_AuraMode;
                float4 _BodyBounds;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 seed:TEXCOORD1;};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half life:TEXCOORD3;};
            float Hash(float3 p) {p=frac(p*.3183099+.1);p*=17;return frac(p.x*p.y*p.z*(p.x+p.y+p.z));}
            float Noise(float3 p)
            {
                float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }
            V Vert(A i)
            {
                V o;float clock=_EclipseAuraClock;
                float3 world=TransformObjectToWorld(i.positionOS.xyz);
                float3 normal=TransformObjectToWorldNormal(i.normalOS);
                o.uv=i.uv;o.life=1;
                if(_AuraMode<.5)
                {
                    float noise=Noise(world*6-float3(0,clock*2.3,0));
                    // Narrow inverted hull: the original opaque body depth masks its interior.
                    world+=normal*(.027+noise*.045*_AuraMotion);
                    world.y+=noise*.045*_AuraMotion;
                }
                else
                {
                    float phase=frac(i.seed.x+clock*.52*_AuraMotion);
                    float t=i.uv.y;
                    float y=-.49+i.seed.z+t*.40+phase*.45;
                    float bend=sin(t*3.14159)*(.12+.03*sin(clock*2+i.seed.w));
                    float x=i.seed.y*(.36+bend+.055*sin(t*4+clock*2.5+i.seed.w)*_AuraMotion);
                    float width=(.025+.070*sin(t*3.14159))*pow(1-t,.7);
                    float3 local=float3(x+i.positionOS.x*width,y,.12);
                    world=TransformObjectToWorld(local);
                    o.life=sin(phase*3.14159)*smoothstep(0,.10,t)*(1-smoothstep(.63,1,t));
                    normal=float3(0,0,-1);
                }
                o.positionWS=world;o.normalWS=normal;o.positionCS=TransformWorldToHClip(world);return o;
            }
            half4 Frag(V i):SV_Target
            {
                half3 colour=_EclipseAuraColor.rgb;
                half alpha;
                if(_AuraMode<.5)
                {
                    float wave=Noise(i.positionWS*7-float3(0,_EclipseAuraClock*2.3,0));
                    half rim=1-abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)));
                    alpha=(.25+wave*.19)*smoothstep(.18,.75,rim)*_EclipseAuraStrength;
                    half3 core=lerp(half3(.63,.92,1),half3(1,.78,.43),step(.5,_EclipseAuraColor.r));
                    colour=lerp(colour,core,.12+wave*.06);
                }
                else
                {
                    float crossSection=pow(saturate(1-abs(i.uv.x*2-1)),1.3);
                    alpha=crossSection*i.life*.63*_EclipseAuraStrength*_AuraMotion;
                    colour=lerp(colour,half3(1,.96,.87),crossSection*.32);
                }
                return half4(colour*1.35,alpha);
            }
            ENDHLSL
        }
    }
}
