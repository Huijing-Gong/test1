Shader "FoldedRealm/EclipseAtmosphere"
{
    Properties
    {
        _Tint("Tint and density",Color)=(.32,.4,.53,.23)
        _LayerMode("0 mist / 1 cloud / 2 ember",Float)=0
        _Density("Mist layer density",Float)=1
        _Seed("Variation",Float)=0
        _Motion("Motion enabled",Float)=1
        _AtmosphereTime("Scaled atmosphere clock",Float)=0
        _SoftDepth("Optional copied-depth soft fade",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "EclipseAtmosphere"
            Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _LayerMode,_Density,_Seed,_Motion,_AtmosphereTime,_SoftDepth;
            CBUFFER_END
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float4 seed:TEXCOORD1;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float eye:TEXCOORD2;float life:TEXCOORD3;};
            float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
            float noise(float2 p)
            {
                float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            float fbm(float2 p){return noise(p)*.57+noise(p*2.03+7.1)*.29+noise(p*4.11+13.7)*.14;}
            V Vert(A v)
            {
                V o;float3 world=TransformObjectToWorld(v.p.xyz);o.life=0;
                if(_LayerMode>1.5)
                {
                    o.life=frac(_AtmosphereTime*v.seed.w+v.seed.x);
                    world.y+=o.life*v.seed.z;
                    world.x+=sin(o.life*4+v.seed.y)*.07;
                }
                o.world=world;o.p=TransformWorldToHClip(world);o.uv=v.uv;
                o.eye=-TransformWorldToView(world).z;return o;
            }
            half4 Frag(V i):SV_Target
            {
                float alpha;float3 colour=_Tint.rgb;
                if(_LayerMode<.5)
                {
                    float2 p=i.world.xy*float2(.14,.54)+float2(_Seed,_Seed*.31);
                    p-=float2(_AtmosphereTime*.014,_AtmosphereTime*.003);
                    float fog=.20+.80*smoothstep(.25,.78,fbm(p+noise(p*.7)*.35));
                    float window=(1-smoothstep(.18,.5,abs(i.uv.x-.5)))*(1-smoothstep(.18,.5,abs(i.uv.y-.5)));
                    float ceiling=1-smoothstep(-.65,.15,i.world.y);
                    float soft=1;
                    if(_SoftDepth>.5)
                    {
                        // URP helper handles RenderTexture Y orientation on Metal.
                        float depth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.p)),_ZBufferParams);
                        soft=depth<=max(_ProjectionParams.y*1.5,.5)?1:saturate((depth-i.eye)/1.3);
                    }
                    alpha=fog*window*ceiling*soft*_Tint.a*_Density;
                }
                else if(_LayerMode<1.5)
                {
                    float2 p=i.uv*float2(3.4,4.1)+float2(_AtmosphereTime*.0027,-_AtmosphereTime*.0008);
                    float field=fbm(p+float2(4.7,2.1));
                    float sky=smoothstep(.49,.70,i.uv.y);
                    float edge=(1-smoothstep(.42,.5,abs(i.uv.x-.5)))*(1-smoothstep(.43,.5,abs(i.uv.y-.5)));
                    float body=smoothstep(.38,.76,field);
                    float moon=exp(-length((i.uv-float2(.53,.78))*float2(4,5)));
                    float rim=smoothstep(.36,.50,field)*(1-smoothstep(.51,.65,field))*moon;
                    colour+=float3(.32,.035,.018)*rim;
                    alpha=(body*.86+rim*.14)*sky*edge*_Tint.a;
                }
                else
                {
                    float2 p=(i.uv-.5)*2;
                    float spark=pow(saturate(1-dot(p,p)),2);
                    float life=smoothstep(0,.15,i.life)*(1-smoothstep(.6,1,i.life));
                    alpha=spark*life*_Tint.a*_Motion;
                    colour*=.8+.2*sin(i.life*10);
                }
                return half4(colour,saturate(alpha));
            }
            ENDHLSL
        }
    }
}
