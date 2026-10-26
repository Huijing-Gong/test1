Shader "FoldedRealm/EclipsePlatform"
{
    Properties
    {
        _BaseColor("Stone / metal tint", Color) = (1,1,1,1)
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        _Metallic("Metal", Range(0,1)) = 0
        _Pulse("Emission breathing", Range(0,1)) = 0
        _Grain("Stone grain", Range(0,1)) = .15
        _SurfaceMap("Shared surface atlas", 2D) = "white" {}
        [Normal] _NormalMap("Shared shallow relief atlas", 2D) = "bump" {}
        _AtlasRect("Atlas quadrant (UV offset / size)", Vector) = (0,.5,.5,.5)
        _TextureWeight("Surface texture", Range(0,1)) = 0
        _TileScale("Repeats per metre", Float) = .5
        _NormalStrength("Shallow relief", Range(0,1)) = .22
        _Smoothness("Smoothness", Range(0,1)) = .4
        _RelicGlow("Selected relic illumination", Range(0,1)) = 1
        [HideInInspector] _EclipseEnabled("Eclipse atmosphere enabled", Float) = 0
        [HideInInspector] _EclipseMotion("Eclipse motion enabled", Float) = 0
        [HideInInspector] _EclipseClock("Eclipse scaled clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor, _EmissionColor;
        float _Metallic, _Pulse, _Grain;
        float4 _AtlasRect;
        float _TextureWeight, _TileScale, _NormalStrength, _Smoothness;
        float _EclipseEnabled, _EclipseMotion, _EclipseClock, _RelicGlow;
        CBUFFER_END
        struct A {float4 p:POSITION; float3 n:NORMAL; float4 c:COLOR; float4 motion:TEXCOORD1; float4 relic:TEXCOORD2; float4 chip:TEXCOORD3;};
        float ChipPhase(A input) { return input.motion.z+UNITY_MATRIX_M._m03*.237; }
        float ChipAngle(A input)
        {
            float phase=ChipPhase(input),agility=saturate(input.chip.w);
            return (.022+.024*agility)*sin(_EclipseClock*(.39+.13*agility)+phase)*
                saturate(input.motion.y)*saturate(_EclipseEnabled)*saturate(_EclipseMotion);
        }
        float3 ChipRotate(float3 p,A input)
        {
            float3 axis=normalize(float3(.32*sin(ChipPhase(input)),.46,1));
            float a=ChipAngle(input),s=sin(a),c=cos(a);
            return p*c+cross(axis,p)*s+axis*dot(axis,p)*(1-c);
        }
        float3 EclipseNormalWS(A input) { return ChipRotate(TransformObjectToWorldNormal(input.n),input); }
        float3 EclipsePositionWS(A input)
        {
            float3 world = TransformObjectToWorld(input.p.xyz);
            float enabled = saturate(_EclipseEnabled) * saturate(_EclipseMotion);
            float phase = input.motion.z;
            float clock = _EclipseClock;
            float cloth = saturate(input.motion.x) * enabled;
            float chip = saturate(input.motion.y) * enabled;
            // Fabric stays pinned at its top edge; gold piping uses the same
            // baked weight/phase so ornaments follow the underlying fabric.
            world.x += cloth * .065 * sin(clock * 1.1 + phase + world.y * .48);
            world.z += cloth * .045 * sin(clock * .91 + phase + world.y * .62);
            // Size-dependent inertia, instance-offset phases, two slow waves.
            // No mesh deformation: rotate the whole chip about its baked pivot.
            if(chip>0)
            {
                float3 pivot=TransformObjectToWorld(input.chip.xyz);
                world=pivot+ChipRotate(world-pivot,input);
            }
            float agility=saturate(input.chip.w),p=ChipPhase(input);
            float rate=.60+.32*agility+.08*sin(p);
            world.y += chip*(.09+.075*agility)*(.78*sin(clock*rate+p)+.22*sin(clock*rate*.47+p*1.7));
            world.x += chip*(.018+.015*agility)*sin(clock*.43+p*1.3);
            world.z += chip*.018*sin(clock*.37+p*.8);
            return world;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            struct V {float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; float4 c:COLOR; float fog:TEXCOORD2; float3 rest:TEXCOORD3; float kind:TEXCOORD4; float4 relic:TEXCOORD5; float chip:TEXCOORD6;};
            V Vert(A v) { V o; o.rest=TransformObjectToWorld(v.p.xyz); o.world=EclipsePositionWS(v); o.p=TransformWorldToHClip(o.world); o.n=EclipseNormalWS(v); o.c=v.c; o.fog=ComputeFogFactor(o.p.z); o.kind=v.motion.w; o.relic=v.relic; o.chip=v.motion.y; return o; }
            float hash(float3 p) {p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            float noise(float3 p) {float3 a=floor(p),f=frac(p); f=f*f*(3-2*f);return lerp(lerp(lerp(hash(a),hash(a+float3(1,0,0)),f.x),lerp(hash(a+float3(0,1,0)),hash(a+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(a+float3(0,0,1)),hash(a+float3(1,0,1)),f.x),lerp(hash(a+float3(0,1,1)),hash(a+float3(1,1,1)),f.x),f.y),f.z);}
            float2 AtlasUV(float2 metres, out float2 mirrorSign)
            {
                // Mirrored tiles avoid an abrupt border in AI-authored swatches.
                float2 cell=frac(metres*.5);
                mirrorSign=float2(cell.x<.5?1:-1,cell.y<.5?1:-1);
                float2 tile=1-abs(cell*2-1);
                // Four-pixel inset keeps adjacent atlas materials out of filtering.
                return _AtlasRect.xy+(_AtlasRect.zw*.008)+tile*(_AtlasRect.zw*.984);
            }
            void Surface(float3 world,float3 normal,out float3 colour,out float3 bumped)
            {
                float3 weights=pow(abs(normal),4); weights/=max(dot(weights,1),.0001);
                float3 p=world*max(_TileScale,.01);
                float2 sx,sy,sz;
                float2 ux=AtlasUV(p.zy,sx),uy=AtlasUV(p.xz,sy),uz=AtlasUV(p.xy,sz);
                colour=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,ux).rgb*weights.x+
                    SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,uy).rgb*weights.y+
                    SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,uz).rgb*weights.z;
                float3 nx=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,ux));
                float3 ny=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uy));
                float3 nz=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uz));
                nx.xy*=sx;ny.xy*=sy;nz.xy*=sz;
                // Perturb the original mesh normal, retaining flat stone facets.
                float3 gradient=float3(0,nx.y,nx.x)*weights.x*sign(normal.x)+
                    float3(ny.x,0,ny.y)*weights.y*sign(normal.y)+
                    float3(nz.x,nz.y,0)*weights.z*sign(normal.z);
                bumped=normalize(normal+gradient*_NormalStrength*_TextureWeight);
            }
            half4 Frag(V i):SV_Target
            {
                float3 n=normalize(i.n),v=GetWorldSpaceNormalizeViewDir(i.world);
                if(dot(n,v)<0) n=-n;
                float topFacing=saturate(n.y);
                float3 surfaceColour;
                Surface(i.rest,n,surfaceColour,n);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                float grain=1-_Grain*(.65*noise(i.rest*13)+.35*noise(i.rest*53));
                float3 albedo=_BaseColor.rgb*i.c.rgb*grain*lerp(1,surfaceColour,_TextureWeight);
                // Cool, restrained bounce lets the warm overhead key define
                // the volume. The old ambient floor/front wrap lit every face.
                float3 ambient=max(SampleSH(n)*.65,float3(.070,.080,.110));
                float diffuse=saturate(dot(n,light.direction));
                // Classify with the geometric normal, before shallow relief:
                // bumps should not turn a vertical facade into a lit roof.
                diffuse=min(diffuse,lerp(.18,1,smoothstep(.05,.85,topFacing)));
                float3 lit=albedo*(ambient+light.color*(.025+.975*diffuse)*light.shadowAttenuation);
                float wear=lerp(.65,1,dot(surfaceColour,float3(.299,.587,.114)));
                float spec=pow(saturate(dot(n,normalize(v+light.direction))),lerp(12,80,_Smoothness*wear))*_Metallic;
                lit+=light.color*lerp(float3(.06,.06,.06),albedo,.75)*spec*light.shadowAttenuation;
                float clock=lerp(_Time.y,_EclipseClock,saturate(_EclipseEnabled));
                float pulse=1-_Pulse*(.5+.5*sin(clock*1.8+i.world.x*.55+i.world.y*.7));
                // A quiet static lunar edge keeps the stone silhouette coherent
                // with the red moon; reduced motion retains this lighting.
                float rim=pow(1-saturate(dot(n,v)),3)*saturate(dot(n,normalize(float3(-.35,.65,-.7))));
                float3 moon=lerp(float3(.27,.045,.025),float3(.28,.16,.045),saturate(n.x*.5+.5));
                float nonEmissive=1-saturate(dot(_EmissionColor.rgb,float3(1,1,1)));
                lit+=moon*rim*.12*saturate(_EclipseEnabled)*nonEmissive;
                float3 emission=_EmissionColor.rgb*pulse;
                float relicEnabled=saturate(_EclipseEnabled)*saturate(_RelicGlow);
                if(relicEnabled>0 && i.kind>.5)
                {
                    float motion=saturate(_EclipseMotion);
                    float phase=i.relic.w+UNITY_MATRIX_M._m03*.131;
                    float gain=saturate(i.relic.z);
                    if(i.kind<3.5)
                    {
                        // The thin authored gold pipes are the glow mask; stone,
                        // banner cloth, trim and the solid crescent stay dark.
                        float angle=atan2(i.relic.y,i.relic.x);
                        float travel=clock*(6.2831853/9.0)+phase;
                        float head=pow(saturate(.5+.5*cos(angle-travel)),72);
                        float tail=pow(saturate(.5+.5*cos(angle-travel+.28)),24)*.24;
                        float sweep=(head+tail)*motion;
                        if(i.kind>2.5)
                        {
                            float path=frac(i.relic.y*.50+clock*.12+phase*.15915494);
                            sweep=(1-smoothstep(.035,.14,abs(path-.5)))*motion*.55;
                        }
                        float breath=1+.075*sin(clock*.80+phase)*motion;
                        float3 gold=float3(.92,.48,.105);
                        float3 extra=(gold*(.15+sweep*.86)+float3(.30,.22,.10)*pow(head,3)*motion*step(i.kind,2.5))*gain*breath;
                        float baseScale=i.kind<1.5?1:lerp(.25,.70,gain);
                        emission=lerp(emission,emission*baseScale+extra,relicEnabled);
                    }
                    else
                    {
                        // Small, anti-aliased fissures on only three floating
                        // stones. No whole-rock glow and no new realtime lights.
                        float2 q=i.relic.xy;
                        float zig=q.x-.08*sin(q.y*13+phase)-.035*sin(q.y*29-phase);
                        float aa=max(fwidth(zig),.006);
                        float crack=1-smoothstep(.014,.026+aa,abs(zig));
                        float branch=q.x+q.y*.65+.07;
                        crack=max(crack,(1-smoothstep(.009,.018+aa,abs(branch)))*smoothstep(-.05,.12,q.y)*(1-smoothstep(.22,.34,q.y)));
                        crack*=smoothstep(-.5,-.36,q.y)*(1-smoothstep(.34,.5,q.y));
                        float slow=.88+.12*sin(clock*.75+phase)*motion;
                        float front=smoothstep(.20,.70,abs(normalize(i.n).z));
                        emission+=float3(.95,.038,.012)*crack*gain*slow*front*relicEnabled;
                    }
                }
                // Restrained gravity-field edge on isolated chips only.
                lit+=float3(.23,.008,.006)*pow(1-saturate(dot(n,v)),2.4)*i.chip*saturate(_EclipseEnabled);
                lit+=emission;
                return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        // Match the animated silhouette in depth and shadow passes. These
        // visual meshes never drive the separate gameplay collision surfaces.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowV { float4 p:SV_POSITION; };
            ShadowV ShadowVert(A input)
            {
                ShadowV output;
                float3 world=EclipsePositionWS(input);
                float3 normal=EclipseNormalWS(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-world);
                #else
                    float3 direction=_LightDirection;
                #endif
                output.p=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(world,normal,direction)));
                return output;
            }
            half4 ShadowFrag(ShadowV input):SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthV { float4 p:SV_POSITION; };
            DepthV DepthVert(A input) { DepthV output; output.p=TransformWorldToHClip(EclipsePositionWS(input)); return output; }
            half4 DepthFrag(DepthV input):SV_Target { return 0; }
            ENDHLSL
        }
    }
}
