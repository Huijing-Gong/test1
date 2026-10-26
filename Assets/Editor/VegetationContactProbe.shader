// Editor diagnostic only. Each six-vertex quad writes one original vertex's
// actual production deformation into one floating-point render-target pixel.
Shader "Hidden/FoldedRealm/VegetationContactProbe"
{
    Properties
    {
        _ProbeVertexCount("Original vertex count", Float) = 1
        _VertexDataValid("Prepared wind vertex data", Float) = 1
        _WindAmplitude("Stem bend / plant height", Float) = .16
        _WindDetail("Leaf flutter in metres", Float) = .016
        _InteractionStrength("Player bending", Float) = 1
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ProbeVertex
            #pragma fragment ProbeFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ProbeVertexCount;
                float _VertexDataValid;
                float _WindAmplitude;
                float _WindDetail;
                float _InteractionStrength;
            CBUFFER_END
            #include "Assets/Art/LivingEnvironment/VegetationWind.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 rootAndWeight : TEXCOORD2;
                float4 plantData : TEXCOORD3;
                uint vertexID : SV_VertexID;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float3 deformedWS : TEXCOORD0;
            };
            Varyings ProbeVertex(Attributes input)
            {
                Varyings output;
                float3 normalWS;
                FRDeformVegetation(input.positionOS, input.normalOS,
                    input.rootAndWeight, input.plantData, output.deformedWS, normalWS);
                uint originalIndex = input.vertexID / 6;
                uint cornerIndex = input.vertexID % 6;
                float2 corner = float2(
                    (cornerIndex == 2 || cornerIndex == 4 || cornerIndex == 5) ? 1 : 0,
                    (cornerIndex == 1 || cornerIndex == 2 || cornerIndex == 4) ? 1 : -1);
                // Bypass every camera transform: only the payload uses the
                // production object-to-world path, supplied by DrawMeshNow.
                output.positionCS = float4(
                    (originalIndex + corner.x) * (2.0 / _ProbeVertexCount) - 1,
                    corner.y, 0, 1);
                return output;
            }
            float4 ProbeFragment(Varyings input) : SV_Target
            {
                return float4(input.deformedWS, 1);
            }
            ENDHLSL
        }
    }
}
