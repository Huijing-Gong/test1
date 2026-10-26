Shader "KingOfGraphic/Progressive Eclipse"
{
    Properties {
        _Stage("Progress", Range(0,2)) = 0 _Direction("Direction", Vector) = (0,.5,1,0) _Clock("Unscaled time", Float)=0
        _MainTex("Original panorama",2D)="white" {} _HasPanorama("Use original sky",Float)=0
        _Exposure("Original exposure",Float)=1 _Rotation("Original rotation",Float)=0
        _Tint("Original tint",Color)=(.5,.5,.5,1)
        _StormTex("Blood moon cloud panorama",2D)="black" {} _HasStorm("Storm texture available",Float)=0
        _TwilightTex("First seal twilight panorama",2D)="gray" {}
        _OcclusionStage("Lunar shadow progress",Float)=-1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p:POSITION; };
            struct V { float4 p:SV_POSITION; float3 d:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float _Stage, _Clock, _HasPanorama, _Exposure, _Rotation, _HasStorm, _OcclusionStage; float4 _Direction, _Tint;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_StormTex); SAMPLER(sampler_StormTex);
            TEXTURE2D(_TwilightTex); SAMPLER(sampler_TwilightTex);
            float3 Panorama(TEXTURE2D_PARAM(tex,samp),float2 uv)
            {
                uv=float2(frac(uv.x),clamp(uv.y,.001,.999));
                float edge=min(uv.x,1-uv.x);
                float blend=.5*(1-smoothstep(0,.08,edge));
                float2 a=float2(clamp(uv.x,.0005,.9995),uv.y);
                float2 b=float2(1-a.x,uv.y);
                // Longitude wraps abruptly at atan2's branch cut. Explicit LOD avoids
                // its huge screen derivative selecting a blurry mip in a vertical stripe.
                // Blend both authored edges equally at the join, for every sky stage.
                return lerp(SAMPLE_TEXTURE2D_LOD(tex,samp,a,0).rgb,
                    SAMPLE_TEXTURE2D_LOD(tex,samp,b,0).rgb,blend);
            }
            V vert(A a) { V o; o.p=TransformObjectToHClip(a.p.xyz); o.d=a.p.xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 i=floor(p),f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
            float hash3(float3 p) { p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            float noise3(float3 p)
            {
                float3 a=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash3(a),hash3(a+float3(1,0,0)),f.x),
                    lerp(hash3(a+float3(0,1,0)),hash3(a+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(hash3(a+float3(0,0,1)),hash3(a+float3(1,0,1)),f.x),
                    lerp(hash3(a+float3(0,1,1)),hash3(a+1),f.x),f.y),f.z);
            }
            float terrain(float3 p)
            {
                return noise3(p)*.55+noise3(p*2.03+7)*.27+noise3(p*4.11+19)*.13+noise3(p*8.23)*.05;
            }
            half4 frag(V i):SV_Target
            {
                float3 d=normalize(i.d);
                float first=saturate(_Stage), total=saturate(_Stage-1);
                float3 top=lerp(float3(.22,.56,.79),float3(.18,.04,.055),first);
                top=lerp(top,float3(.09,.025,.045),total);
                float3 horizon=lerp(float3(.74,.86,.90),float3(.23,.075,.095),first);
                horizon=lerp(horizon,float3(.14,.055,.08),total);
                float3 sky=lerp(horizon,top,saturate(d.y*.95+.12));
                // A continuous layered cloud ceiling, including in front of the moon.
                float3 cloudP=d*5+float3(_Clock*.006,0,_Clock*.003);
                float broad=terrain(cloudP);
                float n=terrain(cloudP*2.1+float3(broad*2,0,broad));
                n=n*.7+broad*.3+noise3(cloudP*28)*.035;
                float cloud=smoothstep(.37,.61,n)*smoothstep(-.14,.08,d.y);
                float3 cloudColor=lerp(float3(.98,.97,.93),float3(.055,.043,.065),first);
                cloudColor=lerp(cloudColor,float3(.035,.028,.05),total);
                if(_HasPanorama>.5)
                {
                    float longitude=atan2(d.z,d.x)-radians(_Rotation);
                    float2 uv=float2(.5-longitude/(2*PI),1-acos(clamp(d.y,-1,1))/PI);
                    float3 original=Panorama(TEXTURE2D_ARGS(_MainTex,sampler_MainTex),uv)*_Exposure*_Tint.rgb*2;
                    float3 grading=lerp(float3(1,1,1),float3(.8,.55,.6),first);
                    grading=lerp(grading,float3(.42,.48,.62),total);
                    // Retire the sunlit panorama as the eclipse advances, not just its exposure.
                    sky=lerp(original*grading,sky,smoothstep(0,.95,_Stage));
                }
                // Sparse, steady stars behind the clouds; never screen-space particles.
                float2 starUV=float2(atan2(d.z,d.x)/(2*PI)+.5,acos(clamp(d.y,-1,1))/PI)*float2(560,280);
                float starSeed=hash(floor(starUV));
                float star=pow(saturate(1-length(frac(starUV)-.5)*2),8)*step(.996,starSeed);
                sky+=star*float3(.42,.52,.72)*total*(1-cloud)*smoothstep(.12,.4,d.y);
                float3 axis=normalize(_Direction.xyz), right=normalize(cross(float3(0,1,0),axis)), up=cross(axis,right);
                float3 paintedCloud=0;
                float cloudCover=0;
                if(_HasStorm>.5)
                {
                    float2 stormUV=float2(frac(.5-atan2(d.z,d.x)/(2*PI)+_Clock*.00035),.5+asin(clamp(d.y,-1,1))/PI);
                    paintedCloud=Panorama(TEXTURE2D_ARGS(_StormTex,sampler_StormTex),stormUV);
                    float3 twilight=Panorama(TEXTURE2D_ARGS(_TwilightTex,sampler_TwilightTex),stormUV);
                    cloudCover=lerp(.08,1-smoothstep(.025,.13,paintedCloud.r),smoothstep(0,1,total));
                    paintedCloud=pow(max(paintedCloud,0),.9)*lerp(1.0,.85,total);
                    paintedCloud=lerp(twilight,paintedCloud,smoothstep(0,1,total));
                    sky=lerp(sky,paintedCloud,smoothstep(0,.95,first));
                }
                float2 q=float2(dot(d,right),dot(d,up));
                float radius=.064, r=length(q), aa=max(fwidth(r),.0001);
                float visible=smoothstep(0,.35,first)*step(0,dot(d,axis));
                // Work in the celestial sphere's local frame, not on two overlapping discs.
                // Restrict expensive detail to its small angular footprint.
                if(r<radius*3 && visible>.001)
                {
                    float2 uv=q/radius;
                    float rho=length(uv), disk=1-smoothstep(radius-aa,radius+aa,r);
                    float3 normal=normalize(float3(uv,sqrt(saturate(1-dot(uv,uv)))));
                    float3 surface=normal*5.5;
                    float land=terrain(surface+float3(2,8,1));
                    float detail=terrain(surface*3.4);
                    float basins=smoothstep(.22,.72,land);
                    float ridges=1-abs(detail*2-1);
                    float lighting=.16+.84*saturate(dot(normal,normalize(float3(-.45,.38,.85))));
                    float limb=pow(saturate(normal.z),.45);
                    float3 rock=lerp(float3(.42,.12,.07),float3(.75,.23,.13),basins);
                    rock*=lerp(.85,1.05,detail)*lighting*(.6+.4*limb);
                    // Thin irregular hot veins follow the surface, not a flat projected texture.
                    float veins=pow(saturate(ridges),28)*smoothstep(.43,.65,land);
                    rock+=float3(.12,.025,.008)*veins*limb*.04;
                    // The Earth's round umbra enters diagonally across a full lunar disc.
                    // A finite radius produces an arc, while the surrounding penumbra
                    // softens the bite without turning it into a straight phase terminator.
                    float shadowStage=_OcclusionStage<0?_Stage:_OcclusionStage;
                    float2 shadowCenter=lerp(float2(3.5,.55),float2(1.48,.32),saturate(shadowStage));
                    shadowCenter=lerp(shadowCenter,float2(.08,.02),saturate(shadowStage-1));
                    float shadowDistance=length(uv-shadowCenter);
                    float umbra=1-smoothstep(1.50,1.66,shadowDistance);
                    float penumbra=1-smoothstep(1.64,1.94,shadowDistance);
                    float shadow=saturate(umbra*.86+penumbra*.14);
                    float moonLight=.72+.28*saturate(normal.z);
                    float3 pale=lerp(float3(.54,.48,.39),float3(.88,.81,.66),basins)*moonLight;
                    float3 litMoon=lerp(pale,rock,total);
                    float3 shadedMoon=lerp(rock*.15,float3(.003,.004,.009)*(.7+.3*detail),total);
                    float3 moon=lerp(litMoon,shadedMoon,shadow);

                    float2 radial=uv/max(rho,.001);
                    float outward=max(0,rho-1);
                    // Domain-warped circular noise is seamless at the angular seam.
                    float flow=noise(radial*4.7+float2(_Clock*.035,-outward*3.4-_Clock*.12));
                    float wisps=noise(radial*11+flow*2-outward*8+_Clock*.045);
                    float reach=.06+.22*pow(flow,2)+.13*pow(wisps,4);
                    float flame=exp(-outward/max(.025,reach))*(.25+.75*wisps);
                    float thread=pow(saturate(wisps),5)*exp(-outward/.32);
                    float rim=exp(-abs(rho-1)/.019)*(.55+.45*flow);
                    float haze=exp(-outward*4.3)*(.4+.6*flow);
                    float3 redGlow=float3(.62,.025,.008);
                    float3 hotGlow=lerp(float3(1,.20,.035),float3(1,.43,.12),total);
                    // Restrained atmospheric halo; no decorative fire or radial pattern.
                    float3 atmosphere=redGlow*exp(-outward*3)*.09+hotGlow*rim*.14*total;
                    atmosphere*=1-smoothstep(2.3,3,rho);
                    sky+=atmosphere*(1-disk)*visible;
                    sky=lerp(sky,moon,disk*visible);
                    // Wispy foreground haze gives the moon depth in the sky.
                    float veil=noise(uv*float2(2,6)+float2(_Clock*.022,.4));
                    veil=smoothstep(.54,.8,veil)*.24*exp(-rho*rho*.3);
                    sky=lerp(sky,float3(.075,.013,.025),veil*visible);
                }
                float moonProximity=pow(saturate(dot(d,axis)),20);
                float silverLining=exp(-abs(n-.435)*30)*(.22+.78*moonProximity);
                float3 storm=cloudColor+float3(.48,.065,.028)*silverLining*first;
                if(_HasStorm>.5)
                    sky=lerp(sky,paintedCloud,cloudCover*.82*first);
                else sky=lerp(sky,storm,cloud*.96*first);
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
