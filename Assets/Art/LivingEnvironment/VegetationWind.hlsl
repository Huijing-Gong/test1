#ifndef FOLDED_REALM_VEGETATION_WIND_INCLUDED
#define FOLDED_REALM_VEGETATION_WIND_INCLUDED

// Runtime owns these globals. Time is scaled game time, so menu pauses freeze
// plants, shadows and the interaction trail at precisely the same instant.
float4 _FRWindDirection; // xy = world x/z direction, z = strength, w = enabled
float _FRWindClock;
float4 _FRWindPlayer;    // xyz = foot position, w = enabled
float4 _FRWindTrail[6];  // xyz = previous foot position, w = fading influence
float4 _FRWindPlayerDirection; // xy = horizontal passage direction
float4 _FRWindTrailDirection[6];
float4 _FRWindCamera;   // xyz = camera position, w = detail cutoff distance
float _FRWindInteractionRadius;

float2 FRPlantInteraction(float3 rootWS, float3 footWS, float influence,
    float2 motion, float2 fallbackDirection)
{
    float2 delta = rootWS.xz - footWS.xz;
    float radius = _FRWindInteractionRadius > 0.01 ? _FRWindInteractionRadius : 0.72;
    // Most plants are far from the Hero; reject using squared distance before
    // square roots/smoothsteps, especially for the meadow's instanced draws.
    if (influence <= 0.001 || dot(delta, delta) >= radius * radius ||
        abs(rootWS.y - footWS.y) >= 1.5) return 0;
    float distanceToFoot = length(delta);
    float range = saturate(1.0 - distanceToFoot / max(radius, 0.1));
    range = range * range * (3.0 - 2.0 * range);
    float vertical = 1.0 - smoothstep(0.65, 1.5, abs(rootWS.y - footWS.y));
    float2 away = distanceToFoot > 0.025 ? delta / distanceToFoot : fallbackDirection;
    // Sweep in the direction of passage, with a small sideways deflection. A
    // radial field alone opens every flower into an artificial circular halo.
    float2 sweep = dot(motion, motion) > .01 ? motion * .85 + away * .25 : away;
    sweep *= rsqrt(max(dot(sweep, sweep), .0001));
    return sweep * range * vertical * saturate(influence);
}

float3 FRRotatePlant(float3 v, float3 axis, float angle)
{
    float s, c; sincos(angle, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1 - c);
}

// All four render passes call this same world-space deformation. UV2 contains
// per-plant root/weight even when many plants share one combined source mesh.
// UV3 is legacy height/foliage/phase for trees; coherent plants store a constant
// object-space botanical stem-span vector and a version flag in W.
void FRDeformVegetation(float3 positionOS, float3 normalOS,
    float4 rootAndWeight, float4 plantData,
    out float3 positionWS, out float3 normalWS)
{
    positionWS = TransformObjectToWorld(positionOS);
    normalWS = TransformObjectToWorldNormal(normalOS);
    if (dot(normalWS, normalWS) < 0.001) normalWS = float3(0, 1, 0);

    float weight = saturate(rootAndWeight.w);
    if (_FRWindDirection.w < 0.5 || _VertexDataValid < 0.5 || weight <= 0.0)
        return;

    float3 rootWS = TransformObjectToWorld(rootAndWeight.xyz);
    float coherent = step(.5, plantData.w);
    // Recover actual world-space stem span from its baked anchor and weight.
    // This includes instance TRS/FBX rotations exactly once (meshToRoot may
    // already contain import scaling); local Y is not always botanical up.
    float height = coherent > .5 ?
        max(length(TransformObjectToWorld(rootAndWeight.xyz + plantData.xyz) - rootWS), .12) :
        max(abs(positionWS.y-rootWS.y)/max(weight,0.001),0.12);
    float foliage = coherent > .5 ? 1 : saturate(plantData.y);
    float phase = coherent > .5 ? frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.283185 : plantData.z;
    float2 wind = _FRWindDirection.xy;
    wind *= rsqrt(max(dot(wind, wind), 0.0001));
    float2 crossWind = float2(-wind.y, wind.x);
    float strength = max(0.0, _FRWindDirection.z);
    float clock = _FRWindClock;

    // The moving wave fronts are shared by the whole landscape. A small local
    // component prevents clones from marching in lockstep without losing gusts.
    float travelling = dot(rootWS.xz, wind) * 0.19 - clock * 0.72;
    float gust = 0.65 + 0.21 * sin(travelling) +
        0.14 * sin(travelling * 0.53 + clock * 0.11 + 1.7);
    float sway = sin(clock * 1.18 + phase) * 0.20 +
        sin(clock * 0.57 + phase * 1.71) * 0.12;
    float2 mainBend = wind * (0.37 + gust * 0.63 + sway) +
        crossWind * sin(clock * 0.86 + phase * 1.23) * 0.17;
    // Above the flexible stem the entire flower head shares its displacement.
    // Coherent metadata is explicitly versioned; trees keep their original wind.
    float bendWeight = lerp(weight * weight, smoothstep(0, .62, weight), coherent);
    float2 horizontal = mainBend * (_WindAmplitude * height * strength) * bendWeight;

    float cameraRange = max(_FRWindCamera.w, 1.0);
    float detailFade = 1.0 - smoothstep(cameraRange * 0.60, cameraRange,
        distance(positionWS, _FRWindCamera.xyz));
    float detailPhase = clock * 3.4 + phase * 2.1 + dot(positionWS.xz, float2(1.13, 1.79));
    float2 detail = wind * sin(detailPhase) + crossWind * sin(detailPhase * 1.37 + 0.9) * 0.55;
    horizontal += detail * (_WindDetail * strength * detailFade) * weight * foliage;

    float2 interaction = 0;
    if (_InteractionStrength > 0.001 && coherent > .5)
    {
        float2 fallback = float2(cos(phase), sin(phase));
        interaction = FRPlantInteraction(rootWS, _FRWindPlayer.xyz,
            _FRWindPlayer.w, _FRWindPlayerDirection.xy, fallback);
        // Choose the strongest trail contribution; summing six samples would
        // overbend plants at low frame rates or when the player stops walking.
        [unroll] for (int sampleIndex = 0; sampleIndex < 6; ++sampleIndex)
        {
            float2 candidate = FRPlantInteraction(rootWS, _FRWindTrail[sampleIndex].xyz,
                _FRWindTrail[sampleIndex].w, _FRWindTrailDirection[sampleIndex].xy, fallback);
            if (dot(candidate, candidate) > dot(interaction, interaction))
                interaction = candidate;
        }
    }

    // A conservative displacement bound is shared with the installer's expanded
    // renderer bounds. The collision geometry and NavMesh never move.
    float maxBend = height > 2.4 ? 0.78 : 0.63;
    float horizontalLength = length(horizontal);
    horizontal *= min(1.0, maxBend / max(horizontalLength, 0.00001));
    float sag = -min(dot(horizontal, horizontal) / max(height * 1.8, 0.25), 0.12) * weight;
    positionWS += float3(horizontal.x, sag, horizontal.y);

    // Approximate the inverse-transpose of the stem bend, keeping lighting on
    // the bent leaves (and shadow bias) coherent without extra vertex samples.
    float2 bendSlope = horizontal * (2.0 / max(height * max(weight, 0.1), 0.12));
    float stemSign=positionWS.y>=rootWS.y?1.0:-1.0;
    normalWS.y -= dot(normalWS.xz, bendSlope)*stemSign;
    normalWS = SafeNormalize(normalWS);

    // Contact is a bounded rotation about ONE botanical root, not a displacement
    // calculated from each petal's own XZ. The upper flower is rigid: all its
    // vertices share axis/angle, preserving petal lengths and corolla diameter.
    float contact = length(interaction);
    if (contact > .0001)
    {
        float2 tilt = interaction / contact;
        float3 axis = float3(tilt.y, 0, -tilt.x);
        float angle = min(.22, .14 / max(height, .2)) * saturate(contact * _InteractionStrength) * bendWeight;
        positionWS = rootWS + FRRotatePlant(positionWS - rootWS, axis, angle);
        normalWS = SafeNormalize(FRRotatePlant(normalWS, axis, angle));
    }
}

#endif
