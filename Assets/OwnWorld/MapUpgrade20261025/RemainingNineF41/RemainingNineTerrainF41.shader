// OUTPUT ONLY F18 candidate. Original grass fields/lighting unchanged. Rock-only albedo/strata/derivative bump. Not Unity compiled.
Shader "Own/MapUpgrade20261025/RemainingNineTerrainF41"
{
 Properties {
  _GrassMap("Own grass albedo",2D)="white"{}
  _GrassScale("Tile per metre",Float)=.25
  _GrassStrength("Albedo mix",Range(0,1))=.75

  _StoneMap("Own ivory rock albedo",2D)="white"{}
  _StoneScale("Stone texture tiles per metre",Float)=.18
  _StoneStrength("Low contrast stone multiply",Range(0,1))=.4
  _RockSkyFill("Rock wrapped sky fill",Range(0,.8))=.45
  _RockSkyNeutral("Rock sky tint neutralization",Range(0,1))=.65
  _RockAmbientFloor("Rock minimum indirect light",Range(0,.3))=.14
  _RockSunWrap("Rock mild diffuse wrap",Range(0,.4))=.16
  _RockColor("Exposed warm ivory",Color)=(.70,.67,.59,1)
  _RockCoolColor("Quiet cool mineral",Color)=(.62,.65,.62,1)
  _RockGoldColor("Matte sparse mineral seam",Color)=(.48,.40,.25,1)
  _RockScale("Stone broad patches per metre",Float)=.20
  _RockVariation("Low contrast mineral variation",Range(0,.3))=.11
  _RockStrata("Soft irregular strata contrast",Range(0,.15))=.045
  _RockFine("Fine stone contrast",Range(0,.08))=.018
  _RockGold("Sparse selected gold strength",Range(0,.5))=.13
  _RockFacet("Geometric lighting on cliff",Range(0,1))=.4
  _RockBumpMetres("Rock micro relief metres",Range(0,.12))=.035
  _RockOnly("Rock-only material",Range(0,1))=0
  _RockSlopeLowY("Full rock below normalY",Range(.1,.8))=.40
  _RockSlopeHighY("Grass above normalY",Range(.2,.95))=.74
  _BaseColor("Meadow olive green",Color)=(.34,.43,.17,1)
  _CoolColor("Quiet cool green",Color)=(.25,.37,.17,1)
  _DryColor("Warm grass tip green",Color)=(.43,.46,.22,1)
  _PatchScale("Broad patch scale per metre",Float)=.055
  _PatchStrength("Broad colour variation",Range(0,.65))=.28
  _FineStrength("Fine blade contrast",Range(0,.1))=.022
  _AmbientFloor("Minimum ambient light",Range(0,.2))=.07
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Back ZWrite On
  HLSLINCLUDE

   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor,_CoolColor,_DryColor,_RockColor,_RockCoolColor,_RockGoldColor;
    float _RockScale,_RockVariation,_RockStrata,_RockFine,_RockGold,_RockFacet,_RockOnly,_RockBumpMetres;
    float _GrassScale,_GrassStrength;
    float _StoneScale,_StoneStrength,_RockSkyFill,_RockSkyNeutral,_RockAmbientFloor,_RockSunWrap;
    float _PatchScale,_PatchStrength,_FineStrength,_AmbientFloor,_RockSlopeLowY,_RockSlopeHighY;
   CBUFFER_END
   TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
   TEXTURE2D(_StoneMap); SAMPLER(sampler_StoneMap);
   struct A {float4 p:POSITION;float3 n:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   struct D {float4 p:SV_POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.fog=ComputeFogFactor(o.p.z);return o;}
   D DepthVert(A a){D o=(D)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.p=TransformObjectToHClip(a.p.xyz);return o;}
   float3 _LightDirection;
   float3 _LightPosition;
   D ShadowVert(A a){D o=(D)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);float3 w=TransformObjectToWorld(a.p.xyz);float3 n=TransformObjectToWorldNormal(a.n);
   #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 dir=normalize(_LightPosition-w);
   #else
    float3 dir=_LightDirection;
   #endif
    o.p=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(w,n,dir)));return o;}
   half DepthFrag(D i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return i.p.z;}
   half4 ShadowFrag(D i):SV_Target{UNITY_SETUP_INSTANCE_ID(i);return 0;}
   float OwnSurfaceHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float OwnSurfaceNoise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(OwnSurfaceHash(a),OwnSurfaceHash(a+float2(1,0)),f.x),lerp(OwnSurfaceHash(a+float2(0,1)),OwnSurfaceHash(a+1),f.x),f.y);}
   half3 OwnSurfaceDiffuse(half3 albedo,half3 n,float3 w){
   #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 sc=ComputeScreenPos(TransformWorldToHClip(w));
   #else
    float4 sc=TransformWorldToShadowCoord(w);
   #endif
    Light sun=GetMainLight(sc);
    half3 ambient=max(SampleSH(n),half3(_AmbientFloor,_AmbientFloor,_AmbientFloor));
    return albedo*(ambient+sun.color*saturate(dot(n,sun.direction))*sun.distanceAttenuation*sun.shadowAttenuation);
   }

   // Existing grass lighting function above is unchanged. Rock=0 returns it exactly.
   half3 OwnSurfaceDiffuseF10(half3 albedo,half3 n,float3 w,half rockMask){
    if(rockMask<.0001) return OwnSurfaceDiffuse(albedo,n,w);
   #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 sc=ComputeScreenPos(TransformWorldToHClip(w));
   #else
    float4 sc=TransformWorldToShadowCoord(w);
   #endif
    Light sun=GetMainLight(sc);
    half3 ambientOld=max(SampleSH(n),half3(_AmbientFloor,_AmbientFloor,_AmbientFloor));
    // Downward/inward cliff normals must not sample ONLY dark green lower-hemisphere SH.
    // Sample a broad upper hemisphere, but keep geometric direct light + real shadows.
    half3 skyN=normalize(half3(n.x*.45,max(n.y,0)+.85,n.z*.45));
    half3 sky=max(SampleSH(skyN),half3(0,0,0));
    half skyLuma=dot(sky,half3(.2126,.7152,.0722));
    sky=lerp(sky,half3(skyLuma,skyLuma,skyLuma),_RockSkyNeutral);
    half3 rockAmbient=lerp(ambientOld,sky,_RockSkyFill);
    rockAmbient=max(rockAmbient,half3(_RockAmbientFloor,_RockAmbientFloor,_RockAmbientFloor));
    half3 ambient=lerp(ambientOld,rockAmbient,rockMask);
    half ndl=dot(n,sun.direction);
    half diffuse=lerp(saturate(ndl),saturate((ndl+_RockSunWrap)/(1+_RockSunWrap)),rockMask);
    return albedo*(ambient+sun.color*diffuse*sun.distanceAttenuation*sun.shadowAttenuation);
   }

   float2 RockProjection(float2 p){
    float broad=OwnSurfaceNoise(p*_RockScale+float2(11.31,-7.83));
    float fineFade=1-smoothstep(.12,.42,length(fwidth(p*2.1)));
    float fine=(OwnSurfaceNoise(p*2.1+float2(23.2,19.7))-.5)*fineFade;
    return float2(broad,fine);
   }
   half3 OwnTriplanarStone(float3 w,half3 geometricN){
    float3 weight=pow(abs(geometricN),4);weight/=max(.0001,weight.x+weight.y+weight.z);
    float2 field=RockProjection(w.zy)*weight.x+RockProjection(w.xz)*weight.y+RockProjection(w.xy)*weight.z;
    float warp=OwnSurfaceNoise(w.xz*.085+float2(6.2,21.3));
    // Warped ~3m mineral bands. No rectangular tiling, no emissive seam.
    float layer=.5+.5*sin(w.y*2.05+warp*4.8+field.x*1.4);
    half3 stone=_RockColor.rgb*(1+(field.x-.5)*_RockVariation+(layer-.5)*_RockStrata+field.y*_RockFine);
    stone=lerp(stone,_RockCoolColor.rgb,smoothstep(.62,.88,field.x)*_RockVariation*.65);
    // Sparse broken mineral strokes: no rectangular cells, no emissive gold.
    float ridgeField=OwnSurfaceNoise(w.xy*.145+warp*.37)*weight.z+OwnSurfaceNoise(w.zy*.145+warp*.37)*weight.x+OwnSurfaceNoise(w.xz*.145+warp*.37)*weight.y;
    float aa=max(fwidth(ridgeField)*1.3,.0018);
    float vein=1-smoothstep(.004,.004+aa,abs(ridgeField-.53));
    float selected=smoothstep(.77,.92,OwnSurfaceNoise(w.xz*.052+w.y*.011+31.7));
    float veinFade=1-smoothstep(.09,.35,length(fwidth(w.xy*.145)));
    stone=lerp(stone,_RockGoldColor.rgb,vein*selected*_RockGold*veinFade);
    // Same pow(abs(normal),4) triplanar weights; no UV seam requirement or new geometry.
    half3 texX=SAMPLE_TEXTURE2D(_StoneMap,sampler_StoneMap,w.zy*float2(_StoneScale,_StoneScale*3.2)).rgb;
    half3 texY=SAMPLE_TEXTURE2D(_StoneMap,sampler_StoneMap,w.xz*_StoneScale).rgb;
    half3 texZ=SAMPLE_TEXTURE2D(_StoneMap,sampler_StoneMap,w.xy*float2(_StoneScale,_StoneScale*3.2)).rgb;
    half3 stoneTex=texX*weight.x+texY*weight.y+texZ*weight.z;
    // sRGB import decodes in shader. Keep narrow bounded multiply; do not use raw tex as albedo.
    half3 texMultiply=clamp(half3(1,1,1)+(stoneTex-half3(.42,.42,.42))*1.35,half3(.48,.48,.48),half3(1.18,1.18,1.18));
    return stone*lerp(half3(1,1,1),texMultiply,_StoneStrength);
   }
   float OwnRockHeightF18(float3 w){
    // Continuous world-space field; reuses existing procedural noise only.
    float a=OwnSurfaceNoise(w.xy*.43+float2(6.1,13.2));
    float b=OwnSurfaceNoise(w.zy*.51+float2(-9.3,4.7));
    float c=OwnSurfaceNoise(w.xz*.36+float2(17.8,-3.2));
    return (a*.45+b*.4+c*.15-.5)*_RockBumpMetres;
   }
   half3 OwnRockNormalF18(float3 w,half3 n,float mask){
    float height=OwnRockHeightF18(w);
    float3 dx=ddx(w),dy=ddy(w);
    float3 rx=cross(dy,n),ry=cross(n,dx);
    float determinant=dot(dx,rx);
    float safeDet=(determinant<0?-1:1)*max(abs(determinant),1e-7);
    float3 gradient=(rx*ddx(height)+ry*ddy(height))/safeDet;
    // Grazing/far pixels fade to prevent unstable tiny procedural shading.
    float fade=1-smoothstep(.15,.65,max(length(dx),length(dy)));
    return mask<.0001?n:normalize(n-gradient*mask*fade);
   }
   half4 Frag(V i):SV_Target{
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 p=i.w.xz;half3 n=normalize(i.n);
    float broad=OwnSurfaceNoise(p*_PatchScale+float2(13.1,7.2));
    float warm=OwnSurfaceNoise(p*(_PatchScale*.51)+float2(-4.3,19.7));
    half3 quiet=lerp(_CoolColor.rgb,_DryColor.rgb,smoothstep(.24,.78,broad));
    half3 albedo=lerp(_BaseColor.rgb,quiet,_PatchStrength);
    albedo*=1+(warm-.5)*_PatchStrength*.18;
    // Fine blade suggestion fades before minification; no speckled noise carpet.
    float microFade=1-smoothstep(.12,.55,length(fwidth(p)));
    float phase=OwnSurfaceNoise(p*.62)*2.0;
    float blade=sin(p.x*15.3+p.y*3.7+phase)*sin(p.y*12.1-p.x*2.9);
    float tuft=smoothstep(.58,.84,OwnSurfaceNoise(p*1.9+9.1));
    albedo*=1+(blade*.40+(tuft-.25)*.35)*_FineStrength*microFade;
    half3 grassTex=SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,p*_GrassScale).rgb;
    albedo=lerp(albedo,grassTex*.68,_GrassStrength);
    float zoneF41=max(1-smoothstep(-62,-55,p.y),max(smoothstep(55,62,p.x),smoothstep(0,7,p.x)*max(smoothstep(55,62,p.y),1-smoothstep(-7,0,p.y))));
    half3 fineGrassF41=SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,p*.22).rgb;
    half3 quieterF41=lerp(lerp(_BaseColor.rgb,quiet,.55),fineGrassF41*.68,.39);
    albedo=lerp(albedo,quieterF41,zoneF41);
    float3 face=cross(ddy(i.w),ddx(i.w));face*=rsqrt(max(dot(face,face),1e-12));
    float slopeY=saturate(min(abs(n.y),abs(face.y))+(OwnSurfaceNoise(i.w.xz*.18)-.5)*.06);float rock=1-smoothstep(_RockSlopeLowY,max(_RockSlopeLowY+.01,_RockSlopeHighY),slopeY);
    rock=max(rock,_RockOnly);
    if(dot(face,n)<0)face=-face;
    half3 rockAlbedo=OwnTriplanarStone(i.w,face);
    albedo=lerp(albedo,rockAlbedo,rock);
    half3 litN=normalize(lerp(n,face,_RockFacet*rock));
    litN=OwnRockNormalF18(i.w,litN,rock);
    return half4(MixFog(OwnSurfaceDiffuseF10(albedo,litN,i.w,rock),i.fog),1);
   }

  ENDHLSL

  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   ENDHLSL
  }
  Pass {
   Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
   ZWrite On ZTest LEqual ColorMask 0
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ShadowVert
   #pragma fragment ShadowFrag
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   ENDHLSL
  }
  Pass {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
   ZWrite On ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex DepthVert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   ENDHLSL
  }
 }
 FallBack Off
}

