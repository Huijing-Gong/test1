using System;
using UnityEngine;

namespace SamProduction
{
    /// <summary>Steady surface-mounted combat fire, independent of punch/ignition bursts.</summary>
    [Serializable]
    public sealed class SamCombatHeatSettings
    {
        public bool enabled=true;
        [Header("Open surface fire tongues - independent of detached sparks")]
        public bool surfaceTonguesEnabled=true;
        [Range(.04f,.28f)] public float surfaceSpread=.19f;
        [Range(.03f,.20f)] public float tongueHalfWidth=.115f;
        [Range(.08f,.65f)] public float tongueHeight=.16f;
        [Range(.02f,.35f)] public float tongueOutwardReach=.055f;
        [Range(.02f,.40f)] public float tongueSideReach=.21f;
        [Range(.5f,2.5f)] public float chestHeightMultiplier=1.7f;
        [Range(.1f,1)] public float tongueOpacity=.66f;
        [Range(0,6)] public float tonguePower=2.9f;
        [Range(4,40)] public float particlesPerSecond=18;
        [Range(.02f,.20f)] public float flameWidth=.055f;
        [Range(.06f,.65f)] public float flameHeight=.15f;
        [Range(.10f,.80f)] public float particleLifetime=.38f;
        [Range(.1f,2f)] public float riseSpeed=.34f;
        [Range(0,.2f)] public float surfaceOffset=.035f;
        [Range(0,1)] public float opacity=.62f;
        [Range(0,1)] public float prewarmSeconds=.38f;
        [Range(0,2)] public float warmLightIntensity=.35f;
        [Range(.1f,2)] public float warmLightRange=.8f;
    }
}
