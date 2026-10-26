Shader "KingOfGraphic/Own/LakeBankFaceted136"
{
    Properties { _BaseColor ("Tint", Color) = (1,1,1,1) _GrassTex ("Original meadow detail", 2D) = "white" {} _RockTex ("Owned limestone detail", 2D) = "white" {} _StoneBounce ("Limestone indirect tint", Color) = (.50,.52,.48,1) _StairClearance ("Arrival visual clearance", Vector) = (0,0,0,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex); TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; half4 _StoneBounce; float4 _StairClearance;
        CBUFFER_END
        float MeadowHash(float2 cell)
        {
            float3 h=frac(float3(cell.x,cell.y,cell.x)*.1031);
            h+=dot(h,h.yzx+33.33);
            return frac((h.x+h.y)*h.z);
        }
        float MeadowNoise(float2 at)
        {
            float2 cell=floor(at);float2 u=frac(at);u=u*u*(3-2*u);
            return lerp(lerp(MeadowHash(cell),MeadowHash(cell+float2(1,0)),u.x),lerp(MeadowHash(cell+float2(0,1)),MeadowHash(cell+1),u.x),u.y);
        }
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 bary:TEXCOORD1; };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; float2 bary:TEXCOORD3; };
        Varyings Vert(Attributes v) { Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.color=v.color; o.bary=v.bary; o.fog=ComputeFogFactor(o.positionCS.z); return o; }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 Frag(Varyings i):SV_Target
            {
                if(_StairClearance.w>.5 && abs(i.positionWS.x)<_StairClearance.x && i.positionWS.z>_StairClearance.y && i.positionWS.z<_StairClearance.z)clip(-1); float3 n=normalize(i.normalWS);
                // Keep the walking mesh unchanged; shade only the eastern chalk bank as hard folds.
                float3 faceNormal=cross(ddy(i.positionWS),ddx(i.positionWS));
                faceNormal*=rsqrt(max(dot(faceNormal,faceNormal),1e-12));
                faceNormal=dot(faceNormal,n)<0?-faceNormal:faceNormal;
                float chalkBank=smoothstep(-28,-26,i.positionWS.x)*(1-smoothstep(6,8,i.positionWS.x));
                chalkBank*=smoothstep(-8,-6,i.positionWS.z)*(1-smoothstep(89,91,i.positionWS.z))*smoothstep(4,4.3,i.positionWS.y);
                chalkBank*=step(.15,faceNormal.y)*step(faceNormal.y,.85);
                n=lerp(n,faceNormal,chalkBank);

                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 albedo=i.color.rgb*_BaseColor.rgb;
                half3 grassDetail=SAMPLE_TEXTURE2D(_GrassTex,sampler_GrassTex,i.positionWS.xz*.32).rgb;
                half detail=dot(grassDetail,half3(.23,.64,.13));
                half grassMask=saturate((i.color.g-i.color.r)*12);
                float broad=MeadowNoise(i.positionWS.xz*.075);
                float patches=MeadowNoise(i.positionWS.xz*.28+float2(18.1,7.6));
                // Gentle eastern lake slopes blend owned meadow between chalk folds.
                float bankZone=smoothstep(-4,4,i.positionWS.x)*(1-smoothstep(52,60,i.positionWS.x));
                bankZone*=smoothstep(-4,4,i.positionWS.z)*(1-smoothstep(82,90,i.positionWS.z));
                bankZone*=smoothstep(9.5,12,i.positionWS.y);
                grassMask=max(grassMask,bankZone*smoothstep(.73,.94,n.y)*smoothstep(.22,.62,patches));
                half3 meadow=lerp(half3(.20,.31,.08),half3(.34,.46,.15),saturate(detail*1.15+broad*.26-.06));
                meadow*=lerp(.90,1.06,broad)*lerp(.96,1.02,patches); albedo=lerp(albedo,meadow,grassMask); float limestone=.94+.07*MeadowNoise(float2(i.positionWS.x*.23+i.positionWS.z*.17,i.positionWS.y*.32))+(.02*MeadowNoise(float2(i.positionWS.x*.91-i.positionWS.z*.44,i.positionWS.y*.78))-.01);albedo*=lerp(limestone,1,grassMask);
                float3 bary=float3(i.bary,1-i.bary.x-i.bary.y);
                // Some original terrain meshes do not provide barycentric UV1.
                // A constant coordinate must not turn the whole stone face into an edge.
                float3 edgeWidth=fwidth(bary);
                float edgeData=step(.000001,edgeWidth.x+edgeWidth.y+edgeWidth.z);
                float3 edge=smoothstep(0,max(edgeWidth*.85,.000001),bary);
                half goldEdge=edgeData*(1-min(edge.x,min(edge.y,edge.z)))*step(.66,i.color.r)*step(n.y,.84)*(1-grassMask);

                // The existing terrain is a 170-cell grid across 220 metres.
                float2 bankCell=(i.positionWS.xz+110)*(170.0/220.0);
                float2 bankUV=frac(bankCell);
                float bankSeam=min(min(min(bankUV.x,1-bankUV.x),min(bankUV.y,1-bankUV.y)),abs(1-bankUV.x-bankUV.y)*.70710678);
                float bankWidth=max(fwidth(bankCell.x)+fwidth(bankCell.y),.00001)*.45;
                float bankFineEdge=1-smoothstep(bankWidth*.25,bankWidth*.9,bankSeam);
                float sparseBankSeam=step(.88,MeadowHash(floor(bankCell)+float2(13.6,6.1)));
                goldEdge=max(goldEdge,chalkBank*bankFineEdge*sparseBankSeam*step(.66,i.color.r)*(1-grassMask));
                albedo=lerp(albedo,half3(.83,.68,.35),goldEdge*.58);
                half3 ambient=lerp(_StoneBounce.rgb,half3(.32,.37,.43),grassMask);
                half3 direct=sun.color*saturate(dot(n,sun.direction))*.90*sun.shadowAttenuation;
                half3 lit=albedo*(ambient+direct);half peak=max(lit.r,max(lit.g,lit.b));half knee=.60h;half width=.34h;half shoulder=knee+width*(1-exp(-max(0,peak-knee)/width));lit*=peak>knee?lerp(shoulder/max(peak,.0001h),1,grassMask):1;return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            half4 NormalFrag(Varyings i):SV_Target{return half4(normalize(i.normalWS),0);}
            ENDHLSL
        }
    }
}
