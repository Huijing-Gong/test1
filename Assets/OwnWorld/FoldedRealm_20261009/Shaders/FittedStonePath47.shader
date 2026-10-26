Shader "KingOfGraphic/Own/FittedStonePath47" {
 Properties{_BaseColor("Tint",Color)=(1,1,1,1)}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}Cull Back
 Pass{Tags{"LightMode"="UniversalForward"}HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)half4 _BaseColor;CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half fog:TEXCOORD2;};
 V Vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.fog=ComputeFogFactor(o.p.z);return o;}
 float2 Hash(float2 p){float3 h=frac(float3(p.x,p.y,p.x)*.1031);h+=dot(h,h.yzx+33.33);return frac((h.xx+h.yz)*h.zy);}
 half4 Frag(V i):SV_Target{
 float2 at=i.w.xz*1.47;float2 cell=floor(at);float nearest=99,second=99;float2 selected=0,firstSite=0,nextSite=0;
 for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){float2 index=cell+float2(x,y);float2 site=index+.18+Hash(index)*.64;float d=dot(site-at,site-at);if(d<nearest){second=nearest;nextSite=firstSite;nearest=d;selected=index;firstSite=site;}else if(d<second){second=d;nextSite=site;}}
 float gap=99;float2 edgeAxis=0;
 for(int ey=-1;ey<=1;ey++)for(int ex=-1;ex<=1;ex++){float2 edgeIndex=cell+float2(ex,ey);float2 site=edgeIndex+.18+Hash(edgeIndex)*.64;float2 delta=site-firstSite;float sq=dot(delta,delta);if(sq<.0001)continue;float2 axis=delta*rsqrt(sq);float boundary=dot((site+firstSite)*.5-at,axis);if(boundary<gap){gap=boundary;edgeAxis=axis;}}
 float aa=max(.0006,fwidth(gap));float mortar=1-smoothstep(max(0,.012-aa),.024+aa,gap);
 float2 stoneVariation=Hash(selected+float2(17.31,6.41));float variation=stoneVariation.x;half3 stone=lerp(half3(.70,.665,.575),half3(.91,.84,.70),variation);
 float2 textureAt=i.w.xz*13.1;float2 tile=floor(textureAt);float2 u=frac(textureAt);u=u*u*(3-2*u);float grain=lerp(lerp(Hash(tile).x,Hash(tile+float2(1,0)).x,u.x),lerp(Hash(tile+float2(0,1)).x,Hash(tile+1).x,u.x),u.y);
 float fine=(Hash(floor(i.w.xz*75)).x-.5)*(1-smoothstep(.006,.025,length(fwidth(i.w))));stone*=.90+grain*.20+fine*.028;
 half3 albedo=lerp(stone,half3(.52,.465,.36),mortar*.64)*_BaseColor.rgb;
 float2 gradient=edgeAxis;
 float bevel=(1-smoothstep(.024,.10,gap))*(1-mortar);
 float3 baseNormal=normalize(i.n);float2 faceTilt=(stoneVariation-.5)*.045;float3 tilt=float3(faceTilt.x,0,faceTilt.y);tilt-=baseNormal*dot(tilt,baseNormal);half3 n=normalize(baseNormal+tilt+float3(gradient.x,0,gradient.y)*bevel*.16);
 float relief=grain*.0018;float3 dx=ddx(i.w),dy=ddy(i.w);float3 gx=cross(dy,baseNormal),gy=cross(baseNormal,dx);float determinant=dot(dx,gx);float3 surfaceGradient=(gx*ddx(relief)+gy*ddy(relief))*sign(determinant)/max(abs(determinant),.000001);surfaceGradient*=min(1,.12/max(.0001,length(surfaceGradient)));n=normalize(n-surfaceGradient);
 InputData input=(InputData)0;input.positionWS=i.w;input.normalWS=n;input.viewDirectionWS=SafeNormalize(GetWorldSpaceViewDir(i.w));input.shadowCoord=TransformWorldToShadowCoord(i.w);input.bakedGI=SampleSH(n);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);input.shadowMask=half4(1,1,1,1);
 SurfaceData surface=(SurfaceData)0;surface.albedo=albedo;surface.alpha=1;surface.metallic=0;surface.smoothness=lerp(.14,.24,stoneVariation.y);surface.occlusion=1;surface.normalTS=half3(0,0,1);
 half4 color=UniversalFragmentPBR(input,surface);half peak=max(color.r,max(color.g,color.b));half knee=.70h;half width=.28h;half shoulder=knee+width*(1-exp(-max(0,peak-knee)/width));color.rgb*=peak>knee?shoulder/max(peak,.0001h):1;color.rgb=MixFog(color.rgb,i.fog);return color;
 }
 ENDHLSL}
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }}
