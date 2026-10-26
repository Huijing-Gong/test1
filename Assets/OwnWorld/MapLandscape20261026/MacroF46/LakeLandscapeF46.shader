// F46 owned derivative. Vertex color R carries authored shore distance and B fades geometric ripples at banks.
Shader "KingOfGraphic/Own/LakeLandscapeF46"
{
    Properties
    {
        _ShallowColor ("Shallow turquoise", Color) = (.10,.58,.52,1)
        _DeepColor ("Deep turquoise", Color) = (.018,.27,.36,1)
        _WaveStrength ("Wave strength", Range(0,1)) = .18
        _ShoreWidth ("Contact foam depth m", Range(.02,2)) = .85
        _DebugDepth("DEBUG actual depth / 2.2",Range(0,1))=0
        _UseAuthoredShore("Use baked shore distance R",Range(0,1))=0
        _FoamStrength("Broken shoreline foam strength",Range(0,1))=.72
        _BottomTransmission("Actual bed contribution",Range(0,1))=.40
        _AbsorptionScale ("Absorption scale", Range(.1,3)) = 1
        _ReflectionScale ("Reflection mix", Range(0,1)) = .7
        [HideInInspector] _PlanarReflectionTex ("Owned scene reflection",2D)="black" {}
        [HideInInspector] _PlanarAvailable ("Reflection ready",Float)=0
        [Normal] _NormalMap ("Original ripple normal", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "ClearLake"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            TEXTURE2D(_PlanarReflectionTex);SAMPLER(sampler_PlanarReflectionTex);float4x4 _PlanarVP;
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor;
            half4 _DeepColor;
            half _WaveStrength;half _PlanarAvailable;half _ShoreWidth;half _AbsorptionScale;half _ReflectionScale;half _UseAuthoredShore;half _FoamStrength;half _BottomTransmission;half _DebugDepth;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float eyeDepth:TEXCOORD1; half fog:TEXCOORD2; half shoreMask:TEXCOORD3; float shoreDistance:TEXCOORD4; };
float OwnHash(float2 p);
float3 OwnNoiseGradient155(float2 p){
float2 a=floor(p),f=frac(p);float2 u=f*f*(3-2*f),du=6*f*(1-f);
float h00=OwnHash(a),h10=OwnHash(a+float2(1,0));
float h01=OwnHash(a+float2(0,1)),h11=OwnHash(a+1);
float low=lerp(h00,h10,u.x),high=lerp(h01,h11,u.x);
return float3(lerp(low,high,u.y),
lerp(h10-h00,h11-h01,u.y)*du.x,(high-low)*du.y);}
float3 OwnRawField155(float2 p,float t){
float3 a=OwnNoiseGradient155(float2(dot(p,float2(.69,-.65)),dot(p,float2(.17,.18)))+float2(t*.17,-t*.03));
float3 b=OwnNoiseGradient155(float2(dot(p,float2(-.28,.88)),dot(p,float2(.27,.09)))+float2(-t*.13,t*.021));
float3 c=OwnNoiseGradient155(p*.47+float2(t*.035,t*.027));
float height=(_WaveStrength/.38)*(.11*(a.x-.5)+.08*(b.x-.5)+.04*(c.x-.5));
float2 gradient=(_WaveStrength/.38)*(.11*(a.y*float2(.69,-.65)+a.z*float2(.17,.18))
+.08*(b.y*float2(-.28,.88)+b.z*float2(.27,.09))+.04*c.yz*.47);return float3(height,gradient);}
float3 OwnField155(float2 p,float t){return OwnRawField155(p,t);}
float OwnHeight155(float2 p,float t){return OwnField155(p,t).x;}
float2 OwnSlope155(float2 p,float t){return OwnField155(p,t).yz;}
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.shoreDistance=v.color.r*12; o.shoreMask=v.color.b; o.positionWS.y+=OwnHeight155(o.positionWS.xz,_Time.y)*o.shoreMask;o.positionCS=TransformWorldToHClip(o.positionWS);
                o.eyeDepth=-TransformWorldToView(o.positionWS).z;
                o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }

            float OwnHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float OwnNoise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(OwnHash(a),OwnHash(a+float2(1,0)),f.x),lerp(OwnHash(a+float2(0,1)),OwnHash(a+1),f.x),f.y);}
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.positionWS.xz;
                float t=_Time.y;
                float windPatch=OwnNoise(p*.075+float2(t*.008,-t*.006)); float broadPatch=OwnNoise(float2(p.x*.052,p.y*.085)+float2(-t*.004,t*.005)); float a=dot(p,float2(.53,.31))+t*.29;
                float b=dot(p,float2(-.26,.75))-t*.37;
                float3 n1=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,p*.215+float2(t*.006,t*.004)));
                float3 n2=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,p*.41+float2(-t*.006,t*.004)));
                float2 ripple=float2(sin(dot(p,float2(8.31,5.42))+t*.78)+.45*sin(dot(p,float2(-6.83,11.17))+t*1.17),sin(dot(p,float2(-8.91,9.68))-t*.66)+.42*sin(dot(p,float2(12.26,4.93))-t*1.38));
                float phase=(OwnNoise(p*.57+float2(t*.016,-t*.011))-.5)*4.8+(OwnNoise(p*1.71)-.5)*1.3;
                ripple=float2(sin(dot(p,float2(8.31,5.42))+t*.78+phase)+.45*sin(dot(p,float2(-6.83,11.17))+t*1.17-phase*.71),sin(dot(p,float2(-8.91,9.68))-t*.66-phase*.83)+.42*sin(dot(p,float2(12.26,4.93))-t*1.38+phase*.47));
                float microFade=1-smoothstep(.07,.30,length(fwidth(p)));
                float2 slope=(ripple*.11*(.22+.78*microFade)+n1.xy*.23+n2.xy*.10*microFade)*_WaveStrength;
                slope*=i.shoreMask; slope*=lerp(.48,1.25,smoothstep(.18,.85,windPatch)); slope+=OwnSlope155(p,t)*i.shoreMask+OwnNoiseGradient155(p*.12+float2(t*.006,-t*.004)).yz*.025*i.shoreMask;float3 n=normalize(float3(-slope.x,1,-slope.y));
                float3 view=normalize(GetWorldSpaceViewDir(i.positionWS));
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float rawDepth=SampleSceneDepth(uv);
                float depth=max(0,LinearEyeDepth(rawDepth,_ZBufferParams)-i.eyeDepth);
                float reconstructDepth=rawDepth;
#if !UNITY_REVERSED_Z
reconstructDepth=lerp(UNITY_NEAR_CLIP_VALUE,1,reconstructDepth);
#endif
float3 bottomWS=ComputeWorldSpacePosition(uv,reconstructDepth,UNITY_MATRIX_I_VP);
float waterDepth=clamp(depth*abs(view.y)/max(.001,abs(TransformWorldToViewDir(view).z)),0,30);
if(_DebugDepth>.5){float debugValue=saturate(waterDepth/2.2);return half4(debugValue,debugValue,debugValue,1);}
float2 refractUV=uv+slope*.006*saturate(waterDepth*.5);
                half3 bottom=SampleSceneColor(refractUV);
                float physicalDeep=smoothstep(.35,3.7,waterDepth);
                float authoredDeep=smoothstep(.35,12.0,i.shoreDistance);
                float deepMix=lerp(physicalDeep,authoredDeep,_UseAuthoredShore);
                half3 tint=lerp(_ShallowColor.rgb,_DeepColor.rgb,deepMix);
                half transmission=_BottomTransmission*exp(-waterDepth*_AbsorptionScale)* (1-smoothstep(.8,2.6,waterDepth))*(1-smoothstep(3.0,7.0,i.shoreDistance));
                half3 water=lerp(tint,bottom*tint*1.65,transmission);
                water*=lerp(.975,1.035,broadPatch);
                float3 reflectionDir=reflect(-view,n);
                half3 reflection=GlossyEnvironmentReflection(reflectionDir,.11,1);
                half3 sky=lerp(half3(.42,.69,.80),half3(.16,.43,.71),saturate(reflectionDir.y));
                reflection=lerp(sky,reflection,.30);
                float4 planar=mul(_PlanarVP,float4(i.positionWS,1));float2 puv=planar.xy/max(.001,planar.w)*.5+.5;
                puv.y=1-puv.y;
                half3 sceneReflection=SAMPLE_TEXTURE2D(_PlanarReflectionTex,sampler_PlanarReflectionTex,puv+slope*.023).rgb;
                float2 inside=step(0,puv)*step(puv,1);
float edgeFade=saturate(min(min(puv.x,1-puv.x),min(puv.y,1-puv.y))*35);
float valid=inside.x*inside.y*step(.001,planar.w)*edgeFade;
reflection=lerp(reflection,sceneReflection,saturate(_PlanarAvailable)*valid);
                half fresnel=.14+.46*pow(1-saturate(dot(n,view)),5);
                water=lerp(water,reflection,fresnel*_ReflectionScale);
                Light sun=GetMainLight();
                float3 h=normalize(view+sun.direction);
                float shine=pow(saturate(dot(n,h)),64)*.19;
                water+=sun.color*shine;
                float contact=saturate(1-waterDepth/max(.02,_ShoreWidth));
// At low camera angles screen depth may hit distant bank faces. Only shoreline proximity creates lake contact foam.
contact*=lerp(1,1-smoothstep(.8,2.8,i.shoreDistance),_UseAuthoredShore);
contact=max(contact,_UseAuthoredShore*saturate(1-i.shoreDistance/1.9));
float grain=OwnNoise(p*3.1+float2(t*.13,-t*.07));
float crest=.5+.5*sin(waterDepth*12-t*1.8+grain*3);
float shorePhase=i.shoreDistance*5.4-t*.9+(grain-.5)*2.8;
float fringe=(1-smoothstep(1.4,2.8,i.shoreDistance))*smoothstep(.3,.7,OwnNoise(p*1.4+float2(t*.08,-t*.045)))*pow(saturate(.5+.5*sin(shorePhase)),6)*.33;
float foam=max(contact*contact*(.43+.47*crest)*smoothstep(.16,.63,grain),fringe);
float shallowSpark=pow(saturate(.5+.5*sin(a*5+phase)*sin(b*4-phase)),7)*exp(-waterDepth*.8)*.065*lerp(1,1-smoothstep(1.5,4,i.shoreDistance),_UseAuthoredShore);
// Short cell edges produce moving sunlight on actual shallow bed, not a full-lake white mesh.
float2 cp=p*1.67+float2(t*.045,-t*.03)+float2(OwnNoise(p*.4+float2(13,5)),OwnNoise(p*.4+float2(29,3)))*.42;float2 cell=floor(cp),f=frac(cp);float nearest=9,second=9;
for(int cy=-1;cy<=1;cy++)for(int cx=-1;cx<=1;cx++){float2 off=float2(cx,cy);float2 seed=float2(OwnHash(cell+off),OwnHash(cell+off+float2(41.7,19.4)));seed=.5+.34*sin(seed*6.283+float2(t*.18,-t*.13));float d=length(off+seed-f);if(d<nearest){second=nearest;nearest=d;}else second=min(second,d);}
float causticWidth=.035+fwidth(second-nearest)*1.2;float caustic=1-smoothstep(.018,causticWidth,second-nearest);
float shallowWindow=exp(-waterDepth*1.15)*(1-smoothstep(2,4,waterDepth))*(1-smoothstep(1.5,6.0,i.shoreDistance));
water+=half3(.14,.25,.20)*caustic*shallowWindow*.28+half3(.32,.69,.78)*shallowSpark;
float fleckA=OwnNoise(float2(p.x*2.3,p.y*6.5)+float2(t*.045,-t*.08));
float fleckB=OwnNoise(float2(p.x*3.4,p.y*8.3)+float2(-t*.06,t*.10)+float2(4.7,19.1));
// Soft wind-wave fragments reuse noise and fade before undersampling.
float contour=sin(dot(p,float2(.9,16.7))+t*.65+(fleckA-.5)*3.2);
float waveFade=1-smoothstep(.7,2.0,fwidth(dot(p,float2(.9,16.7))));
float brokenCrest=pow(saturate(.5+.5*contour),12)*pow(smoothstep(.52,.87,fleckB),3);
water+=half3(.32,.53,.64)*brokenCrest*microFade*waveFade*.92*lerp(.40,1,smoothstep(.35,.75,windPatch))*(.65+.35*smoothstep(.1,.65,1-saturate(dot(n,view))));
float loose=OwnNoise(float2(p.x*.63,p.y*1.43)+float2(t*.024,-t*.038));
float ripplePhase=dot(p,float2(2.65,7.42))+t*.85+phase*.52;
float rippleAA=max(.035,fwidth(ripplePhase)*.8);
float ribbons=1-smoothstep(.035,.035+rippleAA,abs(sin(ripplePhase)));
float broken=smoothstep(.64,.84,loose)*smoothstep(.35,.65,OwnNoise(p*.37+float2(5,t*.014)));
water+=half3(.14,.32,.45)*ribbons*broken*(1-smoothstep(.15,.65,length(fwidth(p))))*.55;
                water=lerp(water,half3(.88,.98,.97),foam*_FoamStrength);
                return half4(MixFog(water,i.fog),1);
            }
            ENDHLSL
        }
    }
}
