using System.Collections.Generic;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    public enum HeroFocusPhase { None, Approach, Ready, Striking, Return }

    // Both combat hosts use this sequence and their existing collision/damage owners.
    public sealed class HeroFocusSequence
    {
        public const float EnemyScale = .16f, MaximumSeconds = 6.5f;
        public const int MaximumStrikes = 6;
        public HeroFocusPhase Phase { get; private set; }
        public bool Active => Phase != HeroFocusPhase.None;
        public bool Travelling => Phase == HeroFocusPhase.Approach || Phase == HeroFocusPhase.Return;
        public bool Prompt => Phase == HeroFocusPhase.Ready || Phase == HeroFocusPhase.Striking;
        public Vector3 Origin { get; private set; }
        public float Age { get; private set; }
        public float PhaseAge { get; private set; }
        public int Strikes { get; private set; }
        readonly List<Vector3> path = new List<Vector3>(128);
        int returnIndex; long lastStrike = -1; float lastInput;

        public void Begin(Vector3 position)
        {
            Origin = position; Age = PhaseAge = lastInput = 0; Strikes = 0; lastStrike = -1;
            path.Clear(); path.Add(position); SetPhase(HeroFocusPhase.Approach);
        }
        public void Advance(float delta, Vector3 position)
        {
            if (!Active) return;
            Age += Mathf.Max(0, delta); PhaseAge += Mathf.Max(0, delta);
            if (Phase != HeroFocusPhase.Return && (position-path[path.Count-1]).sqrMagnitude>.0064f)
                path.Add(position);
        }
        void SetPhase(HeroFocusPhase phase) { Phase = phase; PhaseAge = 0; }
        public void Arrive() { SetPhase(HeroFocusPhase.Ready); lastInput = Age; }
        public void ObserveStrike(bool attacking, long strike)
        {
            if (!Prompt || !attacking || strike == lastStrike) return;
            lastStrike = strike; Strikes++; if (Phase == HeroFocusPhase.Ready) SetPhase(HeroFocusPhase.Striking);
        }
        public bool AttackInput(bool held, bool pressed)
        {
            if (!Prompt || Strikes >= MaximumStrikes) return false;
            if (held || pressed) { lastInput = Age; return true; }
            return false;
        }
        public bool ShouldReturn(bool targetAlive, bool attacking) => Active && Phase != HeroFocusPhase.Return &&
            (!targetAlive || Age >= MaximumSeconds-.9f ||
             Phase == HeroFocusPhase.Approach && PhaseAge > .7f ||
             Phase == HeroFocusPhase.Ready && PhaseAge > 1.4f ||
             Phase == HeroFocusPhase.Striking && !attacking && (Strikes >= MaximumStrikes || Age-lastInput > .4f));
        public void Return(Vector3 position)
        {
            if (!Active || Phase == HeroFocusPhase.Return) return;
            if ((position-path[path.Count-1]).sqrMagnitude>.0001f) path.Add(position);
            returnIndex = Mathf.Max(0,path.Count-2); SetPhase(HeroFocusPhase.Return);
        }
        public Vector3 ReturnStep(Vector3 position, float delta)
        {
            while (returnIndex>0 && Vector3.ProjectOnPlane(path[returnIndex]-position,Vector3.up).sqrMagnitude<.0064f) returnIndex--;
            Vector3 to=Vector3.ProjectOnPlane(path[returnIndex]-position,Vector3.up);
            return Vector3.ClampMagnitude(to,32*Mathf.Max(0,delta));
        }
        public bool Returned(Vector3 position) => Phase == HeroFocusPhase.Return && returnIndex == 0 &&
            Vector3.ProjectOnPlane(Origin-position,Vector3.up).sqrMagnitude <= .0144f;
        public void Cancel() { Phase = HeroFocusPhase.None; path.Clear(); }
    }

    public static class HeroCombatTravel
    {
        static readonly RaycastHit[] ground = new RaycastHit[32], sight = new RaycastHit[64];
        public static bool ClearSight(Transform player, Transform enemy, Vector3 from, Vector3 to)
        {
            Vector3 offset=to-from;float length=offset.magnitude;if(length<.001f)return true;
            int count=Physics.RaycastNonAlloc(from,offset/length,sight,length,~0,QueryTriggerInteraction.Ignore);
            if(count==sight.Length)return false;
            for(int i=0;i<count;i++)if(!sight[i].transform.IsChildOf(player)&&(!enemy||!sight[i].transform.IsChildOf(enemy)))return false;
            return true;
        }
        public static bool SupportedStep(CharacterController capsule, Vector3 step, Transform enemy)
        {
            Vector3 next=capsule.transform.position+step;
            int count=Physics.RaycastNonAlloc(next+Vector3.up*.65f,Vector3.down,ground,1.65f,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)if(!ground[i].transform.IsChildOf(capsule.transform)&&
                (!enemy||!ground[i].transform.IsChildOf(enemy))&&ground[i].normal.y>.55f)return true;
            return false;
        }
        public static void Face(Transform actor, Vector3 point, float delta, bool immediate=false)
        {
            Vector3 toward=Vector3.ProjectOnPlane(point-actor.position,Vector3.up);
            if(toward.sqrMagnitude>.0001f)actor.rotation=Quaternion.RotateTowards(actor.rotation,Quaternion.LookRotation(toward),immediate?360:1440*Mathf.Max(0,delta));
        }
    }
}
