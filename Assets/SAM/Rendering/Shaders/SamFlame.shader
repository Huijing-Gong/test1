Shader "SamProduction/Flame"
{
    Properties
    {
        _MainTex("Authored Shape / Erosion RGBA",2D)="white"{}
        [HDR] _BaseColor("Outer Flame",Color)=(1,0.10,0.008,1)
        [HDR] _HotColor("Hot Core",Color)=(1,0.68,0.18,1)
        _EmissionPower("Emission",Range(0,12))=3
        _Opacity("Opacity",Range(0,1))=1
        _Dissolve("Dissolve",Range(0,1))=0
        _FlowSpeed("Flow Speed",Float)=1.4
        _NoiseScale("Erosion Scale",Float)=2.5
        _EdgeSoftness("Edge Softness",Range(0.01,0.5))=0.09
        _Fragmentation("Energy Coherent Breakup",Range(0,1))=0
        _LocalTime("Instance Time",Float)=0
        _Seed("Instance Seed",Float)=0
        _ShapeMode("Shape: 0 Particle / 1 Trail / 2 Petal / 3 Open Mass / 4 Volume / 5 Surface Fire / 6 Spray",Float)=0
        _VolumeDensity("Volume extinction per metre",Range(0.2,10))=3.7
        [IntRange] _VolumeSteps("Volume samples (maximum 48)",Range(16,48))=40
        _VolumeTurbulence("Volume turbulent density",Range(0,1))=0.62
        _VolumeCoreBreakup("Volume internal thermal channels",Range(0,1))=0.72
        _VolumePhase("Volume normalized lifetime",Range(0,1))=0
        _VolumeDomainScale("Proxy padding / physical density domain",Float)=2.6
        [HideInInspector] _ZTest("Depth test",Float)=4
        _SoftDistance("Depth Fade Distance",Float)=0.16
        [Toggle] _UseDepthFade("Enable Depth Fade",Float)=1
        [IntRange] _ReviewMode("Diagnostic: 0 Final / 1 Solid / 2 Alpha / 3 Depth Fade / 4 UV / 5 No Depth",Range(0,5))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "FlamePremultiplied"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZTest [_ZTest]
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor,_HotColor;
                float _EmissionPower,_Opacity,_Dissolve,_FlowSpeed,_NoiseScale,_EdgeSoftness,_Fragmentation;
                float _LocalTime,_Seed,_ShapeMode,_SoftDistance,_UseDepthFade,_ReviewMode;
                float _VolumeDensity,_VolumeSteps,_VolumeTurbulence,_VolumePhase,_VolumeDomainScale,_VolumeCoreBreakup;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float eyeDepth:TEXCOORD1;half fog:TEXCOORD2;half3 normalWS:TEXCOORD3;float3 positionWS:TEXCOORD4;float3 positionLS:TEXCOORD5;UNITY_VERTEX_OUTPUT_STEREO};
            V vert(A input)
            {
                V output=(V)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=p.positionCS;output.eyeDepth=-p.positionVS.z;
                output.positionWS=p.positionWS;output.positionLS=input.positionOS.xyz;
                output.normalWS=dot(input.normalOS,input.normalOS)>.0001?TransformObjectToWorldNormal(input.normalOS):half3(0,0,0);
                output.uv=input.uv;output.color=input.color;output.fog=ComputeFogFactor(p.positionCS.z);return output;
            }
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7))+_Seed*0.371)*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
            float hashVolume(float3 p)
            {
                p=frac((p+_Seed*.019)*.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float noiseVolume(float3 p)
            {
                float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                float z0=lerp(lerp(hashVolume(i),hashVolume(i+float3(1,0,0)),f.x),
                    lerp(hashVolume(i+float3(0,1,0)),hashVolume(i+float3(1,1,0)),f.x),f.y);
                float z1=lerp(lerp(hashVolume(i+float3(0,0,1)),hashVolume(i+float3(1,0,1)),f.x),
                    lerp(hashVolume(i+float3(0,1,1)),hashVolume(i+float3(1,1,1)),f.x),f.y);
                return lerp(z0,z1,f.z);
            }
            float compactFlameLobe(float3 p,float3 center,float3 inverseSize)
            {
                float3 d=(p-center)*inverseSize;
                float r2=dot(d,d);
                // Compact support belongs to the gas lobe, not to the proxy's box faces.
                return exp2(-r2*3.1)*(1-smoothstep(2.25,4.0,r2));
            }
            float volumeDensityAt(float3 proxyPoint,out float temperature)
            {
                // Physical density space is independent of proxy padding. Multiple compact
                // lobes have zero support well inside the integration bounds: no box fade.
                float3 p=proxyPoint*_VolumeDomainScale;
                float3 warp=sin(float3(p.y*4.3+p.z*2.1,p.x*3.7-p.z*2.4,p.x*3.0+p.y*3.3)
                    +float3(_LocalTime*11,_Seed*.13,-_LocalTime*9))*(_VolumeTurbulence*.22);
                float3 q=p+warp;
                float3 flow=float3(.13,-.18,-1)*_LocalTime*6.5;
                float n=noiseVolume(q*2.6+flow)*.72+noiseVolume(q*5.7+flow*.81+13.3)*.28;
                float splitMotion=sin(_Seed*.17+_LocalTime*19);
                float core=compactFlameLobe(q,float3(-.06,.015,-.16),float3(1.40,1.63,.94));
                float sideA=compactFlameLobe(q,float3(.36,.19+splitMotion*.055,.18+_VolumePhase*.32),float3(2.18,2.08,1.42));
                float sideB=compactFlameLobe(q,float3(-.29,-.28,.40+_VolumePhase*.31),float3(2.15,2.94,1.62));
                float sideC=compactFlameLobe(q,float3(.11,.44,-.05+_VolumePhase*.18),float3(3.13,3.23,1.96));
                float decay=smoothstep(.32,.98,_VolumePhase);
                float breakup=1-decay*smoothstep(.35,.77,n)*.84;
                float density=(core*.91*(.76+n*.24)+(sideA*.63+sideB*.62+sideC*.39)*(.34+n*.90))*breakup;
                // Anisotropic 3D channels stretch with the release flow. They modulate both
                // opacity and temperature so integration does not merge every lobe into one flat white patch.
                float channelNoise=noiseVolume(float3(q.x*.72,q.y*9.5,q.z*3.6)+float3(_LocalTime*1.7,-_LocalTime*7.2,-_LocalTime*3.4));
                float channel=smoothstep(.26,.69,channelNoise);
                float detail=saturate(_VolumeCoreBreakup)*lerp(1,.40,smoothstep(.38,.90,density));
                density*=lerp(1,.27+channel*1.01,detail);
                temperature=smoothstep(.09,.62,density+n*.035)*lerp(1,.16+channel*.84,detail);
                return max(0,density);
            }
            half4 renderVolume(V input,bool frontFace)
            {
                float3 rayDirectionWS=-GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 rayOriginWS=GetCurrentViewPosition();
                if(unity_OrthoParams.w>0.5)rayOriginWS=input.positionWS-rayDirectionWS*input.eyeDepth;
                float3 ro=TransformWorldToObject(rayOriginWS);
                float3 rd=TransformWorldToObjectDir(rayDirectionWS,false);
                bool inside=all(abs(ro)<1);
                if((inside&&frontFace)||(!inside&&!frontFace))discard;
                float3 safeDir=lerp(-1.0,1.0,step(0,rd))*max(abs(rd),.000001);
                // Conservative physical support bounds for all four compact lobes, all phases
                // and turbulence <= 1. These bounds and step intervals do not grow with proxy padding.
                float3 support=float3(1.74,1.50,2.55)/max(2.6,_VolumeDomainScale);
                float3 t0=(-support-ro)/safeDir,t1=(support-ro)/safeDir;
                float3 low=min(t0,t1),high=max(t0,t1);
                float enter=max(0,max(low.x,max(low.y,low.z)));
                float exitDistance=min(high.x,min(high.y,high.z));
                if(exitDistance<=enter)return 0;
                float fullExit=exitDistance;
                float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                float sceneEye=LinearEyeDepth(raw,_ZBufferParams);
                if(unity_OrthoParams.w>0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw=1-raw;
                    #endif
                    sceneEye=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                }
                float sceneRay=sceneEye/max(.0001,dot(rayDirectionWS,GetViewForwardDir()));
                float clippedExit=min(fullExit,sceneRay);
                if(_ReviewMode>2.5&&_ReviewMode<3.5)
                {
                    float fraction=saturate((clippedExit-enter)/max(.001,fullExit-enter));
                    return half4(fraction,fraction,fraction,1);
                }
                // IntrinsicAlpha and NoDepthFade deliberately bypass scene-depth truncation.
                bool intrinsic=_ReviewMode>1.5&&_ReviewMode<2.5;
                if(_UseDepthFade>.5&&_ReviewMode<4.5&&!intrinsic)exitDistance=clippedExit;
                if(exitDistance<=enter)return 0;
                if(_ReviewMode>.5&&_ReviewMode<1.5)return half4(.12h,1,.38h,1);
                if(_ReviewMode>3.5&&_ReviewMode<4.5)return half4(saturate(input.positionLS*.5+.5),1);
                int steps=(int)clamp(round(_VolumeSteps),16,48);
                float stepWorld=(exitDistance-enter)/steps;
                float transmittance=1;
                float3 radiance=0;
                [loop] for(int i=0;i<48;i++)
                {
                    if(i>=steps||transmittance<.006)break;
                    float distanceAlongRay=enter+(i+.5)*stepWorld;
                    float temperature;
                    float density=volumeDensityAt(ro+rd*distanceAlongRay,temperature);
                    float stepAlpha=1-exp(-density*_VolumeDensity*_Opacity*stepWorld);
                    float3 emitted=lerp(_BaseColor.rgb,_HotColor.rgb,temperature)*_EmissionPower;
                    radiance+=transmittance*stepAlpha*emitted;
                    transmittance*=1-stepAlpha;
                }
                float alpha=1-transmittance;
                if(intrinsic)return half4(alpha,alpha,alpha,1);
                return half4(MixFogColor(radiance,half3(0,0,0),input.fog),alpha);
            }
                        half4 renderRay(V input,bool frontFace)
            {
                if(!frontFace)discard;
                float3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing=saturate(dot(normalize(input.normalWS),view));
                float alpha=input.color.a*_Opacity*pow(facing,.65);
                float wave=sin((input.uv.x*2.05-_LocalTime*1.8+input.color.r)*6.2831853);
                float gaps=smoothstep(.70,.97,wave)*smoothstep(.075,.18,_LocalTime);
                alpha*=1-gaps*_Fragmentation*.86;
                clip(alpha-.001);
                if(_ReviewMode>.5&&_ReviewMode<1.5)return half4(.12,1,.38,1);
                if(_ReviewMode>3.5&&_ReviewMode<4.5)return half4(input.uv,0,1);
                bool intrinsic=_ReviewMode>1.5&&_ReviewMode<2.5;
                if(intrinsic)return half4(alpha,alpha,alpha,1);
                float fade=1;
                if(_UseDepthFade>.5&&_ReviewMode<4.5)
                {
                    float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float sceneEye=LinearEyeDepth(raw,_ZBufferParams);
                    if(unity_OrthoParams.w>.5)
                    {
                        #if UNITY_REVERSED_Z
                            raw=1-raw;
                        #endif
                        sceneEye=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                    }
                    fade=saturate((sceneEye-input.eyeDepth)/max(.001,_SoftDistance));
                }
                if(_ReviewMode>2.5&&_ReviewMode<3.5)return half4(fade,fade,fade,1);
                alpha*=fade;
                float hot=pow(facing,5)*(1-smoothstep(.32,.97,input.uv.x)*.58);
                float3 color=lerp(_BaseColor.rgb,_HotColor.rgb,hot)*_EmissionPower;
                return half4(color*alpha,alpha);
            }

            half4 frag(V input,bool frontFace:SV_IsFrontFace):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                [branch] if(_ShapeMode>3.5&&_ShapeMode<4.5)return renderVolume(input,frontFace);
                [branch] if(_ShapeMode>7.5)return renderRay(input,frontFace);
                float2 uv=input.uv;
                if(_ReviewMode>0.5&&_ReviewMode<1.5)return half4(0.12h,1.0h,0.38h,1);
                if(_ReviewMode>3.5&&_ReviewMode<4.5)return half4(saturate(uv),0.2h,1);
                float2 flow=uv*float2(_NoiseScale,_NoiseScale*1.45)-float2(_LocalTime*0.14,_LocalTime*_FlowSpeed);
                if(_ShapeMode>1.5&&_ShapeMode<2.5)
                    flow=uv*float2(_NoiseScale*1.7,_NoiseScale*.7)-float2(_LocalTime*_FlowSpeed,_LocalTime*.16);
                if(_ShapeMode>4.5&&_ShapeMode<5.5)flow=uv*float2(2.1,.85)-float2(_LocalTime*1.65,_LocalTime*.07);
                if(_ShapeMode>5.5)flow=uv*float2(3.8,1.4)+input.positionLS.xy*.43-float2(_LocalTime*5.1,_LocalTime*.29);
                float n=noise(flow)*0.65+noise(flow*2.7+4.3)*0.35;
                float4 tex=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
                n=lerp(n,tex.r,0.24);
                float radial=saturate(1-length((uv-.5)*float2(2,1.8)));
                float ribbon=saturate(1-abs(uv.y*2-1));
                float shape=_ShapeMode<0.5?tex.a*radial:ribbon;
                if(_ShapeMode>1.5&&_ShapeMode<2.5)
                {
                    // Geometry provides the curling silhouette; the surface opens unequal holes
                    // and erodes its edges. It is not a luminous rectangular strip.
                    float bend=sin(uv.x*11.7+_Seed*.19+_LocalTime*4)*(.025+uv.x*.05);
                    float edge=abs((uv.y-.5-bend)*2);
                    float lace=noise(flow*2.1+19.4)*.13+sin(uv.x*31+_Seed)*.035;
                    shape=saturate((1-edge-lace)*2.8);
                    shape*=smoothstep(0.0,0.035,uv.x)*(1-smoothstep(.82,1.0,uv.x));
                    float tear=noise(float2(uv.x*6.7-_LocalTime*6.2,uv.y*3.9)+_Seed*.061);
                    float split=smoothstep(.74-_Fragmentation*.19,.89-_Fragmentation*.16,tear);
                    shape*=1-split*smoothstep(.18,.55,uv.x);
                }
                if(_ShapeMode>2.5&&_ShapeMode<3.5)
                {
                    // Both sides of open curved flame sheets are visible. Broad advected holes,
                    // asymmetrical edge erosion and transparency preserve actual gaps in the mass.
                    float2 advect=float2(uv.x*5.8-_LocalTime*7.4,uv.y*3.3);
                    float broad=noise(advect+input.positionLS.zz*.73+_Seed*.041);
                    n=broad*.73+noise(advect*2.2+17)*.27;
                    float bend=sin(uv.x*9.3+input.positionLS.z*2.4-_LocalTime*9)*.065;
                    float edge=abs((uv.y-.5-bend)*2);
                    shape=saturate((1-edge-.12*noise(advect*1.4+7))*2.0);
                    shape*=smoothstep(0,.055,uv.x)*(1-smoothstep(.75,1,uv.x));
                    float opening=smoothstep(.66-_Fragmentation*.20,.83-_Fragmentation*.16,broad);
                    shape*=1-opening*smoothstep(.08,.36,uv.x);
                }
                if(_ShapeMode>4.5&&_ShapeMode<5.5)
                {
                    // Lower surface fire has softer shoulders and a base-to-tip thermal falloff.
                    // Keep this separate so shared energy/stage mode2 does not change.
                    float bend=sin(uv.x*5.6+_Seed*.13-_LocalTime*2.2)*.055;
                    float edge=abs((uv.y-.5-bend)*2);
                    float large=noise(flow*1.3+_Seed*.017);
                    shape=(1-smoothstep(.38+large*.13,.99,edge));
                    shape*=smoothstep(0,.035,uv.x)*(1-smoothstep(.61,.99,uv.x));
                    shape*=1-smoothstep(.77,.95,large)*smoothstep(.25,.70,uv.x)*.62;
                }
                if(_ShapeMode>5.5)
                {
                    // Disconnected short world-space packets have individual rounded shoulders.
                    // Coarse moving density forms thick hot islands with warm separated edges.
                    float2 packetFlow=uv*float2(2.4,2.2)+input.positionLS.yz*.51-float2(_LocalTime*3.8,_LocalTime*.21);
                    float broad=noise(packetFlow+_Seed*.021);
                    float fine=noise(packetFlow*2.1+input.positionLS.xz*.63+11.7);
                    n=broad*.82+fine*.18;
                    float bend=sin(uv.x*6.7+input.positionLS.z*1.3-_LocalTime*7.2)*.08;
                    float edge=abs((uv.y-.5-bend)*2);
                    shape=1-smoothstep(.27+broad*.22,1.0,edge);
                    shape*=smoothstep(0,.055,uv.x)*(1-smoothstep(.52,1.0,uv.x));
                    float warmGap=smoothstep(.54-_Fragmentation*.08,.84-_Fragmentation*.05,broad);
                    shape*=lerp(.38,1,warmGap);
                }
                float erosion=n*0.65+shape*0.62-_Dissolve;
                float a=shape*smoothstep(0.18,0.18+_EdgeSoftness,erosion)*input.color.a*_Opacity;
                float depthFade=1;
                if((_UseDepthFade>0.5&&_ReviewMode<4.5)||(_ReviewMode>2.5&&_ReviewMode<3.5))
                {
                    float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float depth=LinearEyeDepth(raw,_ZBufferParams);
                    if(unity_OrthoParams.w>0.5)
                    {
                        #if UNITY_REVERSED_Z
                            raw=1-raw;
                        #endif
                        depth=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                    }
                    depthFade=saturate((depth-input.eyeDepth)/max(_SoftDistance,0.001));
                }
                // These outputs deliberately keep alpha one so zero-valued diagnostics remain visible.
                if(_ReviewMode>1.5&&_ReviewMode<2.5)return half4(a,a,a,1);
                if(_ReviewMode>2.5&&_ReviewMode<3.5)return half4(depthFade,depthFade,depthFade,1);
                if(_ReviewMode<4.5)a*=depthFade;
                float core=smoothstep(0.43,0.88,shape+n*0.24-_Dissolve*0.3);
                if(_ShapeMode>4.5&&_ShapeMode<5.5)core=pow(saturate(1-abs(uv.y*2-1)),1.5)*(1-smoothstep(.14,.85,uv.x));
                if(_ShapeMode>5.5)
                {
                    float thermalAxis=pow(saturate(1-abs(uv.y*2-1)),1.05);
                    float hotPocket=smoothstep(.32,.75,n);
                    core=thermalAxis*(1-smoothstep(.28,.90,uv.x))*lerp(.32,1,hotPocket);
                }
                float3 color=lerp(_BaseColor.rgb,_HotColor.rgb,core)*input.color.rgb*_EmissionPower;
                color=MixFogColor(color,half3(0,0,0),input.fog);
                return half4(color*a,a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
