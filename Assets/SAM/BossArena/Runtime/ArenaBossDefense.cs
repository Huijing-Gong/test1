using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    // The shared fighter owns health and strike deduplication. This state only resolves
    // directional guard windows and presents the authored guard/reaction clips.
    public sealed class ArenaBossDefense : MonoBehaviour
    {
        public AnimationClip guardLoop,guardHit,bodyHit;
        public GameObject shieldEffect,blockEffect,hitEffect;
        ArenaBossBrain owner;ArenaSession arena;
        Func<ArenaHit,ArenaDefenseResult> previousResolver;
        float guardAge=-1,hitAge=-1,flinchAge=-1,cooldown;
        int blocks;Visual shield;
        readonly List<Visual> visuals=new List<Visual>();
        sealed class Visual { public GameObject root;public ParticleSystem[] particles;public float age,life;public bool follow; }
        public bool Guarding=>guardAge>=0;
        public bool Reacting=>flinchAge>=0;
        public bool Active=>Guarding||Reacting;
        public int VisualCount=>visuals.Count;
        public bool CanBlock=>Guarding&&guardAge>=.18f&&guardAge<1.1f&&blocks<2;
        public void Initialize(ArenaBossBrain brain)
        {
            if(owner)return;owner=brain;
            previousResolver=owner.fighter.ResolveDefense;owner.fighter.ResolveDefense=Resolve;
            owner.fighter.DefenseResolved+=OnDefense;owner.fighter.Damaged+=OnDamage;
        }
        public bool BeginGuard(ArenaSession session)
        {
            if(!owner||!guardLoop||Active||cooldown>0||owner.Attacking||owner.fighter.Dead||session==null||!session.Active)return false;
            arena=session;var forward=Vector3.ProjectOnPlane(session.player.transform.position-transform.position,Vector3.up);
            if(forward.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(forward);
            guardAge=0;hitAge=-1;blocks=0;cooldown=4.5f;
            arena.Record("boss_guard_started","right_hand_shield",owner.fighter,0,transform.position);return true;
        }
        ArenaDefenseResult Resolve(ArenaHit hit)
        {
            var incoming=hit.source?Vector3.ProjectOnPlane(hit.source.transform.position-transform.position,Vector3.up):Vector3.zero;
            if(CanBlock&&incoming.sqrMagnitude>.001f&&Vector3.Dot(transform.forward,incoming.normalized)>=.45f)
                return new ArenaDefenseResult(ArenaDefenseOutcome.Block,.2f,.15f);
            return previousResolver!=null?previousResolver(hit):ArenaDefenseResult.None;
        }
        void OnDefense(ArenaDefenseEvent value)
        {
            if(!value.result.IsBlock)return;
            blocks++;hitAge=0;
            arena?.Record("boss_guard_block","right_hand_shield",owner.fighter,value.hit.strike,value.hit.point);
        }
        void OnDamage(ArenaHit hit)
        {
            if(owner.fighter.Dead||owner.fighter.LastDefense.IsBlock)return;
            Spawn(hitEffect,hit.point,.6f,.35f,false);
            if(!owner.Attacking){guardAge=-1;ExpireShield();flinchAge=0;}
        }
        public bool Step(float dt,ArenaSession session)
        {
            arena=session;
            if(!session.Active||owner.fighter.Dead){ResetDefense();return false;}
            cooldown=Mathf.Max(0,cooldown-dt);
            if(!Active&&owner.decisionEnabled&&!owner.Attacking&&session.player.AttackTime>=0&&
                Vector3.Distance(transform.position,session.player.transform.position)<3.4f)BeginGuard(session);
            bool ownsPose=Active;
            if(Guarding)
            {
                guardAge+=dt;if(hitAge>=0)hitAge+=dt;
                bool recoil=guardHit&&hitAge>=0&&hitAge<guardHit.length;
                owner.animationDriver.Pose(recoil?guardHit:guardLoop,recoil?hitAge:guardAge,dt,!recoil,.09f);
                ArenaArmContact.Constrain(owner.animationDriver.GetComponent<Animator>(),session.player.capsule);
                if(guardAge>=1.25f){guardAge=-1;ExpireShield();}
            }
            else if(Reacting)
            {
                flinchAge+=dt;if(bodyHit)owner.animationDriver.Pose(bodyHit,flinchAge,dt,false,.06f);
                if(!bodyHit||flinchAge>=Mathf.Min(.5f,bodyHit.length))flinchAge=-1;
            }
            for(int i=visuals.Count-1;i>=0;i--)
            {
                var v=visuals[i];v.age+=dt;
                if(!v.root||v.age>=v.life){if(v.root){v.root.SetActive(false);Destroy(v.root);}visuals.RemoveAt(i);continue;}
                if(v.follow)v.root.transform.SetPositionAndRotation(owner.Socket("socket_hand.R").position+transform.forward*.12f,transform.rotation);
                foreach(var ps in v.particles)if(ps&&ps.gameObject.activeInHierarchy){ps.Simulate(dt,false,false,true);ps.Pause(false);}
            }
            return ownsPose;
        }
        Visual Spawn(GameObject prefab,Vector3 point,float scale,float life,bool follow)
        {
            if(!prefab)return null;
            var go=Instantiate(prefab,point,transform.rotation);go.name="Boss defense FX: "+prefab.name;go.transform.localScale*=scale;
            var v=new Visual{root=go,particles=go.GetComponentsInChildren<ParticleSystem>(true),life=life,follow=follow};
            foreach(var ps in v.particles){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true);ps.Pause(false);}
            visuals.Add(v);return v;
        }
        void ExpireShield(){if(shield!=null)shield.life=shield.age;shield=null;}
        public void ResetDefense()
        {
            guardAge=hitAge=flinchAge=-1;cooldown=0;blocks=0;shield=null;
            foreach(var v in visuals)if(v.root){v.root.SetActive(false);Destroy(v.root);}visuals.Clear();
        }
        void OnDisable(){ResetDefense();}
        void OnDestroy()
        {
            if(!owner)return;
            owner.fighter.Damaged-=OnDamage;owner.fighter.DefenseResolved-=OnDefense;
            if(owner.fighter.ResolveDefense==Resolve)owner.fighter.ResolveDefense=previousResolver;
        }
    }
}
