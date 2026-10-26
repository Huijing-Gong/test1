using UnityEngine;

namespace SamProduction.BossArena
{
    // Presentation only: the arena's existing capsule, sword sweep and clocks own combat.
    [DisallowMultipleComponent]
    public sealed class ArenaCurrentCharacter : MonoBehaviour
    {
        public GameObject visualPrefab;
        public ArenaTrainingAvatar poseSource;
        public ArenaPlayerController player;
        public Animator Animator { get; private set; }
        public KingOfGraphic.Hero.HeroPoseDriver Hero {get;private set;}
        readonly KingOfGraphic.Hero.HeroGroundTurnController groundTurn=new KingOfGraphic.Hero.HeroGroundTurnController();
        readonly KingOfGraphic.Hero.HeroGaitClock heroGait=new KingOfGraphic.Hero.HeroGaitClock();
        float heroLocomotionTime,heroGuardTime,heroResponseTime,heroPreviousYaw;bool heroWasGuarding,heroYawInitialized;string heroResponse;
        public void ResetHeroPresentation()
        {groundTurn.Cancel();heroGait.Reset();heroYawInitialized=false;heroLocomotionTime=heroGuardTime=heroResponseTime=0;heroDeath.Reset();heroWasGuarding=false;heroResponse=null;if(Hero)Hero.ResetPresentation();}
        public void PresentHeroResponse(string id){if(!Hero)return;heroResponse=id;heroResponseTime=0;}
        public bool Ready => Hero?Hero.Ready:Animator != null && Animator.isHuman;
        readonly KingOfGraphic.Hero.HeroDeathPresentation heroDeath=new KingOfGraphic.Hero.HeroDeathPresentation();
        public bool DeathPresentationComplete=>!Hero||heroDeath.ReadyForUI;
        public float MaximumHandError { get; private set; }
        Transform visual, hips;
        Transform[] upper = new Transform[2], lower = new Transform[2], hand = new Transform[2];
        Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        readonly Vector3[] plantedAnkle = new Vector3[2];
        float move;
        Transform hiddenRig, currentSword;
        bool trainingSword;
        Renderer[] trainingSwordRenderers;
        const float OriginalSwordTipY = 1.047957f;
        public bool MainSwordReady => currentSword != null;
        public Vector3 MainSwordTip => currentSword != null ? currentSword.TransformPoint(Vector3.up * OriginalSwordTipY) : Vector3.zero;
        static readonly int Move = Animator.StringToHash("MoveAmount");

        void Awake()
        {
            if (poseSource == null) poseSource = GetComponent<ArenaTrainingAvatar>();
            if (player == null) player = GetComponent<ArenaPlayerController>();
            trainingSword = System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--current-training-sword") >= 0;
            Initialize();
            if (poseSource != null && !Hero) poseSource.Posed += Present;
        }
        void OnDisable(){groundTurn.Cancel();heroDeath.Reset();}
        void OnDestroy() { if (poseSource != null) poseSource.Posed -= Present; }

        public void Initialize()
        {
            if (Ready || visualPrefab == null) return;
            visual = Instantiate(visualPrefab, transform).transform;
            visual.name = "CurrentMainWorldCharacter";
            visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity;
            Hero=visual.GetComponent<KingOfGraphic.Hero.HeroPoseDriver>();
            if(Hero){Hero.ConfigureSupportHost(GetComponent<CharacterController>());foreach(var c in visual.GetComponentsInChildren<Collider>(true))c.enabled=false;Animator=Hero.animator;currentSword=Hero.sword;foreach(var t in visual.GetComponentsInChildren<Transform>(true))t.gameObject.layer=9;Hero.SetSwordVisible(false);groundTurn.Initialize(Hero,GetComponent<CharacterController>());return;}
            Animator = visual.GetComponentInChildren<Animator>();
            if (Animator == null || !Animator.isHuman) { Debug.LogError("Current character needs its existing humanoid avatar",this); return; }
            // No second gameplay controller or equipment/save owner is instantiated.
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            var socket = Find(visual,"WeaponSocket");
            if (socket != null) socket.gameObject.SetActive(false);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=9;
            Animator.applyRootMotion=false; Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            Animator.enabled=false; Animator.Rebind();
            Animator.SetBool("IsGrounded",true); Animator.Play("Locomotion",0,0); Animator.Update(0);
            hips=Animator.GetBoneTransform(HumanBodyBones.Hips);
            for (int i=0;i<2;i++)
            {
                upper[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightUpperArm:HumanBodyBones.LeftUpperArm);
                lower[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightLowerArm:HumanBodyBones.LeftLowerArm);
                hand[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightHand:HumanBodyBones.LeftHand);
                thigh[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightUpperLeg:HumanBodyBones.LeftUpperLeg);
                shin[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightLowerLeg:HumanBodyBones.LeftLowerLeg);
                foot[i]=Animator.GetBoneTransform(i==0?HumanBodyBones.RightFoot:HumanBodyBones.LeftFoot);
            }
        }
        static Transform Find(Transform root,string name)
        { foreach(var t in root.GetComponentsInChildren<Transform>(true)) if(t.name==name)return t;return null; }

        void HideTrainingBody()
        {
            var rig=transform.Find("TrainingVisual");
            if(rig==null||rig==hiddenRig)return;
            if(Hero){foreach(var renderer in rig.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;hiddenRig=rig;return;}
            // Keep the original grip, sweep endpoints and timing. Only the visible mesh changes.
            if (!trainingSword && currentSword == null)
            {
                var original = Find(visual,"PracticeSword");
                if (original == null || poseSource.DefenseContactAnchor == null)
                { Debug.LogError("Original held sword or posed grip missing",this); return; }
                currentSword = original;
                currentSword.SetParent(poseSource.DefenseContactAnchor,false);
                currentSword.localPosition=Vector3.zero;
                currentSword.localRotation=Quaternion.FromToRotation(Vector3.up,Vector3.forward);
                // Read-only original mesh inspection: positive Y tip 1.047957m.
                // Uniform presentation scale brings that tip to the original .86m sweep end.
                currentSword.localScale=Vector3.one*(.86f/OriginalSwordTipY);
                foreach(var c in currentSword.GetComponentsInChildren<Collider>(true))c.enabled=false;
                currentSword.gameObject.SetActive(player != null && player.SwordDrawn);
            }
            foreach(var r in rig.GetComponentsInChildren<Renderer>(true))
                if(!r.transform.IsChildOf(trainingSword?poseSource.DefenseContactAnchor:currentSword))r.enabled=false;
            if (trainingSword && poseSource.DefenseContactAnchor != null)
                trainingSwordRenderers = poseSource.DefenseContactAnchor.GetComponentsInChildren<Renderer>(true);
            hiddenRig=rig;
            RefreshWeaponVisibility();
        }
        public void RefreshWeaponVisibility()
        {
            bool drawn = player != null && player.SwordDrawn;
            if(Hero){Hero.SetSwordCarried(player!=null&&!player.fighter.Dead);Hero.SetSwordVisible(drawn);}
            if (currentSword != null && currentSword.gameObject.activeSelf != drawn) currentSword.gameObject.SetActive(drawn);
            if (trainingSwordRenderers != null) foreach (var renderer in trainingSwordRenderers) if (renderer != null) renderer.enabled = drawn;
        }
        public void CancelGroundTurn(){groundTurn.Cancel();}
        public bool GroundTurnActive=>groundTurn.Active;
        public float GroundTurnAge=>groundTurn.Age;
        public void ObserveGroundFacing(Vector3 facing,bool allowed,float delta)
        {groundTurn.ObserveFacing(facing,allowed&&Hero&&Hero.Ready&&player!=null&&!player.SwordDrawn&&!player.Guarding&&player.HeroMotionId==null&&heroResponse==null,delta);}
        public void PoseHero(float delta,Vector3 localVelocity,bool dead,bool guarding,bool grounded=false,bool allowGroundStart=false,bool allowGroundStop=false)
        {
            if(!Hero||!Ready)return;HideTrainingBody();RefreshWeaponVisibility();
            PresentHero(delta,localVelocity,dead,guarding,player!=null&&player.SwordDrawn,grounded,allowGroundStart,allowGroundStop);
        }
        void Present(float delta,Vector3 localVelocity,bool grounded,float attackTime,float dodgeTime,int combo,bool dead,bool guarding)
        {
            if(!Ready)return;
            HideTrainingBody();
            RefreshWeaponVisibility();
            bool drawn = player != null && player.SwordDrawn;
            if(Hero){Hero.SetSwordCarried(player!=null&&!player.fighter.Dead);Hero.SetSwordVisible(drawn);}
            if(Hero){PresentHero(delta,localVelocity,dead,guarding,drawn,grounded,false,false);return;}
            float speed=Vector3.ProjectOnPlane(localVelocity,Vector3.up).magnitude;
            float wanted=speed<.08f||dead||dodgeTime>=0?0:
                speed<=player.walkSpeed?.5f*Mathf.Clamp01(speed/player.walkSpeed):
                Mathf.Lerp(.5f,1,Mathf.InverseLerp(player.walkSpeed,player.runSpeed,speed));
            move=Mathf.Lerp(move,wanted,1-Mathf.Exp(-12*Mathf.Max(0,delta)));
            Animator.SetFloat(Move,move);
            Animator.Update(Mathf.Max(0,delta));
            for(int i=0;i<2;i++)plantedAnkle[i]=foot[i].position;
            float duck=dodgeTime>=0?Mathf.Sin(Mathf.Clamp01(dodgeTime/player.dodgeDuration)*Mathf.PI):0;
            hips.position+=transform.up*(-duck*.20f-(dead?.48f:0));
            var chest=Animator.GetBoneTransform(HumanBodyBones.Chest);
            var sourceChest=poseSource.TorsoTarget;
            if(chest!=null&&sourceChest!=null&&(drawn||duck>0||dead))
                chest.rotation=sourceChest.rotation*Quaternion.Inverse(transform.rotation)*chest.rotation;
            for(int i=0;i<2;i++)
            {
                Transform target=poseSource.HandTarget(i), pole=poseSource.ElbowTarget(i);
                if(drawn && target!=null && pole!=null && upper[i]!=null && lower[i]!=null && hand[i]!=null)
                {
                    SolveArm(upper[i],lower[i],hand[i],target.position,pole.position);
                    // Preserve the animated wrist relative to the solved forearm.
                    MaximumHandError=Mathf.Max(MaximumHandError,Vector3.Distance(hand[i].position,target.position));
                }
                if(duck>0||dead)
                {
                    var footRotation=foot[i].rotation;
                    SolveArm(thigh[i],shin[i],foot[i],plantedAnkle[i]+transform.up*duck*.115f,
                        thigh[i].position+transform.forward*.6f);
                    foot[i].rotation=footRotation;
                }
            }
            if(drawn) CurlGrip(HumanBodyBones.RightIndexProximal,HumanBodyBones.RightLittleDistal);
        }
        void PresentHero(float dt,Vector3 velocity,bool dead,bool guarding,bool drawn,bool grounded,bool allowGroundStart,bool allowGroundStop)
        {
            float yaw=transform.eulerAngles.y;float yawRate=heroYawInitialized?KingOfGraphic.Hero.HeroGaitClock.SignedYawRate(heroPreviousYaw,yaw,dt):0;heroPreviousYaw=yaw;heroYawInitialized=true;
            if(KingOfGraphic.Hero.HeroRecoveryPresentation.ShouldYield(heroResponse,velocity,dead||guarding||player.HeroMotionId!=null))heroResponse=null;
            heroLocomotionTime+=dt;if(heroResponse!=null){heroResponseTime+=dt;if(heroResponseTime>=Hero.library.Duration(heroResponse))heroResponse=null;}
            if(heroWasGuarding&&!guarding&&player.HeroMotionId==null)PresentHeroResponse("GUARD_EXIT");
            if(!dead)heroDeath.Reset();
            if(guarding)heroGuardTime=heroWasGuarding?heroGuardTime+dt:dt;else if(heroWasGuarding)heroGuardTime=0;
            string id=player.HeroMotionId;float time=player.HeroMotionTime;float sheathTime=!dead&&id=="EQUIP_SHEATHE"?time:-1;if(sheathTime>=0)id=null;float playbackRate=1,fade=-1;bool ordinaryLocomotion=false;
            if(dead){if(!heroDeath.Active)heroDeath.Begin(Hero.library,false,grounded);heroDeath.TickAfterPhysics(dt,grounded);id=heroDeath.Id;time=heroDeath.Time;fade=heroDeath.BlendDuration;}
            else if(id==null&&heroResponse!=null){id=heroResponse;time=heroResponseTime;}
            else if(id==null&&guarding){id=heroGuardTime<Hero.library.Duration("GUARD_ENTER")?"GUARD_ENTER":"GUARD_HOLD";time=heroGuardTime;}
            else if(id==null)
            {
                float speed=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
                id=drawn?(speed>=.08f?heroGait.SelectCombatMove(velocity):"COMBAT_IDLE"):heroGait.SelectOrdinaryGroundCycle(velocity);
                ordinaryLocomotion=true;bool eligible=grounded&&allowGroundStart&&!drawn&&KingOfGraphic.Hero.HeroGaitClock.AllowsForwardStart(speed,velocity.z,yawRate);
                time=heroGait.StepPresented(id,Hero.library,speed,dt,eligible,grounded&&allowGroundStop&&!drawn&&KingOfGraphic.Hero.HeroGaitClock.AllowsForwardStart(speed,velocity.z,yawRate));id=heroGait.PresentedAction;playbackRate=heroGait.PlaybackRate;if(heroGait.CompletedStart)fade=0;
            }
            if(!ordinaryLocomotion)heroGait.InterruptGroundTransitions();
            KingOfGraphic.Hero.HeroGroundTurnFrame turnFrame=default;
            if(ordinaryLocomotion)
            {
                if(groundTurn.TryAdvance(sheathTime<0&&grounded&&!drawn&&!guarding&&heroResponse==null&&Vector3.ProjectOnPlane(velocity,Vector3.up).sqrMagnitude<.0064f,dt,out var turnId,out var turnTime,out var turnDelta,out turnFrame))
                {id=turnId;time=turnTime;dt=turnDelta;playbackRate=1;fade=-1;}
            }
            else groundTurn.Cancel();
            if(player.AuthorEnabled&&!dead&&player.HeroMotionId==null&&!guarding&&heroResponse==null)
            {
                player.Author.Locomotion(Hero.library,ref id,ref time,velocity,drawn,false,0,dt);
                player.Author.Air(Hero.library,ref id,ref time,grounded,player.AuthorJumpIndex,player.AuthorVertical,drawn,new Vector2(velocity.x,velocity.z).magnitude,dt);
            }
            heroWasGuarding=guarding;Hero.Sample(id,time,dt,drawn,fade:fade,playbackRate:playbackRate,frameStartTime:turnFrame.IsActive?turnFrame.StartTime:!dead&&id==player.HeroMotionId?player.HeroMotionPreviousTime:float.NaN,locomotionVelocity:velocity,groundTurn:turnFrame,sheathTime:sheathTime,actionInstance:!dead&&id==player.HeroMotionId?player.HeroMotionInstance:0);Hero.SetSwordVisible(drawn);if(dead)heroDeath.MarkPresented(Hero.CurrentAction);
        }
        void CurlGrip(HumanBodyBones first,HumanBodyBones last)
        {
            for(int id=(int)first;id<=(int)last;id++)
            {
                var finger=Animator.GetBoneTransform((HumanBodyBones)id);
                if(finger!=null)finger.localRotation*=Quaternion.Euler(60,0,0);
            }
        }
        static void SolveArm(Transform upper,Transform lower,Transform end,Vector3 goal,Vector3 pole)
        {
            Vector3 root=upper.position,axis=goal-root;
            float a=Vector3.Distance(root,lower.position),b=Vector3.Distance(lower.position,end.position);
            float d=Mathf.Clamp(axis.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
            axis=axis.sqrMagnitude>.000001f?axis.normalized:Vector3.forward;
            Vector3 bend=Vector3.ProjectOnPlane(pole-root,axis).normalized;
            if(bend.sqrMagnitude<.000001f)bend=Vector3.ProjectOnPlane(upper.forward,axis).normalized;
            float along=(a*a-b*b+d*d)/(2*d);
            Vector3 elbow=root+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-root,elbow-root)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(end.position-lower.position,root+axis*d-lower.position)*lower.rotation;
        }
    }
}
