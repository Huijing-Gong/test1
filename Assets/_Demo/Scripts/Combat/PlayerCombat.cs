using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerCombat : MonoBehaviour
{
    public const float FirstSlashDuration = .9f;
    [SerializeField] private bool allowHeavyAttack = true;
    public bool HeavyAttackAvailable => allowHeavyAttack;
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackStateTag = "Attack";
    [SerializeField, Min(0.1f)] private float attackFailSafeDuration = 1.6f;
    [Header("Damage")]
    [SerializeField, Min(1)] private int attackDamage = 20;
    [SerializeField, Range(0f, 1f)] private float hitWindowStart = 0.25f;
    [SerializeField, Range(0f, 1f)] private float hitWindowEnd = 0.65f;
    [SerializeField, Min(0f)] private float hitCenterHeight = 1f;
    [SerializeField, Min(0f)] private float hitForwardOffset = 0.9f;
    [SerializeField, Min(0.01f)] private float hitRadius = 0.9f;
    [SerializeField, Range(-1f, 1f)] private float targetDotMinimum = 0.05f;
    [SerializeField] private LayerMask damageLayers = ~0;
    [SerializeField] private QueryTriggerInteraction queryTriggers = QueryTriggerInteraction.Ignore;

    [Header("Hit Feedback")]
    [SerializeField] private ThirdPersonCamera feedbackCamera;
    [SerializeField, Range(0f, 0.25f)] private float hitShakeAmplitude = 0.055f;
    [SerializeField, Range(0f, 0.5f)] private float hitShakeDuration = 0.1f;
    [SerializeField, Range(0f, 0.25f)] private float deathShakeAmplitude = 0.1f;
    [SerializeField, Range(0f, 0.5f)] private float deathShakeDuration = 0.17f;
    [SerializeField, Range(0f, 0.1f)] private float hitStopDuration = 0.055f;
    [SerializeField, Range(0f, 0.1f)] private float deathHitStopDuration = 0.09f;

    private KingOfGraphic.Hero.HeroWorldAdapter hero;
    private bool UsesAuthorHero => hero && hero.isActiveAndEnabled;
    private PlayerBattleActions battleActions;
    private int comboIndex;
    private bool queuedAttack, heavyAttack;
    private Health attackTarget;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private float attackDuration;
    private bool flurryAttack, startingFlurry;
    private float attackBufferUntil;
    public bool IsFlurryAttacking => IsAttacking && flurryAttack;
    public int FlurryStrikesStarted { get; private set; }
    public bool TryStartFlurryStrike() {
        if (!battleActions || !battleActions.FocusActive || !battleActions.FocusTarget) return false;
        startingFlurry=true;heavyAttack=false;bool result=TryAttack();startingFlurry=false;return result;
    }
    private bool Enhanced => animator && animator.HasState(0, Animator.StringToHash("SwordSlash1"));
    public int ComboIndex => comboIndex;
    public bool CanCancelIntoDodge => IsAttacking && Enhanced && attackElapsedTime >= attackDuration * .52f;
    public void CancelAttackForDodge() { RestoreOwnedTimeScale(); FinishAttack(false); }
    public bool TryQueueAttack() { if (!IsAttacking || (battleActions && battleActions.IsDodging) || !Enhanced || heavyAttack || comboIndex >= 2 || attackElapsedTime < .045f) return false; queuedAttack = true; return true; }
    public bool TryHeavyAttack() { if(UsesAuthorHero)return hero.TryAttack("ATK_HEAVY");if (!HeavyAttackAvailable || !CanAttackNow) return false; heavyAttack = true; bool result = TryAttack(); if (!result) heavyAttack = false; return result; }
    private PlayerContactFeedback contactFeedback;
    private Animator animator;
    private PlayerMovement playerMovement;
    private PlayerEquipment playerEquipment;
    private int attackTriggerHash;
    private int attackTagHash;
    private int locomotionStateHash;
    private bool attackStateEntered;
    private bool ownsMovementLock;
    private float attackElapsedTime;
    private readonly Collider[] hitColliderBuffer = new Collider[16];
    private readonly Health[] damagedTargets = new Health[8];
    private int damagedTargetCount;
    private bool targetCapacityWarningIssued;
    private bool ownsHitStop;
    private float timeScaleBeforeHitStop;
    private float hitStopEndRealtime;
    private float hitStopMaximumEndRealtime;

    [SerializeField] private KingOfGraphic.SwordVFX.DigitalSwordVfx swordVfx;
    private bool swordVfxSwingOpen;
    public event System.Action SwordSwingStarted;
    private KingOfGraphic.SwordVFX.DigitalSwordVfx SwordVfx
    {
        get { if(!swordVfx)swordVfx=GetComponentInChildren<KingOfGraphic.SwordVFX.DigitalSwordVfx>(true);return swordVfx; }
    }
    private void SetSwordVfxSwing(bool open)
    {
        if(swordVfxSwingOpen==open)return;
        swordVfxSwingOpen=open;
        if(open)SwordSwingStarted?.Invoke();
        if(!SwordVfx)return;
        if(open)SwordVfx.BeginSwing();else SwordVfx.EndSwing();
    }
    // Compatibility for separate legacy test scenes; the world hero has no legacy adapter.
    public int HeroBaseDamage => attackDamage;
    public void SetHeroSwordWindow(bool value) => SetSwordVfxSwing(value);
    public void RegisterHeroHit(bool fatal, Vector3 point) { SuccessfulHitCount++;if(SwordVfx)SwordVfx.PlayImpact(point);TriggerSuccessfulHitFeedback(fatal); }
    private bool legacyIsAttacking;
    public bool IsAttacking { get => UsesAuthorHero ? hero.Attacking : legacyIsAttacking; private set => legacyIsAttacking=value; }
    public bool IsApplyingHitStop => ownsHitStop;
    public int SuccessfulHitCount { get; private set; }

    public bool CanAttackNow
    {
        get
        {
            if(UsesAuthorHero)return hero.CanAttack&&!hero.Attacking;
            if (animator == null || playerMovement == null || playerEquipment == null ||
                GameSession.Instance == null || !GameSession.Instance.HasSword ||
                !playerEquipment.CanAttack || playerEquipment.PracticeSword == null ||
                !playerEquipment.PracticeSword.activeInHierarchy || IsAttacking || (battleActions && (battleActions.IsDodging || battleActions.IsRushing)) ||
                !playerMovement.CanStartInteraction || Cursor.lockState != CursorLockMode.Locked ||
                !animator.isActiveAndEnabled || animator.layerCount == 0 ||
                (animator.IsInTransition(0) && !startingFlurry && !animator.GetCurrentAnimatorStateInfo(0).IsTag("Evade")))
            {
                return false;
            }

            var state = animator.GetCurrentAnimatorStateInfo(0);
            return startingFlurry || state.shortNameHash == locomotionStateHash || state.IsName("BattleIdle") || (state.IsTag("Evade") && state.normalizedTime >= .58f);
        }
    }

    private void Awake()
    {
        hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        contactFeedback=GetComponent<PlayerContactFeedback>();if(!contactFeedback)contactFeedback=gameObject.AddComponent<PlayerContactFeedback>();
        animator = GetComponentInChildren<Animator>(true);
        playerMovement = GetComponent<PlayerMovement>();
        playerEquipment = GetComponent<PlayerEquipment>();
        battleActions = GetComponent<PlayerBattleActions>(); if (!battleActions) battleActions = gameObject.AddComponent<PlayerBattleActions>();
        if (feedbackCamera == null)
        {
            feedbackCamera = FindFirstObjectByType<ThirdPersonCamera>();
        }
        attackTriggerHash = Animator.StringToHash(attackTriggerName);
        attackTagHash = Animator.StringToHash(attackStateTag);
        locomotionStateHash = Animator.StringToHash("Locomotion");
    }

    private void Update()
    {
        UpdateHitStop();
        if(UsesAuthorHero)return; // One input/pose owner: the approved world adapter.

        if (IsAttacking)
        {
            if (WorldInputBindings.Pressed("Attack")) TryQueueAttack();
            if (Enhanced) UpdateEnhancedAttack(); else UpdateAttackState();
            return;
        }

        if (attackBufferUntil>0 && attackBufferUntil>=CombatClock.UnscaledTime && Time.timeScale>0 && battleActions && battleActions.FinishDodgeForAttack() && TryAttack()) attackBufferUntil=0;
        if (IsAttacking) return;
        if (HeavyAttackAvailable && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked) { TryHeavyAttack(); return; }
        Mouse mouse = Mouse.current;
        if (Cursor.lockState == CursorLockMode.Locked &&
            WorldInputBindings.Pressed("Attack"))
        {
            if (!TryAttack() && battleActions && (battleActions.IsDodging || (Enhanced && !playerMovement.IsGameplayInputLocked))) attackBufferUntil=CombatClock.UnscaledTime+.22f;
        }
    }

    public bool TryAttack()
    {
        if(UsesAuthorHero)return hero.TryAttack();
        if (!startingFlurry && !heavyAttack && battleActions && battleActions.FocusActive) return battleActions.ConfirmCounter();
        if (battleActions && battleActions.IsDodging && !battleActions.FinishDodgeForAttack()) return false;
        if (!CanAttackNow)
        {
            return false;
        }

        ownsMovementLock = !playerMovement.IsGameplayInputLocked;
        if (!ownsMovementLock)
        {
            return false;
        }

        playerMovement.SetActionInputLocked(this, true);
        flurryAttack=startingFlurry;attackBufferUntil=0;
        animator.ResetTrigger(attackTriggerHash);
        if (Enhanced) { comboIndex = 0; StartEnhancedAttack(); } else animator.SetTrigger(attackTriggerHash);
        attackStateEntered = false;
        attackElapsedTime = 0f;
        ClearDamagedTargets();
        IsAttacking = true;
        if(SwordVfx)SwordVfx.BeginAttack();
        return true;
    }

    private void StartEnhancedAttack()
    {
        queuedAttack = false; attackElapsedTime = 0; ClearDamagedTargets();
        AcquireAttackTarget();
        attackDuration = heavyAttack ? 1.02f : comboIndex == 2 ? .82f : comboIndex == 0 ? FirstSlashDuration : .68f;
        if(flurryAttack)FlurryStrikesStarted++;
        if (battleActions) battleActions.MarkCombat();
        animator.CrossFadeInFixedTime(heavyAttack ? "SwordHeavy" : "SwordSlash" + (comboIndex + 1), comboIndex>0 ? .1f : .085f, 0, 0);
    }
    private void UpdateEnhancedAttack()
    {
        if (flurryAttack && (!battleActions.FocusActive || !battleActions.FocusTarget)) { animator.CrossFadeInFixedTime("Locomotion", .15f); FinishAttack(true); return; }
        if (ownsMovementLock && !playerMovement.IsGameplayInputLocked) { FinishAttack(false); return; }
        attackElapsedTime += battleActions ? battleActions.PlayerDeltaTime : Time.deltaTime;
        if (flurryAttack) battleActions.FollowFlurryTarget(battleActions.PlayerDeltaTime);
        // Turn only during anticipation; the committed cut cannot track behind the player.
        if (attackTarget && !attackTarget.IsDead && attackElapsedTime < attackDuration * .24f)
        {
            Vector3 direction = Vector3.ProjectOnPlane(attackTarget.transform.position-transform.position, Vector3.up);
            if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 1080f * (battleActions ? battleActions.PlayerDeltaTime : Time.deltaTime));
        }
        ProcessHitWindow(attackElapsedTime / attackDuration);
        bool nextAllowed=!flurryAttack || (battleActions.FocusActive && battleActions.FocusTarget);
        if (queuedAttack && comboIndex < 2 && nextAllowed && attackElapsedTime >= attackDuration * .72f)
        {
            SetSwordVfxSwing(false); if (SwordVfx) SwordVfx.EndAttack(); comboIndex++; StartEnhancedAttack(); if (SwordVfx) SwordVfx.BeginAttack(); return;
        }
        if (attackElapsedTime >= attackDuration) { animator.CrossFadeInFixedTime("Locomotion", .12f); FinishAttack(true); }
    }
    private void UpdateAttackState()
    {
        attackElapsedTime += Time.deltaTime;

        if (ownsMovementLock && !playerMovement.IsGameplayInputLocked)
        {
            FinishAttack(false);
            return;
        }

        if (animator != null && animator.isActiveAndEnabled && animator.layerCount > 0)
        {
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : default;

            if (current.tagHash == attackTagHash || next.tagHash == attackTagHash)
            {
                attackStateEntered = true;
            }

            AnimatorStateInfo attackState = current.tagHash == attackTagHash ? current : next;
            if (attackStateEntered && attackState.tagHash == attackTagHash)
            {
                ProcessHitWindow(attackState.normalizedTime);
            }

            if (attackStateEntered && !animator.IsInTransition(0) &&
                current.shortNameHash == locomotionStateHash)
            {
                FinishAttack(true);
                return;
            }
        }

        if (attackElapsedTime >= Mathf.Max(0.1f, attackFailSafeDuration))
        {
            FinishAttack(false);
        }
    }

    private void FinishAttack(bool completedNormally)
    {
        if (animator != null && animator.isActiveAndEnabled &&
            animator.runtimeAnimatorController != null)
        {
            animator.ResetTrigger(attackTriggerHash);
        }

        SetSwordVfxSwing(false);
        if(SwordVfx)SwordVfx.EndAttack();
        if (flurryAttack && battleActions) battleActions.FinishCounterStrike();
        IsAttacking = false; queuedAttack = false; heavyAttack = false; flurryAttack = false;
        attackStateEntered = false;
        attackElapsedTime = 0f;

        if (ownsMovementLock && playerMovement != null && playerMovement.IsGameplayInputLocked)
        {
            playerMovement.SetActionInputLocked(this, false);
        }

        ownsMovementLock = false;
        ClearDamagedTargets();
    }

    private void ProcessHitWindow(float normalizedTime)
    {
        // The return cut travels through the target earlier than the forward cut.
        float windowStart = Enhanced ? (heavyAttack ? .46f : comboIndex==2 ? .38f : comboIndex==1 ? .22f : .30f) : Mathf.Clamp01(hitWindowStart);
        float windowEnd = Enhanced ? (heavyAttack ? .68f : comboIndex==2 ? .65f : comboIndex==1 ? .48f : .62f) : Mathf.Clamp(hitWindowEnd, windowStart + .001f, 1f);
        if (normalizedTime < windowStart || normalizedTime > windowEnd)
        {
            SetSwordVfxSwing(false);
            return;
        }
        SetSwordVfxSwing(true);

        Vector3 center = transform.position + Vector3.up * Mathf.Max(0f, hitCenterHeight) +
                         transform.forward * Mathf.Max(0f, hitForwardOffset);
        int count = Physics.OverlapSphereNonAlloc(
            center,
            Mathf.Max(0.01f, hitRadius),
            hitColliderBuffer,
            damageLayers,
            queryTriggers);

        for (int i = 0; i < count; i++)
        {
            Collider candidate = hitColliderBuffer[i];
            hitColliderBuffer[i] = null;
            if (candidate == null || candidate.isTrigger ||
                candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            Health target = candidate.GetComponentInParent<Health>();
            if (target == null || target.IsDead || WasAlreadyDamaged(target) ||
                !target.CanReceiveDamageFrom(gameObject))
            {
                continue;
            }

            Vector3 offset = target.transform.position - transform.position;
            // A large mounted enemy is hit at its near surface, not its distant root pivot.
            Vector3 nearPoint = candidate.ClosestPoint(transform.position + Vector3.up * hitCenterHeight);
            Vector3 planarOffset = Vector3.ProjectOnPlane(nearPoint - transform.position, Vector3.up);
            if (planarOffset.sqrMagnitude <= 0.0001f ||
                Vector3.Dot(transform.forward, planarOffset.normalized) < targetDotMinimum)
            {
                continue;
            }

            if (!HasClearAttackPath(candidate, target)) continue;

            if (!RememberDamagedTarget(target))
            {
                continue;
            }

            // Query from the attacker so contact visuals stay on the near surface,
            // even when the overlap sphere center lies inside a large enemy collider.
            Vector3 hitPoint = candidate.ClosestPoint(transform.position + Vector3.up * Mathf.Max(0f, hitCenterHeight));
            DamageInfo damage = new DamageInfo(
                Mathf.Max(1, Mathf.RoundToInt((attackDamage + (GetComponent<BaselineLoadout>()?.DamageBonus ?? 0)) * (heavyAttack ? 1.7f : comboIndex == 2 ? 1.25f : 1f))),
                gameObject,
                hitPoint,
                offset);
            if (target.TryApplyDamage(in damage))
            {
                if(SwordVfx)SwordVfx.PlayImpact(hitPoint);
                contactFeedback.Contact(hitPoint,offset,target.IsDead,false,target.GetComponent<KingOfGraphic.MechBossMigration.MechSentinelBattle>()==null);
                TriggerSuccessfulHitFeedback(target.IsDead);
            }
        }

        for (int i = count; i < hitColliderBuffer.Length; i++)
        {
            hitColliderBuffer[i] = null;
        }
    }

    private bool HasClearAttackPath(Collider candidate, Health target)
    {
        Vector3 origin = transform.position + Vector3.up * hitCenterHeight;
        Vector3 delta = candidate.ClosestPoint(origin) - origin;
        if (delta.sqrMagnitude < .0001f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i=0; i<count; i++)
        {
            var obstacle = sightHits[i].collider;
            if (obstacle && !obstacle.transform.IsChildOf(transform) && obstacle.GetComponentInParent<Health>() != target) return false;
        }
        return true;
    }

    private void AcquireAttackTarget()
    {
        attackTarget = null;
        Vector3 origin = transform.position + Vector3.up * hitCenterHeight;
        int count = Physics.OverlapSphereNonAlloc(origin, hitForwardOffset + hitRadius, hitColliderBuffer, damageLayers, queryTriggers);
        float best = float.PositiveInfinity;
        Transform locked = feedbackCamera ? feedbackCamera.LockedEnemy : null;
        for (int i=0; i<count; i++)
        {
            var candidate = hitColliderBuffer[i];
            if (!candidate || candidate.isTrigger || candidate.transform.IsChildOf(transform)) continue;
            var target = candidate.GetComponentInParent<Health>();
            if (!target || target.IsDead || !target.CanReceiveDamageFrom(gameObject)) continue;
            Vector3 direction = Vector3.ProjectOnPlane(target.transform.position-transform.position, Vector3.up);
            if (direction.sqrMagnitude < .001f) continue;
            float dot = Vector3.Dot(transform.forward, direction.normalized);
            // Unlocked assistance stays in front. An explicit lock can turn the
            // anticipation toward that target; the committed cut still cannot track.
            bool explicitlyLocked = locked && (locked==target.transform || locked.IsChildOf(target.transform));
            if ((!explicitlyLocked && dot < .5f) || !HasClearAttackPath(candidate, target)) continue;
            float score = (candidate.ClosestPoint(origin)-origin).sqrMagnitude + (1-dot)*2;
            if (locked && (locked==target.transform || locked.IsChildOf(target.transform))) score -= 10;
            if (score < best) { best=score; attackTarget=target; }
        }
    }

    private bool WasAlreadyDamaged(Health target)
    {
        for (int i = 0; i < damagedTargetCount; i++)
        {
            if (damagedTargets[i] == target) return true;
        }
        return false;
    }

    private bool RememberDamagedTarget(Health target)
    {
        if (damagedTargetCount >= damagedTargets.Length)
        {
            if (!targetCapacityWarningIssued)
            {
                targetCapacityWarningIssued = true;
                Debug.LogWarning("PlayerCombat damage target buffer is full; extra targets are ignored.", this);
            }
            return false;
        }

        damagedTargets[damagedTargetCount++] = target;
        return true;
    }

    private void ClearDamagedTargets()
    {
        for (int i = 0; i < damagedTargetCount; i++) damagedTargets[i] = null;
        damagedTargetCount = 0;
    }

    private void TriggerSuccessfulHitFeedback(bool fatalHit)
    {
        SuccessfulHitCount++;
        if (feedbackCamera != null)
        {
            feedbackCamera.RequestHitImpulse(transform.forward,
                fatalHit ? Mathf.Max(.1f,deathShakeAmplitude) : Mathf.Max(.065f,hitShakeAmplitude),
                fatalHit ? deathShakeDuration : hitShakeDuration);
        }

        if (battleActions && battleActions.FocusActive) return;
        RequestHitStop(fatalHit ? deathHitStopDuration : flurryAttack ? .018f : hitStopDuration);
    }

    private void RequestHitStop(float duration)
    {
        float boundedDuration = Mathf.Clamp(duration, 0f, 0.1f);
        if (boundedDuration <= 0f)
        {
            return;
        }

        float now = CombatClock.Realtime;
        if (ownsHitStop)
        {
            if (!Mathf.Approximately(Time.timeScale, 0f))
            {
                ownsHitStop = false;
                return;
            }

            hitStopEndRealtime = Mathf.Max(
                hitStopEndRealtime,
                Mathf.Min(now + boundedDuration, hitStopMaximumEndRealtime));
            return;
        }

        if (Time.timeScale <= 0f)
        {
            return;
        }

        timeScaleBeforeHitStop = Time.timeScale;
        hitStopEndRealtime = now + boundedDuration;
        hitStopMaximumEndRealtime = now + 0.1f;
        Time.timeScale = 0f;
        ownsHitStop = true;
    }

    private void UpdateHitStop()
    {
        if (!ownsHitStop)
        {
            return;
        }

        if (!Mathf.Approximately(Time.timeScale, 0f))
        {
            ownsHitStop = false;
            return;
        }

        if (CombatClock.Realtime >= hitStopEndRealtime)
        {
            RestoreOwnedTimeScale();
        }
    }

    private void RestoreOwnedTimeScale()
    {
        if (ownsHitStop && Mathf.Approximately(Time.timeScale, 0f))
        {
            Time.timeScale = battleActions && battleActions.FocusActive ? battleActions.DesiredTimeScale : (Mathf.Approximately(timeScaleBeforeHitStop, .32f) ? 1f : timeScaleBeforeHitStop);
        }

        ownsHitStop = false;
        hitStopEndRealtime = 0f;
        hitStopMaximumEndRealtime = 0f;
    }

    private void OnDisable()
    {
        RestoreOwnedTimeScale();
        FinishAttack(false);
        if(SwordVfx)SwordVfx.Clear();
    }

    // A menu or scene transfer explicitly takes ownership after cancelling combat locks.
    public void CancelForTransition()
    {
        if(UsesAuthorHero)hero.CancelForTransition();
        attackBufferUntil=0;
        GetComponent<WorldHeroActions>()?.Cancel();
        if (battleActions) battleActions.Cancel();
        if(contactFeedback)contactFeedback.Clear();
        RestoreOwnedTimeScale(); FinishAttack(false); ClearDamagedTargets();
        if(SwordVfx)SwordVfx.Clear();
        if (!UsesAuthorHero && !(GetComponent<WorldHeroActions>()?.Dying??false) && animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)
            animator.Play(locomotionStateHash,0,0);
    }

    private void OnDestroy()
    {
        RestoreOwnedTimeScale();
        FinishAttack(false);
    }
}
