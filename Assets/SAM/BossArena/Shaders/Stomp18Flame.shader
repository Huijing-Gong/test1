Shader "SAM/Stomp18Flame"
{
    Properties { _Erosion("Broken fire surface",Float)=0 _SourceTime("Source time",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float _Erosion, _SourceTime;
            CBUFFER_END
            struct V { float4 positionCS:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float3 world:TEXCOORD1; };
            V vert(A a) { V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.color=a.color;o.uv=a.uv;return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y); }
            half4 frag(V i):SV_Target
            {
                float n=noise(i.world.xz*7+float2(_SourceTime*2,-_SourceTime*3));
                n=.65*n+.35*noise(i.world.xz*17-_SourceTime*4);
                float broken=smoothstep(.4,.65,n);
                i.color.a*=lerp(1,broken,_Erosion);
                return i.color;
            }
            ENDHLSL
        }
    }
}
