using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfGraphic.Hero
{
    [DefaultExecutionOrder(-40),DisallowMultipleComponent]
    public sealed partial class HeroWorldAdapter:MonoBehaviour
    {
        public HeroPoseDriver pose;
        public HeroActionLibrary Library=>pose?pose.library:null;
        public readonly HeroCombatClock combat=new HeroCombatClock();
        readonly HeroAuthoredControls author=new HeroAuthoredControls();
        public bool AuthorControls=>Library&&Library.authoredControls;
        public bool Prone=>AuthorControls&&author.Posture==2;
        public float AuthoredSpeedLimit=>author.SpeedLimit;
        public bool PrepareAuthorStanding(){if(!AuthorControls||author.Posture==0)return true;if(!author.SetPosture(0,capsule))return false;Crouching=false;author.ClearTransition();return true;}
        string requestedDodge;float activeDodgeDuration=.52f;
        bool ResponsiveSword=>Library&&Library.responsiveSwordCombat;
        float quickAttackBuffer,quickDodgeBuffer;
        bool QuickDodge=>ResponsiveSword&&dodgeId!=null&&(dodgeId.StartsWith("DODGE_")||dodgeId.StartsWith("PERFECT_DODGE_"));
        readonly HeroGroundTurnController groundTurn=new HeroGroundTurnController();
        readonly HeroBladeSweep bladeSweep=new HeroBladeSweep();readonly HeroGaitClock gait=new HeroGaitClock();readonly HeroSwimClock swimGait=new HeroSwimClock();
        public bool Attacking=>combat.Active;
        public static AttackImpactKind ImpactKindForAction(string id)=>id=="ATK_CHARGE_RELEASE"?AttackImpactKind.Charged:
            id=="ATK_HEAVY"?AttackImpactKind.Heavy:AttackImpactKind.Normal;
        public bool Guarding {get;private set;}
        public bool Crouching {get;private set;}
        public bool Dodging=>dodgeTime>=0;
        public bool ExternalMovement=>Dodging||FocusOwnsMovement||(traversal&&traversal.Active);
        public bool OwnsAction=>ownsLock;
        public bool KeepsSword=>FocusSequenceActive||Attacking||Guarding||Dodging||counterRemaining>0||(transient?.StartsWith("HIT_",StringComparison.Ordinal)==true)||transient=="GUARD_BREAK";
        public bool DeathAnimationFinished=>dead&&deathPresentation.ReadyForUI&&(!traversal||!traversal.RecoveringContact);
        readonly HeroDeathPresentation deathPresentation=new HeroDeathPresentation();
        int deathBeganFrame=-1;uint deathRecoveryStep;
        public bool Busy=>FocusSequenceActive||(traversal&&traversal.RecoveringContact)||Attacking||Dodging||Guarding||dead||transientLock||(traversal&&traversal.Active);
        public string CurrentAction=>pose?pose.CurrentAction:null;
        public int SuccessfulHits {get;private set;}
        public int PerfectDodges {get;private set;}
        public int PerfectBlocks {get;private set;}
        PlayerMovement movement; PlayerEquipment equipment; PlayerCombat legacyCombat; PlayerVitals vitals;
        Health health; CharacterController capsule; BaselineFlow flow; HeroTraversal traversal;
        long bufferedSequence;bool bufferedAttackPress,bufferedAttackRelease,bufferedDodgePress,bufferedRollPress,dodgeCounterQueued;
        bool CanBufferInput=>combat.Active&&!dead&&Application.isFocused&&(!movement||!movement.HasExternalInputLock)&&(!flow||flow.Started&&!flow.BlockWorldInput&&!flow.MapOpen)&&Cursor.lockState==CursorLockMode.Locked;
        bool ownsLock,wasGrounded,wasSwimming,wasClimbing,wasMoving,dead,hitSampleValid,guardUsed;
        Vector3 lastPosition,localVelocity,previousTip,previousBase,dodgeVector;
        float previousYaw;int traversalSyncFrame=-1;
        float movementClock,dodgeTime=-1,dodgeCooldown,guardTime,counterRemaining,airPeak;
        string dodgeId,counterId,transient,autoAction;
        float transientTime,transientEventAt,autoTime,baseActionTime;string baseAction;
        float movingLandingAge=-1;bool movingLandingHeavy,pendingLanding;
        static bool IsLanding(string id)=>id=="AIR_LAND_LIGHT"||id=="AIR_LAND_HEAVY";
        bool transientLock,eventFired,blockedHit;
        // Reaction strength is presentation, not damage: DemonKing sword strikes
        // get a compact directional response without locking an entire hit clip.
        bool compactHit;
        float hitStunDuration,hitPresentationDuration,hitPlaybackSpeed;
        Action transientCallback;
        long sampledSequence=-1;
        readonly HashSet<Health> hitTargets=new HashSet<Health>();
        readonly Collider[] overlaps=new Collider[64];
        public bool AcceptInput=>isActiveAndEnabled&&Time.timeScale>0&&!dead&&(!movement||!movement.HasExternalInputLock)&&(!flow||flow.Started&&!flow.BlockWorldInput&&!flow.MapOpen)&&
            !(SideWorldTravel.Instance&&(SideWorldTravel.Instance.IsAway||SideWorldTravel.Instance.Busy))&&Cursor.lockState==CursorLockMode.Locked;
        public bool CanAttack=>AcceptInput&&pose&&pose.Ready&&pose.WeaponReady&&equipment&&equipment.CanAttack&&movement&&movement.IsStableGrounded&&
            !movement.SwimmingActive&&!movement.ClimbingActive&&!movement.MantlingActive&&!Dodging&&!FocusOwnsMovement&&!transientLock&&!(traversal&&(traversal.Active||traversal.RecoveringContact));
        void Awake()
        {
            pose=GetComponentInChildren<HeroPoseDriver>(true);combat.library=Library;
            if(!GetComponent<HeroStairFootPlacement>())gameObject.AddComponent<HeroStairFootPlacement>();
            movement=GetComponent<PlayerMovement>();equipment=GetComponent<PlayerEquipment>();legacyCombat=GetComponent<PlayerCombat>();
            health=GetComponent<Health>();vitals=GetComponent<PlayerVitals>();capsule=GetComponent<CharacterController>();if(pose)pose.ConfigureSupportHost(capsule);
            flow=FindFirstObjectByType<BaselineFlow>();traversal=GetComponent<HeroTraversal>();lastPosition=transform.position;airPeak=transform.position.y;
        }
                void Start(){groundTurn.Initialize(pose,capsule);}
        bool GroundTurnContext=>AcceptInput&&pose&&pose.CurrentOverlay==null&&movement&&capsule&&capsule.isGrounded&&movement.IsStableGrounded&&wasGrounded&&movement.GroundAngle<=3&&!movement.GroundJumpActive&&!movement.ImpactActive&&!movement.SwimmingActive&&!movement.ClimbingActive&&!movement.MantlingActive&&!movement.IsWading&&!movement.IsGameplayInputLocked&&!Crouching&&!Prone&&!Busy&&transient==null&&autoAction==null&&!pendingLanding&&movingLandingAge<0&&equipment&&!equipment.SwordDrawn;
        public void ObserveGroundFacing(Vector3 facing,bool allowed,float delta)
        {groundTurn.ObserveFacing(facing,allowed&&GroundTurnContext&&Vector3.ProjectOnPlane(localVelocity,Vector3.up).sqrMagnitude<.0064f,delta);}
        public bool GroundTurnActive=>groundTurn.Active;
        public float GroundTurnAge=>groundTurn.Age;
        void OnEnable(){if(health){health.Damaged+=OnDamaged;health.Died+=OnDied;}lastPosition=transform.position;previousYaw=transform.eulerAngles.y;}
        void OnDisable(){if(health){health.Damaged-=OnDamaged;health.Died-=OnDied;}CancelForTransition();deathPresentation.Reset();}
        public bool TryAttack(string explicitId=null)
        {
            bool air=explicitId=="ATK_JUMP"&&AcceptInput&&pose&&pose.Ready&&pose.WeaponReady&&equipment&&equipment.CanAttack&&!movement.SwimmingActive&&!movement.ClimbingActive&&!movement.MantlingActive&&!Dodging&&!FocusOwnsMovement&&!transientLock&&!(traversal&&(traversal.Active||traversal.RecoveringContact));
            if((!CanAttack&&!air)||combat.Active||Guarding||!PrepareAuthorStanding())return false;
            AcquireAttackAim();FaceAttackAim(0,true);ClearTransient();equipment.DrawForAttack();combat.SelectedCombo=ResponsiveSword?3:author.ComboLength;combat.Begin(explicitId??(counterRemaining>0?counterId??"ATK_BOOST_DODGE":combat.FirstAttack));counterRemaining=0;
            ClearHitSamples();SetOwnedLock(true);return true;
        }
        public void CancelForTransition(bool preserveTraversalRecovery=false)
        {
            CancelFocusSequence();attackAim=null;
            if(compactHit&&movement)movement.ClearImpact();
            ClearTraversalExitBlend();groundTurn.Cancel();author.ClearTransition();requestedDodge=null;
            movingLandingAge=-1;pendingLanding=false;if(IsLanding(autoAction)){autoAction=null;autoTime=0;}combat.Cancel();bufferedAttackPress=bufferedAttackRelease=bufferedDodgePress=bufferedRollPress=dodgeCounterQueued=false;Guarding=false;dodgeTime=-1;counterRemaining=0;ClearTransient();
            quickAttackBuffer=quickDodgeBuffer=0;ClearHitSamples();if(legacyCombat)legacyCombat.SetHeroSwordWindow(false);SetOwnedLock(false);if(traversal)traversal.Cancel(preserveTraversalRecovery);
        }
        public void ResetLocomotionAfterRelocation(){author.Reset(capsule);Crouching=false;ClearTraversalExitBlend();groundTurn.Cancel();if(traversal)traversal.ResetContactRecovery();if(pose)pose.ResetPresentation();lastPosition=transform.position;previousYaw=transform.eulerAngles.y;airPeak=transform.position.y;autoAction=baseAction=null;autoTime=baseActionTime=0;movingLandingAge=-1;pendingLanding=false;gait.Reset();swimGait.Reset();wasGrounded=movement&&movement.IsStableGrounded;wasSwimming=wasClimbing=wasMoving=false;}
        public void SynchronizeAfterTraversal(bool acceptMovementThisFrame=false){ClearTraversalExitBlend();groundTurn.Cancel();traversalSyncFrame=acceptMovementThisFrame?-1:Time.frameCount;if(movement)movement.RefreshAfterExternalTraversal();lastPosition=transform.position;previousYaw=transform.eulerAngles.y;airPeak=transform.position.y;autoAction=baseAction=null;autoTime=baseActionTime=0;movingLandingAge=-1;pendingLanding=false;gait.Reset();wasMoving=false;wasGrounded=movement&&movement.IsStableGrounded;wasSwimming=movement&&movement.SwimmingActive;wasClimbing=movement&&movement.ClimbingActive;}
        float traversalExitBlendRemaining;
        void ClearTraversalExitBlend(){traversalExitBlendRemaining=0;if(pose)pose.CancelRootTransitionPreservation();}
        public void SynchronizeAfterPairedRopeTop(bool completed)
        {
            SynchronizeAfterTraversal(true);
            if(completed)gait.StepPresented("LOC_IDLE",Library,0,0,true);
            else{traversalExitBlendRemaining=.28f;pose.PreserveRootOnNextTransition();}
        }
        public void ResetForRespawn()
        {
            CancelForTransition();author.Reset(capsule);dead=false;deathPresentation.Reset();Crouching=false;RestoreHeight();ResetLocomotionAfterRelocation();
            RequestPresentation("RESPAWN_RECOVER",false,null,0);
        }
        void SetOwnedLock(bool locked)
        {
            if(!movement)return;
            if(locked){movement.SetActionInputLocked(this,true);ownsLock=true;}
            else if(ownsLock){movement.SetActionInputLocked(this,false);ownsLock=false;}
        }
        void ClearTransient()
        {
            transient=null;transientCallback=null;transientLock=false;compactHit=false;
        }
        static bool WantsToMove(Keyboard keyboard)=>keyboard!=null&&
            (keyboard.wKey.isPressed||keyboard.aKey.isPressed||keyboard.sKey.isPressed||keyboard.dKey.isPressed||
             keyboard.upArrowKey.isPressed||keyboard.leftArrowKey.isPressed||keyboard.downArrowKey.isPressed||keyboard.rightArrowKey.isPressed||keyboard.spaceKey.isPressed);
        void Update()
        {
            if(!pose||!pose.Ready||!movement||!health)return;
            if(dead&&!health.IsDead)ResetForRespawn();
            float dt=GetComponent<PlayerBattleActions>()?.PlayerDeltaTime??Time.deltaTime;
            if(dt<=0)
            {
                if(legacyCombat&&legacyCombat.IsApplyingHitStop&&CanBufferInput)
                {
                    var frozen=HeroCombatInput.Read(true);var frozenAuthor=HeroAuthorInput.Read(AuthorControls);
                    bufferedSequence=combat.Sequence;bufferedAttackPress|=frozen.attack;bufferedAttackRelease|=frozen.released;
                    bufferedDodgePress|=frozen.dodge;bufferedRollPress|=frozenAuthor.roll;
                }
                else bufferedAttackPress=bufferedAttackRelease=bufferedDodgePress=bufferedRollPress=false;
                return;
            }
            if(dead){SetOwnedLock(true);return;}
            if(!AcceptInput){if(Time.timeScale>0)CancelForTransition();return;}
            if(combat.FinishCompletedAction()){ClearHitSamples();SetOwnedLock(false);}
            dodgeCooldown=Mathf.Max(0,dodgeCooldown-dt);counterRemaining=Mathf.Max(0,counterRemaining-dt);
            var k=Keyboard.current;var mouse=Mouse.current;var intent=HeroCombatInput.Read(AcceptInput);if(bufferedSequence!=combat.Sequence)bufferedAttackPress=bufferedAttackRelease=bufferedDodgePress=bufferedRollPress=false;
            bool pressed=bufferedAttackPress||intent.attack,held=intent.held,released=(bufferedAttackRelease&&(!bufferedAttackPress||!held))||intent.released;bufferedAttackPress=bufferedAttackRelease=false;
            requestedDodge=null;var extra=HeroAuthorInput.Read(AuthorControls&&AcceptInput);intent.dodge|=bufferedDodgePress;extra.roll|=bufferedRollPress;bufferedDodgePress=bufferedRollPress=false;if(AuthorControls){author.Select(extra);author.Tick(dt,Library);}
            if(FocusSequenceActive){
                intent.dodge=intent.guard=false;extra=default;
                if(TickFocusSequence(dt,ref pressed,ref held,ref released))return;
            }
            if(compactHit&&transientTime+dt>=hitStunDuration)
            {
                transientLock=false;SetOwnedLock(false);
                // Return pose ownership on the same frame that movement/actions
                // recover, including the newer authored uppercut/skill controls.
                if(WantsToMove(k)||pressed||intent.dodge||intent.guard||extra.upper||extra.skill||extra.roll||extra.crouch||extra.prone)ClearTransient();
            }
            if(AuthorControls&&!Busy&&movement.CanStartInteraction)
            {
                if(extra.crouch&&WorldInputBindings.Held("Sprint")&&localVelocity.sqrMagnitude>4){requestedDodge="AUTHOR_Slide";intent.dodge=true;}
                else if(extra.crouch){author.SetPosture(author.Posture==1?0:1,capsule);Crouching=author.Posture==1;}
                if(extra.prone){author.SetPosture(author.Posture==2?0:2,capsule);Crouching=author.Posture==1;}
            }
            else if(!AuthorControls&&k!=null&&k.cKey.wasPressedThisFrame&&!Busy&&movement.CanStartInteraction)ToggleCrouch();
            // Roll uses the same defensive admission as Ctrl, even while a basic attack owns movement.
            if(AuthorControls&&extra.roll){intent.dodge=true;}
            if(AuthorControls&&pressed&&transient=="HIT_KNOCKDOWN"){transient=null;transientLock=false;SetOwnedLock(false);TryAttack("ATK_STANDUP");pressed=false;}
            if(ResponsiveSword){
                quickAttackBuffer=Mathf.Max(0,quickAttackBuffer-dt);quickDodgeBuffer=Mathf.Max(0,quickDodgeBuffer-dt);
                if(intent.dodge)quickDodgeBuffer=.16f;
                if(Dodging&&pressed)quickAttackBuffer=.18f;
                intent.dodge|=quickDodgeBuffer>0;
            }
            if(intent.dodge&&!Dodging&&dodgeCooldown<=0&&movement.IsStableGrounded&&!movement.SwimmingActive&&combat.CanCancelIntoDodge&&!transientLock&&!(traversal&&(traversal.Active||traversal.RecoveringContact)))BeginDodge();
            if(Dodging)
            {
                // Consume only a fresh press in this perfect dodge's final window.
                if(counterRemaining<=0||intent.guard)dodgeCounterQueued=false;
                if(pressed&&!intent.guard&&HeroDodgeCounterWindow.Open(dodgeTime,activeDodgeDuration,dodgeId!=null&&dodgeId.StartsWith("PERFECT_DODGE_"),counterRemaining>0))dodgeCounterQueued=true;
                float dodgeStep=QuickDodge?HeroResponsiveSwordMotion.DodgeStep(dodgeTime,dt):HeroDodgeMotion.Step(dodgeTime,dt,activeDodgeDuration);
                dodgeTime+=dt;capsule.Move(dodgeVector*dodgeStep+Vector3.down*2*dt);
                if(QuickDodge&&dodgeTime>=.27f&&quickAttackBuffer>0&&!intent.guard){dodgeTime=-1;quickAttackBuffer=0;SetOwnedLock(false);TryAttack();return;}
                if(QuickDodge&&dodgeTime>=.31f&&WantsToMove(k)){dodgeTime=-1;SetOwnedLock(false);return;}
                if(dodgeTime>=activeDodgeDuration){bool counter=dodgeCounterQueued&&counterRemaining>0&&!intent.guard;dodgeCounterQueued=false;dodgeTime=-1;SetOwnedLock(false);if(counter)TryAttack();}return;
            }
            bool guardWanted=intent.guard&&equipment.SwordDrawn&&movement.IsStableGrounded&&!transientLock&&!(traversal&&(traversal.Active||traversal.RecoveringContact));
            if(guardWanted&&!combat.Active&&PrepareAuthorStanding())
            {
                if(!Guarding){if(transient=="EQUIP_SHEATHE"){transient=null;transientCallback=null;}Guarding=true;guardTime=0;guardUsed=false;RequestPresentation("GUARD_ENTER",false,null,0);}
                guardTime+=dt;SetOwnedLock(true);
            }
            else if(Guarding){Guarding=false;RequestPresentation("GUARD_EXIT",false,null,0);SetOwnedLock(false);}
            if((pressed||extra.upper||extra.skill)&&!combat.Active)
            {var special=AuthorControls?author.Attack(extra,!movement.IsStableGrounded,!ResponsiveSword&&movement.SprintingActive,localVelocity.sqrMagnitude>.1f):null;TryAttack(special);}
            if(ResponsiveSword&&combat.Active&&combat.Light&&combat.CanCancel&&!combat.HasQueuedCombo&&!pressed&&!held&&WantsToMove(k)){combat.Cancel();ClearHitSamples();SetOwnedLock(false);}
            if(combat.Active)
            {
                long old=combat.Sequence;float before=combat.Time;if(combat.Time<=combat.AimEnd)FaceAttackAim(dt);combat.Tick(dt,pressed,held,released);
                if(old!=combat.Sequence){AcquireAttackAim();FaceAttackAim(0,true);}
                if(ResponsiveSword&&combat.Light&&!FocusSequenceActive){
                    float from=old==combat.Sequence?before:0;
                    float advance=HeroResponsiveSwordMotion.AttackTravel(combat.Time,combat.Open,combat.Close)-HeroResponsiveSwordMotion.AttackTravel(from,combat.Open,combat.Close);
                    capsule.Move(transform.forward*Mathf.Max(0,advance));
                }
                if(AuthorControls&&combat.Id=="ATK_DASH"&&combat.Time<combat.Duration*.5f)capsule.Move(transform.forward*(3.8f*dt));
                if(old!=combat.Sequence)ClearHitSamples();
                SetOwnedLock(combat.Active);
            }
            if(transient!=null)
            {
                transientTime+=dt;
                if(!eventFired&&transientTime>=transientEventAt){eventFired=true;var cb=transientCallback;transientCallback=null;if(cb!=null){transientLock=false;SetOwnedLock(false);cb.Invoke();}}
                float duration=compactHit?hitPresentationDuration:Library.Duration(transient);
                if(transientTime>=duration){if(transient=="HIT_KNOCKDOWN"){transient="HIT_GETUP";transientTime=0;}else{ClearTransient();if(!combat.Active&&!Guarding)SetOwnedLock(false);}}
            }
        }
        public bool RequestPresentation(string id,bool lockMovement,Action onContact,float contactFraction=.55f)
        {
            if(!Library||Library.Find(id)==null||dead)return false;
            if(lockMovement&&(Busy||!movement.CanStartInteraction))return false;
            if(lockMovement&&AuthorControls&&id.StartsWith("INTERACT_")&&!PrepareAuthorStanding())return false;
            if(!lockMovement&&(transientLock||transientCallback!=null))return false;
            ClearTransient();transient=id;transientTime=0;transientCallback=onContact;eventFired=false;transientLock=lockMovement;
            transientEventAt=Library.Duration(id)*Mathf.Clamp01(contactFraction);if(lockMovement)SetOwnedLock(true);return true;
        }
        public bool HasPresentation(string id)=>transient==id;
        public bool PresentInteraction(string id,Action action)=>RequestPresentation(id,true,action,.55f);
        public float SpeedMultiplier=>AuthorControls?Prone?.16f:Crouching?.28f:1f:Crouching?.5f:1f;
        void ToggleCrouch()
        {
            if(Crouching&&!CanStand())return;
            Crouching=!Crouching;capsule.height=Crouching?1.15f:1.8f;capsule.center=Vector3.up*capsule.height*.5f;
            RequestPresentation(Crouching?"LOC_CROUCH_ENTER":"LOC_CROUCH_EXIT",false,null,0);
        }
        bool CanStand()
        {
            int n=Physics.OverlapCapsuleNonAlloc(transform.position+Vector3.up*.32f,transform.position+Vector3.up*1.48f,.28f,overlaps,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)if(overlaps[i]&&!overlaps[i].transform.IsChildOf(transform))return false;return true;
        }
        void RestoreHeight(){if(capsule){capsule.height=1.8f;capsule.center=Vector3.up*.9f;}}
        Vector3 ReadDirection()
        {
            var k=Keyboard.current;if(k==null)return -transform.forward;
            Vector2 input=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            var cam=Camera.main;var forward=cam?Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up).normalized:transform.forward;
            var dir=forward*input.y+Vector3.Cross(Vector3.up,forward)*input.x;return dir.sqrMagnitude>.01f?dir.normalized:-transform.forward;
        }
        public static string DirectionSuffix(Vector3 local)=>Mathf.Abs(local.x)>Mathf.Abs(local.z)?local.x>0?"R":"L":local.z>=0?"F":"B";
        void BeginDodge()
        {
            if(!PrepareAuthorStanding())return;
            bufferedAttackPress=bufferedAttackRelease=bufferedDodgePress=bufferedRollPress=dodgeCounterQueued=false;bool cancelCharge=combat.Charging;combat.Cancel();Guarding=false;ClearTransient();
            dodgeVector=ReadDirection();string suffix=DirectionSuffix(transform.InverseTransformDirection(dodgeVector));
            dodgeId=requestedDodge??(ResponsiveSword?"DODGE_"+suffix:AuthorControls&&movement.SprintingActive&&(suffix=="L"||suffix=="R")?"AUTHOR_DodgeSprint"+(suffix=="L"?"Left":"Right"):"DODGE_"+suffix);requestedDodge=null;
            activeDodgeDuration=AuthorControls?Mathf.Max(.35f,Library.Duration(dodgeId)):.52f;dodgeTime=0;dodgeCooldown=ResponsiveSword?.46f:.78f;quickDodgeBuffer=0;
            ClearHitSamples();if(legacyCombat)legacyCombat.SetHeroSwordWindow(false);
            pose.GetComponent<HeroAuthorSwordEffects>()?.Clear();SetOwnedLock(true);
            if(cancelCharge)pose.BeginOverlay("ATK_CHARGE_CANCEL",Library.Duration("ATK_CHARGE_CANCEL"));
        }
        public bool ResolveIncoming(ref DamageInfo damage)
        {
            blockedHit=false;if(!isActiveAndEnabled||dead||!damage.Source||!AcceptInput)return true;
            if(Dodging&&dodgeTime>=.045f&&dodgeTime<=(QuickDodge?.26f:.32f))
            {
                if(dodgeTime<=.15f&&counterRemaining<=0){counterRemaining=1.1f;counterId="ATK_BOOST_DODGE";PerfectDodges++;GetComponent<PlayerBattleActions>()?.QualifyAuthoredPerfectDodge(damage.Source);if(dodgeId.StartsWith("DODGE_"))dodgeId="PERFECT_"+dodgeId;}
                return false;
            }
            if(Guarding)
            {
                Vector3 toward=Vector3.ProjectOnPlane(damage.Source.transform.position-transform.position,Vector3.up);
                if(toward.sqrMagnitude>.001f&&Vector3.Dot(transform.forward,toward.normalized)>=.5f)
                {
                    RequestPresentation("GUARD_IMPACT",false,null,0);
                    if(guardTime<=.15f&&!guardUsed){guardUsed=true;counterRemaining=1.1f;counterId="ATK_BOOST_GUARD";PerfectBlocks++;return false;}
                    blockedHit=true;damage=new DamageInfo(Mathf.CeilToInt(damage.Amount*.35f),damage.Source,damage.HitPoint,damage.HitDirection,damage.ImpactKind);return true;
                }
            }
            return true;
        }
        void OnDamaged(Health target,DamageInfo damage)
        {
            if(target.IsDead||blockedHit){blockedHit=false;return;}
            CancelFocusSequence();
            bufferedAttackPress=bufferedAttackRelease=bufferedDodgePress=bufferedRollPress=dodgeCounterQueued=false;dodgeTime=-1;dodgeVector=Vector3.zero;bool broken=Guarding;if(traversal&&traversal.Active)traversal.Cancel();combat.Cancel();Guarding=false;ClearHitSamples();
            string side=DirectionSuffix(transform.InverseTransformDirection(-damage.HitDirection));
            if(compactHit&&movement)movement.ClearImpact();
            ClearTransient();
            compactHit=!broken&&damage.Amount<30&&damage.Source&&damage.Source.GetComponentInChildren<DemonKingCombatVisual>(true);
            string id=broken?"GUARD_BREAK":damage.Amount>=30?"HIT_KNOCKDOWN":damage.Amount>=20?"HIT_HEAVY":"HIT_LIGHT_"+side;
            if(compactHit)
            {
                // Keep side/back impacts directional; the frontal heavy pose is
                // reserved for a grounded frontal hit, as in the local version.
                id=side=="F"&&movement.IsStableGrounded?"HIT_HEAVY":"HIT_LIGHT_"+side;
                hitStunDuration=.28f;
                // Authored HIT_HEAVY is shorter than the old local clip: retain
                // the full stun rather than completing the accelerated clip early.
                hitPlaybackSpeed=id=="HIT_HEAVY"?Mathf.Clamp(Library.Duration(id)/hitStunDuration,1f,1.7f):1f;
                hitPresentationDuration=Mathf.Max(hitStunDuration,Library.Duration(id)/hitPlaybackSpeed);
                Vector3 away=Vector3.ProjectOnPlane(damage.HitDirection,Vector3.up);
                if(away.sqrMagnitude<.001f)away=Vector3.ProjectOnPlane(transform.position-damage.Source.transform.position,Vector3.up);
                if(movement.IsStableGrounded&&away.sqrMagnitude>.001f)movement.ApplyImpact(away.normalized*3.6f,.18f);
            }
            autoAction=baseAction=null;autoTime=baseActionTime=0;movingLandingAge=-1;pendingLanding=false;gait.Reset();
            if(movement.IsStableGrounded)author.ResetAir();
            transient=id;transientTime=0;transientLock=true;eventFired=true;transientCallback=null;SetOwnedLock(true);
            if(pose)pose.RestartAction(id);
        }
        bool HasDeathSupport()=>movement&&capsule&&movement.HasImmediateDeathSupport();
        void OnDied(Health target,DamageInfo damage)
        {
            bool supported=HasDeathSupport();
            CancelForTransition(true);dead=true;deathBeganFrame=Time.frameCount;deathRecoveryStep=traversal?traversal.ContactRecoveryStepCount:0;
            deathPresentation.Begin(Library,movement.SwimmingActive,supported&&HasDeathSupport());
            equipment.StowSword();SetOwnedLock(true);
        }
        void LateUpdate()
        {
            if(!pose||!pose.Ready||!movement)return;
            float dt=GetComponent<PlayerBattleActions>()?.PlayerDeltaTime??Time.deltaTime;if(Time.timeScale<=0||dt<=0){previousYaw=transform.eulerAngles.y;return;}
            // Sample even during combat/guard/traversal so recovery cannot inherit stale yaw.
            float yawRate=HeroGaitClock.SignedYawRate(previousYaw,transform.eulerAngles.y,dt);previousYaw=transform.eulerAngles.y;
            Vector3 actual=(transform.position-lastPosition)/dt;lastPosition=transform.position;
            if(traversalSyncFrame==Time.frameCount||(traversal&&traversal.RecoveringContact))actual=Vector3.up*actual.y;
            localVelocity=transform.InverseTransformDirection(actual);movementClock+=dt;
            // A death raised after Update has not consumed a recovery/physics step yet.
            float presentationDelta=dead&&Time.frameCount==deathBeganFrame&&traversal&&traversal.RecoveringContact&&traversal.ContactRecoveryStepCount==deathRecoveryStep?0:dt;
            if(HeroRecoveryPresentation.ShouldYield(transient,actual,dead||Dodging||combat.Active||Guarding||movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive||(traversal&&(traversal.Active||traversal.RecoveringContact)))){transient=null;transientCallback=null;transientLock=false;}
            if(transient=="EQUIP_SHEATHE"&&(dead||Dodging||combat.Active||Guarding||movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive||(traversal&&(traversal.Active||traversal.RecoveringContact)))){transient=null;transientCallback=null;}
            float sheathTime=transient=="EQUIP_SHEATHE"?transientTime:-1;
            string id;float time;float playbackRate=1;float frameStartTime=float.NaN;bool ordinaryLocomotion=false;float fade=-1;HeroActionDefinition definitionOverride=null;
            if(dead){bool supported=HasDeathSupport();if(!deathPresentation.Active)deathPresentation.Begin(Library,movement.SwimmingActive,supported);deathPresentation.TickAfterPhysics(presentationDelta,supported);id=deathPresentation.Id;time=deathPresentation.Time;fade=deathPresentation.BlendDuration;}
            else if(traversal&&traversal.Active){id=traversal.ActionId;time=traversal.ActionTime;definitionOverride=traversal.PoseDefinition;if(traversal.RopeContactFrame.IsActive){playbackRate=traversal.RopeContactFrame.PlaybackRate;frameStartTime=traversal.RopeContactFrame.StartTime;}else if(traversal.TopRopeExitActive){playbackRate=traversal.TopRopeExitRate;frameStartTime=traversal.TopRopeExitStartTime;}else if(traversal.GroundRopeExitActive){playbackRate=traversal.GroundRopeExitRate;frameStartTime=traversal.GroundRopeExitStartTime;}}
            else if(FocusOwnsMovement){id=FocusPhase==HeroFocusPhase.Return?"DODGE_B":"AUTHOR_CombatRun";time=FocusPhase==HeroFocusPhase.Return?Mathf.Min(focus.PhaseAge,.42f):focus.PhaseAge;}
            else if(Dodging){var dodge=HeroDodgePresentation.Resolve(Library,dodgeId,dodgeTime);id=dodge.Id;time=dodge.Time;}
            else if(combat.Active){id=combat.Id;time=combat.Time;frameStartTime=combat.PreviousTime;}
            else if(transient!=null&&sheathTime<0){id=transient;time=transientTime*(compactHit?hitPlaybackSpeed:1f);if(compactHit)playbackRate=hitPlaybackSpeed;}
            else if(Guarding){id="GUARD_HOLD";time=guardTime;}
            else {ordinaryLocomotion=true;id=Locomotion(dt,actual,yawRate);time=autoAction!=null?autoTime:movement.MantlingActive?movement.MantleElapsedTime:movement.LedgeHangingActive?(movement.LedgeHangElapsed<Library.Duration("WALL_LEDGE_GRAB")?movement.LedgeHangElapsed:movement.LedgeHangElapsed-Library.Duration("WALL_LEDGE_GRAB")):baseActionTime;if(id==baseAction&&(HeroGaitClock.IsGroundCycle(id)||HeroGaitClock.IsGroundTransition(id)))playbackRate=gait.PlaybackRate;if(autoAction==null&&gait.CompletedStart)fade=0;}
            if(!ordinaryLocomotion){gait.InterruptGroundTransitions();movingLandingAge=-1;pendingLanding=false;if(IsLanding(autoAction)){autoAction=null;autoTime=0;}wasGrounded=movement.IsStableGrounded;if(wasGrounded)airPeak=transform.position.y;else airPeak=Mathf.Max(airPeak,transform.position.y);}
            HeroGroundTurnFrame turnFrame=default;
            if(ordinaryLocomotion)
            {
                if(groundTurn.TryAdvance(sheathTime<0&&GroundTurnContext&&movement.PhysicalGroundContact&&Vector3.ProjectOnPlane(actual,Vector3.up).sqrMagnitude<.0064f,dt,out var turnId,out var turnTime,out var turnDelta,out turnFrame))
                {id=turnId;time=turnTime;presentationDelta=turnDelta;playbackRate=1;frameStartTime=turnFrame.StartTime;fade=-1;}
            }
            else groundTurn.Cancel();
            if(traversalExitBlendRemaining>0){if(ordinaryLocomotion){fade=.28f;traversalExitBlendRemaining=Mathf.Max(0,traversalExitBlendRemaining-presentationDelta);}else ClearTraversalExitBlend();}
            if(AuthorControls&&ordinaryLocomotion&&!movement.ClimbingActive&&!movement.MantlingActive)
            {
                author.Locomotion(Library,ref id,ref time,localVelocity,equipment.SwordDrawn,movement.SwimmingActive,movement.AuthorSwimVertical,presentationDelta);
                if(!movement.SwimmingActive)author.Air(Library,ref id,ref time,movement.IsStableGrounded,movement.AuthorJumpIndex,actual.y,equipment.SwordDrawn,new Vector2(actual.x,actual.z).magnitude,presentationDelta);else author.ResetAir();
                if(id.StartsWith("AIR_LAND_")||id=="AUTHOR_DoubleLand"||id=="AUTHOR_WeaponLand"||id=="AUTHOR_LandWalk"||id=="AUTHOR_LandRun"){autoAction=null;autoTime=0;}
            }
            if(compactHit)fade=.04f;
            pose.SetSwordVisible(equipment&&equipment.SwordDrawn);pose.Sample(id,time,presentationDelta,equipment&&equipment.SwordDrawn,fade:fade,playbackRate:playbackRate,frameStartTime:frameStartTime,locomotionVelocity:localVelocity,definitionOverride:definitionOverride,movingLandingAge:movingLandingAge,heavyLanding:movingLandingHeavy,preserveWorldContacts:movement.SwimmingActive||movement.ClimbingActive||movement.MantlingActive||(traversal&&((traversal.Active&&!traversal.UsesGroundApproachPose)||traversal.RecoveringContact)),groundTurn:turnFrame,ropeContact:traversal?traversal.RopeContactFrame:default,sheathTime:sheathTime,actionInstance:combat.Active&&id==combat.Id?combat.Sequence:0);
            if(traversal&&traversal.Active){traversal.ApplyContactPose(pose);pose.CommitPose();}
            if(dead)deathPresentation.MarkPresented(pose.CurrentAction);
            legacyCombat.SetHeroSwordWindow(combat.HitWindow&&AcceptInput);
            if(combat.Active&&AcceptInput)ProcessBlade();
        }
        string Locomotion(float dt,Vector3 velocity,float yawRate)
        {
            bool grounded=movement.IsStableGrounded,swimming=movement.SwimmingActive,climbing=movement.ClimbingActive;
            float planarSpeed=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
            bool moving=planarSpeed>(wasMoving?.08f:.16f);
            if(movingLandingAge>=0){if(movingLandingAge>=HeroPoseDriver.MovingLandingDuration(movingLandingHeavy))movingLandingAge=-1;else movingLandingAge+=dt;}
            bool ordinaryGroundMode=!swimming&&!climbing&&!movement.MantlingActive;
            if(!ordinaryGroundMode)pendingLanding=false;
            else if(!grounded&&!wasSwimming&&!wasClimbing&&movement.HasCurrentGroundContactSample)pendingLanding=true;
            // Accepted jumps still touch the floor during anticipation; that is not a landing.
            bool landingContact=pendingLanding&&grounded&&movement.PhysicalGroundContact&&!movement.GroundJumpActive;
            bool awaitingContact=pendingLanding&&!landingContact&&!movement.GroundJumpActive;
            string desired="LOC_IDLE";
            if(movement.MantlingActive)desired="WALL_LEDGE_TOP";
            else if(movement.LedgeHangingActive)desired=movement.LedgeHangElapsed<Library.Duration("WALL_LEDGE_GRAB")?"WALL_LEDGE_GRAB":"WALL_LEDGE_HANG";
            else if(climbing)desired=Mathf.Abs(localVelocity.x)>.15f?localVelocity.x>0?"CLIMB_SIDE_R":"CLIMB_SIDE_L":velocity.y>.05f?"CLIMB_UP":velocity.y<-.05f?"CLIMB_DOWN":"CLIMB_IDLE";
            else if(swimming)desired=moving?movement.FastSwimmingActive?"WATER_SWIM_FAST":"WATER_SWIM":"WATER_TREAD";
            else if(!grounded||awaitingContact)desired=velocity.y>.1f?"AIR_RISE":"AIR_FALL";
            else if(Crouching)desired=moving?"LOC_CROUCH_WALK":"LOC_CROUCH_IDLE";
            else if(moving&&movement.IsWading)desired="LOC_WADE";
            // Stair risers retain the ordinary distance-driven gait; foot placement adapts to geometry.
            else if(equipment&&equipment.SwordDrawn)desired=moving?gait.SelectCombatMove(localVelocity):"COMBAT_IDLE";
            else if(moving)desired=gait.SelectOrdinaryGroundCycle(localVelocity);
            if(HeroSwimClock.IsStroke(desired)){if(!wasSwimming||!wasMoving)swimGait.Reset();baseAction=desired;baseActionTime=swimGait.Step(desired,Library,dt);}
            else if(HeroGaitClock.IsGroundCycle(desired)){bool eligible=grounded&&wasGrounded&&!wasSwimming&&!wasClimbing&&!swimming&&!climbing&&!Crouching&&!movement.IsWading&&!movement.ImpactActive&&autoAction==null&&HeroGaitClock.AllowsForwardStart(planarSpeed,localVelocity.z,yawRate,movement.GroundAngle);baseActionTime=gait.StepPresented(desired,Library,planarSpeed,dt,eligible&&!movement.SprintingActive,eligible);baseAction=desired=gait.PresentedAction;}
            else if(baseAction!=desired){baseAction=desired;baseActionTime=0;}else baseActionTime+=dt;
            if(autoAction!=null){autoTime+=dt;float limit=Library.Duration(autoAction);bool takeoff=autoAction=="AIR_TAKEOFF"||autoAction=="AIR_RUN_JUMP";bool descending=takeoff&&!grounded&&velocity.y<-.2f;if(descending||autoTime>=limit){autoAction=null;if(takeoff)baseActionTime=descending?0:Mathf.Max(0,autoTime-limit);}}
            // Physical transitions take precedence over decorative turning and starts.
            if(swimming&&!wasSwimming)Auto("WATER_ENTER");
            else if(!swimming&&wasSwimming&&grounded)Auto("WATER_EXIT_SHORE");
            else if(climbing&&!wasClimbing)Auto("CLIMB_ENTER");
            else if(!climbing&&wasClimbing&&!movement.MantlingActive)Auto("CLIMB_DETACH");
            else if(landingContact){Auto(airPeak-transform.position.y>3?"AIR_LAND_HEAVY":"AIR_LAND_LIGHT");pendingLanding=false;airPeak=transform.position.y;}
            else if(!grounded&&wasGrounded&&!climbing&&!swimming&&!movement.MantlingActive)
            {
                // Walking off support is a fall; a real jump starts during its grounded anticipation.
                if(movement.GroundJumpActive)Auto(movement.GroundJumpStartedRunning?"AIR_RUN_JUMP":"AIR_TAKEOFF");
                else autoAction=null;
            }
            else if(swimming&&moving&&!wasMoving)Auto("WATER_START");
            else if(swimming&&!moving&&wasMoving)Auto("WATER_STOP");
            
            
            else if(autoAction==null&&swimming&&HeroGaitClock.SelectTurn(yawRate,true)!=null)Auto(HeroGaitClock.SelectTurn(yawRate,true));
            else if(!Library.stationaryTurns&&autoAction==null&&grounded&&!awaitingContact&&!moving&&!climbing&&!movement.MantlingActive&&!equipment.SwordDrawn&&HeroGaitClock.SelectTurn(yawRate,false)!=null)Auto(HeroGaitClock.SelectTurn(yawRate,false));
            if(movement.MantlingActive||movement.LedgeHangingActive)autoAction=null;
            bool landingGait=grounded&&!movement.MantlingActive&&!climbing&&!swimming&&HeroGaitClock.IsOrdinaryCycle(desired);
            if(!AuthorControls&&IsLanding(autoAction)&&landingGait){movingLandingHeavy=autoAction=="AIR_LAND_HEAVY";movingLandingAge=autoTime<HeroPoseDriver.MovingLandingDuration(movingLandingHeavy)?autoTime:-1;autoAction=null;autoTime=0;}
            if(!landingGait)movingLandingAge=-1;
            if(autoAction!=null||(!HeroGaitClock.IsGroundCycle(desired)&&!HeroGaitClock.IsGroundTransition(desired)))gait.InterruptGroundTransitions();
            if(!grounded||pendingLanding)airPeak=Mathf.Max(airPeak,transform.position.y);
            wasGrounded=grounded;wasSwimming=swimming;wasClimbing=climbing;wasMoving=moving;
            return autoAction??desired;
        }
        void Auto(string id){autoAction=id;autoTime=0;}
        void ClearHitSamples(){hitTargets.Clear();hitSampleValid=false;sampledSequence=-1;bladeSweep.Reset();}
        void ProcessBlade()
        {
            if(sampledSequence!=combat.Sequence){hitTargets.Clear();sampledSequence=combat.Sequence;}
            bladeSweep.Sample(pose,combat,Sweep);
        }
        void Sweep(Vector3 a,Vector3 b)
        {
            int n=Physics.OverlapCapsuleNonAlloc(a,b,.115f,overlaps,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)
            {
                var collider=overlaps[i];if(!collider||collider.transform.IsChildOf(transform))continue;
                var target=collider.GetComponentInParent<Health>();if(!target||target.IsDead||hitTargets.Contains(target)||!target.CanReceiveDamageFrom(gameObject))continue;
                hitTargets.Add(target);int amount=Mathf.RoundToInt((legacyCombat.HeroBaseDamage+(GetComponent<BaselineLoadout>()?.DamageBonus??0))*combat.Multiplier);
                var damage=new DamageInfo(amount,gameObject,collider.ClosestPoint((a+b)*.5f),target.transform.position-transform.position,ImpactKindForAction(combat.Id));
                if(target.TryApplyDamage(damage)){pose.GetComponent<HeroAuthorSwordEffects>()?.Contact(damage.HitPoint,transform.position-damage.HitPoint);SuccessfulHits++;legacyCombat.RegisterHeroHit(target.IsDead,damage.HitPoint);}
            }
        }
    }
}


