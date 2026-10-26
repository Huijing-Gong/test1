using UnityEngine;
using UnityEngine.InputSystem;

public class BattleAttack : MonoBehaviour
{
    static readonly int IdleStateHash=Animator.StringToHash("Base Layer.CharacterArmature|Idle");
    static readonly int AttackTriggerHash=Animator.StringToHash("Attack");
    static readonly int WeaponTriggerHash=Animator.StringToHash("Weapon");
    enum AttackState { Ready, Startup, Active, Recovery }
    public Transform attackPoint;
    public float attackRange=1f;
    [Header("Standalone K Weapon Attack")]
    public float weaponAttackRange=1.15f;
    public float weaponAttackDamage=12f;
    [Header("Standalone K Timing")]
    public float startupDuration=.2f, activeDuration=.08f, recoveryDuration=.3f;
    [Header("Legacy Combo Turning (J is now committed)")]
    [Min(0f)] public float comboTurnRecoveryDuration=.1f;
    [SerializeField] AttackState currentState=AttackState.Ready;
    public bool IsMovementLocked => currentState!=AttackState.Ready;
    public bool LastAttackConnected { get; private set; }
    public bool LastAttackResolved { get; private set; }
    public bool LastAttackLanded { get; private set; }
    public bool IsJAttack => currentAttackIsJ && IsMovementLocked;
    public BattleJMove CurrentJMove { get; private set; }
    public int CurrentJStep => comboStep;
    public float CommittedDirection => attackDirection;
    public bool IsRecovering => currentState==AttackState.Recovery;
    public float JPoseProgress => IsJAttack ? Mathf.Clamp01(attackElapsed / Mathf.Max(.01f, jData.Startup+jData.Active+jData.Recovery)) : 0f;
    public float JContactProgress => jData.Startup / Mathf.Max(.01f,jData.Startup+jData.Active+jData.Recovery);
    public float JElapsed => attackElapsed;
    public BattleJMoveData JData => jData;
    public float AttackMovementVelocity => IsJAttack && isComboAttack ? attackDirection*jData.ForwardSpeed : 0f;
    public float AttackMovementControl => IsJAttack && isComboAttack ? .12f : 0f;

    Animator animator;
    PlayerDamage playerDamage;
    BattlePlayerController playerController;
    BattleAttackSweep attackSweep;
    BattleSpecialMoves specialMoves;
    BattleCombatInput combatInput;
    BattleJPresentation jPresentation;
    bool hasAttackTrigger, hasWeaponTrigger;
    bool isComboAttack, currentAttackIsJ, nextComboAttackQueued;
    bool currentAttackUsesWeapon, lastAttackWasBlocked;
    int comboStep=-1;
    float attackDirection=1f, comboWindowTimer, stateTimer, attackElapsed;
    BattleJMoveData jData;
    // One pending press, never a list of future attacks. Simulation time keeps
    // hit-stop from eating the buffer; menus/focus/specials explicitly clear it.
    const float ComboBufferSeconds = .22f, RecoveryBufferSeconds = .12f, ConfirmedLinkRecovery = .045f;
    float comboPressUntil = -1, recoveryPressUntil = -1;
    BattleJMove recoveryMove;
    float recoveryDirection;
    readonly Collider[] jContacts=new Collider[24];

    void Awake()
    {
        combatInput=GetComponent<BattleCombatInput>();
        if (!combatInput) combatInput=gameObject.AddComponent<BattleCombatInput>();
    }

    void Start()
    {
        animator=GetComponentInChildren<Animator>();
        playerDamage=GetComponent<PlayerDamage>();
        playerController=GetComponent<BattlePlayerController>();
        specialMoves=GetComponent<BattleSpecialMoves>();
        attackSweep=GetComponent<BattleAttackSweep>();
        if (!attackSweep) attackSweep=gameObject.AddComponent<BattleAttackSweep>();
        attackSweep.Initialise(false);
        if (!GetComponent<BattleJAttackPose>()) gameObject.AddComponent<BattleJAttackPose>();
        jPresentation=GetComponent<BattleJPresentation>();
        if (!jPresentation) jPresentation=gameObject.AddComponent<BattleJPresentation>();
        hasAttackTrigger=BattleAnimatorUtility.HasParameter(animator,AttackTriggerHash,AnimatorControllerParameterType.Trigger);
        hasWeaponTrigger=BattleAnimatorUtility.HasParameter(animator,WeaponTriggerHash,AnimatorControllerParameterType.Trigger);
    }

    public static BattleJMove ReadJMove(Keyboard keyboard)
    {
        if (keyboard==null) return BattleJMove.Neutral;
        bool up=keyboard.wKey.isPressed, down=keyboard.sKey.isPressed;
        if (up!=down) return up ? BattleJMove.Upper : BattleJMove.Low;
        // Horizontal input aims the same four-hit chain; it must not replace
        // the combo with a side strike while the player is approaching.
        // W/S retain their deliberate uppercut/sweep overrides.
        return BattleJMove.Neutral;
    }

    public float ConstrainComboAdvance(float velocity, float fixedDeltaTime)
    {
        if (!IsJAttack || !isComboAttack || velocity * attackDirection <= 0f)
            return velocity;

        // Fighter body layers intentionally pass through one another. A held
        // direction must not push a committed punch through the rival before
        // its startup ends. Limit only forward combo motion: never pull a
        // target closer, turn the punch, or allow a hit behind the player.
        Vector3 center = transform.position + Vector3.right * (.65f * attackDirection);
        int count = Physics.OverlapBoxNonAlloc(center, new Vector3(.85f, .95f, .65f),
            jContacts, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        float speed = velocity * attackDirection;
        for (int i = 0; i < count; i++)
        {
            var candidate = jContacts[i].GetComponentInParent<EnemyDamage>();
            if (!candidate || !candidate.enabled || !candidate.CompareTag("Enemy")) continue;
            Vector3 offset = candidate.transform.position - transform.position;
            float gap = offset.x * attackDirection;
            if (gap < 0f || Mathf.Abs(offset.y) > 1.4f || Mathf.Abs(offset.z) > .9f) continue;
            float safeSpeed = Mathf.Max(0f, gap - .55f) / Mathf.Max(.001f, fixedDeltaTime);
            speed = Mathf.Min(speed, safeSpeed);
        }
        return speed * attackDirection;
    }

    void Update()
    {
        if (Time.timeScale<=0f) { ClearInputBuffer(); return; }
        if (playerDamage && playerDamage.IsInHitstun) { CancelAttack(); return; }
        if (specialMoves && specialMoves.IsBusy) { ClearInputBuffer(); return; }
        if (!combatInput || !combatInput.CanStartActions) ClearInputBuffer();
        var keyboard=Keyboard.current;
        bool jPressed=combatInput && combatInput.NormalPressed;
        bool kPressed=combatInput && combatInput.SpecialPressed;
        if (kPressed) ClearInputBuffer(); // A special request supersedes old J intent.
        if (nextComboAttackQueued && Time.time > comboPressUntil) nextComboAttackQueued=false;
        if (IsMovementLocked)
        {
            attackElapsed+=Time.deltaTime;
            if (jPressed && ReadJMove(keyboard)!=BattleJMove.Neutral) nextComboAttackQueued=false;
            // Only confirmed early neutral hits can be deliberately cancelled.
            // Whiffs, directional moves and the finisher must pay recovery.
            if (jPressed && isComboAttack && comboStep<BattleJMoveSet.HitCount-1 && ReadJMove(keyboard)==BattleJMove.Neutral)
            {
                nextComboAttackQueued=true;
                comboPressUntil=Time.time+ComboBufferSeconds;
            }
            if (jPressed && currentState==AttackState.Recovery && stateTimer<=RecoveryBufferSeconds &&
                (!isComboAttack || comboStep==BattleJMoveSet.HitCount-1 || !LastAttackLanded || ReadJMove(keyboard)!=BattleJMove.Neutral))
            {
                recoveryMove=ReadJMove(keyboard); recoveryDirection=GetInputDirection();
                recoveryPressUntil=Time.time+RecoveryBufferSeconds;
            }
        }
        switch (currentState)
        {
            case AttackState.Ready:
                UpdateComboWindow();
                if (!combatInput || !combatInput.CanStartActions || (playerController && (playerController.IsDefenceActionLocked || playerController.IsLedgeHanging || playerController.IsLedgeClimbing)) ||
                    kPressed ||
                    (keyboard!=null && keyboard.sKey.isPressed && keyboard.spaceKey.wasPressedThisFrame)) break;
                if (jPressed || recoveryPressUntil >= Time.time)
                {
                    bool buffered=!jPressed;
                    var move=buffered ? recoveryMove : ReadJMove(keyboard);
                    int step=move==BattleJMove.Neutral && comboWindowTimer>0f && comboStep>=0 && LastAttackLanded ? comboStep+1 : 0;
                    float direction=buffered ? recoveryDirection : GetInputDirection();
                    ClearInputBuffer();
                    BeginJ(move,step,direction);
                }
                break;
            case AttackState.Startup:
                if (TickStateTimer())
                {
                    if (!currentAttackIsJ) ResolveK();
                    EnterState(AttackState.Active,currentAttackIsJ ? jData.Active : activeDuration);
                    if (currentAttackIsJ) { jPresentation?.Swing(); ResolveJ(); }
                }
                break;
            case AttackState.Active:
                if (currentAttackIsJ && !LastAttackConnected) ResolveJ();
                if (TickStateTimer())
                {
                    LastAttackResolved=true;
                    if (!LastAttackConnected) Debug.Log("Player attack missed.");
                    float missRecovery=isComboAttack ? BattleJMoveSet.NeutralMissRecovery : BattleJMoveSet.MissRecovery;
                    float recovery=currentAttackIsJ ? jData.Recovery + (!LastAttackLanded ? missRecovery : 0f) : recoveryDuration;
                    if (!LastAttackLanded) nextComboAttackQueued=false;
                    EnterState(AttackState.Recovery,recovery);
                }
                break;
            case AttackState.Recovery:
                bool done=TickStateTimer();
                // Confirmed early punches may link after a short readable recoil;
                // a whiff, block or finisher still pays its complete recovery.
                bool link=nextComboAttackQueued && CanCancelEarlyNeutral() &&
                    jData.Recovery-stateTimer >= ConfirmedLinkRecovery;
                if (done || link) FinishAttackOrContinueCombo();
                break;
        }
    }

    void BeginJ(BattleJMove move,int step,float? directionOverride=null)
    {
        CurrentJMove=move; currentAttackIsJ=true;
        isComboAttack=move==BattleJMove.Neutral;
        comboStep=isComboAttack ? Mathf.Clamp(step,0,BattleJMoveSet.HitCount-1) : -1;
        jData=BattleJMoveSet.Get(move,comboStep);
        LastAttackConnected=LastAttackLanded=LastAttackResolved=lastAttackWasBlocked=false;
        nextComboAttackQueued=false; comboWindowTimer=0f; attackElapsed=0f;
        comboPressUntil=recoveryPressUntil=-1;
        attackDirection=directionOverride ?? GetInputDirection();
        playerController?.FaceLaunchDirection(attackDirection);
        currentAttackUsesWeapon=jData.Weapon && hasWeaponTrigger;
        PlayCurrentAttackAnimation(move==BattleJMove.Upper ? 1.2f : 1.05f);
        attackSweep?.Stop();
        jPresentation?.Begin(move,comboStep,attackDirection,jData);
        EnterState(AttackState.Startup,jData.Startup);
    }

    void BeginStandaloneWeaponAttack()
    {
        ResetComboProgress(); currentAttackIsJ=false;
        LastAttackConnected=LastAttackLanded=LastAttackResolved=lastAttackWasBlocked=false;
        attackDirection=GetAttackDirection();
        currentAttackUsesWeapon=hasWeaponTrigger;
        PlayCurrentAttackAnimation(1f);
        SideAAudio.Weapon();
        attackSweep?.Play(attackDirection,startupDuration+activeDuration,true,-1,false);
        EnterState(AttackState.Startup,startupDuration);
    }
    void PlayCurrentAttackAnimation(float speed)
    {
        if (!animator) return;
        animator.speed=speed;
        animator.ResetTrigger(AttackTriggerHash); animator.ResetTrigger(WeaponTriggerHash);
        if (currentAttackIsJ && animator.isHuman)
        {
            string clip = CurrentJMove == BattleJMove.Low ? "Idle_Loop" :
                CurrentJMove == BattleJMove.Neutral && comboStep == 2 ? "Melee_Hook" :
                CurrentJMove == BattleJMove.Neutral && comboStep == 0 ? "Punch_Jab" : "Punch_Cross";
            int hash = Animator.StringToHash("Base Layer.Humanoid|" + clip);
            if (animator.HasState(0,hash))
            {
                var source = System.Array.Find(animator.runtimeAnimatorController.animationClips,c=>c.name==clip);
                if (source) animator.speed = source.length / Mathf.Max(.05f,jData.Startup+jData.Active+jData.Recovery);
                animator.CrossFadeInFixedTime(hash,.035f,0,0f);
                return;
            }
        }
        if (currentAttackUsesWeapon) animator.SetTrigger(WeaponTriggerHash);
        else if (hasAttackTrigger) animator.SetTrigger(AttackTriggerHash);
    }
    void ResolveJ()
    {
        if (LastAttackConnected || !BattleMeleeQuery.TryFindPlayerJTarget(transform,attackDirection,jData,
                CurrentJMove,jContacts,out EnemyDamage enemy,out Vector3 contact)) return;
        LastAttackConnected=LastAttackResolved=true;
        lastAttackWasBlocked=enemy.TakeHit(attackDirection,jData.Damage,jData.Knockback,jData.Hitstun,
            isComboAttack ? comboStep : -1,jData.HorizontalWeight,jData.VerticalWeight,
            requestCameraShake:false,
            linkHitSpeedCap:isComboAttack && comboStep<BattleJMoveSet.HitCount-1 ? BattleJMoveSet.LinkHitSpeedCap : (float?)null);
        LastAttackLanded=!lastAttackWasBlocked;
        jPresentation?.Contact(enemy,contact,lastAttackWasBlocked);
        if (lastAttackWasBlocked) { Debug.Log("Player attack was blocked."); return; }
        BattleCombatFeedback.PlayJHitStop(CurrentJMove,comboStep);
        Debug.Log(isComboAttack ? "Player combo hit "+(comboStep+1)+" connected." : "Player J "+CurrentJMove+" connected.");
    }
    void ResolveK()
    {
        LastAttackResolved=true;
        if (!attackPoint || !BattleMeleeQuery.TryFindForwardTarget(attackPoint.position,weaponAttackRange,"Enemy",transform,attackDirection,out EnemyDamage enemy)) return;
        LastAttackConnected=true;
        lastAttackWasBlocked=enemy.TakeHit(attackDirection,weaponAttackDamage,1f,.4f,-1);
        LastAttackLanded=!lastAttackWasBlocked;
        Debug.Log(lastAttackWasBlocked ? "Player attack was blocked." : "Player weapon attack connected.");
    }
    float GetInputDirection()
    {
        var keyboard=Keyboard.current;
        if (keyboard!=null && keyboard.aKey.isPressed!=keyboard.dKey.isPressed)
            return keyboard.dKey.isPressed ? 1f : -1f;
        return GetAttackDirection();
    }
    float GetAttackDirection() => attackPoint && attackPoint.localPosition.x<0f ? -1f : 1f;
    void EnterState(AttackState next,float duration) { currentState=next; stateTimer=Mathf.Max(0f,duration); }
    bool TickStateTimer() { stateTimer-=Time.deltaTime; return stateTimer<=0f; }
    void FinishAttackOrContinueCombo()
    {
        if (isComboAttack && nextComboAttackQueued && comboStep<BattleJMoveSet.HitCount-1 && LastAttackLanded)
        { BeginJ(BattleJMove.Neutral,comboStep+1); return; }
        bool canContinue=isComboAttack && comboStep<BattleJMoveSet.HitCount-1 && LastAttackLanded;
        RestoreAnimatorSpeed(); attackSweep?.Stop(); currentState=AttackState.Ready;
        if (animator && animator.isHuman) animator.CrossFade(IdleStateHash,.04f,0,0f);
        if (canContinue) comboWindowTimer=BattleJMoveSet.ComboWindow;
        else ResetComboProgress();
    }
    void UpdateComboWindow()
    {
        if (comboWindowTimer<=0f) return;
        comboWindowTimer-=Time.deltaTime;
        if (comboWindowTimer<=0f) ResetComboProgress();
    }
    void ResetComboProgress()
    {
        comboStep=-1; comboWindowTimer=0f; nextComboAttackQueued=false;
        isComboAttack=false; currentAttackIsJ=false;
    }
    void ClearInputBuffer()
    {
        nextComboAttackQueued=false;
        comboPressUntil=recoveryPressUntil=-1;
    }
    void RestoreAnimatorSpeed() { if (animator) animator.speed=1f; }
    // Kept for existing controller integration; new J moves never turn mid-swing.
    public bool UpdateAttackDirection(float horizontalDirection) => false;
    bool CanCancelEarlyNeutral() => currentAttackIsJ && isComboAttack && comboStep<BattleJMoveSet.HitCount-1 && LastAttackLanded && !lastAttackWasBlocked && currentState==AttackState.Recovery;
    public bool TryCancelComboForAction()
    {
        if (!CanCancelEarlyNeutral()) return false;
        CancelAttack(true); return true;
    }
    // Specials take ownership only after their arbitration succeeds; never let an old queued J resume afterward.
    public void ResetComboForSpecial() { CancelAttack(true); }
    public void CancelForInputFocusLoss() { CancelAttack(true); GetComponent<BattleJAttackPose>()?.Restore(); }
    void CancelAttack(bool returnToIdle=false)
    {
        ClearInputBuffer();
        if (!playerDamage || !playerDamage.IsInHitstun) RestoreAnimatorSpeed();
        if (returnToIdle && animator)
        {
            animator.ResetTrigger(AttackTriggerHash); animator.ResetTrigger(WeaponTriggerHash);
            animator.CrossFade(IdleStateHash,.04f,0,0f);
        }
        ResetComboProgress(); attackSweep?.Stop(); jPresentation?.CancelSwing(); currentState=AttackState.Ready;
    }
    void OnDisable() { CancelAttack(); GetComponent<BattleJAttackPose>()?.Restore(); }
    void OnDrawGizmosSelected()
    {
        if (currentAttackIsJ && IsMovementLocked)
        {
            Vector3 center=transform.position+new Vector3(jData.Center.x*attackDirection,jData.Center.y,jData.Center.z);
            Gizmos.color=Color.cyan; Gizmos.DrawWireCube(center,jData.HalfExtents*2f);
        }
        else if (attackPoint) Gizmos.DrawWireSphere(attackPoint.position,weaponAttackRange);
    }
}
