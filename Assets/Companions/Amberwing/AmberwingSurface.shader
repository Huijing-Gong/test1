Shader "Companions/Amberwing Velvet" {
 Properties { _Color("Tint",Color)=(1,1,1,1) }
 SubShader { Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
 Pass { Tags {"LightMode"="UniversalForward"} Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 p:POSITION;float3 n:NORMAL;float4 c:COLOR;};
 struct V {float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 w:TEXCOORD1;float4 c:COLOR;};
 half4 _Color;
 V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.n=TransformObjectToWorldNormal(i.n);o.w=TransformObjectToWorld(i.p.xyz);o.c=i.c;return o;}
 half4 frag(V i):SV_Target {float3 n=normalize(i.n);Light l=GetMainLight();float3 key=normalize(float3(-.45,.75,.65));float d=dot(n,key);float lit=smoothstep(-.45,.85,d);float rim=pow(1-saturate(dot(n,normalize(GetWorldSpaceViewDir(i.w)))),2.6);float grain=frac(sin(dot(i.w,float3(143.17,317.41,91.07)))*41758.53);float3 c=i.c.rgb*_Color.rgb*(.985+.015*grain);return half4(c*(.68+.32*lit)+rim*c*.17,1);}
 ENDHLSL }
 Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 float4 vert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}half4 frag():SV_Target{return 0;}
 ENDHLSL }
 }
}
