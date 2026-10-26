Shader "OverworldV2/RegionalGround"
{
 Properties
 {
  _GrassTex("Grass",2D)="white"{}
  _PathTex("Path",2D)="white"{}
  _StoneTex("Stone",2D)="white"{}
  _CliffTex("Natural cliff",2D)="white"{}
  _CliffTiling("Cliff world tile size",Float)=5
  _CliffTint("Cliff tint",Color)=(.76,.75,.70,1)
  _GrassTint("Grass tint",Color)=(.68,.75,.59,1)
  _SandTint("Shore tint",Color)=(.64,.56,.43,1)
  _Tiling("World tile size",Float)=5
  [HideInInspector] _BaseMap("Shadow alpha",2D)="white"{}
  [HideInInspector] _BaseColor("Shadow tint",Color)=(1,1,1,1)
  [HideInInspector] _Cull("Cull",Float)=2
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass
  {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_GrassTex);SAMPLER(sampler_GrassTex);
   TEXTURE2D(_PathTex);SAMPLER(sampler_PathTex);
   TEXTURE2D(_StoneTex);SAMPLER(sampler_StoneTex);
   TEXTURE2D(_CliffTex);SAMPLER(sampler_CliffTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _GrassTint,_SandTint,_CliffTint;float _Tiling,_CliffTiling;
   CBUFFER_END
   struct A{float4 pos:POSITION;float3 normal:NORMAL;half4 color:COLOR;};
   struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;half4 color:COLOR;half fog:TEXCOORD2;};
   V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.pos.xyz);o.pos=p.positionCS;o.world=p.positionWS;o.normal=TransformObjectToWorldNormal(v.normal);o.color=v.color;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   float region(float2 p,float2 center,float2 radius,float noise)
   {
    return 1-smoothstep(.65,1.15,length((p-center)/radius)+noise*.13);
   }
   half4 frag(V i):SV_Target
   {
    float2 uv=i.world.xz/max(.1,_Tiling),p=i.world.xz;
    half3 textureGrass=SAMPLE_TEXTURE2D(_GrassTex,sampler_GrassTex,uv).rgb;
    half luminance=dot(textureGrass,half3(.2126,.7152,.0722));
    half3 grass=lerp(luminance.xxx,textureGrass,.52)*_GrassTint.rgb;
    float noise=sin(p.x*.19+sin(p.y*.12)*1.7)*sin(p.y*.16+p.x*.08);
    float meadow=max(region(p,float2(-10,-28),float2(23,18),noise),region(p,float2(19,-46),float2(17,14),noise));
    float dry=region(p,float2(12,23),float2(12,16),noise);
    float forest=region(p,float2(43,4),float2(27,32),noise);
    grass*=lerp(half3(.95,1,.91),half3(1.24,1.12,.70),meadow);
    grass=lerp(grass,luminance*half3(.92,.75,.38),dry*.78);
    half3 path=SAMPLE_TEXTURE2D(_PathTex,sampler_PathTex,uv*.85).rgb;
    half3 floor=lerp(grass*half3(.62,.77,.66),path*half3(.48,.53,.36),.28);
    grass=lerp(grass,floor,forest*.78);
    grass*=.97+.045*noise;
    half3 stone=SAMPLE_TEXTURE2D(_StoneTex,sampler_StoneTex,uv).rgb;
    half3 surface=lerp(grass,path,saturate(i.color.r));
    half3 shore=lerp(_SandTint.rgb,stone*_SandTint.rgb,.25);
    float wet=(1-smoothstep(.35,1.1,i.world.y))*smoothstep(-.1,.2,i.world.y);
    shore=lerp(shore,shore*half3(.57,.66,.65),wet*.65);
    surface=lerp(surface,shore,saturate(i.color.g));
    half3 normal=normalize(i.normal);half3 weights=pow(abs(normal),4);weights/=max(.001,weights.x+weights.y+weights.z);
    float tile=max(.1,_CliffTiling);
    half3 cliff=SAMPLE_TEXTURE2D(_CliffTex,sampler_CliffTex,i.world.zy/tile).rgb*weights.x;
    cliff+=SAMPLE_TEXTURE2D(_CliffTex,sampler_CliffTex,i.world.xy/tile).rgb*weights.z;
    cliff+=SAMPLE_TEXTURE2D(_CliffTex,sampler_CliffTex,i.world.xz/tile).rgb*weights.y;
    surface=lerp(surface,cliff*_CliffTint.rgb,smoothstep(.22,.46,1-normal.y)*(1-saturate(i.color.r)));
    surface=lerp(cliff*_CliffTint.rgb,surface,saturate(i.color.a));
    surface*=lerp(.85,1.12,i.color.b);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    half3 lit=SampleSH(normal)+light.color*saturate(dot(normal,light.direction))*light.shadowAttenuation;
    return half4(MixFog(surface*lit,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
