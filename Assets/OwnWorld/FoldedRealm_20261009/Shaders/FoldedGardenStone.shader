Shader "KingOfGraphic/Own/FoldedGardenStone"
{
    Properties { _BaseColor("Owned limestone tint",Color)=(1,1,1,1) }
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
            CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; half4 c:COLOR; float2 bary:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; float2 bary:TEXCOORD2; half fog:TEXCOORD3; };
            V Vert(A a) { V o; o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c;o.bary=a.bary;o.fog=ComputeFogFactor(o.p.z);return o; }
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
                half3 light=max(SampleSH(n)*.75,half3(.42,.46,.49))+sun.color*(.10+saturate(dot(n,sun.direction))*.82)*sun.shadowAttenuation;
                half3 lit=stone*light; half peak=max(lit.r,max(lit.g,lit.b)); half knee=.68h; half width=.30h; half shoulder=knee+width*(1-exp(-max(0,peak-knee)/width)); lit*=peak>knee?shoulder/max(peak,.0001h):1; return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}

