using UnityEngine;

// A ruins sentinel: readable shield intervals alternate with committed spear attacks.
// Damage, perception, navigation and the single-contact blade sweep stay in the base.
[DisallowMultipleComponent]
public sealed class BishopKnightAI : AdventureCreatureAI
{
    enum Phase { Idle, Notice, Approach, Guard, Windup, Strike, Recover, Stagger, Dizzy, Return, Chain, Rally }
    [Header("Territory guardian")]
    [SerializeField] bool isTerritoryBoss = true;
    [SerializeField] string territoryName = "Silver Sentinel";
    [Tooltip("Zero uses notice range. Acquisition stays inside the leash so its edge is not a healing loop.")]
    [SerializeField, Min(0)] float encounterRadius;
    [SerializeField, Min(.5f)] float guardEnterRange = 3.2f;
    [SerializeField, Min(.5f)] float approachStop = 2.9f;
    [SerializeField, Min(.1f)] float recoverySeconds = .9f;
    [SerializeField, Range(25f, 80f)] float shieldHalfAngle = 58f;
    [SerializeField, Min(.5f)] float guardSeconds = 1.65f;
    [SerializeField, Min(.5f)] float punishSeconds = 1.65f;
    Phase phase;
    float elapsed, guardFlash, unseen, blocked, punishRemaining, strikeOpen, strikeClose, strikeDuration;
    int guardedContacts, attackIndex;
    bool preserveFrontBreak, territoryEngaged;
    Quaternion strikeFacing;
    BishopKnightCombatVfx combatVfx;
    bool thrust, followupPending, secondInPair;
    Vector3 attackOrigin;
    bool rallied;
    Vector3 lastSeenTarget;
    float guardStress, sinceGuardHit;
    bool resistLightUntilAttack;
    public bool Rallying => phase == Phase.Rally;
    public bool AimLocked => Telegraphing && elapsed >= .18f;
    public float GuardStrain => GuardBroken ? 1 : guardStress;
    public bool GuardCritical => ShieldRaised && guardStress >= .65f;
    public int RallyCount { get; private set; }
    [Header("Readable attack envelope (world metres)")]
    [SerializeField, Min(.5f)] float attackReach = 3.8f;
    [SerializeField, Range(20, 88)] float sweepHalfAngle = 80f;
    [SerializeField, Min(.1f)] float thrustHalfWidth = .85f;
    public bool Enraged => HealthNormalized > 0 && HealthNormalized <= .5f;
    public bool PresentationActive => CombatActive && Time.timeScale > 0;
    public bool Telegraphing => phase == Phase.Windup;
    public bool Attacking => phase == Phase.Strike;
    public bool ChainPreparing => phase == Phase.Chain;
    public bool IsThrust => thrust;
    public bool SecondInPair => secondInPair;
    public Vector3 AttackOrigin => attackOrigin;
    public Quaternion AttackFacing => strikeFacing;
    public float AttackReach => attackReach;
    public float SweepHalfAngle => sweepHalfAngle;
    public float ThrustHalfWidth => thrustHalfWidth;
    public float ShieldHalfAngle => shieldHalfAngle;
    public Vector3 SpearBase => weapon ? weapon.TransformPoint(weaponBaseLocal) : transform.position;
    public Vector3 SpearTip => weapon ? weapon.TransformPoint(weaponTipLocal) : transform.position;

    public override string StateName => health && health.IsDead ? "Dead" : phase.ToString();
    public bool ShieldRaised => CombatActive && health && !health.IsDead && phase == Phase.Guard;
    public bool IsTerritoryBoss => isTerritoryBoss;
    public string TerritoryName => territoryName;
    public float EncounterRadius => Mathf.Min(encounterRadius > 0 ? encounterRadius : noticeRange,
        Mathf.Max(.5f, leashRange - .75f));
    // Encounter memory survives menus; the display gate additionally follows the
    // native world/player gate, death, scene changes and the actual territory.
    public bool TerritoryEngaged => isTerritoryBoss && territoryEngaged && isActiveAndEnabled && !IsDead;
    public bool HudEligible => TerritoryEngaged && CombatActive && PlayerInsideTerritory;
    public bool ReturningHome => phase == Phase.Return;
    public bool GuardBroken => punishRemaining > 0 && !IsDead;
    public bool Recovering => phase == Phase.Recover && !IsDead;
    public float HealthNormalized => health ? health.NormalizedHealth : 0;
    public int CurrentHealth => health ? health.CurrentHealth : 0;
    public int MaximumHealth => health ? health.MaxHealth : 0;
    public int GuardContacts => guardedContacts;
    public int TerritoryResetCount { get; private set; }
    public float PhaseElapsed => elapsed;
    public float StrikeOpenTime => strikeOpen;
    public float StrikeCloseTime => strikeClose;
    public float StrikeDuration => strikeDuration;
    public string TerritoryStatus => Rallying ? "SECOND PHASE" : GuardCritical ? "GUARD STRAINED" : ShieldRaised ? "GUARD" : GuardBroken ? "GUARD BROKEN" :
        Recovering || phase == Phase.Stagger ? "RECOVERY" : phase == Phase.Return ? "RETURNING" :
        phase == Phase.Chain ? "FOLLOW-UP" :
        phase == Phase.Windup ? (AimLocked ? "AIM LOCKED" : "PREPARING") : phase == Phase.Strike ?
            (secondInPair ? "SECOND STRIKE" : thrust ? "THRUST" : "SWEEP") : "";
    public int Blocks { get; private set; }
    public int GuardBreaks { get; private set; }
    bool PlayerInsideTerritory => player && WithinHome(player.transform.position);
    bool TargetInsideEncounter => Flat(Target - home) <= EncounterRadius && WithinHome(Target);

    protected override void Awake()
    {
        base.Awake();
        combatVfx = GetComponent<BishopKnightCombatVfx>();
        if (!combatVfx) combatVfx = gameObject.AddComponent<BishopKnightCombatVfx>();
        if (!GetComponent<BishopKnightShieldVfx>()) gameObject.AddComponent<BishopKnightShieldVfx>();
        if (isTerritoryBoss) BishopKnightTerritoryHud.Ensure(this);
    }

    protected override void ResetBrain()
    {
        TerritoryResetCount = 0;
        lastSeenTarget = home + homeFacing * Vector3.forward;
        ResetEncounter();
    }

    void ResetEncounter()
    {
        phase = Phase.Idle;
        elapsed = guardFlash = unseen = blocked = punishRemaining = 0;
        guardedContacts = attackIndex = Blocks = GuardBreaks = 0;
        preserveFrontBreak = territoryEngaged = false;
        followupPending = secondInPair = thrust = false;
        rallied = false; RallyCount = 0;
        guardStress = sinceGuardHit = 0;
        resistLightUntilAttack = false;
        strikeFacing = homeFacing;
        CancelStrike(); Cue(0); Stand("IdleNormal");
    }

    public override bool ResolveIncoming(ref DamageInfo damage)
    {
        if (!base.ResolveIncoming(ref damage)) return false;
        var source = damage.Source ? damage.Source.GetComponentInParent<PlayerVitals>() : null;
        // Other enemies, environmental damage and a shield-down knight never block.
        if (!ShieldRaised || !source || source != player || !damage.IsValid) return true;
        Vector3 toward = source.transform.position - transform.position;
        toward.y = 0;
        if (toward.sqrMagnitude < .001f)
            toward = Vector3.ProjectOnPlane(-damage.HitDirection, Vector3.up);
        if (toward.sqrMagnitude < .001f || Vector3.Dot(transform.forward, toward.normalized) <
            Mathf.Cos(shieldHalfAngle * Mathf.Deg2Rad)) return true;

        if (awareness) awareness.Confirm(source);
        guardedContacts++;
        sinceGuardHit = 0;
        guardStress = Mathf.Clamp01(guardStress + GuardPressure(damage.ImpactKind));
        BlockFeedback(damage.HitPoint);
        if (combatVfx) combatVfx.ShieldContact(damage.HitPoint);
        GetComponent<BishopKnightShieldVfx>()?.Contact();
        if (guardStress >= .999f && damage.ImpactKind != AttackImpactKind.Normal)
        {
            // A heavy front hit deals its normal damage and breaks the shield. The
            // ensuing accepted-hit callback must not replace Dizzy with GetHit.
            BreakGuard(); preserveFrontBreak = true;
            return true;
        }
        Blocks++;
        if (guardStress >= .999f) BreakGuard();
        else
        {
            guardFlash = .22f;
            Play("DefendHit", guardFlash, true, .04f);
        }
        return false;
    }

    public static float GuardPressure(AttackImpactKind kind) => kind == AttackImpactKind.Charged ? 1f :
        kind == AttackImpactKind.Heavy ? .6f : .12f;

    protected override bool InterruptOnHit(DamageInfo damage)
    {
        if (preserveFrontBreak || GuardBroken) return false;
        if (damage.ImpactKind != AttackImpactKind.Normal) return true;
        return !resistLightUntilAttack && phase != Phase.Strike && phase != Phase.Chain && !AimLocked;
    }

    protected override void ReceiveHit(DamageInfo damage)
    {
        // A real player hit inside the territory can resume a returning encounter,
        // retaining all HP loss. Hits from outside the leash cannot drag it away.
        var source = damage.Source ? damage.Source.GetComponentInParent<PlayerVitals>() : null;
        if (phase == Phase.Return && (!source || source != player || !PlayerInsideTerritory)) return;
        if (source && source == player && PlayerInsideTerritory) territoryEngaged = true;
        if (preserveFrontBreak)
        {
            preserveFrontBreak = false;
            return;
        }
        // Armor does not negate HP loss: only the attack/stagger interruption is
        // suppressed. Dizzy's existing deadline also cannot be extended by jabs.
        if (!InterruptOnHit(damage)) return;
        // Eligible flank hits stagger; repeated light hits still deal HP damage.
        // Cover stagger, recovery, guard and initial aim so light spam cannot
        // prevent the next commitment. Typed heavy hits can still interrupt.
        CancelStrike(); Cue(0);
        phase = Phase.Stagger; elapsed = guardFlash = unseen = 0;
        resistLightUntilAttack = true;
        Stand("GetHit"); Play("GetHit", .42f, true, .06f);
    }

    void BreakGuard()
    {
        GuardBreaks++; guardedContacts = 0; guardFlash = unseen = 0;
        guardStress = 0;
        punishRemaining = punishSeconds;
        Enter(Phase.Dizzy);
        Sound(EnemySoundCue.Stun, .85f);
    }

    void Enter(Phase next)
    {
        CancelStrike(); Cue(0); phase = next; elapsed = blocked = 0;
        if (next != Phase.Chain) followupPending = false;
        switch (next)
        {
            case Phase.Idle:
                guardedContacts = 0; unseen = 0; Stand("IdleNormal"); break;
            case Phase.Notice:
                territoryEngaged = true;
                Stand("SenseSomethingStart"); Play("SenseSomethingStart", .65f, true);
                Sound(EnemySoundCue.CoreLock, .6f); break;
            case Phase.Guard:
                guardFlash = 0; Stand("Defend"); break;
            case Phase.Recover:
                Stand("IdleBattle"); break;
            case Phase.Rally:
                rallied = true; RallyCount++;
                Stand("Taunting"); Play("Taunting", .95f, true, .08f);
                Sound(EnemySoundCue.CoreLock, .7f); break;
            case Phase.Dizzy:
                Stand("Dizzy"); Play("Dizzy", 0, true); break;
            case Phase.Return:
                preserveFrontBreak = false; punishRemaining = 0;
                if (awareness) awareness.Clear(); break;
        }
    }

    void BeginSpear()
    {
        bool nextThrust = (attackIndex++ & 1) != 0;
        // Below half health: a thrust can lead into a sweep. A sweep alone never
        // grows an unannounced third attack; every pair ends in a longer opening.
        followupPending = Enraged && nextThrust;
        BeginAttack(nextThrust, false);
    }

    void BeginAttack(bool useThrust, bool followup)
    {
        CancelStrike();
        // No Face calls from here through recovery: the player can leave this lane.
        strikeFacing = body.rotation;
        bool second = useThrust;
        thrust = useThrust; secondInPair = followup;
        attackOrigin = body.position;
        // Retiming preserves the native sweep/thrust poses around normalized
        // .41-.61, with a longer readable preparation for the larger sentinel.
        strikeOpen = second ? .68f : .58f;
        strikeClose = second ? .94f : .82f;
        strikeDuration = second ? 1.55f : 1.4f;
        phase = Phase.Windup; elapsed = 0;
        Play(second ? "Attack02" : "Attack01", strikeDuration, true, .08f);
        SetStrike(second ? 20 : 16, strikeOpen, strikeClose, true, .18f);
        Sound(EnemySoundCue.StoneScrape, .65f, strikeOpen);
    }

    // The warning and the additional contact gate share this exact footprint.
    // Actual animated spear contact + line of sight are STILL required by base.
    public bool InsideAttackEnvelope(Vector3 worldPoint, float padding = 0)
    {
        Vector3 p = Quaternion.Inverse(strikeFacing) * (worldPoint - attackOrigin);
        if (thrust) return p.z >= -padding && p.z <= attackReach + padding &&
            Mathf.Abs(p.x) <= thrustHalfWidth + padding;
        float radius = new Vector2(p.x, p.z).magnitude;
        return radius <= attackReach + padding &&
            (radius < padding || p.z >= radius * Mathf.Cos(sweepHalfAngle * Mathf.Deg2Rad) - padding);
    }
    protected override bool AcceptContact(Vector3 point) => InsideAttackEnvelope(point, .02f);

    protected override void TickBrain(float dt)
    {
        elapsed += dt;
        sinceGuardHit += dt;
        if (sinceGuardHit > 2f && !GuardBroken)
            guardStress = Mathf.MoveTowards(guardStress, 0, dt * .1f);
        if (Visible) lastSeenTarget = Target;
        punishRemaining = Mathf.Max(0, punishRemaining - dt);
        unseen = Visible ? 0 : unseen + dt;
        bool fighting = phase != Phase.Idle && phase != Phase.Notice && phase != Phase.Return;
        if (fighting && (!Engaged || !PlayerInsideTerritory || !WithinHome(Target) ||
            !WithinHome(transform.position) || unseen > 3.4f)) Enter(Phase.Return);
        if (Enraged && !rallied && punishRemaining <= 0 &&
            (phase == Phase.Guard || phase == Phase.Approach || phase == Phase.Recover))
            Enter(Phase.Rally);

        switch (phase)
        {
            case Phase.Idle:
                if (CanAcquire && TargetInsideEncounter) Enter(Phase.Notice);
                else if (awareness && awareness.Curious)
                { Stand("SenseSomethingMaint"); Face(awareness.InterestPosition, 140); }
                else Patrol(dt, .65f, "Taunting");
                break;
            case Phase.Notice:
                Face(Target, 130);
                if (elapsed >= .65f)
                {
                    if (Engaged && WithinHome(Target)) Enter(Phase.Approach);
                    else Enter(Phase.Return);
                }
                break;
            case Phase.Approach:
                if (Visible && Distance <= guardEnterRange) Enter(Phase.Guard);
                else
                {
                    if (MoveTo(Target, Distance > 4.5f ? walkSpeed * 1.45f : walkSpeed,
                        Mathf.Min(approachStop, guardEnterRange - .1f), Distance > 4.5f ? "RunFWD" : "WalkFWD")) blocked = 0;
                    else blocked += dt;
                    if (blocked > 1.35f) Enter(Phase.Return);
                }
                break;
            case Phase.Guard:
                // A shield impact never refreshes elapsed: guarding has a real end.
                float flashBefore = guardFlash;
                guardFlash = Mathf.Max(0, guardFlash - dt);
                if (flashBefore > 0 && guardFlash <= 0) Play("Defend");
                if (!Visible || Distance > guardEnterRange + .65f) Enter(Phase.Approach);
                else if (elapsed >= guardSeconds) BeginSpear();
                else if (guardFlash <= 0) Face(Target, 75);
                break;
            case Phase.Windup:
            case Phase.Strike:
                // Only the opening 180 ms can gently aim; the warning moves with
                // that aim. The remaining windup is fully committed and dodgeable.
                if (phase == Phase.Windup && elapsed < .18f)
                {
                    Vector3 aim = Target - body.position; aim.y = 0;
                    if (aim.sqrMagnitude > .001f)
                        strikeFacing = Quaternion.RotateTowards(strikeFacing, Quaternion.LookRotation(aim), 65f * dt);
                }
                body.MoveRotation(strikeFacing);
                TickStrike(elapsed);
                if (elapsed < strikeOpen) Cue(Mathf.Clamp01(elapsed / strikeOpen));
                else
                {
                    if (phase == Phase.Windup)
                    {
                        phase = Phase.Strike; resistLightUntilAttack = false; Cue(0);
                        Sound(EnemySoundCue.StonePunch, .75f);
                    }
                    if (elapsed < strikeClose && thrust) Step(strikeFacing * Vector3.forward, dt * .85f);
                }
                if (elapsed >= strikeDuration)
                {
                    if (followupPending && Visible && Distance < attackReach + .4f)
                        Enter(Phase.Chain);
                    else Enter(Phase.Recover);
                }
                break;
            case Phase.Rally:
                if (elapsed >= .95f) Enter(Visible && Distance <= guardEnterRange ? Phase.Guard : Phase.Approach);
                break;
            case Phase.Chain:
                // A short readable reset, then a fresh locked sweep telegraph.
                Stand("IdleBattle");
                if (elapsed < .18f) Face(Target, 120);
                if (elapsed >= .26f)
                {
                    followupPending = false;
                    BeginAttack(false, true);
                }
                break;
            case Phase.Recover:
                // Both the shield and tracking remain off during this opening.
                if (elapsed >= recoverySeconds + (secondInPair ? .4f : 0))
                    Enter(Visible && Distance <= guardEnterRange ? Phase.Guard : Phase.Approach);
                break;
            case Phase.Stagger:
                if (elapsed >= .42f)
                    Enter(punishRemaining > 0 ? Phase.Dizzy : Phase.Recover);
                break;
            case Phase.Dizzy:
                if (punishRemaining <= 0) Enter(Phase.Recover);
                break;
            case Phase.Return:
                bool returnHadTarget = Visible || Engaged;
                if (!ReturnHome(dt)) break;
                // ReturnHome clears awareness, so capture fresh visibility before
                // it does so. The decision below is supplied by this tick's sight.
                CompleteReturn(returnHadTarget);
                break;
        }
    }

    void CompleteReturn(bool returnHadTarget)
    {
        // A player still fighting within the territory gets the existing damaged
        // Knight back. Only a completed disengagement replenishes its health.
        if (PlayerInsideTerritory && returnHadTarget)
        {
            if (awareness) awareness.Confirm(player);
            Enter(Phase.Notice);
            return;
        }
        if (health && !health.IsDead && isTerritoryBoss && !PlayerInsideTerritory) health.RestoreFullHealth();
        if (awareness) awareness.Clear();
        TerritoryResetCount++;
        ResetEncounter();
        // If the player remains in the territory after an occluded return,
        // investigate the LAST SEEN location. Do not blindly patrol away before
        // distant recognition can complete, and do not magically confirm sight.
        if (PlayerInsideTerritory && health && health.CurrentHealth < health.MaxHealth)
        {
            Vector3 interest = lastSeenTarget - body.position; interest.y = 0;
            if (interest.sqrMagnitude > .001f) body.MoveRotation(Quaternion.LookRotation(interest));
        }
    }
}
