Shader "KingOfGraphic/Own/DistantFoldedVista"
{
    Properties { _HazeColor("Distance atmosphere", Color)=(.51,.66,.77,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Cull Back Blend SrcAlpha OneMinusSrcAlpha ZWrite On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _HazeColor;
            CBUFFER_END
            float _OverworldEclipseDarkness;
            struct A { float4 p:POSITION; float3 n:NORMAL; half4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; half fog:TEXCOORD2; };
            V Vert(A a) { V o; o.w=TransformObjectToWorld(a.p.xyz); o.p=TransformWorldToHClip(o.w); o.n=TransformObjectToWorldNormal(a.n); o.c=a.c; o.fog=ComputeFogFactor(o.p.z); return o; }
            half4 Frag(V i):SV_Target
            {
                half3 n=normalize(i.n); Light sun=GetMainLight();
                half3 color=i.c.rgb*(half3(.38,.42,.44)+sun.color*(.16+.59*saturate(dot(n,sun.direction))));
                float range=distance(_WorldSpaceCameraPos,i.w);
                float haze=.25+.48*smoothstep(200,1050,range);
                haze=saturate(haze+(1-smoothstep(-8,55,i.w.y))*.17);
                // The daytime haze is baked into this vista shader, not RenderSettings fog.
                // Blend both the rock lighting and haze so distant islands follow the eclipse.
                half eclipse=saturate(_OverworldEclipseDarkness);
                half3 nightLight=half3(.065,.045,.075)+sun.color*.18*saturate(dot(n,sun.direction));
                color=lerp(color,i.c.rgb*nightLight,eclipse);
                half3 atmosphere=lerp(_HazeColor.rgb,half3(.105,.055,.085),eclipse);
                color=lerp(color,atmosphere,haze);
                float alpha=smoothstep(-16,38,i.w.y);
                clip(alpha-.015);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
