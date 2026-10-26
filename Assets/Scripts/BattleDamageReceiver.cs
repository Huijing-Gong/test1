using TMPro;
using UnityEngine;

public abstract class BattleDamageReceiver : MonoBehaviour
{
    private static readonly int HitTriggerHash =
        Animator.StringToHash("Hit");

    private const float BaseKnockbackStrength = 4f;
    private const float KnockbackPerDamagePercent = 0.32f;

    public float damagePercent = 0f;
    public TMP_Text damageText;
    // Raised only after an actual unblocked hit, never for parries/protection.
    public event System.Action<BattleDamageReceiver> DamageApplied;

    [Header("Hitstun")]
    public float hitstunDuration = 0.4f;

    public bool IsInHitstun { get; private set; }
    public float PresentationPower {get;private set;}
    public int PresentationSerial {get;private set;}
    public void NotifyPresentation(float power)
    {
        PresentationPower=Mathf.Clamp01(power);PresentationSerial++;
        fighterPresentation?.Flash(PresentationPower);
    }

    protected Rigidbody Body { get; private set; }

    private BattleKnockbackTrail knockbackTrail;
    private BattleFighterPresentation fighterPresentation;
    private Animator animator;
    private bool hasHitTrigger;
    private float hitstunTimer;
    private float hitAnimationSpeedTimer;
    private bool holdHitstunUntilApex;
    private bool upwardLaunchStarted;
    private float apexDetectionGraceTimer;

    protected abstract bool IsEnemyFighter { get; }
    protected abstract string DamageLabel { get; }

    protected virtual void Start()
    {
        Body = GetComponent<Rigidbody>();
        knockbackTrail = GetComponent<BattleKnockbackTrail>();
        if (knockbackTrail == null)
        {
            knockbackTrail = gameObject.AddComponent<BattleKnockbackTrail>();
        }

        knockbackTrail.Initialise(IsEnemyFighter);
        animator = GetComponentInChildren<Animator>();
        hasHitTrigger = BattleAnimatorUtility.HasParameter(
            animator,
            HitTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
        OnReceiverInitialised();
        fighterPresentation =
            BattleFighterPresentation.Create(transform, IsEnemyFighter);
        if(!GetComponent<BattleMotionPresentation>())gameObject.AddComponent<BattleMotionPresentation>();
        UpdateDamageText();
    }

    protected virtual void Update()
    {
        TickHitstun();
        UpdateHitAnimationSpeed();
        fighterPresentation?.Tick();
    }

    public bool TakeHit(
        float horizontalDirection,
        float damage,
        float knockbackMultiplier = 1f,
        float? hitstunDurationOverride = null,
        int comboHitIndex = -1,
        float? horizontalKnockbackWeightOverride = null,
        float? verticalKnockbackOverride = null,
        Transform attackSource = null,
        bool requestCameraShake = true,
        float? linkHitSpeedCap = null
    )
    {
        var stocks = GetComponent<BattleStockLives>();
        if (stocks && stocks.IsProtected) return true;
        float direction = horizontalDirection >= 0f ? 1f : -1f;

        if (TryBlockHit(direction, attackSource))
        {
            SideAAudio.Parry();
            return true;
        }

        BeforeHitApplied();
        damagePercent += Mathf.Max(0f, damage);
        BeginHitstun(
            hitstunDurationOverride ?? hitstunDuration,
            comboHitIndex
        );
        UpdateDamageText();

        float knockbackStrength =
            BaseKnockbackStrength +
            damagePercent * KnockbackPerDamagePercent;
        knockbackStrength *= Mathf.Max(0f, knockbackMultiplier);

        float verticalKnockback = verticalKnockbackOverride ??
            (comboHitIndex >= 0
                ? BattleFourHitCombo.GetVerticalKnockback(comboHitIndex)
                : BattleFourHitCombo.StandaloneVerticalKnockback);
        float horizontalKnockbackWeight =
            horizontalKnockbackWeightOverride ??
            BattleFourHitCombo.HorizontalKnockbackWeight;
        Vector3 knockbackDirection = new Vector3(
            direction * horizontalKnockbackWeight,
            verticalKnockback,
            0f
        ).normalized;

        if (ShouldFaceLaunchDirection(comboHitIndex))
        {
            FaceLaunchDirection(direction);
        }

        holdHitstunUntilApex = knockbackDirection.y > 0.05f;
        upwardLaunchStarted = false;
        apexDetectionGraceTimer = 0.08f;

        float trailStrength = knockbackStrength;
        if (Body != null)
        {
            if (linkHitSpeedCap.HasValue)
            {
                // Light links refresh a small push rather than stacking high-%
                // impulses until the next punch is unreachable. No teleport,
                // target attraction or extended hit volume. Finishers bypass it.
                float cap = Mathf.Max(0,linkHitSpeedCap.Value);
                float mass = Mathf.Max(.01f,Body.mass);
                trailStrength = Mathf.Min(knockbackStrength,cap*mass);
                Vector3 velocity = Body.linearVelocity;
                velocity.x = Mathf.Clamp(knockbackDirection.x*knockbackStrength/mass,-cap,cap);
                Body.linearVelocity = velocity;
                Body.AddForce(Vector3.up * knockbackDirection.y * Mathf.Min(knockbackStrength,cap*mass),ForceMode.Impulse);
            }
            else Body.AddForce(knockbackDirection * knockbackStrength,ForceMode.Impulse);
        }

        knockbackTrail?.Play(
            knockbackDirection,
            trailStrength,
            damagePercent
        );
        if (requestCameraShake) BattleCameraController.RequestHitShake(
            damagePercent,
            knockbackMultiplier
        );
        OnHitApplied();
        SideAAudio.Hit(IsEnemyFighter);
        NotifyPresentation(Mathf.Clamp01(knockbackMultiplier));
        if (damage > 0) DamageApplied?.Invoke(this);
        return false;
    }

    public void ResetForStockRespawn()
    {
        damagePercent = 0;
        IsInHitstun = false;
        hitstunTimer = hitAnimationSpeedTimer = apexDetectionGraceTimer = 0;
        holdHitstunUntilApex = upwardLaunchStarted = false;
        if (animator) { animator.speed = 1; BattleAnimatorUtility.ResetTriggers(animator); }
        UpdateDamageText();
    }

    protected void ApplyForcedHitstun(
        float duration,
        bool stopHorizontalMovement
    )
    {
        BeginHitstun(duration, -1);
        holdHitstunUntilApex = false;
        upwardLaunchStarted = false;

        if (stopHorizontalMovement && Body != null)
        {
            Vector3 velocity = Body.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            Body.linearVelocity = velocity;
            Body.angularVelocity = Vector3.zero;
        }
    }

    protected virtual void OnReceiverInitialised() { }
    protected virtual bool TryBlockHit(float direction) => false;
    // Optional source context lets a counter validate real local contact without changing legacy callers.
    protected virtual bool TryBlockHit(float direction, Transform attackSource) => TryBlockHit(direction);
    protected virtual void BeforeHitApplied() { }
    protected virtual void OnHitApplied() { }
    protected abstract void FaceLaunchDirection(float direction);

    private void TickHitstun()
    {
        if (!IsInHitstun)
            return;

        hitstunTimer -= Time.deltaTime;

        if (holdHitstunUntilApex)
        {
            apexDetectionGraceTimer -= Time.deltaTime;

            if (Body != null && Body.linearVelocity.y > 0.05f)
            {
                upwardLaunchStarted = true;
            }
            else if (upwardLaunchStarted ||
                apexDetectionGraceTimer <= 0f)
            {
                holdHitstunUntilApex = false;
            }
        }

        if (hitstunTimer <= 0f && !holdHitstunUntilApex)
        {
            IsInHitstun = false;
        }
    }

    private void BeginHitstun(float duration, int comboHitIndex)
    {
        IsInHitstun = true;
        hitstunTimer = Mathf.Max(0f, duration);
        PlayHitAnimation(comboHitIndex);
    }

    private static bool ShouldFaceLaunchDirection(int comboHitIndex)
    {
        return comboHitIndex < 0 ||
            comboHitIndex == BattleFourHitCombo.HitCount - 1;
    }

    private void PlayHitAnimation(int comboHitIndex)
    {
        if (animator == null || !hasHitTrigger)
            return;

        if (comboHitIndex >= 0)
        {
            animator.speed =
                BattleFourHitCombo.GetHitAnimationSpeed(comboHitIndex);
            hitAnimationSpeedTimer = Mathf.Max(0.12f, hitstunTimer);
        }

        animator.SetTrigger(HitTriggerHash);
    }

    private void UpdateHitAnimationSpeed()
    {
        if (hitAnimationSpeedTimer <= 0f || animator == null)
            return;

        hitAnimationSpeedTimer -= Time.deltaTime;
        if (hitAnimationSpeedTimer <= 0f)
        {
            animator.speed = 1f;
        }
    }

    private void UpdateDamageText()
    {
        if (damageText == null)
            return;

        int roundedDamage = Mathf.RoundToInt(damagePercent);
        string damageColor = GetDamageColor(roundedDamage);
        damageText.text =
            "<color=" + damageColor + ">" +
            "<size=86><b>" + roundedDamage + "</b></size>" +
            "<size=40><b>%</b></size></color>\n" +
            "<size=24><b>" + DamageLabel + "</b></size>";
    }

    private static string GetDamageColor(int roundedDamage)
    {
        if (roundedDamage >= 100)
            return "#FF6B73";

        if (roundedDamage >= 50)
            return "#FFD166";

        return "#FFFFFF";
    }

    protected virtual void OnDisable()
    {
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    protected virtual void OnDestroy()
    {
        fighterPresentation?.Dispose();
    }
}
