using UnityEngine;

// A ground-only bank sentry: measure distance, bait a step, commit the harpoon,
// then give ground. Weapon contact, safe footing and modal suspension live in base.
[DisallowMultipleComponent]
public sealed class FishmanAI : AdventureCreatureAI
{
    [Header("Sentry spacing")]
    [SerializeField, Min(.6f)] float attackRange = 2.05f;
    [SerializeField, Min(.3f)] float closeSweepRange = 1.15f;
    [SerializeField, Min(.1f)] float alertSeconds = .60f;
    [SerializeField, Min(.1f)] float feintSeconds = .34f;
    [SerializeField, Min(.1f)] float recoverSeconds = .64f;
    [SerializeField, Min(.1f)] float retreatSeconds = .46f;
    [SerializeField, Min(.2f)] float lostSightSeconds = 2.2f;
    [SerializeField, Min(.1f)] float staggerSeconds = .38f;
    [Header("Authored spear contact (seconds after clip start)")]
    [SerializeField, Min(1)] int thrustDamage = 9, sweepDamage = 11;
    [SerializeField, Min(.2f)] float thrustDuration = .95f, sweepDuration = 1.25f;
    [SerializeField, Min(.05f)] float thrustOpen = .38f, thrustClose = .58f;
    [SerializeField, Min(.05f)] float sweepOpen = .52f, sweepClose = .78f;
    [SerializeField, Min(.05f)] float turnLockLead = .20f;

    enum State { Idle, Alert, Approach, Feint, Attack, Recover, Retreat, Search, Return, Stagger }
    State state;
    float age, unseen, blocked, attackDuration, attackOpen;
    int attacks;
    bool swingSound;
    Vector3 lastSeen;
    public override string StateName => health && health.IsDead ? "Dead" : state.ToString();

    protected override void ResetBrain()
    {
        attacks = 0; unseen = blocked = 0; lastSeen = home;
        Enter(State.Idle, "IdleNormal");
    }

    protected override void TickBrain(float dt)
    {
        age += dt;
        if (Visible) { lastSeen = Target; unseen = 0; } else unseen += dt;
        if (state != State.Return && (!WithinHome(body.position) || (Visible && !WithinHome(Target))))
            BeginReturn();
        switch (state)
        {
            case State.Idle:
                if (CanAcquire && WithinHome(Target)) Enter(State.Alert, "SenseSomethingStart", alertSeconds);
                else if (awareness.Curious) { Stand("SenseSomethingMaint"); Face(awareness.InterestPosition, 160); }
                else Patrol(dt, .75f, "Taunting");
                break;
            case State.Alert:
                Face(lastSeen, 220); Cue(Mathf.Clamp01(age / alertSeconds));
                if (age >= alertSeconds)
                    Enter(Visible && Engaged ? State.Approach : State.Search, "IdleBattle");
                break;
            case State.Approach:
                if (!Visible || !Engaged) { Enter(State.Search, "SenseSomethingMaint"); break; }
                Face(Target, 320);
                if (Distance <= attackRange)
                {
                    Stand();
                    if (!Facing(Target, .80f)) break;
                    // Every other approach measures the player's commitment first.
                    if ((attacks & 1) == 0) Enter(State.Feint, "IdleBattle");
                    else BeginAttack();
                }
                else
                {
                    bool running = Distance > 4f;
                    if (MoveTo(Target, walkSpeed * (running ? 1.45f : 1f), attackRange - .05f, running ? "RunFWD" : "WalkFWD")) blocked = 0;
                    else blocked += dt;
                    if (blocked > 1.2f) Enter(State.Search, "SenseSomethingMaint");
                }
                break;
            case State.Feint:
                if (!Visible || !Engaged) { Enter(State.Search, "SenseSomethingMaint"); break; }
                if (age < feintSeconds * .55f) Face(Target, 200);
                if (age < .14f && Distance < attackRange && Step(-transform.forward, walkSpeed * .35f * dt)) Play("WalkBWD");
                else Stand();
                Cue(Mathf.Lerp(.25f, .8f, Mathf.Clamp01(age / feintSeconds)));
                if (age >= feintSeconds)
                {
                    if (Distance <= attackRange + .20f && Facing(Target, .55f)) BeginAttack();
                    else Enter(State.Approach, "IdleBattle");
                }
                break;
            case State.Attack:
                // Track only before commitment. No lunge/root teleport or tracking
                // during the cutting window: a sidestep can make the spear miss.
                if (Visible && age < Mathf.Max(0, attackOpen - turnLockLead)) Face(Target, 180);
                TickStrike(age); Cue(age < attackOpen ? Mathf.Clamp01(age / attackOpen) : 0);
                if (!swingSound && age >= attackOpen)
                { swingSound = true; Sound(EnemySoundCue.StonePunch, .5f); }
                if (age >= attackDuration) Enter(State.Recover, "IdleBattle");
                break;
            case State.Recover:
                if (age >= recoverSeconds)
                    Enter(!Visible || !Engaged ? State.Search : Distance < attackRange + .6f ? State.Retreat : State.Approach, "IdleBattle");
                break;
            case State.Retreat:
                if (!Visible || !Engaged) { Enter(State.Search, "SenseSomethingMaint"); break; }
                Face(Target, 260);
                Vector3 away = Vector3.ProjectOnPlane(body.position - Target, Vector3.up);
                if (away.sqrMagnitude < .001f) away = -transform.forward;
                if (Step(away.normalized, walkSpeed * .7f * dt)) Play("WalkBWD"); else Stand();
                if (age >= retreatSeconds) Enter(State.Approach, "IdleBattle");
                break;
            case State.Search:
                if (Visible && Engaged) { Enter(State.Approach, "IdleBattle"); break; }
                if (unseen >= lostSightSeconds || !WithinHome(lastSeen)) { BeginReturn(); break; }
                if (FlatDistance(body.position, lastSeen) > .45f) MoveTo(lastSeen, walkSpeed * .8f, .45f);
                else Stand("SenseSomethingMaint");
                break;
            case State.Return:
                if (ReturnHome(dt)) Enter(State.Idle, "IdleNormal");
                break;
            case State.Stagger:
                if (age >= staggerSeconds) Enter(Visible && Engaged ? State.Approach : State.Search, "IdleBattle");
                break;
        }
    }

    void BeginAttack()
    {
        bool sweep = Distance < closeSweepRange;
        attackDuration = Mathf.Max(.25f, sweep ? sweepDuration : thrustDuration);
        attackOpen = Mathf.Clamp(sweep ? sweepOpen : thrustOpen, .05f, attackDuration - .10f);
        float close = Mathf.Clamp(sweep ? sweepClose : thrustClose, attackOpen + .02f, attackDuration - .02f);
        Enter(State.Attack, sweep ? "Attack02" : "Attack01", attackDuration);
        attacks++; swingSound = false;
        SetStrike(sweep ? sweepDamage : thrustDamage, attackOpen, close, true, .16f);
        Sound(EnemySoundCue.StoneScrape, .4f, attackOpen);
    }

    protected override void ReceiveHit(DamageInfo damage)
    {
        if (damage.Source) lastSeen = damage.Source.transform.position;
        unseen = blocked = 0;
        Enter(State.Stagger, "GetHit", staggerSeconds);
    }

    void BeginReturn()
    {
        awareness.Clear(); blocked = 0;
        Enter(State.Return, "WalkFWD");
    }

    void Enter(State next, string action, float duration = 0)
    {
        CancelStrike(); Cue(0); state = next; age = 0; blocked = 0;
        Play(action, duration, true);
    }

    bool Facing(Vector3 point, float dot)
    {
        Vector3 direction = Vector3.ProjectOnPlane(point - body.position, Vector3.up);
        return direction.sqrMagnitude < .001f || Vector3.Dot(transform.forward, direction.normalized) >= dot;
    }
    static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
}
