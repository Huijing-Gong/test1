using UnityEngine;

/// <summary>
/// Visual-only motion for the segmented Stone Scout. The gameplay owner moves the
/// parent enemy and decides when attacks, damage, and death actually happen.
/// </summary>
[DisallowMultipleComponent]
public class StoneScoutVisualMotion : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform torso;
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform leftForearm;
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform rightForearm;
    [SerializeField] private Transform leftThigh;
    [SerializeField] private Transform leftShin;
    [SerializeField] private Transform rightThigh;
    [SerializeField] private Transform rightShin;

    private const float WalkCyclesPerSecond = 1.45f;
    private const float AttackDuration = 0.72f;
    private const float HitDuration = 0.38f;
    private const float DeathDuration = 0.85f;

    private enum ActionState { None, Attack, Hit, Dying, Dead }

    private ActionState action;
    private bool moving;
    private float gaitTime;
    private float actionTime;
    private Vector3 torsoRestPosition;
    private Quaternion rootRestRotation;
    private Vector3 hitDirection;
    private float directionalHitTime;
    private float corePower = 1f;
    private float appliedCorePower = -1f;
    private float chargeProgress, appliedCharge = -1f;
    private Renderer[] coreRenderers;
    private MaterialPropertyBlock coreBlock;

    private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
    private static readonly int AttackParameter = Animator.StringToHash("Attack");
    private static readonly int HitParameter = Animator.StringToHash("Hit");
    private static readonly int DieParameter = Animator.StringToHash("Die");

    public virtual bool IsDead => action == ActionState.Dying || action == ActionState.Dead;
    public virtual Vector3 TorsoWorldPosition => torso ? torso.position : transform.position;

    // Approximate centre of the authored R_hand mesh, relative to its forearm pivot.
    // Gameplay may query this after Animator has posed the arm; no physics lives on the visual.
    public virtual Vector3 RightFistWorldPosition => rightForearm != null
        ? rightForearm.TransformPoint(new Vector3(0.025f, -0.34f, 0.085f))
        : transform.position;

    protected virtual void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (torso != null)
            torsoRestPosition = torso.localPosition;
        rootRestRotation = transform.localRotation;
        coreRenderers = GetComponentsInChildren<Renderer>(true);
        coreBlock = new MaterialPropertyBlock();
    }

    protected virtual void Update()
    {
        directionalHitTime = Mathf.Max(0f, directionalHitTime - Time.deltaTime);
        if (IsDead) corePower = Mathf.MoveTowards(corePower, 0f, Time.deltaTime / .12f);
        else corePower = action == ActionState.Hit ? EnemyImpactVfx.StunPower(Time.time) : 1f;
        // The generated Animator clips are the production path. Transform motion
        // below remains a small fallback for an unconfigured art-only instance.
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (action == ActionState.Attack || action == ActionState.Hit)
            {
                actionTime += Time.deltaTime;
                if (actionTime >= (action == ActionState.Attack ? AttackDuration : HitDuration)) action = ActionState.None;
            }
            return;
        }
        if (torso == null || head == null || leftUpperArm == null ||
            leftForearm == null || rightUpperArm == null || rightForearm == null ||
            leftThigh == null || leftShin == null || rightThigh == null ||
            rightShin == null)
            return;

        float delta = Time.deltaTime;
        if (moving && action == ActionState.None)
            gaitTime += delta * WalkCyclesPerSecond;

        if (action != ActionState.None && action != ActionState.Dead)
            actionTime += delta;

        ApplyIdleOrWalk();

        switch (action)
        {
            case ActionState.Attack:
                ApplyAttack(Mathf.Clamp01(actionTime / AttackDuration));
                if (actionTime >= AttackDuration)
                    action = ActionState.None;
                break;
            case ActionState.Hit:
                ApplyHit(Mathf.Clamp01(actionTime / HitDuration));
                if (actionTime >= HitDuration)
                    action = ActionState.None;
                break;
            case ActionState.Dying:
                ApplyDeath(Mathf.Clamp01(actionTime / DeathDuration));
                if (actionTime >= DeathDuration)
                    action = ActionState.Dead;
                break;
            case ActionState.Dead:
                ApplyDeath(1f);
                break;
        }
    }

    /// <summary>Only changes the visual gait; does not move the enemy.</summary>
    public virtual void SetMoving(bool value)
    {
        moving = value;
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetBool(IsMovingParameter, value && !IsDead);
    }

    /// <summary>Visual wind-up and punch; gameplay owns the hit window.</summary>
    public virtual void PlayAttack()
    {
        if (IsDead)
            return;
        action = ActionState.Attack;
        actionTime = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetTrigger(AttackParameter);
    }

    public virtual void CancelAttack()
    {
        if (action != ActionState.Attack || IsDead) return;
        action = ActionState.None; actionTime = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
        { animator.ResetTrigger(AttackParameter); animator.CrossFadeInFixedTime("Idle", .06f); }
    }

    public void PlayHit()
    { PlayHit(Vector3.back); }

    public virtual void PlayHit(Vector3 worldDirection)
    {
        if (IsDead)
            return;
        action = ActionState.Hit;
        actionTime = 0f;
        directionalHitTime = .22f;
        hitDirection = transform.parent ? transform.parent.InverseTransformDirection(worldDirection) : worldDirection;
        hitDirection.y = 0f;
        hitDirection = hitDirection.sqrMagnitude > .001f ? hitDirection.normalized : Vector3.back;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.ResetTrigger(AttackParameter);
            animator.SetTrigger(HitParameter);
        }
    }

    public virtual void PlayDeath()
    {
        if (IsDead)
            return;
        moving = false;
        action = ActionState.Dying;
        actionTime = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool(IsMovingParameter, false);
            animator.ResetTrigger(AttackParameter);
            animator.ResetTrigger(HitParameter);
            animator.SetTrigger(DieParameter);
        }
    }

    public virtual void ResetVisual()
    {
        directionalHitTime = chargeProgress = 0f; corePower = 1f;
        transform.localRotation = rootRestRotation;
        ApplyCorePower();
        moving = false;
        action = ActionState.None;
        actionTime = 0f;
        gaitTime = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool(IsMovingParameter, false);
            animator.ResetTrigger(AttackParameter);
            animator.ResetTrigger(HitParameter);
            animator.ResetTrigger(DieParameter);
            animator.Play("Idle", 0, 0f);
        }
    }

    protected virtual void LateUpdate()
    {
        // A small visual tilt, not a second movement/knockback system. Animator owns the joints.
        float recoil = IsDead || !EnemyPresentationBudget.Details(transform.position) ? 0f : Mathf.Sin(Mathf.Clamp01(directionalHitTime / .22f) * Mathf.PI);
        transform.localRotation = rootRestRotation * Quaternion.Euler(hitDirection.z * 5f * recoil + (action == ActionState.Hit ? 3f : 0f),
            0f, -hitDirection.x * 6f * recoil);
        ApplyCorePower();
    }

    private void ApplyCorePower()
    {
        if (coreBlock == null || coreRenderers == null) return;
        float charge = IsDead ? 0f : chargeProgress;
        if (Mathf.Approximately(appliedCorePower, corePower) && Mathf.Approximately(appliedCharge, charge)) return;
        appliedCorePower = corePower;
        appliedCharge = charge;
        foreach (Renderer renderer in coreRenderers)
        {
            if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.HasProperty("_CorePower")) continue;
            renderer.GetPropertyBlock(coreBlock);
            coreBlock.SetFloat("_CorePower", corePower);
            coreBlock.SetFloat("_ChargeStrength", charge);
            coreBlock.SetFloat("_ChargeVeins", renderer.name == "RightForearm" ? 1f :
                renderer.name == "RightUpperArm" ? .65f : 0f);
            renderer.SetPropertyBlock(coreBlock);
        }
    }

    public virtual void SetChargeProgress(float value) => chargeProgress = Mathf.Clamp01(value);

#if UNITY_EDITOR
    public void Configure(
        Animator visualAnimator,
        Transform body, Transform skull,
        Transform leftArm, Transform leftLowerArm,
        Transform rightArm, Transform rightLowerArm,
        Transform leftLeg, Transform leftLowerLeg,
        Transform rightLeg, Transform rightLowerLeg)
    {
        animator = visualAnimator;
        torso = body;
        head = skull;
        leftUpperArm = leftArm;
        leftForearm = leftLowerArm;
        rightUpperArm = rightArm;
        rightForearm = rightLowerArm;
        leftThigh = leftLeg;
        leftShin = leftLowerLeg;
        rightThigh = rightLeg;
        rightShin = rightLowerLeg;
        torsoRestPosition = body.localPosition;
    }
#endif

    private void ApplyIdleOrWalk()
    {
        float breath = Mathf.Sin(Time.time * 2.4f);
        float step = moving ? Mathf.Sin(gaitTime * Mathf.PI * 2f) : 0f;
        float bounce = moving ? Mathf.Abs(step) * 0.018f : 0f;

        torso.localPosition = torsoRestPosition + Vector3.up * (0.006f * breath + bounce);
        torso.localRotation = Quaternion.Euler(1.5f * breath, 0f, 2.5f * step);
        head.localRotation = Quaternion.Euler(-1.5f * breath, 0f, -1.5f * step);

        leftThigh.localRotation = Quaternion.Euler(23f * step, 0f, 0f);
        rightThigh.localRotation = Quaternion.Euler(-23f * step, 0f, 0f);
        leftShin.localRotation = Quaternion.Euler(18f * Mathf.Max(0f, -step), 0f, 0f);
        rightShin.localRotation = Quaternion.Euler(18f * Mathf.Max(0f, step), 0f, 0f);

        leftUpperArm.localRotation = Quaternion.Euler(-17f * step, 0f, -2f * breath);
        rightUpperArm.localRotation = Quaternion.Euler(17f * step, 0f, 2f * breath);
        leftForearm.localRotation = Quaternion.Euler(-6f, 0f, 0f);
        rightForearm.localRotation = Quaternion.Euler(-6f, 0f, 0f);
    }

    private void ApplyAttack(float progress)
    {
        float armAngle;
        if (progress < 0.32f)
            armAngle = Mathf.Lerp(0f, 32f, Ease(progress / 0.32f));
        else if (progress < 0.60f)
            armAngle = Mathf.Lerp(32f, -88f, Ease((progress - 0.32f) / 0.28f));
        else
            armAngle = Mathf.Lerp(-88f, 0f, Ease((progress - 0.60f) / 0.40f));

        torso.localRotation = Quaternion.Euler(-5f * Mathf.Sin(progress * Mathf.PI),
            -12f * Mathf.Sin(progress * Mathf.PI), 0f);
        rightUpperArm.localRotation = Quaternion.Euler(armAngle, 0f, -12f);
        rightForearm.localRotation = Quaternion.Euler(-35f * Mathf.Sin(progress * Mathf.PI), 0f, 0f);
        leftUpperArm.localRotation = Quaternion.Euler(-20f * Mathf.Sin(progress * Mathf.PI), 0f, 0f);
    }

    private void ApplyHit(float progress)
    {
        float recoil = Mathf.Sin(progress * Mathf.PI);
        torso.localRotation = Quaternion.Euler(-16f * recoil, 0f, 8f * recoil);
        head.localRotation = Quaternion.Euler(14f * recoil, 0f, 0f);
        leftUpperArm.localRotation = Quaternion.Euler(20f * recoil, 0f, -14f * recoil);
        rightUpperArm.localRotation = Quaternion.Euler(20f * recoil, 0f, 14f * recoil);
    }

    private void ApplyDeath(float progress)
    {
        float fall = Ease(progress);
        torso.localPosition = torsoRestPosition + new Vector3(0f, -0.48f * fall, 0.14f * fall);
        torso.localRotation = Quaternion.Euler(76f * fall, 0f, 18f * fall);
        head.localRotation = Quaternion.Euler(-22f * fall, 0f, 0f);
        leftUpperArm.localRotation = Quaternion.Euler(-48f * fall, 0f, -25f * fall);
        rightUpperArm.localRotation = Quaternion.Euler(-45f * fall, 0f, 25f * fall);
        leftThigh.localRotation = Quaternion.Euler(-28f * fall, 0f, 0f);
        rightThigh.localRotation = Quaternion.Euler(18f * fall, 0f, 0f);
        leftShin.localRotation = Quaternion.Euler(40f * fall, 0f, 0f);
        rightShin.localRotation = Quaternion.Euler(45f * fall, 0f, 0f);
    }

    private static float Ease(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
    }
}
