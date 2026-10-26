using System;
using UnityEngine;
namespace SamProduction.BossArena
{
    // References original clips. Only hit windows and original root-motion tracks are configured.
    public sealed class ArenaPunchLibrary:MonoBehaviour
    {
        [Serializable] public sealed class Contact { public bool left;public float time;public float damage=18; }
        [Serializable] public sealed class Punch
        {
            public ArenaAttack attack;public AnimationClip clip;
            public AnimationCurve rootX,rootZ;
            public float minimumRange=.75f,maximumRange=1.5f;
            public Contact[] contacts;
            public Vector3 Delta(float from,float to)=>new Vector3(rootX.Evaluate(to)-rootX.Evaluate(from),0,rootZ.Evaluate(to)-rootZ.Evaluate(from));
        }
        public Punch[] punches=Array.Empty<Punch>();
        public Punch Find(ArenaAttack attack)=>Array.Find(punches,p=>p.attack==attack);
        public static bool IsPunch(ArenaAttack attack)=>attack>=ArenaAttack.BoxLeft&&attack<=ArenaAttack.BoxHook;
    }
}
