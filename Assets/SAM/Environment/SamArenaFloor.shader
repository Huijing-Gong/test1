Shader "SamProduction/ArenaFloor"
{
    Properties
    {
        _BaseColor("Stone", Color)=(.055,.018,.060,1)
        [HDR] _RingColor("Violet inscription", Color)=(.35,.035,.40,1)
        _PatternCenter("Inscription center XZ",Vector)=(2.1,2,0,0)
        _LocalTime("Stage clock",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor,_RingColor,_PatternCenter;
                float _LocalTime;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct V { float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1; };
            V vert(A i) { V o; o.world=TransformObjectToWorld(i.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normal);return o; }
            float stroke(float distance,float width) {return 1-smoothstep(width,width+max(fwidth(distance),.002),abs(distance));}
            half4 frag(V i):SV_Target
            {
                float2 p=i.world.xz-_PatternCenter.xy;
                float radius=length(p),angle=atan2(p.y,p.x);
                float inscription=stroke(radius-2.35,.003)+stroke(radius-2.42,.002)+stroke(radius-3.05,.003)+stroke(radius-5.35,.004);
                // Ground-space arcs and facets, kept editable independently from the source mesh.
                float facet=abs(dot(p,normalize(float2(.72,.69))));
                inscription+=stroke(facet-1.98,.005)*step(radius,3.05);
                float facet2=abs(dot(p,normalize(float2(-.45,.89))));
                inscription+=stroke(facet2-2.17,.005)*step(radius,3.05);
                float sector=abs(frac(angle*12/6.283185+.5)-.5);
                inscription+=stroke(sector,.007)*step(2.50,radius)*step(radius,2.79);
                float glow=exp(-abs(radius-2.35)*25)*.12+exp(-abs(radius-5.35)*20)*.08;
                float hotArc=stroke(radius-1.62,.006)*pow(saturate(.5+.5*sin(angle*3+_LocalTime*.8)),14);
                float stone=.84+.16*sin(i.world.x*2.3+sin(i.world.z*1.7))*sin(i.world.z*2.8);
                Light key=GetMainLight(TransformWorldToShadowCoord(i.world));
                float diffuse=saturate(dot(normalize(i.normal),key.direction));
                float3 lit=_BaseColor.rgb*stone*(.22+key.color*diffuse*key.shadowAttenuation);
                float fireBand=exp(-abs(length(i.world.xz-float2(1,1))-6.4)*1.1);
                lit+=float3(.20,.007,.001)*fireBand;
                float3 emission=_RingColor.rgb*(inscription+glow*2)+float3(1,.08,.006)*hotArc;
                return half4(lit+emission,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
