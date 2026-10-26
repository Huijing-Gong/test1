using UnityEngine;

namespace SamProduction
{
    /// <summary>Candidate09 A: restrained vertical fan. Explicit local-space art controls; no camera or reference input.</summary>
    internal static class SamImpactCoverageProfile
    {
        internal const float VolumeVertical=1.42f, VolumeAxial=.94f, VolumeDensity=.94f;
        internal const float Reach=1.0f, Width=1.0f, SprayPower=1.16f, SprayOpacity=1.0f;
        internal const float LightRange=4.2f, LightPower=7.0f;
        internal static float LightEnvelope(float age)
        {
            if(age<0 || age>=.34f)return 0;
            float rise=Mathf.Lerp(.72f,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.055f)));
            float tail=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((age-.115f)/.225f));
            return rise*tail;
        }
    }
}
