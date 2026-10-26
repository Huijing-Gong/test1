Shader "FoldedRealm/WildlifeWing"
{
    Properties
    {
        _BaseColor("Natural tint", Color) = (1,1,1,1)
        _WingAngle("Butterfly hinge angle", Float) = 0
        _WingTint("Butterfly species wing color", Color) = (.95,.71,.32,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On
        Pass
        {
            Name "Small wildlife"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _WingTint;
                float _WingAngle;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; float2 hinge:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; half4 color:COLOR; float2 uv:TEXCOORD2; half wing:TEXCOORD3; half fog:TEXCOORD4; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float angle = radians(_WingAngle) * sign(i.positionOS.x) * i.hinge.x;
                float s, c; sincos(angle, s, c);
                float3 p = i.positionOS.xyz;
                p.xy = float2(p.x*c-p.y*s,p.x*s+p.y*c);
                float3 n=i.normalOS;
                n.xy=float2(n.x*c-n.y*s,n.x*s+n.y*c);
                o.positionWS=TransformObjectToWorld(p);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(n);
                o.color=i.color*_BaseColor; o.uv=i.uv; o.wing=i.hinge.x;
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(facing,1,-1);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(n,sun.direction)*.7+.3);
                half3 ambient=max(SampleSH(n),half3(.14,.17,.15));
                half3 albedo=lerp(i.color.rgb,_WingTint.rgb,i.wing);
                // Tiny dark wing border, pale tips and delicate radial veins.
                // Pattern applies only to hinged butterfly triangles, not birds.
                float rim=smoothstep(.76,.98,i.uv.x);
                float vein=1-smoothstep(.022,.055,abs(sin((i.uv.y-.5)/(i.uv.x+.18)*9)));
                float spot=(1-smoothstep(.035,.105,length((i.uv-float2(.71,.73))*float2(1,1.5))));
                half3 wingColor=lerp(albedo,half3(.21,.16,.12),rim*.75+vein*.15);
                wingColor=lerp(wingColor,half3(1,.94,.77),spot*.8);
                albedo=lerp(albedo,wingColor,i.wing);
                half3 color=albedo*(ambient+sun.color*diffuse*lerp(.36,1,sun.shadowAttenuation));
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
