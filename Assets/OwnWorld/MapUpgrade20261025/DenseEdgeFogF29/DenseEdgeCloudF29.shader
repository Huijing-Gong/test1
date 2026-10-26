Shader "Own/MapUpgrade20261025/DenseEdgeCloudF29"
{
 Properties {
  _CloudTex("Owned cloud texture",2D)="white"{}
  _GroundEdgeMask("World edge distance and height",2D)="black"{}
  _EdgeWorldRect("World XZ origin and size",Vector)=(-130,-130,260,260)
  _LitTint("Sunlit cloud",Color)=(1,.98,.92,1)
  _ShadeTint("Cool cloud underside",Color)=(.52,.66,.84,1)
  _Opacity("Opacity",Range(0,1))=.92
  _SoftDistance("Opaque intersection fade metres",Float)=2.5
  _NearFade("Camera near fade metres",Float)=5
  _DriftMetres("Bounded drift metres",Float)=.8
  _TextureColor("Preserve authored texture shading",Range(0,1))=1
  _SoftTextureBias("Mist texture softness",Range(0,5))=0
  _RiseMetres("Bounded vertical mist drift",Float)=0
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float4 _LitTint,_ShadeTint,_EdgeWorldRect;
    float _Opacity,_SoftDistance,_NearFade,_DriftMetres,_TextureColor,_SoftTextureBias,_RiseMetres;
   CBUFFER_END
   TEXTURE2D(_CloudTex);SAMPLER(sampler_CloudTex);
   TEXTURE2D(_GroundEdgeMask);SAMPLER(sampler_GroundEdgeMask);
   float _OverworldEclipseDarkness;
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float4 sizeSeed:TEXCOORD1;float2 plane:TEXCOORD2;half4 color:COLOR;float4 edge:TEXCOORD3;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 w:TEXCOORD1;half4 color:COLOR;float eye:TEXCOORD2;half fog:TEXCOORD3;float4 edge:TEXCOORD4;};
   V Vert(A a){
    V o;float3 c=TransformObjectToWorld(a.p.xyz);
    float phase=a.sizeSeed.z;
    c.x+=sin(_Time.y*.033+phase)*_DriftMetres;
    c.z+=cos(_Time.y*.025+phase*1.3)*_DriftMetres*.65;
    c.y+=sin(_Time.y*.13+phase)*_RiseMetres;
    float2 q=a.uv-.5;float angle=a.sizeSeed.w;
    q=mul(float2x2(cos(angle),-sin(angle),sin(angle),cos(angle)),q);
    float3 right=UNITY_MATRIX_I_V._m00_m10_m20;
    float3 up=UNITY_MATRIX_I_V._m01_m11_m21;
    if(a.plane.x>.5){right=float3(1,0,0);up=float3(0,0,1);}
    o.w=c+right*q.x*a.sizeSeed.x+up*q.y*a.sizeSeed.y;
    o.p=TransformWorldToHClip(o.w);o.eye=-TransformWorldToView(o.w).z;
    o.uv=a.uv;o.color=a.color;o.edge=a.edge;o.fog=ComputeFogFactor(o.p.z);return o;
   }
   half4 Frag(V i):SV_Target{
    half4 tex=SAMPLE_TEXTURE2D_BIAS(_CloudTex,sampler_CloudTex,i.uv,_SoftTextureBias);
    float raw=SampleSceneDepth(i.p.xy/_ScaledScreenParams.xy);
    float orthoRaw=raw;
    #if UNITY_REVERSED_Z
     orthoRaw=1-raw;
    #endif
    float opaqueEye=unity_OrthoParams.w>.5?lerp(_ProjectionParams.y,_ProjectionParams.z,orthoRaw):LinearEyeDepth(raw,_ZBufferParams);
    float contact=1;
    float near=smoothstep(.15,max(.2,_NearFade),i.eye);
    float luma=dot(tex.rgb,float3(.2126,.7152,.0722));
    float body=smoothstep(.45,.98,luma);
    half3 c=lerp(lerp(_ShadeTint.rgb,_LitTint.rgb,body),tex.rgb*_LitTint.rgb,_TextureColor)*i.color.rgb;
    c*=lerp(half3(1,1,1),half3(.25,.27,.32),saturate(_OverworldEclipseDarkness));
    float2 edgeUV=(i.w.xz-_EdgeWorldRect.xy)/_EdgeWorldRect.zw;
    float2 edgeData=SAMPLE_TEXTURE2D_LOD(_GroundEdgeMask,sampler_GroundEdgeMask,edgeUV,0).rg;
    float signedDistance=(edgeData.r-.5)*64;
    float groundHeight=edgeData.g*50-5;
    float edgeFade=max(smoothstep(-5,.5,signedDistance),smoothstep(-20,-8,signedDistance)*(1-smoothstep(groundHeight-5,groundHeight-2,i.w.y)));
    float gutter=smoothstep(0,.025,min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y)));
    return half4(MixFog(c,i.fog),tex.a*_Opacity*i.color.a*contact*near*gutter*edgeFade);
   }
   ENDHLSL
  }
 }
}

