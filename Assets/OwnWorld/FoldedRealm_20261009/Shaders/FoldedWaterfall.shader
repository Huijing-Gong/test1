Shader "KingOfGraphic/Own/FoldedWaterfall"
{
    Properties { _BaseColor("Color", Color)=(.12,.52,.68,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+7" "RenderType"="Transparent" }
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
            float OwnHash(float2 p) { p=p-floor(p/64.0)*64.0; return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float OwnNoise(float2 p)
            {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(OwnHash(a),OwnHash(a+float2(1,0)),f.x),lerp(OwnHash(a+float2(0,1)),OwnHash(a+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv; float t=_Time.y;
                // UV y=0 at source, y=1 at lake: foam patterns travel down the surveyed curtain.
                float y=p.y-t*.30;
                float drift=(OwnNoise(float2(p.y*5.0-t*.35,2.8))-.5)*.075;
                float warp=(OwnNoise(float2(p.x*5.0,y*5.0))-.5)*.075;
                float channel=OwnNoise(float2((p.x+warp*.55+drift*.65)*25.0,y*10.0));
                float breakup=OwnNoise(float2((p.x+drift)*38.0,y*35.0));
                float width=.032+min(.025,fwidth(channel)*.45);
                float veins=1-smoothstep(width,width+.045,abs(channel-.5));
                float fragments=smoothstep(.16,.73,breakup);
                float baseFoam=smoothstep(.72,.98,p.y)*smoothstep(.24,.69,breakup);
                float sourceFoam=(1-smoothstep(.015,.16,p.y))*fragments*.20;
                float foam=saturate(veins*(.23+.77*fragments)+baseFoam*.36+sourceFoam);
                float fine=OwnNoise(float2((p.x+warp)*48.0,(p.y-t*.54)*39.0));
                float white=saturate(.13+foam*.80+fine*.07);
                float edgeNoise=OwnNoise(float2(p.y*17-t*2.1,2.7))*.025;
                float edge=smoothstep(edgeNoise,.052+edgeNoise,p.x-drift*.12)
                          *smoothstep(edgeNoise,.052+edgeNoise,1-p.x+drift*.12);
                half3 water=lerp(_BaseColor.rgb*.74,_BaseColor.rgb*1.08,channel);
                half3 color=lerp(water,half3(.93,.985,1),white);
                return half4(MixFog(color,i.fog),edge*(.85+foam*.13));
            }
            ENDHLSL
        }
    }
}
