Shader "PixelShield/TransparentBlocks"
{
    Properties
    {
        // Material properties control the individual layers of the effect.
        [HDR]_ShieldColor ("Shield Tint", Color) = (0.025, 0.38, 0.52, 1)
        [HDR]_EdgeColor ("Light Color", Color) = (0.22, 0.92, 1.15, 1)
        _Alpha ("Surface Opacity", Range(0, 1)) = 0.18
        _MaxAlpha ("Maximum Surface Opacity", Range(0, 1)) = 0.48
        _PixelColumns ("Pixel Columns", Range(8, 128)) = 64
        _PixelRows ("Pixel Rows", Range(4, 48)) = 10
        _LineWidth ("Grid Line Width", Range(0.002, 0.08)) = 0.012
        _GridStrength ("Stationary Grid Brightness", Range(0, 1)) = 0.012
        _BlockFill ("Pixel Density", Range(0, 1)) = 0.48
        _BlockStrength ("Pixel Brightness", Range(0, 3)) = 1.25
        _WaveSpeed ("Pixel Wave Cycles Per Second", Range(0, 2)) = 0.28
        _WaveStrength ("Pixel Wave Strength", Range(0, 1)) = 1
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.5
        _FresnelStrength ("Fresnel Strength", Range(0, 3)) = 0.32
        _OrbitFrequency ("Pixel Cluster Count", Range(1, 8)) = 3
        _OrbitSpeed ("Y Axis Revolutions Per Second", Range(-0.2, 0.2)) = 0.035
        _RimWidth ("Top And Bottom Rim Width", Range(0.001, 0.04)) = 0.006
        _RimStrength ("Rim Brightness", Range(0, 5)) = 2.2
        _BackStrength ("Rear Surface Visibility", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        HLSLINCLUDE
        #pragma target 3.5
        // URP supplies the transform, camera and time utilities used by this shader.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ShieldColor;
            float4 _EdgeColor;
            float _Alpha, _MaxAlpha;
            float _PixelColumns, _PixelRows, _LineWidth, _GridStrength;
            float _BlockFill, _BlockStrength;
            float _WaveSpeed, _WaveStrength;
            float _FresnelPower, _FresnelStrength;
            float _OrbitFrequency, _OrbitSpeed;
            float _RimWidth, _RimStrength, _BackStrength;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = p.positionCS;
            output.positionWS = p.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            return output;
        }
        float hash21(float2 p)
        {
            // Hash cell coordinates to produce stable per-cell variation across frames.
            float3 q = frac(float3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }
        half4 ShadeShield(Varyings input, float visibility)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            const float tau = 6.2831853;
            float2 count = max(round(float2(_PixelColumns, _PixelRows)), 1.0);
            float2 uv = input.uv;
            // Compute derivatives before frac to avoid widening at cell boundaries and the cylinder seam.
            float2 grid = uv * count;
            float2 gridAA = max(fwidth(grid), 0.0001);
            float2 gridDistance = min(frac(grid), 1.0 - frac(grid));
            float2 lines = 1.0 - smoothstep(_LineWidth, _LineWidth + gridAA, gridDistance);
            float gridLine = max(lines.x, lines.y);
            // Orbit only the pixel layer; keep the grid and boundary rings stationary.
            float2 movingGrid = float2(uv.x - _Time.y * _OrbitSpeed, uv.y) * count;
            float2 cell = floor(movingGrid);
            cell.x = cell.x - floor(cell.x / count.x) * count.x;
            cell.y = clamp(cell.y, 0.0, count.y - 1.0);
            float2 local = frac(movingGrid);
            float2 aa = max(fwidth(movingGrid), 0.0001);
            float2 inset = smoothstep(0.045, 0.045 + aa, min(local, 1.0 - local));
            float tile = inset.x * inset.y;
            float2 center = (cell + 0.5) / count;
            float rnd = hash21(cell);
            // Integer cluster frequencies maintain continuity around the cylinder seam.
            float angle = center.x * tau;
            float clusters = round(_OrbitFrequency);
            float waveTime = _Time.y * _WaveSpeed * tau;
            float field = 0.5 + 0.27 * sin(angle * clusters + sin(center.y * 5.0 + waveTime * 0.31 * _WaveStrength) * 1.8)
                              + 0.16 * cos(angle * (clusters + 1.0) - center.y * 8.0 + waveTime * 0.43 * _WaveStrength);
            float cluster = smoothstep(0.43, 0.76, field);
            // Combine a travelling wave with per-cell phase variation to control opacity, emission and selection.
            float wave = 0.5 + 0.5 * sin(waveTime - angle * clusters + center.y * 4.0 + rnd * 2.4);
            float softWave = smoothstep(0.08, 0.92, wave);
            float activity = lerp(rnd, rnd * 0.3 + softWave * 0.7, _WaveStrength);
            float selected = smoothstep(0.88 - _BlockFill, 1.12 - _BlockFill, activity);
            selected *= smoothstep(0.0, 0.04, _BlockFill);
            float breathing = lerp(1.0, 0.06 + 0.94 * softWave, _WaveStrength);
            float block = tile * selected * (0.08 + cluster * 0.92) * breathing;
            float highlight = smoothstep(0.82, 0.99, rnd) * cluster * tile * breathing * selected;
            highlight *= smoothstep(0.0, 0.15, _BlockFill);
            float edgeDistance = min(uv.y, 1.0 - uv.y);
            float edgeAA = max(fwidth(uv.y), 0.0001);
            // UV heights near zero and one define the lower and upper boundary rings.
            float rim = 1.0 - smoothstep(_RimWidth, _RimWidth + edgeAA, edgeDistance);
            float rimHalo = exp2(-edgeDistance / max(_RimWidth * 2.8, 0.0001));
            float bodyFade = smoothstep(0.005, 0.07, edgeDistance);
            float3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
            float facing = abs(dot(normalize(input.normalWS), view));
            // Approximate Fresnel rim lighting by brightening grazing viewing angles.
            float fresnel = pow(1.0 - saturate(facing), _FresnelPower) * _FresnelStrength;
            // Reduce pixel brightness at the silhouette where perspective compresses the cells.
            float pixelLight = (block + highlight * 0.5) * _BlockStrength
                               * bodyFade * smoothstep(0.06, 0.36, facing);
            float gridLight = gridLine * _GridStrength * bodyFade;
            float alpha = min(_Alpha * (0.24 + block * 0.65 + fresnel * 0.4)
                              + rim * 0.26, _MaxAlpha) * visibility;
            // Premultiplied blending separates emission intensity from surface opacity.
            float3 emission = _EdgeColor.rgb * (pixelLight * 0.65 + gridLight
                              + fresnel * 0.28 + (rim + rimHalo * 0.22) * _RimStrength);
            emission += float3(0.55, 0.7, 0.7) * highlight * bodyFade * _BlockStrength * 0.22;
            return half4(_ShieldColor.rgb * alpha + emission * visibility, alpha);
        }
        half4 fragRear(Varyings input) : SV_Target { return ShadeShield(input, _BackStrength); }
        half4 fragFront(Varyings input) : SV_Target { return ShadeShield(input, 1.0); }
        ENDHLSL
        // Configure a faint rear pass followed by the front surface.
        Pass
        {
            Name "ShieldRear"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragRear
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ShieldFront"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFront
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
