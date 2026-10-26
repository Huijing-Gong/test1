Shader "Enemies/GelBlob"
{
    Properties
    {
        _BaseColor ("Turquoise gel", Color) = (.01,.7,.7,.53)
        _Charge ("Compression energy", Range(0,1)) = 0
        _Hit ("Soft hit flash", Range(0,1)) = 0
        _Fade ("Living gel", Range(0,1)) = 1
        _GelClock ("Paused-safe clock", Float) = 0
        _Wobble ("Bounded elastic shear", Vector) = (0,0,0,0)
        _Impact ("Landing pressure", Range(0,1)) = 0
        _HitPoint ("Local sword contact", Vector) = (0,.4,.45,0)
        _HitAge ("Contact wave age", Float) = 10
        _FlowClock ("Paused-safe internal flow", Float) = 0
        _Detail ("Nearby surface details", Range(0,1)) = 1
        [HideInInspector] _Smear ("Contact smear pass", Float) = 0
        [HideInInspector] _SmearFade ("Short wet trace", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Charge, _Hit, _Fade, _Impact;
            half _Detail, _Smear, _SmearFade;
            float _GelClock, _HitAge, _FlowClock;
            float4 _Wobble, _HitPoint;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 colour:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half fog:TEXCOORD2; float3 local:TEXCOORD3; half4 colour:COLOR; float2 uv:TEXCOORD4; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = input.positionOS.xyz;
                // Small bounded ripple only; squash and collider shape share the CPU phase.
                float anchor = saturate(p.y * 3);
                float angle = p.y * 11 + _GelClock * 4;
                float scale = 1 + .017 * sin(angle) * anchor;
                float derivative = .017 * (11 * cos(angle) * anchor + sin(angle) * (p.y > 0 && p.y < .333333 ? 3 : 0));
                float3 corrected = input.normalOS;
                if (_Smear < .5)
                {
                    // Inverse-transpose of this exact radial/shear deformation.
                    // Highlights must bend with the gel, not retain an idle normal.
                    corrected.xz /= scale;
                    corrected.y -= dot(corrected.xz, p.xz * derivative + _Wobble.xz * (p.y > 0 && p.y < .333333 ? 3 : 0));
                    p.xz = p.xz * scale + _Wobble.xz * anchor;
                }
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(corrected);
                o.fog = ComputeFogFactor(o.positionCS.z); o.local = input.positionOS.xyz;
                o.colour = input.colour; o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                if (_Smear > .5)
                {
                    clip(i.colour.a * _SmearFade - .008);
                    #if defined(DEBUG_DISPLAY)
                        if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                    #endif
                    // Small filled irregular contact trace, NEVER an expanding wave.
                    half ripple = .5 + .5 * sin(length(i.uv) * 15 - _GelClock * 4);
                    half3 wet = lerp(half3(.012,.13,.14),half3(.05,.42,.39),i.colour.a * .65);
                    wet += half3(.05,.13,.12) * ripple * i.colour.a;
                    return half4(MixFog(wet,i.fog),i.colour.a * _SmearFade * .65);
                }
                clip(_Fade - .005);
                // Coherent object-space lobes open rounded gaps, no noise texture.
                half dissolve = 1;
                if (_Fade < .999)
                {
                    half lobes = .5 + .2 * sin(i.local.x * 13 + sin(i.local.z * 7)) +
                        .17 * sin(i.local.y * 15 + i.local.z * 9) + .08 * sin(i.local.z * 19 - i.local.x * 4);
                    half threshold = lerp(1.04,-.12,_Fade);
                    dissolve = smoothstep(threshold,threshold + .09,lobes);
                    clip(dissolve - .01);
                }
                #if defined(DEBUG_DISPLAY)
                    if (_DebugSceneOverrideMode == DEBUGSCENEOVERRIDEMODE_OVERDRAW) return _DebugColor;
                #endif
                half3 n = normalize(i.normalWS), view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half contact = 0, contactBoundary = 0;
                if (_Detail > .5 && _HitAge < .34)
                {
                    float3 delta = i.local - _HitPoint.xyz;
                    float distanceOS = length(delta);
                    half wave = 1 - smoothstep(.05,.11,abs(distanceOS - (.02 + _HitAge * 1.6)));
                    contactBoundary = (1 - smoothstep(.105,.15,abs(distanceOS - (.02 + _HitAge * 1.6)))) * _Hit;
                    half patch = 1 - smoothstep(.07,.25,distanceOS);
                    contact = saturate((patch + wave * .85) * _Hit);
                    float3 gradient = TransformObjectToWorldDir(delta / max(.001,distanceOS));
                    n = normalize(n + (gradient - n * dot(n,gradient)) * wave * _Hit * .14);
                }
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half facing = saturate(dot(n, view));
                half rim = pow(1 - facing, 2.4);
                half diffuse = saturate(dot(n, light.direction)) * light.shadowAttenuation;
                half transmission = saturate(dot(-n,light.direction)) * light.shadowAttenuation * (1 - rim * .65);
                half specDot = saturate(dot(n, normalize(view + light.direction)));
                half shine = pow(specDot, 62), gloss = pow(specDot, 18);
                // Thick jelly colour at the lower edge; no opaque-texture capture or refraction pass.
                half bottom = 1 - smoothstep(.04,.32,i.local.y);
                half3 reflection = GlossyEnvironmentReflection(reflect(-view,n),i.positionWS,.18h,1.0h);
                half3 ambient = clamp(SampleSH(n),.08h,.65h);
                half3 colour = _BaseColor.rgb * (.36 + ambient * .3 + diffuse * light.color * .50);
                // Restrained reflected rim, not an emissive forcefield outline.
                colour += reflection * (.15 + rim * .22);
                colour += light.color * light.shadowAttenuation * (half3(.82,1,.96) * shine * .92 + half3(.12,.34,.30) * gloss * .35);
                colour += half3(.04,.28,.22) * transmission;
                colour = lerp(colour,half3(.015,.28,.28),bottom * .35);
                colour += half3(.1,.55,.6) * _Impact * bottom;
                if (_Detail > .5 && _Charge > .01)
                {
                    // Narrow packets converge toward the reactor height. Idle and
                    // recovery have no energy stripes; no extra transparent layer.
                    half lane = pow(saturate(.5 + .5 * sin(i.local.x * 12 + i.local.z * 9)),16);
                    half packet = pow(saturate(.5 + .5 * sin(abs(i.local.y - .34) * 23 + length(i.local.xz) * 5 + _FlowClock * 4)),12);
                    colour += half3(.20,.85,.66) * lane * packet * _Charge * .9;
                    // A narrow moving compression caustic stays inside the original hull.
                    half caustic = pow(saturate(.5 + .5 * sin(i.local.y * 27 + length(i.local.xz) * 8 - _FlowClock * 5)),18);
                    colour += half3(.18,.52,.37) * caustic * _Charge * (1-rim) * .32;
                }
                colour = lerp(colour,half3(.12,.85,.93),_Charge * .22);
                colour *= 1 - contactBoundary * .55;
                colour = lerp(colour,half3(.95,1,.80),contact * .96);
                // Clear central window, thicker curved silhouette/lower skirt.
                half alpha = saturate(_BaseColor.a * .78 + pow(facing,.65) * .055 + rim * .22 +
                    shine * .18 + bottom * .18 + _Charge * .025 + contact * .42) * _Fade * dissolve;
                return half4(MixFog(colour,i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
