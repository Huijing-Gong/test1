using System;
using UnityEngine;

namespace SamProduction
{
    public enum SamImpactTextureBreakupStyle { CoarseMovingPieces, FastThinPieces }

    public enum SamImpactTextureVariant { A_CompactThreeLobes, B_BroadFiveLobes }

    /// <summary>World-unit art settings. ApplyPreset affects subsequent samples, never the release origin.</summary>
    [Serializable]
    public sealed class SamImpactTextureSettings
    {
        public bool enabled=true;
        [Header("Semantic-age flame breakup")]
        public SamImpactTextureBreakupStyle breakupStyle=SamImpactTextureBreakupStyle.CoarseMovingPieces;
        [Range(.04f,.22f)] public float breakupAge=.115f;
        [Tooltip("Zero restores the coherent 14C texture; positive values move independent source-UV pieces.")]
        [Range(0,2)] public float breakupStrength=1f;

        public void ApplyBreakupPreset(SamImpactTextureBreakupStyle style)
        {
            breakupStyle=style;breakupAge=.115f;breakupStrength=1f;
            lifetime=.30f; // Retain the working 14C short-shell lifetime.
        }

        [Tooltip("Replace the procedural impact spray while the original texture material is available.")]
        public bool replaceProceduralSpray=true;
        [Range(1,5)] public int lobeCount=3;
        [Min(.02f)] public float lifetime=.43f;
        [Min(.02f)] public float size=1.55f;
        [Range(.1f,1)] public float initialScale=.56f;
        [Min(.01f)] public float growthSeconds=.070f;
        [Range(0,1)] public float opacity=.88f;
        [Range(0,6)] public float emissionPower=2.5f;
        [Range(0,1)] public float satelliteScale=.57f;
        [Range(0,1)] public float satelliteOpacity=.62f;
        [Min(0)] public float satelliteDelay=.022f;
        [Min(0)] public float forwardDrift=.8f;
        [Min(0)] public float radialSpread=.24f;
        [Min(0)] public float forwardStretch=.22f;
        [Range(0,.5f)] public float curl=.11f;
        [Range(0,.5f)] public float surfaceDepth=.16f;
        [Range(0,1)] public float erosionStrength=.62f;
        [Range(0,.03f)] public float uvWarp=.007f;
        [Min(.001f)] public float depthFadeDistance=.12f;
        [Tooltip("Seconds are normalized to lifetime. A thick initial mass decays into separated flame tongues.")]
        public AnimationCurve envelope=new AnimationCurve(new Keyframe(0,.30f),new Keyframe(.07f,.84f),new Keyframe(.23f,1),new Keyframe(.48f,.88f),new Keyframe(.72f,.50f),new Keyframe(.93f,.08f),new Keyframe(1,0));

        public void ApplyPreset(SamImpactTextureVariant variant)
        {
            bool broad=variant==SamImpactTextureVariant.B_BroadFiveLobes;
            enabled=true;replaceProceduralSpray=true;
            lobeCount=broad?5:3;lifetime=.43f;
            size=broad?1.82f:1.55f;initialScale=broad?.52f:.56f;growthSeconds=broad?.085f:.070f;
            opacity=broad?.76f:.88f;emissionPower=broad?2.35f:2.5f;
            satelliteScale=broad?.65f:.57f;satelliteOpacity=broad?.68f:.62f;
            satelliteDelay=broad?.027f:.022f;forwardDrift=broad?1.15f:.8f;
            radialSpread=broad?.38f:.24f;forwardStretch=broad?.38f:.22f;
            curl=broad?.17f:.11f;surfaceDepth=broad?.23f:.16f;
            erosionStrength=broad?.73f:.62f;uvWarp=.007f;depthFadeDistance=.12f;
        }
    }
}
