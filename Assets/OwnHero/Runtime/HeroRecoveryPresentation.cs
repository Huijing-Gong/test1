using UnityEngine;
namespace KingOfGraphic.Hero
{
    // Respawn is presentation, never an input lock. Both hosts discard it once
    // actual movement or a higher-priority action takes ownership; it cannot replay.
    public static class HeroRecoveryPresentation
    {
        public static bool ShouldYield(string id,Vector3 velocity,bool priorityAction)=>
            id=="RESPAWN_RECOVER"&&(priorityAction||velocity.x*velocity.x+velocity.z*velocity.z>.08f*.08f);
    }
}
