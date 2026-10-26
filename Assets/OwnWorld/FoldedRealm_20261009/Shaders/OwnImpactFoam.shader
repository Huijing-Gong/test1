Shader "KingOfGraphic/Own/WaterfallImpactFoam"
{
    Properties { _BaseColor("Foam", Color)=(.92,.98,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            V Vert(A a) { V o; o.p=TransformObjectToHClip(a.p.xyz); o.uv=a.uv; o.fog=ComputeFogFactor(o.p.z); return o; }
            float OwnHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float OwnNoise(float2 p)
            {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(OwnHash(a),OwnHash(a+float2(1,0)),f.x),lerp(OwnHash(a+float2(0,1)),OwnHash(a+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv; float r=length(p), t=_Time.y;
                float2 drift=float2(sin(t*.17),cos(t*.13))*.65;
                float warp=OwnNoise(p*4.5+drift)-.5;
                float noise=OwnNoise((p+warp*.055)*13+drift*1.5);
                float width=.057+min(.04,fwidth(noise)*.65);
                float lace=1-smoothstep(width,width+.077,abs(noise-.52));
                float churn=smoothstep(.26,.71,noise);
                float core=1-smoothstep(.14,.57,r);
                float fade=1-smoothstep(.66+warp*.16,.98,r);
                float eddies=pow(saturate(.5+.5*sin((r+warp*.15)*24-t*2.5)),4);
                float alpha=fade*(core*(.45+lace*.35+churn*.17)
                           +(1-core)*(lace*(.40+.25*churn)+churn*.13+eddies*churn*.13))*_BaseColor.a;
                return half4(MixFog(_BaseColor.rgb,i.fog),saturate(alpha));
            }
            ENDHLSL
        }
    }
}
