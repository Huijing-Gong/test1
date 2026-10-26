Shader "SamProduction/DashFlame18"
{
    Properties
    {
        [HDR] _BaseColor("Warm outer gas",Color)=(1,.11,.006,1)
        [HDR] _HotColor("Narrow hot channels",Color)=(1,.74,.22,1)
        _EmissionPower("Radiance",Range(0,8))=4
        _Opacity("Lifetime extinction",Range(0,1))=.57
        _Density("Extinction per metre",Range(1,20))=11
        _LocalTime("Owned simulation time",Float)=0
        _Seed("Execution seed",Float)=0
        _CapsuleRadius("Actual damage capsule radius",Float)=.4
        _CapsuleLength("Actual damage capsule segment",Float)=.95
        [Toggle] _UseDepthFade("Clip density to opaque scene",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "PhysicalDomainFire"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _HotColor;
                float _EmissionPower, _Opacity, _Density, _LocalTime, _Seed;
                float _CapsuleRadius, _CapsuleLength, _UseDepthFade;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float eyeDepth:TEXCOORD1;
                half fog:TEXCOORD2;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=position.positionCS;
                output.positionWS=position.positionWS;
                output.eyeDepth=-position.positionVS.z;
                output.fog=ComputeFogFactor(position.positionCS.z);
                return output;
            }
            float hash3(float3 p)
            {
                p=frac((p+_Seed*.017)*.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float noise3(float3 p)
            {
                float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                float low=lerp(lerp(hash3(i),hash3(i+float3(1,0,0)),f.x),
                               lerp(hash3(i+float3(0,1,0)),hash3(i+float3(1,1,0)),f.x),f.y);
                float high=lerp(lerp(hash3(i+float3(0,0,1)),hash3(i+float3(1,0,1)),f.x),
                                lerp(hash3(i+float3(0,1,1)),hash3(i+1),f.x),f.y);
                return lerp(low,high,f.z);
            }
            float capsuleDistance(float3 p)
            {
                p.z-=clamp(p.z,0,_CapsuleLength);
                return length(p)-_CapsuleRadius;
            }
            float densityAt(float3 samplePosition,out float temperature)
            {
                float radius=max(.001,_CapsuleRadius);
                float signedDistance=capsuleDistance(samplePosition);
                // No density, radiance or flame tongue can leave the queried
                // physical capsule. This is not a larger soft proxy volume.
                if(signedDistance>=0){temperature=0;return 0;}
                float axial=saturate((samplePosition.z+radius)/(_CapsuleLength+2*radius));
                float3 p=samplePosition/radius;
                float2 bend=float2(sin(axial*8.7-_LocalTime*10+_Seed),
                                   sin(axial*6.3-_LocalTime*13+_Seed*.7))*.13;
                p.xy-=bend;
                float3 flow=float3(.35,-.55,-2.5)*_LocalTime*5;
                float coarse=noise3(p*float3(3.3,3.8,1.65)+flow);
                float fine=noise3(p*float3(7.6,8.7,3.9)+flow*.77+19.4);
                float turbulent=coarse*.70+fine*.30;
                // Three-dimensional anisotropic channels replace the former
                // flat packet UV treatment. Gas flows along the punch axis.
                float radial=length(p.xy);
                float profile=1-axial*.20;
                float fuel=saturate(1-radial/max(.2,profile));
                float channel=fuel*.91+(turbulent-.45)*.94;
                float core=smoothstep(.17,.69,channel);
                float wisps=smoothstep(.46,.78,turbulent)*(1-smoothstep(.35,.86,radial))*.19;
                float boundary=smoothstep(0,radius*.045,-signedDistance);
                float taper=1-smoothstep(.79,1,axial)*.56;
                float density=(core+wisps)*boundary*taper;
                temperature=pow(saturate(fuel),1.25)*lerp(.18,1,smoothstep(.25,.78,fine))
                            *(1-smoothstep(.62,1,axial)*.42);
                return max(0,density);
            }
            half4 frag(Varyings input,bool frontFace:SV_IsFrontFace):SV_Target
            {
                float3 rayDirectionWS=-GetWorldSpaceNormalizeViewDir(input.positionWS);
                float forwardDot=max(.0001,dot(rayDirectionWS,GetViewForwardDir()));
                float3 rayOriginWS=GetCurrentViewPosition();
                if(unity_OrthoParams.w>.5)
                    rayOriginWS=input.positionWS-rayDirectionWS*(input.eyeDepth/forwardDot);
                float3 origin=TransformWorldToObject(rayOriginWS);
                float3 direction=TransformWorldToObjectDir(rayDirectionWS,false);
                bool inside=capsuleDistance(origin)<0;
                if((inside&&frontFace)||(!inside&&!frontFace))discard;
                float r=max(.001,_CapsuleRadius);
                float3 safe=lerp(-1.0,1.0,step(0,direction))*max(abs(direction),.000001);
                float3 first=(float3(-r,-r,-r)-origin)/safe;
                float3 last=(float3(r,r,_CapsuleLength+r)-origin)/safe;
                float3 nearPoint=min(first,last),farPoint=max(first,last);
                float enter=max(0,max(nearPoint.x,max(nearPoint.y,nearPoint.z)));
                float exitDistance=min(farPoint.x,min(farPoint.y,farPoint.z));
                if(_UseDepthFade>.5)
                {
                    float depth=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float sceneEye=LinearEyeDepth(depth,_ZBufferParams);
                    if(unity_OrthoParams.w>.5)
                    {
                        #if UNITY_REVERSED_Z
                            depth=1-depth;
                        #endif
                        sceneEye=lerp(_ProjectionParams.y,_ProjectionParams.z,depth);
                    }
                    exitDistance=min(exitDistance,sceneEye/forwardDot);
                }
                if(exitDistance<=enter)return 0;
                // One bounded 20-sample integration, only on the small release
                // footprint. No second independent volume or simulation clock.
                const int samples=20;
                float stepSize=(exitDistance-enter)/samples;
                float transmittance=1;
                float3 radiance=0;
                [loop] for(int i=0;i<samples;i++)
                {
                    float temperature;
                    float density=densityAt(origin+direction*(enter+(i+.5)*stepSize),temperature);
                    float alpha=1-exp(-density*_Density*_Opacity*stepSize);
                    float3 emitted=lerp(_BaseColor.rgb,_HotColor.rgb,temperature)*_EmissionPower;
                    radiance+=transmittance*alpha*emitted;
                    transmittance*=1-alpha;
                    if(transmittance<.008)break;
                }
                return half4(MixFogColor(radiance,half3(0,0,0),input.fog),1-transmittance);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
