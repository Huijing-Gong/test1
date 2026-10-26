using System;
using UnityEngine;
namespace SamProduction.BossArena
{
    // Candidate: conservative controller movement; inspect imported root baking and calibrate in Unity.
    // No Update/OnAnimatorMove: ArenaSession -> ArenaBossBrain.Tick is the single clock.
    [RequireComponent(typeof(ArenaEclipseClips))]
    public sealed class ArenaEclipseBossMotor : MonoBehaviour
    {
        public ArenaEclipseClips clips;
        public ArenaBossSpells spells;
        public ArenaBossDefense defense;
        public AnimationClip meteorRaiseClip;
        public AnimationClip groundCastClip;
        public float groundCastContactTime=.441667f;
        public AnimationClip phaseRaiseClip,meteorSlamClip;
        public ArenaPunchLibrary punches;
        ArenaPunchLibrary.Punch ActivePunch=>punches?punches.Find(CurrentAttack):null;
        public float walkSpeed=1.5f,runSpeed=2.8f,walkStride=3f,runStride=3.5f,turnRate=90f;
        public float stopRange=1.25f,punchRadius=.4f,punchDamage=18f,ringDamage=28f;
        public float stompRadius=3.25f,diveRadius=3f,meteorRadius=19f;
        public Collider meteorFloor;
        public float meteorChargeSeconds=6f,meteorFlightHeight=8f,meteorCoreRadius=22.4f;
        Vector3 flightOrigin;
        bool meteorGroundValid;
        const float SourceMeteorRelease=3.041667f;
        public float MeteorReleaseTime=>Mathf.Max(4f,meteorChargeSeconds);
        public float SourceAnimationTime=>CurrentAttack==ArenaAttack.Meteor?MeteorSourceTime(time):time;
        float AttackDuration=>CurrentAttack==ArenaAttack.Stomp&&groundCastClip?groundCastClip.length:ActivePunch!=null?ActivePunch.clip.length:CurrentAttack==ArenaAttack.Ignite&&phaseRaiseClip?2.8f:ArenaSpellRules.IsSpell(CurrentAttack)&&spells!=null?spells.Duration:clips.Attack(CurrentAttack).length+(CurrentAttack==ArenaAttack.Meteor?MeteorReleaseTime-SourceMeteorRelease:0);
        [Tooltip("Reserved serialized compatibility field; ignored. Vertical source body pose is preserved, no controller hop.")]
        public float hopHeight=0f;
        public Vector3 rightHandOffset,leftHandOffset,rightFootOffset,leftFootOffset;
        [Tooltip("Local sole calibration relative to right-foot anchor; verify against rendered boot underside.")]
        public Vector3 rightSoleOffset;
        public float maximumSoleGap=.06f,maximumSolePenetration=.015f,minimumGroundNormalY=.9f;
        static readonly ArenaAttack[] PhaseOnePool={ArenaAttack.BoxLeft,ArenaAttack.BoxRight,ArenaAttack.BoxJab,ArenaAttack.BoxCross,ArenaAttack.BoxStepLeft,ArenaAttack.BoxStepRight,ArenaAttack.BoxHook,ArenaAttack.BoxHeavy,ArenaAttack.Stomp};
        static readonly ArenaAttack[] PhaseTwoPool={ArenaAttack.BoxHeavy,ArenaAttack.BoxHook,ArenaAttack.BoxStepLeft,ArenaAttack.BoxStepRight,ArenaAttack.BoxLeft,ArenaAttack.BoxRight,ArenaAttack.BoxJab,ArenaAttack.BoxCross,ArenaAttack.DiveKick,ArenaAttack.Meteor,ArenaAttack.Stomp};
        bool impactCentreFrozen;
        bool pulseActive;
        float pulseAge,pulseMaximum,pulseDamage;
        long pulseStrike;
        string pulseSkill;
        ArenaBossBrain owner; ArenaAnimation animation; CharacterController cc; ArenaSession arena;
        Animator humanoid;
        Transform rightHand,leftHand,rightFoot,leftFoot,chest;
        float time,idle,death,cooldown=.65f,vertical=-2f,walkClock,hopOffset;
        float approachSpeed;
        bool runningApproach;
        int cursor, blackSunToken; bool dead,phasePending; readonly long[] strikes=new long[3];
        public bool Attacking{get;private set;}
        public bool DeathComplete=>dead&&death>=clips.J026.length;
        public ArenaAttack CurrentAttack{get;private set;}
        public int CombatPhase{get;private set;}=1;
        public int ComboHitIndex{get;private set;}=-1;
        public float ActualSpeed{get;private set;}
        public bool Blocked{get;private set;}
        public float SkillTime=>time;
        public float SkillProgress=>Attacking?Mathf.Clamp01(time/AttackDuration):0;
        public float WalkPhase=>Mathf.Repeat(walkClock,1);
        public float LocomotionTime=>walkClock;
        public bool IgnitionPending=>phasePending;
        public Vector3 GroundCentre{get;private set;}
        public float RecoveryTime=>CurrentAttack==ArenaAttack.Stomp&&groundCastClip?groundCastContactTime+.36f:ActivePunch!=null?ActivePunch.contacts[ActivePunch.contacts.Length-1].time+.09f:ArenaSpellRules.IsSpell(CurrentAttack)&&spells!=null?spells.ReleaseTime:CurrentAttack==ArenaAttack.Meteor?MeteorReleaseTime+.36f:End(CurrentAttack,Count(CurrentAttack)-1)+(Radial(CurrentAttack)?.36f:0);
        public bool Telegraph=>CurrentAttack==ArenaAttack.Stomp&&groundCastClip?Attacking&&time<groundCastContactTime:ActivePunch!=null?Attacking&&time<ActivePunch.contacts[0].time-.04f:Attacking&&CurrentAttack!=ArenaAttack.Ignite&&time<(ArenaSpellRules.IsSpell(CurrentAttack)&&spells!=null?spells.ReleaseTime:CurrentAttack==ArenaAttack.Meteor?MeteorReleaseTime:Start(CurrentAttack,0));
        public bool IsRecovering=>Attacking&&time>=RecoveryTime;
        public string ActionName=>dead?"DISABLED":Attacking?"ECLIPSE "+CurrentAttack:"ECLIPSE SENTINEL";
        public string StateName=>dead?"DOWN":Attacking?Telegraph?"WINDUP":IsRecovering?"RECOVERY":"ATTACK":Blocked?"PATH BLOCKED":ActualSpeed>.05f?"APPROACHING":"WATCHING";
        public void Initialize(ArenaBossBrain brain)
        {
            if(owner!=null){if(owner!=brain)throw new InvalidOperationException("V13 owner changed");return;}
            owner=brain;cc=brain.capsule;animation=brain.animationDriver;clips=clips!=null?clips:GetComponent<ArenaEclipseClips>();
            if(cc==null||animation==null||brain.fighter==null||clips==null)throw new InvalidOperationException("V13 bindings incomplete");
            clips.Validate();var animator=animation.GetComponent<Animator>();
            if(animator==null||!animator.isHuman||animator.avatar==null||!animator.avatar.isValid)throw new InvalidOperationException("V13 requires verified Humanoid Avatar");
            humanoid=animator;
            punches=GetComponent<ArenaPunchLibrary>();
            spells=GetComponent<ArenaBossSpells>();spells?.Initialize(brain);
            defense=GetComponent<ArenaBossDefense>();defense?.Initialize(brain);
            animator.applyRootMotion=false;animator.runtimeAnimatorController=null;
            rightHand=Anchor(animator,HumanBodyBones.RightHand,"V13_hand_R",rightHandOffset);
            leftHand=Anchor(animator,HumanBodyBones.LeftHand,"V13_hand_L",leftHandOffset);
            rightFoot=Anchor(animator,HumanBodyBones.RightFoot,"V13_foot_R",rightFootOffset);
            leftFoot=Anchor(animator,HumanBodyBones.LeftFoot,"V13_foot_L",leftFootOffset);
            chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            if(chest==null)throw new InvalidOperationException("V13 chest mapping missing");
            animation.guard=clips.J002;animation.walk=clips.WALK;animation.walkStride=walkStride;
        }
        static Transform Anchor(Animator animator,HumanBodyBones bone,string name,Vector3 offset)
        {
            var p=animator.GetBoneTransform(bone);if(p==null)throw new InvalidOperationException("V13 missing bone "+bone);
            var t=new GameObject(name).transform;t.SetParent(p,false);t.localPosition=offset;return t;
        }
        public Transform Socket(string name)
        {
            switch(name){case "socket_hand.R":return rightHand;case "socket_hand.L":return leftHand;
                case "socket_foot.R":return rightFoot;case "socket_foot.L":return leftFoot;case "socket_chest":return chest;
                default:throw new InvalidOperationException("V13 unknown socket "+name);}
        }
        public void ResetMotor(Vector3 p,Quaternion q)
        {
            defense?.ResetDefense();
            spells?.ResetSpells();animation.ClearUpperBody();
            bool enabled=cc.enabled;cc.enabled=false;owner.transform.SetPositionAndRotation(p,q);cc.enabled=enabled;
            if(enabled){Physics.SyncTransforms();cc.Move(Vector3.down*.25f);}
            Attacking=dead=phasePending=impactCentreFrozen=pulseActive=false;CombatPhase=1;ComboHitIndex=-1;time=idle=death=walkClock=hopOffset=pulseAge=0;
            cooldown=.65f;vertical=-2;ActualSpeed=0;Blocked=false;cursor=0;Array.Clear(strikes,0,strikes.Length);
            approachSpeed=0;runningApproach=false;
            animation.ResetPose();arena?.effects?.HideWarning();arena?.effects?.ClearBlackSun();blackSunToken=0;
        }
        public void CancelAttack()
        {
            defense?.ResetDefense();
            spells?.ResetSpells();animation.ClearUpperBody();
            Attacking=pulseActive=false;time=0;cooldown=.5f;arena?.effects?.HideWarning();arena?.effects?.ClearBlackSun();blackSunToken=0;
            // Death/outcome is still resolved only by ArenaSession/Fighter.
            if(owner.fighter.Dead){dead=true;death=0;}
        }
        public bool ForceAttack(ArenaAttack attack,ArenaSession session=null)
        {
            if(attack==ArenaAttack.DashPunch)return false;
            if(session!=null)arena=session;
            if(attack==ArenaAttack.Ignite)attack=ArenaAttack.Meteor;
            if(attack==ArenaAttack.Jab)attack=ArenaAttack.BoxJab;
            if(attack==ArenaAttack.ComboTwo)attack=ArenaAttack.BoxLeft;
            if(attack==ArenaAttack.ComboThree)attack=ArenaAttack.BoxHeavy;
            if(arena==null||!arena.Active||owner.fighter.Dead||Attacking||(defense!=null&&defense.Active))return false;
            if(ArenaPunchLibrary.IsPunch(attack))
            {
                var punch=punches?punches.Find(attack):null;if(punch==null)return false;
                CurrentAttack=attack;Attacking=true;time=0;ComboHitIndex=-1;ActualSpeed=0;Array.Clear(strikes,0,strikes.Length);
                animation.PoseContactClip(punch.clip,0,0);
                arena.effects?.Charge(punch.contacts[0].left?leftHand:rightHand);
                arena.Record("boss_attack_started",attack.ToString(),owner.fighter,0,owner.transform.position);return true;
            }
            if(ArenaSpellRules.IsSpell(attack))
            {
                if(spells==null||!spells.Begin(attack,arena))return false;
                CurrentAttack=attack;Attacking=true;time=0;ComboHitIndex=-1;ActualSpeed=0;
                arena.Record("boss_attack_started",attack.ToString(),owner.fighter,0,owner.transform.position);
                return true;
            }
            if(attack==ArenaAttack.Meteor&&!TryMeteorGround(out _)){arena.Record("boss_attack_invalid_ground","Meteor",owner.fighter,0,Vector3.zero);return false;}
            CurrentAttack=attack;Attacking=true;impactCentreFrozen=pulseActive=false;time=0;ComboHitIndex=-1;ActualSpeed=0;Array.Clear(strikes,0,strikes.Length);
            GroundCentre=Vector3.zero;animation.PoseContactClip(attack==ArenaAttack.Stomp&&groundCastClip?groundCastClip:clips.Attack(attack),0,0);
            if(TryGround(owner.transform.position,out var initialGround))GroundCentre=initialGround;
            if(attack==ArenaAttack.Meteor)
            {
                meteorGroundValid=TryMeteorGround(out var centre);GroundCentre=centre;
                flightOrigin=owner.transform.position;vertical=0;
                arena.effects?.blackSun?.Configure(MeteorReleaseTime,meteorCoreRadius,owner.transform,meteorFloor.bounds);
                blackSunToken=arena.effects!=null?arena.effects.BeginBlackSun(GroundCentre,meteorRadius,arena.view.GetComponent<Camera>()):0;
            }
            if(attack!=ArenaAttack.Meteor || blackSunToken==0)arena.effects?.Charge(attack==ArenaAttack.Stomp&&!groundCastClip?rightFoot:rightHand);
            arena.Record("boss_attack_started",attack.ToString(),owner.fighter,0,owner.transform.position);return true;
        }
        public void Tick(float delta,ArenaSession session)
        {
            arena=session;if(delta<=0)return;
            // Bounded120Hz internal samples preserve active-window hand trajectories even in a coarse ForceAttack test.
            float remaining=delta;while(remaining>.000001f){float dt=Mathf.Min(remaining,1f/120f);Step(dt);remaining-=dt;}
        }
        void Step(float dt)
        {
            spells?.Tick(dt,arena);
            if(owner.fighter.Dead){if(!dead){CancelAttack();dead=true;}death=Mathf.Min(death+dt,clips.J026.length);Move(Vector3.zero,dt);animation.Pose(clips.J026,death,dt,false,.12f);return;}
            if(!arena.Active){if(Attacking)CancelAttack();Move(Vector3.zero,dt);idle+=dt;animation.Pose(clips.J002,idle,dt,true);return;}
            if(defense!=null&&defense.Step(dt,arena)){approachSpeed=0;Move(Vector3.zero,dt);return;}
            if(CombatPhase==1&&owner.fighter.Health<=owner.fighter.maximumHealth*.5f)phasePending=true;
            if(Attacking){approachSpeed=0;AttackStep(dt);return;}
            cooldown=Mathf.Max(0,cooldown-dt);idle+=dt;
            if(owner.decisionEnabled&&phasePending)
            {
                if(ForceAttack(ArenaAttack.Meteor)){CombatPhase=2;phasePending=false;arena.Record("boss_v13_phase_two","Meteor_entry",owner.fighter,0,owner.transform.position);}
                return;
            }
            var target=Vector3.ProjectOnPlane(arena.player.transform.position-owner.transform.position,Vector3.up);
            if(owner.decisionEnabled&&target.sqrMagnitude>.001f)owner.transform.rotation=Quaternion.RotateTowards(owner.transform.rotation,Quaternion.LookRotation(target),turnRate*dt);
            if(owner.decisionEnabled&&cooldown<=0&&spells!=null&&spells.TryChoose(target.magnitude,out var spell))
            { if(ForceAttack(spell))return; }
            if(owner.decisionEnabled&&cooldown<=0){
                ArenaAttack[] pool=CombatPhase==2?PhaseTwoPool:PhaseOnePool;
                for(int scan=0;scan<pool.Length;scan++)
                {
                    int index=(cursor+scan)%pool.Length;var candidate=pool[index];var punch=punches?punches.Find(candidate):null;
                    bool inRange=punch!=null?target.magnitude>=punch.minimumRange&&target.magnitude<=punch.maximumRange:target.magnitude<=stopRange+.10f;
                    if(inRange&&ForceAttack(candidate)){cursor=index+1;return;}
                }
            }
            bool moving=owner.decisionEnabled&&target.magnitude>stopRange;
            runningApproach=moving&&(target.magnitude>6||(runningApproach&&target.magnitude>4.5f));
            bool run=runningApproach;
            approachSpeed=Mathf.MoveTowards(approachSpeed,moving?(run?runSpeed:walkSpeed):0,7*dt);
            float speed=moving?Mathf.Min(approachSpeed,Mathf.Max(0,target.magnitude-stopRange)/dt):0;
            float travelled=Move(owner.transform.forward*speed,dt);
            if(moving){walkClock+=travelled/Mathf.Max(.1f,run?runStride:walkStride);var c=run?clips.RUN:clips.WALK;animation.Pose(c,walkClock*c.length,dt,true);}
            else animation.Pose(clips.J002,idle,dt,true);
        }
        float Move(Vector3 velocity,float dt,float hopDelta=0)
        {
            if(cc.isGrounded&&vertical<0)vertical=-2;vertical=Mathf.Max(-25,vertical-24*dt);
            var before=owner.transform.position;if(cc.enabled)cc.Move(velocity*dt+Vector3.up*(hopDelta+vertical*dt));
            float distance=Vector3.ProjectOnPlane(owner.transform.position-before,Vector3.up).magnitude;
            ActualSpeed=distance/dt;Blocked=velocity.sqrMagnitude>.01f&&distance<velocity.magnitude*dt*.25f;return distance;
        }
        void AttackStep(float dt)
        {
            if(CurrentAttack==ArenaAttack.Stomp&&groundCastClip){GroundCastStep(dt);return;}
            if(ActivePunch!=null){AuthoredPunchStep(dt);return;}
            if(ArenaSpellRules.IsSpell(CurrentAttack)) { SpellStep(dt);return; }
            if(CurrentAttack==ArenaAttack.Meteor){MeteorStep(dt);return;}
            if(CurrentAttack==ArenaAttack.Ignite&&phaseRaiseClip)
            {
                time=Mathf.Min(time+dt,AttackDuration);Move(Vector3.zero,dt);
                animation.Pose(phaseRaiseClip,Mathf.Min(time,1.3f),dt,false,.18f);
                if(TryGround(owner.transform.position,out var floor))
                {
                    float sole=Mathf.Min(rightFoot.TransformPoint(rightSoleOffset).y,leftFoot.TransformPoint(rightSoleOffset).y);
                    humanoid.GetBoneTransform(HumanBodyBones.Hips).position+=Vector3.up*(floor.y-sole);
                }
                Finish();return;
            }
            float previous=time;float next=Mathf.Min(time+dt,clips.Attack(CurrentAttack).length);
            // Heading is committed at entry; no automatic homing in active/recovery frames.
            // Original normalized RootT XZ is stripped from the visual clip and applied exactly once
            // through the actor's collision controller. Covers windup, release AND recovery, including Ignite.
            Transform scaledVisual=owner.visual!=null?owner.visual:animation.transform;
            float metresPerHumanUnit=humanoid.humanScale*scaledVisual.lossyScale.x;
            Vector3 localDelta=clips.GetDelta(clips.Attack(CurrentAttack),previous,next)*metresPerHumanUnit;
            Vector3 worldDelta=owner.transform.rotation*localDelta;
            Move(worldDelta/dt,dt);
            // Keep source vertical body pose untouched. No procedural hop or extra dash translation.
            time=next;
            if(CurrentAttack==ArenaAttack.Ignite){animation.Pose(clips.J045,time,dt);Finish();return;}
            bool radial=Radial(CurrentAttack);
            float radius=CurrentAttack==ArenaAttack.Meteor?meteorRadius:CurrentAttack==ArenaAttack.DiveKick?diveRadius:stompRadius;
            // Evaluate THIS time before reading feet, including telegraph position.
            PoseMelee(time,dt);
            Vector3 previewPoint=CurrentAttack==ArenaAttack.Meteor?owner.transform.position:rightFoot.TransformPoint(rightSoleOffset);
            bool previewValid=TryGround(previewPoint,out var previewGround);
            if(!impactCentreFrozen&&previewValid)GroundCentre=previewGround;
            if(CurrentAttack==ArenaAttack.Meteor)arena.effects?.PoseBlackSun(blackSunToken,time,GroundCentre,previewValid);
            if(Telegraph){if(radial&&previewValid)arena.effects?.WarnRing(previewGround,radius,time/Start(CurrentAttack,0));else if(!radial)arena.effects?.WarnStrike(owner.transform.position,owner.transform.forward,2.3f,.85f,time/Start(CurrentAttack,0));else arena.effects?.HideWarning();}
            else arena.effects?.HideWarning();
            // Sample only actual hand path within the release interval, never a pre-windup sweep.
            bool sampled=false;
            for(int hit=0;hit<Count(CurrentAttack);hit++){
                float a=Mathf.Max(previous,Start(CurrentAttack,hit)),b=Mathf.Min(time,End(CurrentAttack,hit));if(b<a||previous>=End(CurrentAttack,hit))continue;
                Transform hand=hit==1?leftHand:rightHand;PoseMelee(a,0);Vector3 from=hand.position;
                PoseMelee(b,0);Vector3 to=hand.position;sampled=true;
                if(radial&&!impactCentreFrozen){
                    // Current release pose, not the previous frame. Meteor is explicitly ground-targeted magic.
                    Vector3 sole=rightFoot.TransformPoint(rightSoleOffset);
                    Vector3 target=CurrentAttack==ArenaAttack.Meteor?owner.transform.position:sole;
                    if(!TryGround(target,out var contact))continue;
                    float gap=sole.y-contact.y;
                    if(CurrentAttack!=ArenaAttack.Meteor&&(!cc.isGrounded||gap>Mathf.Max(0,maximumSoleGap)||gap<-Mathf.Max(0,maximumSolePenetration)))continue;
                    GroundCentre=contact;impactCentreFrozen=true;
                    arena.Record("boss_v13_ground_contact",CurrentAttack==ArenaAttack.Meteor?"ground_targeted_magic":"actual_right_sole",owner.fighter,0,contact);
                }
                if(strikes[hit]==0){strikes[hit]=arena.NewStrike();ComboHitIndex=hit;
                    bool blackSunOwned=CurrentAttack==ArenaAttack.Meteor&&arena.effects!=null&&arena.effects.ReleaseBlackSun(blackSunToken,GroundCentre);
                    arena.Record("boss_v13_release",CurrentAttack+"_"+hit,owner.fighter,strikes[hit],radial?GroundCentre:to);
                    arena.Events[arena.Events.Count-1].blackSunOwned=blackSunOwned;
                    if(radial){
                        if(!blackSunOwned)
                            arena.effects?.GroundPulse(GroundCentre,radius);
                        pulseActive=true;pulseAge=0;pulseMaximum=radius;pulseStrike=strikes[hit];
                        pulseDamage=ringDamage*(CurrentAttack==ArenaAttack.Meteor?1.5f:1);pulseSkill="v13_"+CurrentAttack;
                    }else arena.effects?.Swing(hand,owner.transform.forward);}
                if(!radial)arena.StrikeCapsule(owner.fighter,strikes[hit],"v13_"+CurrentAttack,from,to,punchRadius,punchDamage*(hit==2?1.4f:1),hit==Count(CurrentAttack)-1?4:1.3f,"v13_actual_humanoid_hand");
            }
            PoseMelee(time,0);
            TickPulse(dt);
            Finish();
        }
        void PoseMelee(float at,float dt)=>animation.PoseContactClip(clips.Attack(CurrentAttack),at,dt);
        void GroundCastStep(float dt)
        {
            float previous=time;time=Mathf.Min(time+dt,groundCastClip.length);
            Move(Vector3.zero,dt);
            animation.PoseContactClip(groundCastClip,time,dt);
            if(previous<groundCastContactTime&&time>=groundCastContactTime)
            {
                animation.PoseContactClip(groundCastClip,groundCastContactTime,0);
                if(TryGround(rightHand.position,out var contact))
                {
                    GroundCentre=contact;impactCentreFrozen=true;strikes[0]=arena.NewStrike();ComboHitIndex=0;
                    arena.Record("boss_v13_ground_contact","original_right_palm",owner.fighter,0,contact);
                    arena.Record("boss_v13_release","Stomp_0",owner.fighter,strikes[0],contact);
                    arena.effects?.GroundPulse(contact,stompRadius);
                    pulseActive=true;pulseAge=0;pulseMaximum=stompRadius;pulseStrike=strikes[0];pulseDamage=ringDamage;pulseSkill="v13_Stomp";
                }
                animation.PoseContactClip(groundCastClip,time,0);
            }
            TickPulse(dt);Finish();
        }
        void AuthoredPunchStep(float dt)
        {
            var punch=ActivePunch;float before=time;time=Mathf.Min(time+dt,punch.clip.length);
            var visual=owner.visual?owner.visual:animation.transform;
            Move(owner.transform.rotation*punch.Delta(before,time)*(humanoid.humanScale*visual.lossyScale.x)/dt,dt);
            for(int i=0;i<punch.contacts.Length;i++)
            {
                var contact=punch.contacts[i];float start=contact.time-.055f,end=contact.time+.065f;
                if(time<start||before>=end)continue;
                var hand=contact.left?leftHand:rightHand;
                animation.PoseContactClip(punch.clip,Mathf.Max(before,start),0);var from=hand.position;
                animation.PoseContactClip(punch.clip,Mathf.Min(time,end),0);var to=hand.position;
                if(strikes[i]==0){strikes[i]=arena.NewStrike();ComboHitIndex=i;arena.effects?.Swing(hand,owner.transform.forward);arena.Record("boss_v13_release",CurrentAttack+"_"+i,owner.fighter,strikes[i],to);}
                arena.StrikeCapsule(owner.fighter,strikes[i],"authored_"+CurrentAttack,from,to,punchRadius,contact.damage,i==punch.contacts.Length-1?3:0.8f,"original_humanoid_hand");
            }
            animation.PoseContactClip(punch.clip,time,dt);
            Finish();
        }
        void SpellStep(float dt)
        {
            bool complete=spells.AdvanceCast(dt);time=spells.CastTime;
            if(ArenaSpellRules.IsMobile(CurrentAttack))
            {
                var target=Vector3.ProjectOnPlane(arena.player.transform.position-owner.transform.position,Vector3.up);
                if(target.sqrMagnitude>.01f)owner.transform.rotation=Quaternion.RotateTowards(owner.transform.rotation,Quaternion.LookRotation(target),turnRate*dt);
                bool moving=target.magnitude>stopRange;
                float travelled=Move(moving?owner.transform.forward*walkSpeed:Vector3.zero,dt);
                walkClock+=travelled/Mathf.Max(.1f,walkStride);
                animation.Pose(moving?clips.WALK:clips.J002,moving?walkClock*clips.WALK.length:idle,dt,true);
                animation.PoseUpperBody(spells.Clip,time,Mathf.Min(Mathf.Clamp01(time/.18f),Mathf.Clamp01((spells.Duration-time)/.2f)));
            }
            else { Move(Vector3.zero,dt);animation.Pose(spells.Clip,time,dt,false,.18f); }
            if(CurrentAttack==ArenaAttack.ThunderOrb||CurrentAttack==ArenaAttack.DelayedBlast)
                ArenaArmContact.Constrain(humanoid,arena.player.capsule);
            if(complete)
            {
                animation.ClearUpperBody();spells.EndCast();Attacking=false;idle=0;cooldown=1.1f;
                arena.Record("boss_recovered",CurrentAttack.ToString(),owner.fighter,0,owner.transform.position);
            }
        }
        float MeteorSourceTime(float elapsed)
        {
            if(meteorRaiseClip)
            {
                const float openArms=1.3f;
                return Mathf.Min(elapsed,openArms);
            }
            // Extend the raised-hand anticipation; retain the final .8s downstroke at source speed.
            float holdEnd=MeteorReleaseTime-.8f;
            return elapsed<holdEnd?Mathf.Lerp(0,SourceMeteorRelease-.8f,elapsed/holdEnd)
                :Mathf.Min(clips.J063.length,SourceMeteorRelease-.8f+elapsed-holdEnd);
        }
        bool TryMeteorGround(out Vector3 ground)
        {
            ground=default;
            if(!meteorFloor||!meteorFloor.enabled||!meteorFloor.gameObject.activeInHierarchy)return false;
            var bounds=meteorFloor.bounds;
            var ray=new Ray(new Vector3(bounds.center.x,bounds.max.y+1,bounds.center.z),Vector3.down);
            if(!meteorFloor.Raycast(ray,out var hit,2)||hit.normal.y<minimumGroundNormalY)return false;
            ground=hit.point;return true;
        }
        void MeteorStep(float dt)
        {
            float previous=time;time=Mathf.Min(time+dt,AttackDuration);
            // Finish opening both arms before lifting the collision body.
            float liftStart=meteorRaiseClip?1.3f:0;
            float ascent=Mathf.SmoothStep(0,1,Mathf.Clamp01((time-liftStart)/1.6f));
            float descent=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(MeteorReleaseTime+.15f,MeteorReleaseTime+1.65f,time));
            Vector3 desired=flightOrigin+Vector3.up*(Mathf.Max(0,meteorFlightHeight)*ascent*descent);
            // The real body and its capsule rise together. J063 root XZ is intentionally committed
            // to this hover origin; every other attack retains its original source root trajectory.
            if(cc.enabled)cc.Move(desired-owner.transform.position);
            vertical=0;ActualSpeed=0;Blocked=false;
            animation.PoseContactClip(meteorRaiseClip?meteorRaiseClip:clips.J063,MeteorSourceTime(time),dt);
            bool valid=meteorGroundValid&&TryMeteorGround(out var ground)&&(ground-GroundCentre).sqrMagnitude<.0001f;
            arena.effects?.PoseBlackSun(blackSunToken,time,GroundCentre,valid);
            if(Telegraph&&valid)arena.effects?.WarnCourt(GroundCentre,meteorRadius,Mathf.Min(meteorFloor.bounds.extents.x,meteorFloor.bounds.extents.z),time/MeteorReleaseTime);
            else arena.effects?.HideWarning();
            if(previous<MeteorReleaseTime&&time>=MeteorReleaseTime&&valid)
            {
                impactCentreFrozen=true;strikes[0]=arena.NewStrike();ComboHitIndex=0;
                bool owned=arena.effects!=null&&arena.effects.ReleaseBlackSun(blackSunToken,GroundCentre);
                arena.Record("boss_v13_ground_contact","fixed_court_centre",owner.fighter,0,GroundCentre);
                arena.Record("boss_v13_release","Meteor_0",owner.fighter,strikes[0],GroundCentre);
                arena.Events[arena.Events.Count-1].blackSunOwned=owned;
                if(!owned)arena.effects?.GroundPulse(GroundCentre,meteorRadius);
                pulseActive=true;pulseAge=0;pulseMaximum=meteorRadius;pulseStrike=strikes[0];
                pulseDamage=ringDamage*1.5f;pulseSkill="v13_Meteor";
            }
            TickPulse(dt);Finish();
        }
        void TickPulse(float dt)
        {
            if(!pulseActive)return;
            // Same .36s radius curve as ArenaFlameFx.UpdateWave and fallback ArenaCombatFx.Tick.
            // Session ticks effects AFTER this motor: the initial query covers the initial visible .15m core
            // and this step's outward travel; subsequent queries cover only the swept annulus.
            float before=pulseAge;pulseAge=Mathf.Min(.36f,pulseAge+dt);
            float inner=before==0?0:ArenaCombatFx.PulseRadius(before,pulseMaximum);
            float outer=ArenaCombatFx.PulseRadius(pulseAge,pulseMaximum);
            if(arena.Active&&!owner.fighter.Dead&&impactCentreFrozen)
                arena.StrikeRing(owner.fighter,pulseStrike,pulseSkill,GroundCentre,outer,inner,pulseDamage,6);
            if(pulseAge>=.36f){pulseActive=false;arena.Record("boss_v13_wave_complete",pulseSkill,owner.fighter,pulseStrike,GroundCentre);}
        }
        bool TryGround(Vector3 point,out Vector3 ground)
        {
            ground=default;
            // Short upward origin avoids selecting an unrelated wall cap metres above the sole.
            float lift=Mathf.Max(.1f,maximumSolePenetration+.02f);
            if(!Physics.Raycast(point+Vector3.up*lift,Vector3.down,out var hit,30,1<<8,QueryTriggerInteraction.Ignore)
                ||hit.normal.y<Mathf.Clamp01(minimumGroundNormalY))return false;
            ground=hit.point;return true;
        }
        void OnDisable(){arena?.effects?.ClearBlackSun();blackSunToken=0;}
        void Finish()
        {
            if(time<AttackDuration||pulseActive)return;
            if(CurrentAttack==ArenaAttack.Ignite){CombatPhase=2;phasePending=false;arena.Record("boss_v13_phase_two","J045_complete",owner.fighter,0,owner.transform.position);}
            Attacking=false;idle=0;cooldown=.55f;arena.effects?.HideWarning();arena.Record("boss_recovered",CurrentAttack.ToString(),owner.fighter,0,owner.transform.position);
        }
        static int Count(ArenaAttack a)=>a==ArenaAttack.ComboThree?3:a==ArenaAttack.ComboTwo?2:1;
        static bool Radial(ArenaAttack a)=>a==ArenaAttack.Stomp||a==ArenaAttack.DiveKick||a==ArenaAttack.Meteor;
        static float Start(ArenaAttack a,int hit){if(hit==1)return 2.458333f;if(hit==2)return 3.416667f;switch(a){case ArenaAttack.Stomp:return 1.583333f;case ArenaAttack.DashPunch:return 1.708333f;case ArenaAttack.Ignite:return 2.25f;case ArenaAttack.DiveKick:return 1.958333f;case ArenaAttack.Meteor:return 3.041667f;default:return 1.5f;}}
        static float End(ArenaAttack a,int hit)=>a==ArenaAttack.Stomp?1.85f:a==ArenaAttack.DiveKick?2.30f:Start(a,hit)+(a==ArenaAttack.Ignite?.208333f:.125f);
    }
}
