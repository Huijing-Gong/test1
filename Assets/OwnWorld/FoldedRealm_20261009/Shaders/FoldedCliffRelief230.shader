Shader "KingOfGraphic/Own/FoldedCliffRelief230"
{
    Properties { _BaseColor("Owned limestone tint",Color)=(1,1,1,1) _DetailStrength("Cliff surface detail",Range(0,2))=1 _ReliefStrength("Cliff normal folds",Range(0,2))=.9 _StrataStrength("Broken strata tint",Range(0,1))=.45 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _BaseColor; float _DetailStrength; float _ReliefStrength; float _StrataStrength; CBUFFER_END
            float _OverworldEclipseDarkness;
            struct A { float4 p:POSITION; float3 n:NORMAL; half4 c:COLOR; float2 bary:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; float2 bary:TEXCOORD2; half fog:TEXCOORD3; };
            V Vert(A a) { V o; o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c;o.bary=a.bary;o.fog=ComputeFogFactor(o.p.z);return o; }
            
            float2 FoldHash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453); }
            void FoldCells(float2 p,out float nearest,out float second,out float2 local,out float2 id)
            {
                float2 cell=floor(p);float2 f=frac(p);nearest=100;second=100;local=0;id=0;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 q=float2(x,y);float2 offset=.14+.72*FoldHash(cell+q);float2 r=q+offset-f;float d=dot(r,r);
                    if(d<nearest){second=nearest;nearest=d;local=-r;id=cell+q;}
                    else if(d<second)second=d;
                }
            }
            half4 Frag(V i):SV_Target
            {
                half3 n=normalize(i.n);Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));
                float3 bary=float3(i.bary,1-i.bary.x-i.bary.y);
                // Existing rear-bank seams retain a soft pixel footprint at grazing angles.
                float bank=smoothstep(-96,-90,i.w.x)*(1-smoothstep(-27,-20,i.w.x));
                bank*=smoothstep(40,45,i.w.z)*(1-smoothstep(95,100,i.w.z))*(1-smoothstep(.72,.90,n.y));
                float3 delta=max(fwidth(bary),.000001);
                float3 edges=smoothstep(delta*bank*.22,delta*lerp(.65,1.35,bank),bary);
                half seam=(1-min(edges.x,min(edges.y,edges.z)))*i.c.a;
                half3 stone=i.c.rgb;
#ifndef UNITY_COLORSPACE_GAMMA
stone=SRGBToLinear(stone);
#endif
stone*=_BaseColor.rgb;
                stone=lerp(stone,lerp(half3(.48,.30,.075),half3(.64,.44,.16),bank),seam*.75);
                float u=dot(i.w.xz,float2(.87,.493));float2 plane=float2(u*.72,i.w.y*.42);
                float f1,f2;float2 local,cell;FoldCells(plane,f1,f2,local,cell);
                float gap=max(0,sqrt(f2)-sqrt(f1));float2 hash=FoldHash(cell);
                float3 foldTangent=normalize(cross(float3(0,1,0),n)+float3(.00001,0,0));
                float3 foldUp=normalize(cross(n,foldTangent));
                n=normalize(n+foldTangent*(hash.x-.5)*.30*_ReliefStrength*bank+foldUp*(hash.y-.5)*.22*_ReliefStrength*bank);
                float crackFootprint=max(fwidth(gap)*1.15,.0025);
                float crack=1-smoothstep(crackFootprint*.35,crackFootprint*1.65,gap);
                float panelTone=lerp(.92,1.04,hash.y);
                float layer=sin(i.w.y*2.15+u*.11+hash.x*.45)*.035*_StrataStrength;
                float factor=bank*_DetailStrength;
                stone*=lerp(1,panelTone+layer,factor);
                stone=lerp(stone,stone*.66,crack*factor*.035);
                float gold=crack*step(.78,hash.x)*factor*.15;
                stone=lerp(stone,half3(.70,.53,.25),gold);
                half3 fill=lerp(half3(.42,.46,.49),half3(.055,.085,.14),saturate(_OverworldEclipseDarkness));
                half3 light=max(SampleSH(n)*.75,fill)+sun.color*(.10+saturate(dot(n,sun.direction))*.82)*sun.shadowAttenuation;
                half3 lit=stone*light; half peak=max(lit.r,max(lit.g,lit.b)); half knee=.68h; half width=.30h; half shoulder=knee+width*(1-exp(-max(0,peak-knee)/width)); lit*=peak>knee?shoulder/max(peak,.0001h):1; return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}

