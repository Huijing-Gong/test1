using UnityEngine;

namespace SamProduction.BossArena
{
    // Reject blocked takeoffs before acquiring animation/camera ownership.
    // This checks the actual authored ascent and collision capsule. Retargeted
    // flight still uses CharacterController collision during execution.
    public static class ArenaAerialClearance
    {
        private static readonly Collider[] overlaps = new Collider[32];
        public static bool TakeoffClear(ArenaBossBrain owner, ArenaMotionClip motion,
            float from, float until, bool inverted)
        {
            var capsule = owner.capsule;
            var origin = owner.transform.position;
            var heading = owner.transform.rotation;
            float radius = Mathf.Max(.03f, capsule.radius-capsule.skinWidth*.5f);
            float half = Mathf.Max(0, capsule.height*.5f-capsule.radius);
            int samples = Mathf.CeilToInt((until-from)*120);
            for (int i=0;i<=samples;i++)
            {
                float t=Mathf.Lerp(from,until,i/(float)Mathf.Max(1,samples));
                motion.Sample(t,out var offset,out _);
                Vector3 localCentre=inverted
                    ? Quaternion.AngleAxis(ArenaMeteorMotor.Pitch(t),Vector3.right)*capsule.center
                    : capsule.center;
                Vector3 centre=origin+heading*(offset+localCentre);
                int count=Physics.OverlapCapsuleNonAlloc(centre+Vector3.up*half,
                    centre-Vector3.up*half,radius,overlaps,1<<8,QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length)return false;
                for(int n=0;n<count;n++)
                {
                    // The arena's existing level floor is legitimate support.
                    // Walls, raised platforms and ceilings are not ignored.
                    if(overlaps[n].bounds.max.y>origin.y+.05f)return false;
                }
            }
            return true;
        }
    }
}
