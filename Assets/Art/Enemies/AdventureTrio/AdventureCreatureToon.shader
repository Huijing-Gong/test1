Shader "FoldedRealm/AdventureCreatureToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Surface Color", 2D) = "white" {}
        [MainColor] _BaseColor("Surface Tint", Color) = (0.72,0.70,0.78,1)
        _ShadowColor("Shadow Multiplier (linear ratio)", Vector) = (0.38,0.40,0.46,1)
        _DeepShadowColor("Deep Shadow Multiplier (linear ratio)", Vector) = (0.16,0.18,0.22,1)
        _ShadeThreshold("Lit Band Threshold", Range(-1,1)) = 0.48
        _ShadeSoftness("Band Transition", Range(0.005,0.5)) = 0.08
        _DeepShadeThreshold("Deep Band Threshold", Range(-1,1)) = 0.03
        _FormStrength("Within-Band Form", Range(0,1)) = 0.55
        _LightColorPreservation("White Armor Light Identity", Range(0,1)) = 0.78
        _ShadowLightSaturation("Key Color In Unlit Bands", Range(0,1)) = 0.12
        _AdditionalLightStrength("Additional Diffuse Response", Range(0,2)) = 0.65
        _GrazingShade("Grazing Form Accent", Range(0,0.5)) = 0.18
        _AmbientStrength("Ambient Response", Range(0,2)) = 0.35
        _OcclusionMap("Baked Cavity R (UV2)",2D)="white"{}
        _OcclusionStrength("Cavity Strength",Range(0,1))=0.75
        _OcclusionDirect("Cavity On Lit Surfaces",Range(0,1))=0.45
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 1
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.4
        _SpecularStrength("Specular Strength", Range(0,2)) = 0.14
        _SpecularAA("Specular Normal Filtering", Range(0,2)) = 1
        _SpecularColor("Specular Tint", Color) = (1,0.94,0.82,1)
        _RimColor("Rim Tint", Color) = (0.32,0.35,0.48,1)
        _RimStrength("Rim Strength", Range(0,1)) = 0.025
        _RimPower("Rim Power", Range(1,10)) = 5
        _Heat("Heat", Range(0,1)) = 0
        _HeatMask("Heat Mask R", 2D) = "white" {}
        [HDR] _HeatColor("Heat Tint", Color) = (1,0.10,0.025,1)
        _HeatTintStrength("Heat Surface Tint", Range(0,1)) = 0.42
        _EmissionMap("Emission Mask RGB", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Tint", Color) = (0.02,1,0.68,1)
        _EmissionStrength("Emission Strength", Range(0,20)) = 0
        _TechMask("Ancient Tech Regions RGB", 2D) = "black" {}
        _TechStrength("Ancient Tech Intensity", Range(0,2)) = 0
        _TechWeapon("Weapon Channel", Float) = 0
        [HideInInspector] _StyleClock("Local Active Clock", Float) = 0
        [HideInInspector] _TechPulse("Attack Anticipation", Float) = 0
        [HideInInspector] _StyleLive("Live Creature Emission", Float) = 1
        [Toggle] _ReceiveShadows("Receive Main Shadow", Float) = 1
        [IntRange] _DebugView("Review Diagnostic 0-8 (see integration guide)",Range(0,8))=0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_HeatMask); SAMPLER(sampler_HeatMask);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_TechMask); SAMPLER(sampler_TechMask);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _DeepShadowColor;
            half4 _SpecularColor, _RimColor, _HeatColor, _EmissionColor;
            half _ShadeThreshold, _ShadeSoftness, _DeepShadeThreshold, _AmbientStrength;
            half _FormStrength, _LightColorPreservation, _ShadowLightSaturation, _AdditionalLightStrength;
            half _GrazingShade, _OcclusionStrength, _OcclusionDirect;
            half _BumpScale, _Metallic, _Smoothness, _SpecularStrength, _SpecularAA;
            half _RimStrength, _RimPower, _Heat, _HeatTintStrength, _EmissionStrength;
            half _Cull, _ReceiveShadows, _DebugView;
            float _StyleClock;
            half _TechStrength, _TechWeapon, _TechPulse, _StyleLive;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 uv2 : TEXCOORD1;
            float3 stylePosition : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half fog : TEXCOORD4;
            float2 uv2 : TEXCOORD5;
            float3 stylePosition : TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings SamVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = p.positionCS;
            output.positionWS = p.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            float3 tangent = TransformObjectToWorldDir(input.tangentOS.xyz);
            // Imported rigid armor occasionally has no tangents. Keep a finite orthogonal basis.
            if (dot(tangent,tangent) < 0.0001)
                tangent = cross(abs(output.normalWS.y) < 0.99 ? float3(0,1,0) : float3(1,0,0), output.normalWS);
            output.tangentWS = half4(SafeNormalize(tangent), (input.tangentOS.w == 0 ? 1 : input.tangentOS.w) * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.uv2 = input.uv2;
            output.stylePosition = input.stylePosition;
            output.fog = ComputeFogFactor(p.positionCS.z);
            return output;
        }
        half3 SamNormal(Varyings input)
        {
            half3 n = NormalizeNormalPerPixel(input.normalWS);
            half3 t = SafeNormalize(input.tangentWS.xyz - n * dot(n, input.tangentWS.xyz));
            half3 b = cross(n, t) * input.tangentWS.w;
            half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,input.uv), _BumpScale);
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(t,b,n)));
        }
        struct SamSpecularFilter
        {
            float3 normalDx,normalDy,viewDx,viewDy;
            half exponent;
        };
        SamSpecularFilter SamPrepareSpecular(half3 n,half3 v)
        {
            SamSpecularFilter filter;
            // Evaluate quad derivatives exactly once, before any debug return or varying light loop.
            filter.normalDx=ddx(n);filter.normalDy=ddy(n);
            filter.viewDx=ddx(v);filter.viewDy=ddy(v);
            float variance=max(dot(filter.normalDx,filter.normalDx),dot(filter.normalDy,filter.normalDy))*_SpecularAA;
            half smoothness=saturate(_Smoothness-min(0.45,sqrt(variance)*0.7));
            filter.exponent=lerp(exp2(3+7*smoothness),exp2(2+4*smoothness),_Metallic);
            return filter;
        }
        half3 SamSpecular(half3 n,half3 v,half3 baseColor,Light light,SamSpecularFilter filter)
        {
            float3 sum=light.direction+v;
            half3 h=SafeNormalize(sum);
            half ndh=saturate(dot(n,h));
            half highlight=pow(ndh,filter.exponent);
            // Approximate the lobe footprint from precomputed normal/view derivatives.
            // No derivative instruction executes in a divergent additional-light iteration.
            float inverseLength=rsqrt(max(dot(sum,sum),.0625));
            float3 hDx=(filter.viewDx-h*dot(h,filter.viewDx))*inverseLength;
            float3 hDy=(filter.viewDy-h*dot(h,filter.viewDy))*inverseLength;
            float angularWidth=abs(dot(filter.normalDx,h)+dot(n,hDx))+abs(dot(filter.normalDy,h)+dot(n,hDy));
            half footprint=filter.exponent*pow(max(ndh,.001h),max(filter.exponent-1,0))*angularWidth;
            half edgeWidth=max(0.10h,min(1.0h,footprint));
            highlight = smoothstep(0.25h-edgeWidth,0.65h+edgeWidth,highlight);
            half facingLight=smoothstep(0.0h,0.18h,dot(n,light.direction));
            half3 f0 = lerp(_SpecularColor.rgb * 0.04h, baseColor, _Metallic);
            return f0 * highlight * facingLight * _SpecularStrength * lerp(1.0h,1.5h,_Metallic) * light.color * light.distanceAttenuation * light.shadowAttenuation;
        }
        half SamWhiteIdentity(half3 albedo)
        {
            half lowest=min(albedo.r,min(albedo.g,albedo.b));
            half highest=max(albedo.r,max(albedo.g,albedo.b));
            return smoothstep(0.18h,0.60h,lowest)*smoothstep(0.45h,0.85h,lowest/max(highest,0.001h));
        }
        half3 SamStyledLight(half3 lightColor,half whiteIdentity,half facing)
        {
            // Only bright, near-neutral armor keeps a pale highlight under strongly colored light.
            // Gold, dark joints and colored inlays retain their own light/color response.
            half peak=max(lightColor.r,max(lightColor.g,lightColor.b));
            half preserve=whiteIdentity*_LightColorPreservation*(0.35h+0.65h*saturate(facing));
            return lerp(lightColor,peak.xxx,preserve);
        }
        // Immutable bind-pose coordinates travel with each skinned vertex.
        // Never use world position or the global time: menu suspension must freeze the marks.
        half TechLine(float distanceToLine, float width)
        { return 1-smoothstep(width,width+max(fwidth(distanceToLine),0.0015),distanceToLine); }
        half AncientTech(Varyings input)
        {
            half3 mask=SAMPLE_TEXTURE2D(_TechMask,sampler_TechMask,input.uv).rgb;
            float3 q=input.stylePosition;
            // Native Fishman renderer is rotated -90 degrees around X: Z is height.
            float3 bodyQ=q.xzy;
            float side=abs(bodyQ.x-0.5);
            half shoulder=TechLine(abs(bodyQ.y-(0.64+0.16*side)),0.009)
                *smoothstep(0.13,0.18,side)*(1-smoothstep(0.32,0.38,side));
            half cheek=TechLine(abs(bodyQ.y-(0.82-0.24*side)),0.007)
                *smoothstep(0.025,0.07,side)*(1-smoothstep(0.16,0.20,side));
            half fin=TechLine(abs(bodyQ.y-0.60),0.007)*smoothstep(0.08,0.19,side);
            half bodyMark=saturate((shoulder+cheek)*mask.r+fin*mask.g*0.65);
            half shaft=TechLine(abs(q.y-(0.982-0.970*q.x)),0.008)
                *(1-smoothstep(0.035,0.065,abs(q.z-(1.015*q.x-0.017))))
                *smoothstep(0.25,0.30,q.x)*(1-smoothstep(0.90,0.95,q.x))*mask.b;
            half travelling=0.65+0.35*pow(saturate(0.5+0.5*sin(q.x*20-_StyleClock*3)),3);
            half trace=lerp(bodyMark,shaft*travelling,saturate(_TechWeapon));
            return trace*_TechStrength*(0.55+0.65*saturate(_TechPulse))*saturate(_StyleLive);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SamVertex
            #pragma fragment SamFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 SamFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = SamNormal(input);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 n = inputData.normalWS;
                half3 v = inputData.viewDirectionWS;
                SamSpecularFilter specularFilter=SamPrepareSpecular(n,v);
                if(_DebugView>6.5h&&_DebugView<7.5h)return half4(0.32h,0.32h,0.32h,1);
                half3 baseColor = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb * _BaseColor.rgb;
                if(_DebugView>0.5h&&_DebugView<1.5h)return half4(baseColor,1);
                if(_DebugView>1.5h&&_DebugView<2.5h)return half4(NormalizeNormalPerPixel(input.normalWS)*0.5h+0.5h,1);
                if(_DebugView>2.5h&&_DebugView<3.5h)return half4(n*0.5h+0.5h,1);
                half3 emissionMask=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,input.uv).rgb;
                half emissiveRegion=saturate(max(emissionMask.r,max(emissionMask.g,emissionMask.b))*_EmissionStrength);
                half heat = saturate(_Heat) * SAMPLE_TEXTURE2D(_HeatMask,sampler_HeatMask,input.uv).r*(1-emissiveRegion);
                half whiteIdentity=SamWhiteIdentity(baseColor);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                if(_DebugView>7.5h)
                {
                    // Independent geometry/vertex-normal check: no material, bump, shadow or specular.
                    half lambert=saturate(dot(NormalizeNormalPerPixel(input.normalWS),mainLight.direction));
                    return half4(half3(0.075h,0.075h,0.075h)+mainLight.color*lambert*0.55h,1);
                }
                half ndl = dot(n,mainLight.direction);
                if(_DebugView>3.5h&&_DebugView<4.5h)return half4(ndl*0.5h+0.5h,ndl*0.5h+0.5h,ndl*0.5h+0.5h,1);
                if(_DebugView>4.5h&&_DebugView<5.5h)return half4(mainLight.shadowAttenuation.xxx,1);
                half width = max(_ShadeSoftness, fwidth(ndl));
                half litBand = smoothstep(_ShadeThreshold-width,_ShadeThreshold+width,ndl);
                half deepBand = smoothstep(_DeepShadeThreshold-width,_DeepShadeThreshold+width,ndl);
                half3 shaded = lerp(_DeepShadowColor.rgb,_ShadowColor.rgb,deepBand);
                mainLight.shadowAttenuation=lerp(1.0h,mainLight.shadowAttenuation,saturate(_ReceiveShadows));
                half visibility = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 tone = lerp(shaded,half3(1,1,1),litBand * visibility);
                half form=lerp(1.0h,0.55h+0.45h*pow(saturate(ndl*0.5h+0.5h),1.3h),_FormStrength);
                tone*=form;
                half heatAmount=heat*_HeatTintStrength*(1-litBand*0.72h)*(1-whiteIdentity*0.35h);
                half3 heated=baseColor*lerp(half3(1,1,1),_HeatColor.rgb,0.65h)+_HeatColor.rgb*0.10h;
                baseColor=lerp(baseColor,heated,heatAmount);
                half3 mainColor=SamStyledLight(mainLight.color,whiteIdentity,saturate(ndl));
                half keyPeak=max(mainLight.color.r,max(mainLight.color.g,mainLight.color.b));
                half3 shadowIllumination=lerp(keyPeak.xxx,mainLight.color,_ShadowLightSaturation);
                // Colored key illumination affects the receiving side; the entire shadow ramp
                // must not be dyed by a fire-colored main light.
                mainColor=lerp(shadowIllumination,mainColor,litBand*visibility);
                half3 color = baseColor * tone * mainColor;
                half cavity=lerp(1.0h,SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,input.uv2).r,_OcclusionStrength);
                color*=lerp(1.0h,cavity,lerp(1.0h,_OcclusionDirect,litBand*visibility));
                half grazing=pow(1-saturate(dot(n,v)),5)*_GrazingShade;
                color*=1-grazing;
                color += baseColor * max(SampleSH(n),half3(0,0,0)) * _AmbientStrength*cavity;
                if(_DebugView<5.5h)color += SamSpecular(n,v,baseColor,mainLight,specularFilter);
                // Forward+ needs InputData in scope and its clustered loop, not a plain light-count loop.
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                {
                    Light l=GetAdditionalLight(lightIndex,inputData.positionWS,half4(1,1,1,1));
                    half facing=saturate(dot(n,l.direction));
                    half diffuse=facing*smoothstep(0.0h,0.12h,facing);
                    color += baseColor * SamStyledLight(l.color,whiteIdentity,facing) * diffuse * l.distanceAttenuation * l.shadowAttenuation * _AdditionalLightStrength*lerp(1.0h,cavity,_OcclusionDirect);
                    if(_DebugView<5.5h)color += SamSpecular(n,v,baseColor,l,specularFilter);
                }
                #endif
                uint pixelLightCount=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l=GetAdditionalLight(lightIndex,inputData.positionWS,half4(1,1,1,1));
                    half facing=saturate(dot(n,l.direction));
                    half diffuse=facing*smoothstep(0.0h,0.12h,facing);
                    color += baseColor * SamStyledLight(l.color,whiteIdentity,facing) * diffuse * l.distanceAttenuation * l.shadowAttenuation * _AdditionalLightStrength*lerp(1.0h,cavity,_OcclusionDirect);
                    if(_DebugView<5.5h)color += SamSpecular(n,v,baseColor,l,specularFilter);
                LIGHT_LOOP_END
                #endif
                if(_DebugView>5.5h)return half4(MixFog(color,input.fog),1);
                half rim=pow(1-saturate(dot(n,v)),_RimPower) * _RimStrength;
                color += _RimColor.rgb * rim;
                color += (emissionMask * _EmissionStrength * saturate(_StyleLive) + AncientTech(input)) * _EmissionColor.rgb;
                color = MixFog(color,input.fog);
                return half4(color,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            float4 ShadowVertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p=TransformObjectToWorld(input.positionOS.xyz);
                float3 n=TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-p);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                float4 positionCS=TransformWorldToHClip(ApplyShadowBias(p,n,lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z=min(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #else
                    positionCS.z=max(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #endif
                return positionCS;
            }
            half4 DepthFragment() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SamVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SamVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n=SamNormal(input);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct=PackNormalOctQuadEncode(n);
                    half3 packed=PackFloat2To888(saturate(oct*0.5+0.5));
                    return half4(packed,0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}

