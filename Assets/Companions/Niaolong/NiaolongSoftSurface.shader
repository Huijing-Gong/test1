Shader "Companions/Niaolong Soft Surface" {
 Properties { _Color("Tint",Color)=(1,1,1,1) }
 SubShader { Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
 Pass { Tags {"LightMode"="UniversalForward"} Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A {float4 p:POSITION;float3 n:NORMAL;float4 c:COLOR;};
 struct V {float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 w:TEXCOORD1;float4 c:COLOR;float3 local:TEXCOORD2;};
 half4 _Color;
 V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.n=TransformObjectToWorldNormal(i.n);o.w=TransformObjectToWorld(i.p.xyz);o.c=i.c;o.local=i.p.xyz;return o;}
 half4 frag(V i):SV_Target {
 float3 n=normalize(i.n),view=normalize(GetWorldSpaceViewDir(i.w));
 float key=smoothstep(-.5,.92,dot(n,normalize(float3(-.55,.8,.65))));
 float rim=pow(1-saturate(dot(n,view)),3);
 float fine=sin(i.local.x*870)*sin(i.local.y*820)*sin(i.local.z*890);
 float3 shade=lerp(float3(.78,.74,.62),float3(1.04,1.01,.95),key);
 float3 c=_Color.rgb*i.c.rgb*shade*(1+.007*fine);
 c+=_Color.rgb*rim*.075;return half4(c,1);
 }
 ENDHLSL }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
