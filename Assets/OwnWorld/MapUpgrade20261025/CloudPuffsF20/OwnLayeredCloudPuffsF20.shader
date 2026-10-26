// OUTPUT-ONLY CANDIDATE. Not compiled or installed. Existing shaders remain untouched.
Shader "KingOfGraphic/Own/Candidate/LayeredCloudPuffsF20"
{
 Properties {
  _Tint("Lit tint",Color)=(.89,.94,1,.42)
  _Shade("Deep tint",Color)=(.47,.63,.80,1)
  _Scale("World noise scale",Float)=.024
  _Drift("Drift XZ",Vector)=(.0008,-.0005,0,0)
  _Offset("Noise offset",Vector)=(0,0,0,0)
  _Threshold("Cloud threshold",Range(0,1))=.43
  _Softness("Edge softness",Range(.01,.5))=.22
  _OuterRadius("Outer radius",Float)=345
  _OuterFade("Outer fade",Float)=70
  _DensityFloor("Minimum cloud density",Range(0,1))=0
  _PuffRelief("Apparent puff height metres",Range(0,12))=7
  _PuffLight("Rounded puff lighting",Range(0,.5))=.28
  _NearFade("Camera near fade",Float)=3
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-80" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Tint,_Shade; float4 _Drift,_Offset;
    float _Scale,_Threshold,_Softness,_OuterRadius,_OuterFade,_NearFade,_DensityFloor,_PuffRelief,_PuffLight;
   CBUFFER_END
   float _OverworldEclipseDarkness; // Existing gameplay global, never material-local.
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half fog:TEXCOORD1;};
   V Vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.fog=ComputeFogFactor(o.p.z);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
   float PuffField(float2 p){
    // Sum broad round kernels instead of ridged abs-noise or linear bands.
    // Two overlapping noninteger scales and gentle warp conceal the cell grid.
    float2 cell=floor(p);float sum=0;
    [unroll] for(int x=-1;x<=1;x++){
     [unroll] for(int y=-1;y<=1;y++){
      float2 key=cell+float2(x,y);
      float2 centre=key+.5+(float2(Hash(key+3.7),Hash(key-17.1))-.5)*.60;
      float2 d=p-centre;float radius=lerp(.55,.88,Hash(key+41.3));
      sum+=exp2(-dot(d,d)*4.2/(radius*radius))*lerp(.72,1.12,Hash(key+9.8));
     }
    }
    return saturate(sum);
   }
   float3 PuffNormal(float3 w,float height){
    float3 dx=ddx(w),dy=ddy(w);float3 up=float3(0,1,0);
    float3 rx=cross(dy,up),ry=cross(up,dx);float det=dot(dx,rx);
    float safe=(det<0?-1:1)*max(abs(det),1e-7);
    float3 gradient=(rx*ddx(height)+ry*ddy(height))/safe;
    float fade=1-smoothstep(.8,3.5,max(length(dx),length(dy)));
    return normalize(up-gradient*fade);
   }
   half4 Frag(V i):SV_Target{
    float2 p=i.w.xz*_Scale+_Offset.xy+_Time.y*_Drift.xy;
    float2 warp=float2(Noise(p*.37+11.2),Noise(p*.41-7.8))-.5;
    float broadPuff=PuffField(p+warp*.36);
    float detailPuff=PuffField(p*2.17+float2(19.3,-8.1)+warp*.21);
    float n=saturate(broadPuff*.74+detailPuff*.22+Noise(p*.43+5.1)*.04);
    float alpha=smoothstep(_Threshold-_Softness,_Threshold+_Softness,n);
    alpha=max(alpha,saturate(_DensityFloor));
    alpha*=1-smoothstep(max(0,_OuterRadius-_OuterFade),_OuterRadius,length(i.w.xz));
    // Avoid a hard plane flashing through the free camera. No depth-texture dependency.
    alpha*=smoothstep(0,max(.01,_NearFade),distance(i.w,_WorldSpaceCameraPos));
    float body=smoothstep(.10,.88,n);
    float3 cloudN=PuffNormal(i.w,(broadPuff*.78+detailPuff*.22)*_PuffRelief);
    // A fixed soft upper-left key suggests rounded relief, not physical volume/shadows.
    float diffuse=saturate(dot(cloudN,normalize(float3(-.48,.82,-.31))));
    float light=1-_PuffLight+_PuffLight*diffuse;
    half3 c=lerp(_Shade.rgb,_Tint.rgb,body*.90+.035)*light;
    c+=_Tint.rgb*(pow(saturate(n),4)*.035);
    c*=lerp(half3(1,1,1),half3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
    return half4(MixFog(c,ComputeFogFactor(TransformWorldToHClip(i.w).z)),alpha*_Tint.a);
   }
   ENDHLSL
  }
 }
}
