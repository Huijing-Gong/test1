Shader "KingOfGraphic/Own/CompassMosaic"
{
    Properties { _BaseColor("Own mosaic tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; half4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; float3 n:TEXCOORD1; float2 uv:TEXCOORD2; half4 c:COLOR; half fog:TEXCOORD3; };
            float2 Hash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453); }
            V Vert(A a) { V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.uv=a.uv;o.c=a.c;o.fog=ComputeFogFactor(o.p.z);return o; }
            half4 Frag(V i):SV_Target
            {
                half3 stone=i.c.rgb*_BaseColor.rgb;
                if(i.c.a>.9)
                {
                    float2 q=i.uv*1.18,cell=floor(q),local=frac(q);
                    float nearest=10,second=10,tileShade=.5;
                    [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++)
                    {
                        float2 offset=float2(x,y),site=.25+.5*Hash(cell+offset);
                        float d=length(offset+site-local);
                        if(d<nearest){second=nearest;nearest=d;tileShade=Hash(cell+offset).x;}else second=min(second,d);
                    }
                    float gap=second-nearest,aa=max(fwidth(gap),.004);
                    float grout=1-smoothstep(.028-aa,.028+aa,gap);
                    stone=lerp(half3(.07,.24,.50),half3(.13,.36,.64),tileShade);
                    stone=lerp(stone,half3(.49,.37,.15),grout*.72);
                }
                float grain=frac(sin(dot(floor(i.uv*110),float2(12.9898,78.233)))*43758.5453);
                stone*=.987+.026*grain;
#ifndef UNITY_COLORSPACE_GAMMA
                stone=SRGBToLinear(stone);
#endif
                half3 n=normalize(i.n);Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));
                half3 light=max(SampleSH(n)*.75,half3(.40,.43,.47))+sun.color*(.08+saturate(dot(n,sun.direction))*.65)*sun.shadowAttenuation;
                return half4(MixFog(stone*light,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
