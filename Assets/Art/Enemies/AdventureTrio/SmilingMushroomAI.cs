using UnityEngine;

// A small, readable ambusher, not a disguised damage volume. It must be awake,
// visibly committed and in real body contact before its single hit can land.
[DisallowMultipleComponent]
public sealed class SmilingMushroomAI : AdventureCreatureAI
{
    enum Phase { Plant, Wake, Watch, Approach, Telegraph, Attack, Recover, Retreat, Return, Settle, Stagger }
    [Header("Smiling mushroom: wake and body bump")]
    [Min(.2f)] public float wakeSeconds = .67f;
    [Min(.25f)] public float warningSeconds = .48f;
    [Min(.4f)] public float attackSeconds = .84f;
    [Min(.15f)] public float recoverySeconds = .58f;
    [Min(.1f)] public float staggerSeconds = .42f;
    [Min(.5f)] public float attackRange = 1.35f;
    [Min(0f)] public float lungeDistance = .75f;
    [Min(1)] public int attackDamage = 5;
    Phase phase;
    float age, lostAge;
    Vector3 attackDirection;
    int pattern;
    bool shortBump;
    public override string StateName => IsDead ? "Dead" : phase.ToString();
    public bool IsDisguised => !IsDead && phase == Phase.Plant;
    public int WakeCount { get; private set; }
    public int AttackCount { get; private set; }
    public float PhaseTime => age;

    protected override void ResetBrain()
    {
        age = lostAge = 0f; pattern = 0; attackDirection = Vector3.zero;
        WakeCount = AttackCount = 0;
        CancelStrike(); Cue(0f);
        phase = Phase.Plant;
        Play("IdlePlant", 0f, true, .18f);
    }

    protected override void ReceiveHit(DamageInfo hit)
    {
        // An accepted player hit reveals the plant immediately, including from behind.
        if (phase == Phase.Plant) WakeCount++;
        Enter(Phase.Stagger);
        Sound(EnemySoundCue.GelHit, .42f);
    }

    protected override void TickBrain(float dt)
    {
        age += dt;
        if (Engaged) lostAge = 0f; else lostAge += dt;
        if (phase != Phase.Plant && phase != Phase.Return && phase != Phase.Settle &&
            phase != Phase.Stagger && (!WithinHome(body.position) ||
            (Engaged && !WithinHome(Target))))
        { Enter(Phase.Return); return; }

        switch (phase)
        {
            case Phase.Plant:
                // Suspicion gets the shared '?' marker, but does not reveal the body.
                if (Engaged) { WakeCount++; Enter(Phase.Wake); }
                else if (!AmbientRest(dt, "IdlePlant", "IdlePlant")) Stand("IdlePlant");
                break;
            case Phase.Wake:
                Face(Target, 150f);
                if (age >= wakeSeconds) Enter(Phase.Watch);
                break;
            case Phase.Watch:
                Face(Target);
                if (age >= .28f) Enter(Engaged ? Phase.Approach : Phase.Return);
                break;
            case Phase.Approach:
                if (!Engaged) { Enter(Phase.Return); break; }
                if (Visible && Distance <= attackRange)
                { Enter(Phase.Telegraph); break; }
                // Target is perception's last-seen position, never an occluded live player.
                if (!Visible && Vector3.ProjectOnPlane(Target - body.position, Vector3.up).sqrMagnitude < 1f)
                    Stand("SenseSomethingMaintain");
                else MoveTo(Target, walkSpeed * 1.3f, .65f, "RunFWD");
                break;
            case Phase.Telegraph:
                // Turning stops before launch: a dodge can genuinely make this miss.
                if (age < warningSeconds * .65f) Face(Target, 180f);
                Cue(Mathf.Clamp01(age / warningSeconds));
                if (age >= warningSeconds)
                {
                    if (Visible && Distance <= attackRange + .8f && WithinHome(Target))
                        Enter(Phase.Attack);
                    else Enter(Phase.Recover);
                }
                break;
            case Phase.Attack:
                // All three native Smile attacks are brief melee motions, not poison.
                // The body only travels during its committed middle part; Step rejects
                // walls/unsupported terrain. No homing, teleport, or distance-only hit.
                float from = attackSeconds * .18f, until = attackSeconds * .48f;
                float moveTime = Mathf.Max(0f, Mathf.Min(age, until) - Mathf.Max(age - dt, from));
                if (moveTime > 0f)
                    Step(attackDirection, lungeDistance * (shortBump ? .32f : 1f) * moveTime / (until - from));
                TickStrike(age);
                if (age >= attackSeconds) Enter(Phase.Recover);
                break;
            case Phase.Recover:
                Stand("IdleBattle");
                if (age >= recoverySeconds) Enter(Engaged ? Phase.Retreat : Phase.Return);
                break;
            case Phase.Retreat:
                Face(Target, 240f);
                Vector3 away = body.position - Target; away.y = 0f;
                if (away.sqrMagnitude > .001f && Distance < 2.15f)
                {
                    Vector3 next = body.position + away.normalized * dt * walkSpeed * .65f;
                    if (WithinHome(next)) Step(away.normalized, dt * walkSpeed * .65f);
                }
                if (age >= .48f) Enter(Engaged ? Phase.Approach : Phase.Return);
                break;
            case Phase.Return:
                if (Engaged && Visible && WithinHome(Target) && WithinHome(body.position))
                { Enter(Phase.Approach); break; }
                if (ReturnHome(dt)) Enter(Phase.Settle);
                break;
            case Phase.Settle:
                Stand("IdleNormal");
                if (Engaged && WithinHome(Target)) Enter(Phase.Approach);
                else if (age >= .75f && lostAge >= 1f) Enter(Phase.Plant);
                break;
            case Phase.Stagger:
                if (age >= staggerSeconds) Enter(Engaged ? Phase.Approach : Phase.Return);
                break;
        }
    }

    void Enter(Phase next)
    {
        CancelStrike(); Cue(0f); phase = next; age = 0f;
        switch (next)
        {
            case Phase.Plant:
                body.rotation = homeFacing;
                Play("IdlePlant", 0f, true, .25f); break;
            case Phase.Wake:
                Play("IdlePlantToBattle", wakeSeconds, true, .07f);
                Sound(EnemySoundCue.GelJump, .32f); break;
            case Phase.Watch:
                Play("IdleBattle"); break;
            case Phase.Approach:
                Play("RunFWD"); break;
            case Phase.Telegraph:
                Play("SenseSomethingStart", warningSeconds, true, .08f);
                Sound(EnemySoundCue.GelCharge, .35f, warningSeconds); break;
            case Phase.Attack:
                shortBump = Distance < .85f;
                attackDirection = Target - body.position; attackDirection.y = 0f;
                attackDirection = attackDirection.sqrMagnitude > .001f ? attackDirection.normalized : transform.forward;
                Face(body.position + attackDirection, 10000f);
                pattern = (pattern + 1) % 3; AttackCount++;
                Play(pattern == 0 ? "Attack03" : pattern == 1 ? "Attack01" : "Attack02", attackSeconds, true, .045f);
                SetStrike(attackDamage, attackSeconds * .23f, attackSeconds * .67f, false, .13f);
                Sound(EnemySoundCue.GelJump, .42f); break;
            case Phase.Recover:
                Play("IdleBattle", 0f, true, .12f); break;
            case Phase.Retreat:
                Play("WalkBWD", 0f, true); break;
            case Phase.Return:
                Play("WalkFWD"); break;
            case Phase.Settle:
                Play("IdleNormal", 0f, true, .18f); break;
            case Phase.Stagger:
                Play("GetHit", staggerSeconds, true, .045f); break;
        }
    }
}
