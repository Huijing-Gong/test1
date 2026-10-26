using UnityEngine;

namespace SamProduction.BossArena
{
    [RequireComponent(typeof(CharacterController), typeof(ArenaFighter))]
    public sealed partial class ArenaPlayerController : MonoBehaviour
    {
        public ArenaFighter fighter;
        public CharacterController capsule;
        public ArenaThirdPersonCamera view;
        public ArenaTrainingAvatar avatar;
        public ArenaGuardController guard;
        public KingOfGraphic.Hero.HeroPoseDriver Hero => GetComponent<ArenaCurrentCharacter>()?.Hero;
        public readonly KingOfGraphic.Hero.HeroAuthoredControls Author=new KingOfGraphic.Hero.HeroAuthoredControls();
        public bool AuthorEnabled=>Hero&&Hero.library.authoredControls;
        public int AuthorJumpIndex{get;private set;}
        public float AuthorJumpAge{get;private set;}
        public float AuthorVertical=>verticalSpeed;
        readonly KingOfGraphic.Hero.HeroCombatClock heroCombat=new KingOfGraphic.Hero.HeroCombatClock();
        readonly KingOfGraphic.Hero.HeroBladeSweep heroBlade=new KingOfGraphic.Hero.HeroBladeSweep();
        ArenaSession strikeArena;
        bool PoseReady=>Hero?Hero.Ready&&Hero.WeaponReady:avatar!=null&&avatar.isActiveAndEnabled;
        float heroStowTime=-1;
        string heroCounterId="ATK_BOOST_DODGE",heroHurtId="HIT_LIGHT_F",heroDodgeId="DODGE_B";
        float heroHurtDuration;
        public Vector3 WeaponBase=>Hero&&Hero.WeaponReady?Hero.WeaponBase:avatar?avatar.WeaponBase:transform.position;
        public Transform DefenseContactAnchor=>Hero?Hero.grip:avatar?avatar.DefenseContactAnchor:null;
        KingOfGraphic.Hero.HeroDodgeSample DodgePresentation=>KingOfGraphic.Hero.HeroDodgePresentation.Resolve(Hero?Hero.library:null,dodgeState.PerfectStrike>0?heroDodgeId.Replace("DODGE_","PERFECT_DODGE_"):heroDodgeId,dodgeTime);
        public string HeroMotionId=>focus.Travelling?(focus.Phase==KingOfGraphic.Hero.HeroFocusPhase.Return?"DODGE_B":"AUTHOR_CombatRun"):heroStowTime>=0?"EQUIP_SHEATHE":dodgeTime>=0?DodgePresentation.Id:heroCombat.Active?heroCombat.Id:hurtTime>0?heroHurtId:null;
        public float HeroMotionTime=>focus.Travelling?(focus.Phase==KingOfGraphic.Hero.HeroFocusPhase.Return?Mathf.Min(focus.PhaseAge,.42f):focus.PhaseAge):heroStowTime>=0?heroStowTime:dodgeTime>=0?DodgePresentation.Time:heroCombat.Active?heroCombat.Time:hurtTime>0?heroHurtDuration-hurtTime:0;
        public long HeroMotionInstance=>heroStowTime<0&&dodgeTime<0&&heroCombat.Active?heroCombat.Sequence:0;
        public float HeroMotionPreviousTime=>heroStowTime<0&&dodgeTime<0&&heroCombat.Active?heroCombat.PreviousTime:float.NaN;
        void SyncHeroAttack()
        {
            attackDuration=heroCombat.Duration;attackOpen=heroCombat.Open>=0?heroCombat.Open:float.PositiveInfinity;attackClose=heroCombat.Close;
            currentSwordDamage=swordDamage*(heroCombat.Id!=null&&heroCombat.Id.StartsWith("ATK_BOOST")?perfectCounterDamageMultiplier:heroCombat.Multiplier);combo=heroCombat.Combo;
        }
        public float perfectCounterSeconds = 1.1f, perfectCounterDamageMultiplier = 1.3f;
        public float walkSpeed = 3.1f, runSpeed = 5.2f, acceleration = 22;
        public float dodgeDuration = .54f, dodgeCooldown = .78f, dodgeSpeed = 8.5f;
        public float invulnerableStart = .045f, invulnerableEnd = .32f;
        public float attackDuration = .67f, attackOpen = .18f, attackClose = .38f;
        public float swordRadius = .115f, swordDamage = 18f, swordPush = .45f;
        public string ActionName => fighter != null && fighter.Dead ? "Dead" : dodgeTime >= 0 ? "Dodge" : attackTime >= 0 ? "Sword" : hurtTime > 0 ? "Hurt" : Guarding ? "Block" : Speed > .15f ? "Move" : "Ready";
        public bool Guarding => guard != null && guard.IsGuarding;
        public bool SwordDrawn { get; private set; }
        public const float SwordIdleSeconds = .45f;
        private float swordIdleRemaining;
        private void StowSword(bool clearTarget = false)
        {
            heroStowTime=-1;HideSwordAfterSheathContact();
            if (clearTarget) view?.ClearTargetLock();

        }
        private void HideSwordAfterSheathContact()
        {
            SwordDrawn=false;swordIdleRemaining=0;
            GetComponent<ArenaCurrentCharacter>()?.RefreshWeaponVisibility();
        }
        private void TickSwordPresentation(float delta)
        {
            if(FocusActive||attackTime>=0||Guarding||PerfectCounterReady||counterQueued||hurtTime>0||dodgeTime>=0)
            {if(SwordDrawn)swordIdleRemaining=SwordIdleSeconds;heroStowTime=-1;return;}
            if(heroStowTime>=0)
            {
                if(!Hero){StowSword();return;}
                heroStowTime+=delta;float duration=Hero.library.Duration("EQUIP_SHEATHE");
                if(SwordDrawn&&heroStowTime>=duration*KingOfGraphic.Hero.HeroPoseDriver.SheathHideFraction)HideSwordAfterSheathContact();
                if(heroStowTime>=duration)heroStowTime=-1;
                return;
            }
            if(!SwordDrawn)return;
            swordIdleRemaining-=delta;
            if(swordIdleRemaining<=0){if(AuthorEnabled)StowSword();else if(Hero)heroStowTime=0;else StowSword();}
        }
        public bool PerfectCounterReady => perfectCounterTime > 0;
        public float PerfectCounterRemaining => Mathf.Max(0, perfectCounterTime);
        public bool CounterQueued => counterQueued && PerfectCounterReady;
        public bool CounterInputWindowOpen => KingOfGraphic.Hero.HeroDodgeCounterWindow.Open(dodgeTime,dodgeDuration,dodgeState.Active&&dodgeState.PerfectStrike>0,PerfectCounterReady) && !lastGuardHeld && hurtTime <= 0 &&
            capsule != null && capsule.enabled && isActiveAndEnabled && fighter != null && !fighter.Dead &&
            PoseReady;
        public bool CounterBladeVisible => isActiveAndEnabled && fighter != null && !fighter.Dead &&
            counterSword && attackTime >= (Hero?heroCombat.SwingCue:.15f) && attackTime <= attackClose;
        public long CounterSwordStrike => counterSword && attackTime >= 0 ? strike : 0;
        public const float CounterInputWindow = KingOfGraphic.Hero.HeroDodgeCounterWindow.Duration;
        public ArenaDodgeState DodgeState => dodgeState;
        private readonly ArenaDodgeState dodgeState = new ArenaDodgeState();
        private float perfectCounterTime, currentSwordDamage;
        private bool lastGuardHeld;
        public float Speed { get; private set; }
        public Vector3 PlanarVelocity { get; private set; }
        public bool Dodging => dodgeTime >= 0;
        public float AttackTime => attackTime;
        public Vector3 WeaponTip => Hero&&Hero.WeaponReady?Hero.WeaponTip:avatar != null ? avatar.WeaponTip : transform.position + Vector3.up;
        private Vector3 velocity, dodgeDirection, attackDirection, previousBase, previousTip;
        private Quaternion attackStartRotation, attackTargetRotation;
        private float verticalSpeed, dodgeTime = -1, dodgeRecovery, attackTime = -1, hurtTime;
        private long strike;
        private int combo;
        private bool queuedAttack, bladeSampleValid;
        // Input intent is separate from the existing timed damage reward.
        private bool counterQueued, counterSword;
        private long counterDodgeId;
        private ArenaDefenseFeedback counterFeedback;

        private void Awake()
        {
            if (fighter == null) fighter = GetComponent<ArenaFighter>();
            if (capsule == null) capsule = GetComponent<CharacterController>();
            if (avatar == null) avatar = GetComponentInChildren<ArenaTrainingAvatar>();
            if (guard == null) guard = GetComponent<ArenaGuardController>() ?? gameObject.AddComponent<ArenaGuardController>();
            fighter.Damaged += OnDamaged;
            fighter.Avoided += OnAvoided;
            fighter.DefenseResolved += OnDefenseResolved;
            fighter.ResolveDefense = ResolveGuard;
        }
        private void OnDestroy()
        {
            SuspendDefense(); counterFeedback = null;
            if (fighter == null) return;
            fighter.Damaged -= OnDamaged; fighter.Avoided -= OnAvoided;
            fighter.DefenseResolved -= OnDefenseResolved;
            if (fighter.ResolveDefense == ResolveGuard) fighter.ResolveDefense = null;
        }
        private void OnDisable() { SuspendDefense(); StowSword(true); }

        private ArenaDefenseResult ResolveGuard(ArenaHit hit)
        {
            if (guard == null || dodgeTime >= 0) return ArenaDefenseResult.None;
            var result = guard.ResolveHit(hit);
            if (!result.Blocked) return ArenaDefenseResult.None;
            return new ArenaDefenseResult(result.Perfect ? ArenaDefenseOutcome.PerfectBlock : ArenaDefenseOutcome.Block,
                result.Hit.damage / hit.damage, hit.push > 0 ? result.Hit.push / hit.push : 0);
        }
        private void OnAvoided(ArenaHit hit)
        {
            if (dodgeState.TryConfirmPerfect(hit)) fighter.MarkPerfectDodgeDuringAvoided(hit.strike);
        }
        private void OnDefenseResolved(ArenaDefenseEvent value)
        {
            if(value.result.IsBlock&&!fighter.Dead)GetComponent<ArenaCurrentCharacter>()?.PresentHeroResponse("GUARD_IMPACT");
            if(value.result.outcome==ArenaDefenseOutcome.PerfectDodge&&!fighter.Dead)BeginFocus(value.hit.source);
            if (value.result.IsPerfect && !fighter.Dead && !FocusActive)
                {perfectCounterTime = Mathf.Max(perfectCounterTime, perfectCounterSeconds);heroCounterId=value.result.outcome==ArenaDefenseOutcome.PerfectBlock?"ATK_BOOST_GUARD":"ATK_BOOST_DODGE";}
        }

        public void SuspendDefense()
        {
            CancelActions(); perfectCounterTime = 0; guard?.SuspendGuard();
        }

        private void OnDamaged(ArenaHit hit)
        {
            if (fighter.Dead) { CancelActions(); StowSword(true); return; }
            if (fighter.LastDefense.IsBlock) return;
            heroHurtId=Guarding?"GUARD_BREAK":hit.push>3?"HIT_KNOCKDOWN":hit.damage>=25?"HIT_HEAVY":"HIT_LIGHT_"+KingOfGraphic.Hero.HeroPoseDriver.DirectionSuffix(transform.InverseTransformDirection(-hit.direction));
            heroHurtDuration=Hero?Hero.library.Duration(heroHurtId):.26f;hurtTime=heroHurtDuration;
            CancelActions();
            avatar?.ReactToHit();
        }

        public void ResetPlayer(Vector3 position, Quaternion rotation, bool guardHeldOnReset = false)
        {
            if (capsule == null) capsule = GetComponent<CharacterController>();
            bool enabled = capsule != null && capsule.enabled;
            if (capsule != null) capsule.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (capsule != null) capsule.enabled = enabled;
            Author.Reset(capsule);AuthorJumpIndex=0;AuthorJumpAge=0;
            velocity = Vector3.zero; verticalSpeed = -2; Speed = hurtTime = dodgeRecovery = 0;
            PlanarVelocity=Vector3.zero;
            combo = 0; strike = 0; StowSword(true);
            perfectCounterTime = 0; currentSwordDamage = swordDamage;
            CancelActions();
            dodgeState.Reset(); guard?.ResetGuard(guardHeldOnReset || lastGuardHeld);
            if (fighter != null) fighter.Invulnerable = false;
            avatar?.ResetPose();GetComponent<ArenaCurrentCharacter>()?.ResetHeroPresentation();GetComponent<ArenaCurrentCharacter>()?.PresentHeroResponse("RESPAWN_RECOVER");
        }

        private void CancelActions()
        {
            CancelFocus();
            GetComponent<ArenaCurrentCharacter>()?.CancelGroundTurn();heroCombat.Cancel();heroBlade.Reset();heroStowTime=-1;
            if(Hero)Hero.GetComponent<KingOfGraphic.Hero.HeroAuthorSwordEffects>()?.Clear();
            ClearCounterRequest(); counterSword = false;
            counterFeedback?.SetCounterBlade(false, Vector3.zero, Vector3.zero);
            attackTime = dodgeTime = -1;
            dodgeState.Cancel(); guard?.SuspendGuard();
            bladeSampleValid = queuedAttack = false;
            if (fighter != null) fighter.Invulnerable = false;
        }

        private void ClearCounterRequest() { counterQueued = false; counterDodgeId = 0; }

        public void Tick(ArenaCommand command, float delta, ArenaSession arena)
        {
            if (delta <= 0) return;
            // Missing actor references are control detachment, not a paused clock.
            // Never retain an old request/reward to replay when a reference returns.
            if (fighter == null || capsule == null) { SuspendDefense(); return; }
            var feedback = arena != null ? arena.defenseFeedback : null;
            if (counterFeedback != feedback)
            {
                counterFeedback?.SetCounterBlade(false, Vector3.zero, Vector3.zero);
                ClearCounterRequest(); counterFeedback = feedback;
            }
            lastGuardHeld = command.guardHeld;
            bool physicalGuardPressed = command.guardPressed;
            bool cinematic=arena!=null && arena.view!=null && arena.view.CinematicActive;
            if(cinematic) { command=default;CancelActions();perfectCounterTime=0;velocity=Vector3.zero;fighter.ClearKnockback(); }
            
            dodgeRecovery = Mathf.Max(0, dodgeRecovery - delta);
            perfectCounterTime = Mathf.Max(0, perfectCounterTime - delta);
            float priorHurt=hurtTime;hurtTime = Mathf.Max(0, hurtTime - delta);
            if(Hero&&priorHurt>0&&hurtTime<=0&&heroHurtId=="HIT_KNOCKDOWN"){heroHurtId="HIT_GETUP";heroHurtDuration=Hero.library.Duration(heroHurtId);hurtTime=heroHurtDuration;}
            bool active = arena != null && (arena.Active||FocusActive&&arena.State==ArenaRoundState.Victory) && !fighter.Dead && !cinematic;
            if (!active) { Author.ClearTransition();AuthorJumpIndex=0;command = default; CancelActions(); StowSword(true); perfectCounterTime = 0; }
            if(active && Hero && heroCombat.FinishCompletedAction())
            { attackTime=-1;counterSword=false;bladeSampleValid=false;heroBlade.Reset(); }
            if(FocusActive&&TickFocus(ref command,delta,arena))return;
            if (view != null) view.TickLook(command.look, active && command.toggleLock);
            if (!PerfectCounterReady || command.guardHeld || hurtTime > 0) ClearCounterRequest();
            Vector3 forward = view != null ? view.PlanarForward : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 input = Vector3.ClampMagnitude(forward * command.move.y + right * command.move.x, 1);
            if(AuthorEnabled&&active)
            {
                Author.Select(command.author);Author.Tick(delta,Hero.library);AuthorJumpAge+=delta;
                if(capsule.isGrounded&&verticalSpeed<=0)AuthorJumpIndex=0;
                if(hurtTime<=0&&attackTime<0&&dodgeTime<0&&!Guarding&&capsule.isGrounded)
                {
                    if(command.author.crouch&&!(command.run&&input.sqrMagnitude>.1f))Author.SetPosture(Author.Posture==1?0:1,capsule);
                    if(command.author.prone)Author.SetPosture(Author.Posture==2?0:2,capsule);
                }
                if(command.author.jump&&hurtTime<=0&&attackTime<0&&dodgeTime<0&&!Guarding&&(capsule.isGrounded||SwordDrawn&&AuthorJumpIndex==1)&&
                    (Author.Posture==0||Author.SetPosture(0,capsule)))
                {Author.ClearTransition();AuthorJumpIndex=capsule.isGrounded?1:2;AuthorJumpAge=0;verticalSpeed=Mathf.Sqrt(2*24*1.25f);}
                command.dodge|=command.author.roll||command.author.crouch&&command.run&&input.sqrMagnitude>.1f;
                if(command.attack&&heroHurtId=="HIT_KNOCKDOWN"&&hurtTime>0){hurtTime=0;StartAttack(arena,"ATK_STANDUP");command.attack=false;}
            }

            if (active && command.dodge && dodgeTime<0 && dodgeRecovery <= 0 && hurtTime <= 0 && (attackTime < 0 || (Hero?heroCombat.CanCancelIntoDodge:attackTime >= .43f)) && (!AuthorEnabled||Author.Posture==0||Author.SetPosture(0,capsule)))
            {
                bool cancelCharge=heroCombat.Charging;CancelActions();if(cancelCharge&&Hero)Hero.BeginOverlay("ATK_CHARGE_CANCEL",Hero.library.Duration("ATK_CHARGE_CANCEL"));heroStowTime=-1;dodgeTime = 0; dodgeRecovery = Responsive?.46f:dodgeCooldown;if(Hero)dodgeDuration=.52f;
                dodgeState.TryBegin(dodgeDuration, invulnerableStart, invulnerableEnd);
                dodgeDirection = input.sqrMagnitude > .01f ? input.normalized : -transform.forward;
                heroDodgeId="DODGE_"+KingOfGraphic.Hero.HeroPoseDriver.DirectionSuffix(transform.InverseTransformDirection(dodgeDirection));
                if(AuthorEnabled){Author.ClearTransition();
                    if(command.author.crouch&&command.run)heroDodgeId="AUTHOR_Slide";
                    else if(command.author.roll||!SwordDrawn)heroDodgeId="DODGE_"+KingOfGraphic.Hero.HeroPoseDriver.DirectionSuffix(transform.InverseTransformDirection(dodgeDirection));
                    else if(!Responsive&&command.run&&(heroDodgeId=="DODGE_L"||heroDodgeId=="DODGE_R"))heroDodgeId="AUTHOR_DodgeSprint"+(heroDodgeId=="DODGE_L"?"Left":"Right");
                    dodgeDuration=Mathf.Max(.35f,Hero.library.Duration(heroDodgeId));dodgeState.Cancel();dodgeState.TryBegin(dodgeDuration,invulnerableStart,Responsive?.26f:invulnerableEnd);}
                arena.Record("player_dodge_started", "dodge", fighter, 0, transform.position);
            }
            if (active && (command.attack||command.author.upper||command.author.skill) && !(command.guardHeld && SwordDrawn) && dodgeTime < 0 && hurtTime <= 0)
            {
                if (attackTime < 0) StartAttack(arena,AuthorEnabled?Author.Attack(command.author,!capsule.isGrounded,!Responsive&&command.run,input.sqrMagnitude>.1f):null);
                else if (!Hero && attackTime > .30f) queuedAttack = true;
            }

            // ArenaCommand.attack is a fresh hardware edge; WithoutEdges removes it.
            // An earlier guard reward must never promote an ordinary current dodge.
            if (active && command.attack && !counterQueued && CounterInputWindowOpen)
            {
                counterQueued = true; counterDodgeId = dodgeState.DodgeId;
                arena.Record("perfect_counter_queued", "player_sword", fighter, dodgeState.PerfectStrike, transform.position);
            }

            bool guardEligible=active&&SwordDrawn&&dodgeTime<0&&attackTime<0&&hurtTime<=0;
            bool canAuthorGuard=!AuthorEnabled||Author.Posture==0||(guardEligible&&command.guardHeld&&Author.SetPosture(0,capsule));
            if(canAuthorGuard&&command.guardHeld&&AuthorEnabled)Author.ClearTransition();
            guard?.TickInput(lastGuardHeld, physicalGuardPressed,
                active && canAuthorGuard && SwordDrawn && dodgeTime < 0 && attackTime < 0 && hurtTime <= 0, delta);
            dodgeState.Advance(delta, active);
            float beforeAttack = attackTime;
            bool completedDodge=false;
            if (dodgeTime >= 0)
            {
                float dodgeStep=Responsive?KingOfGraphic.Hero.HeroResponsiveSwordMotion.DodgeStep(dodgeTime,delta):KingOfGraphic.Hero.HeroDodgeMotion.Step(dodgeTime,delta,dodgeDuration);
                dodgeTime += delta;
                fighter.Invulnerable = active && dodgeState.IsInvulnerable;
                velocity = dodgeDirection * (dodgeStep / delta);
                if (dodgeTime >= dodgeDuration)
                {
                    dodgeTime = -1; fighter.Invulnerable = false;completedDodge=true;
                    bool startCounter = counterQueued && counterDodgeId == dodgeState.DodgeId &&
                        active && PerfectCounterReady && !command.guardHeld && hurtTime <= 0 && attackTime < 0 &&
                        capsule.enabled && isActiveAndEnabled && PoseReady;
                    // Even a failed admission consumes the request, never retries it.
                    ClearCounterRequest();
                    if (startCounter) StartAttack(arena);
                }
            }
            else
            {
                fighter.Invulnerable = FocusActive;
                float speed = Guarding ? walkSpeed * guard.MovementMultiplier : command.run ? runSpeed : walkSpeed;
                if(AuthorEnabled){speed=command.run?Hero.library.authoredSprintSpeed:command.author.walk?Hero.library.authoredWalkSpeed:Hero.library.authoredRunSpeed;speed=Mathf.Min(speed,Author.SpeedLimit);if(Guarding)speed*=guard.MovementMultiplier;}
                Vector3 wanted = input * speed;
                if (hurtTime > 0 || !active) wanted = Vector3.zero;
                if (attackTime >= 0)
                {
                    if(Hero)
                    {
                        long old=heroCombat.Sequence;heroCombat.Tick(delta,command.attack,command.attackHeld,command.attackReleased);
                        if(heroCombat.Sequence!=old&&heroCombat.Active)
                        {
                            // A chain or charge transition continues the current commitment.
                            // Replaying the first segment's aim would pull the root back.
                            attackStartRotation = attackTargetRotation = transform.rotation;AimArenaAttack(arena,true);
                            SyncHeroAttack();strike=arena.NewStrike();bladeSampleValid=false;beforeAttack=0;
                            arena.Record("hero_action_started",heroCombat.Id,fighter,strike,transform.position);
                        }
                        attackTime=heroCombat.Active?heroCombat.Time:-1;
                    }
                    else attackTime += delta;
                    // Commit during anticipation; never snap the root at the input edge or
                    // steer the blade back toward a target during the damaging swing.
                    float anticipationEnd=Hero?(heroCombat.AimEnd>=0?heroCombat.AimEnd:float.PositiveInfinity):attackOpen;
                    if (attackTime >=0 && attackTime <= anticipationEnd && !heroCombat.Charging)
                    {
                        float aim = Mathf.SmoothStep(0, 1, Mathf.Clamp01(attackTime / Mathf.Max(.01f, anticipationEnd * .85f)));
                        transform.rotation = Quaternion.Slerp(attackStartRotation, attackTargetRotation, aim);
                        attackDirection = transform.forward;
                    }
                    wanted = Hero ? heroCombat.Id=="ATK_DASH"&&heroCombat.Time<heroCombat.Duration*.5f?transform.forward*3.8f:Vector3.zero : input * .45f;
                    if (!Hero && attackTime >= .15f && attackTime < .31f) wanted += attackDirection * 1.75f;
                }
                velocity = Vector3.MoveTowards(velocity, wanted, acceleration * delta);
            }
            bool canFace=active&&dodgeTime<0&&attackTime<0&&hurtTime<=0;
            Vector3 face=canFace?input:Vector3.zero;
            if(canFace&&view!=null&&view.LockedOn&&view.target!=null)face=Vector3.ProjectOnPlane(view.target.position-transform.position,Vector3.up);
            if(Hero)GetComponent<ArenaCurrentCharacter>().ObserveGroundFacing(face,canFace&&!Guarding&&!command.guardHeld&&!command.run&&input.sqrMagnitude<.0001f&&Speed<.08f&&fighter.Knockback.sqrMagnitude<.0001f,delta);
            if(canFace&&face.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(face),640*delta);

            if (capsule.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed = Mathf.Max(-28, verticalSpeed - 24 * delta);
            Vector3 oldPosition = transform.position;
            if (capsule.enabled)
                capsule.Move(velocity * delta + Vector3.up * verticalSpeed * delta + fighter.ConsumeKnockback(delta));
            // Apply the last partial step, then discard dodge momentum so recovery cannot add a slide.
            if(completedDodge)velocity=Vector3.zero;
            Vector3 actual = (transform.position - oldPosition) / delta;
            PlanarVelocity=Vector3.ProjectOnPlane(actual,Vector3.up);Speed=PlanarVelocity.magnitude;
            if(Hero)GetComponent<ArenaCurrentCharacter>().PoseHero(delta,transform.InverseTransformDirection(actual),fighter.Dead,Guarding,capsule.isGrounded,!command.run&&hurtTime<=0&&fighter.Knockback.sqrMagnitude<.0001f,hurtTime<=0&&fighter.Knockback.sqrMagnitude<.0001f);
            else avatar?.Pose(delta, transform.InverseTransformDirection(actual), capsule.isGrounded, attackTime, dodgeTime, combo, fighter.Dead, Guarding);
            counterFeedback?.SetCounterBlade(CounterBladeVisible,
                WeaponBase, WeaponTip);

            if (active && attackTime >= 0 && PoseReady)
            {
                Vector3 bladeBase = WeaponBase, bladeTip = WeaponTip;
                float swingCue=Hero?heroCombat.SwingCue:.15f;
                if(Hero ? heroCombat.CrossedSwingCue : (beforeAttack < swingCue && attackTime >= swingCue))
                    arena.EmitSound(ArenaSound.BladeWhoosh, bladeBase, strike);
                bool strikeWindow = attackTime >= attackOpen && beforeAttack <= attackClose;
                if(Hero){strikeArena=arena;heroBlade.Sample(Hero,heroCombat,HeroSweep);}
                else if (strikeWindow)
                {
                    arena.StrikeCapsule(fighter, strike, "player_sword", bladeBase, bladeTip, swordRadius, currentSwordDamage, swordPush);
                    if (bladeSampleValid)
                    {
                        arena.StrikeCapsule(fighter, strike, "player_sword", previousTip, bladeTip, swordRadius, currentSwordDamage, swordPush);
                        arena.StrikeCapsule(fighter, strike, "player_sword", (previousBase + previousTip) * .5f,
                            (bladeBase + bladeTip) * .5f, swordRadius, currentSwordDamage, swordPush);
                    }
                }
                previousBase = bladeBase; previousTip = bladeTip; bladeSampleValid = true;
                if (!Hero && attackTime >= attackDuration)
                {
                    bool chain = queuedAttack;
                    attackTime = -1; queuedAttack = bladeSampleValid = false;
                    counterSword = false;
                    if (chain && arena.Active) StartAttack(arena);
                }
            }
            TickSwordPresentation(delta);
        }

        void HeroSweep(Vector3 a,Vector3 b)
        {
            if(strikeArena!=null)strikeArena.StrikeCapsule(fighter,strike,"player_sword",a,b,swordRadius,currentSwordDamage,swordPush);
        }
        private void StartAttack(ArenaSession arena,string explicitId=null)
        {
            if (arena == null || !arena.Active || fighter == null || fighter.Dead || !PoseReady) return;
            if(Hero && ((!capsule.isGrounded&&explicitId!="ATK_JUMP") || Hero.library.Find(explicitId??(PerfectCounterReady?heroCounterId:heroCombat.FirstAttack))?.clip == null)) return;
            if(AuthorEnabled&&Author.Posture!=0&&!Author.SetPosture(0,capsule))return;Author.ClearTransition();
            // Admission owns draw: a rejected LMB or target lock never shows the blade.
            if(!SwordDrawn&&Hero)Hero.BeginOverlay("EQUIP_DRAW",.3f,true);heroStowTime=-1;
            SwordDrawn = true; swordIdleRemaining = SwordIdleSeconds;
            counterSword = PerfectCounterReady;
            currentSwordDamage = swordDamage * (counterSword ? perfectCounterDamageMultiplier : 1);
            ClearCounterRequest();
            perfectCounterTime = 0; guard?.SuspendGuard();
            attackTime = 0; queuedAttack = bladeSampleValid = false; combo++;
            if(Hero){heroCombat.library=Hero.library;heroCombat.SelectedCombo=Responsive?3:Author.ComboLength;heroCombat.Begin(explicitId??(counterSword?heroCounterId:heroCombat.FirstAttack));heroBlade.Reset();SyncHeroAttack();}
            strike = arena.NewStrike();
            if (counterSword) arena.Record("perfect_counter_started", "player_sword", fighter, strike, transform.position);
            attackStartRotation = attackTargetRotation = transform.rotation;
            AimArenaAttack(arena,true);
            attackDirection = transform.forward;
            arena.Record("player_attack_started", "player_sword", fighter, strike, transform.position);
        }
    }
}
