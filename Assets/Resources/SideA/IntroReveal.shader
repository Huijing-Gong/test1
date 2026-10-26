Shader "SideA/Intro Reveal"
{
    Properties
    {
        _BaseMap("Color",2D)="white"{}
        _BaseColor("Base",Color)=(1,1,1,1)
        _TeamColor("Team",Color)=(.1,.55,1,1)
        _RecolorTeam("Recolor cloth",Float)=0
        _RevealHeight("World reveal height",Float)=1000
        _Glow("Glow",Float)=0
        _RimTint("Rim",Color)=(.52,.9,.8,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor, _TeamColor, _RimTint;
                float _RevealHeight, _RecolorTeam, _Glow;
            CBUFFER_END
            struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0;};
            struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2;};
            V Vert(A i)
            {
                V o; o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float noise=sin(dot(i.positionWS,float3(57.1,31.7,73.3)))*sin(i.positionWS.y*89.0)*.025;
                float distance=_RevealHeight-i.positionWS.y+noise;
                clip(distance);
                half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half green=(color.g-max(color.r,color.b))/max(.015h,color.g);
                half mask=smoothstep(.10h,.28h,green)*_RecolorTeam;
                half luminance=dot(color,half3(.2126h,.7152h,.0722h));
                color=lerp(color,_TeamColor.rgb*(.16h+luminance*2.6h),mask)*_BaseColor.rgb;
                half3 normal=normalize(i.normalWS);
                Light light=GetMainLight();
                half3 lighting=max(half3(.20h,.20h,.20h),SampleSH(normal))*.65h+
                    light.color*saturate(dot(normal,light.direction))*.75h;
                half rim=pow(1-saturate(dot(normal,GetWorldSpaceNormalizeViewDir(i.positionWS))),4);
                half edge=1-smoothstep(.015,.10,distance);
                half scan=1-smoothstep(.025,.11,abs(distance-.07));
                return half4(color*lighting+half3(1,.76,.32)*edge*2.2+
                    _RimTint.rgb*(rim*.8+scan*.4)*_Glow,1);
            }
            ENDHLSL
        }
    }
}
