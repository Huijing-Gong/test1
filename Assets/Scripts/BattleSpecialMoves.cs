using UnityEngine;
using UnityEngine.InputSystem;

public enum BattleSpecialKind { None, Charge, Dash, Rise, Counter }
public enum BattleSpecialPhase { Ready, Startup, Charging, Active, Recovery, Helpless }

/// <summary>Rabbit specials own their motion and contacts, never a second independent attack input.</summary>
[DefaultExecutionOrder(-80)]
public sealed class BattleSpecialMoves : MonoBehaviour
{
    public BattleSpecialKind Kind { get; private set; }
    public BattleSpecialPhase Phase { get; private set; }
    public bool IsBusy => Kind != BattleSpecialKind.None;
    public bool IsCharging => Kind == BattleSpecialKind.Charge && Phase == BattleSpecialPhase.Charging;
    public float Charge01 { get; private set; }
    public float Phase01 => phaseDuration > 0f ? Mathf.Clamp01(1f - phaseTimer / phaseDuration) : 0f;
    public float FacingDirection { get; private set; } = 1f;
    public bool IsCounterWindow => Kind == BattleSpecialKind.Counter && Phase == BattleSpecialPhase.Active && !CounterSucceeded;
    public bool CounterSucceeded { get; private set; }
    public bool RecoverySpent { get; private set; }
    public bool DashSpent { get; private set; }
    public bool IsBeamActive => Kind == BattleSpecialKind.Charge && Phase == BattleSpecialPhase.Active;
    public Vector3 BeamOrigin { get; private set; }
    public Vector3 BeamEnd { get; private set; }
    public float BeamRadius { get; private set; }
    public float BeamMaxLength { get; private set; }
    public int BeamHitCount { get; private set; }
    // Do not let an Update-time edge catch finish Rise before FixedUpdate
    // applies its pending launch. Once launched, normal edge recovery resumes.
    public bool AllowLedgeGrab => Kind == BattleSpecialKind.Rise && Phase != BattleSpecialPhase.Startup && !launchPending;
    public BattleSpecialVfx Vfx { get; private set; }
    public bool LastContactLanded { get; private set; }
    public float RiseHeightVelocity => 20f;

    static readonly string[] PhaseNames = { "Ready", "Startup", "Charging", "Active", "Recovery", "Helpless" };
    Rigidbody body;
    BattlePlayerController controller;
    BattleAttack attack;
    PlayerDamage damage;
    BattleManager manager;
    BattleCombatInput combatInput;
    readonly Collider[] contacts = new Collider[24];
    readonly RaycastHit[] motionHits = new RaycastHit[24];
    readonly RaycastHit[] dashContactHits = new RaycastHit[24];
    readonly Collider[] beamContacts = new Collider[32];
    readonly RaycastHit[] beamWalls = new RaycastHit[32];
    readonly EnemyDamage[] beamHitTargets = new EnemyDamage[16];
    CapsuleCollider capsule;
    float phaseTimer, phaseDuration, chargeTime, dashSpeed;
    bool chargeReleased, chargeFromMouse, strikeConsumed, launchPending, wasGrounded;
    bool continuousCollisionOverride;
    CollisionDetectionMode previousCollisionMode;
    EnemyDamage counterTarget;
    int beamContactCount;
    bool beamWallImpactShown;
    Vector3 dashPreviousRoot;
    bool dashSweepReady;

    // Runtime composition keeps both the prefab and the overworld completely unchanged.
    void Awake()
    {
        body = GetComponent<Rigidbody>(); controller = GetComponent<BattlePlayerController>();
        attack = GetComponent<BattleAttack>(); damage = GetComponent<PlayerDamage>();
        combatInput = GetComponent<BattleCombatInput>();
        if (!combatInput) combatInput = gameObject.AddComponent<BattleCombatInput>();
        capsule = GetComponent<CapsuleCollider>();
        Vfx = GetComponent<BattleSpecialVfx>();
        if (!Vfx) Vfx = gameObject.AddComponent<BattleSpecialVfx>();
        if (!GetComponent<BattleSpecialPose>()) gameObject.AddComponent<BattleSpecialPose>();
    }

    void Start()
    {
        manager = FindFirstObjectByType<BattleManager>();
        wasGrounded = controller && controller.IsOnGround;
    }

    // Specials process right-click/K first, so simultaneous left-click/J cannot create two attacks.
    void Update()
    {
        bool playable = controller && controller.enabled && (!manager || manager.IsBattleActive && !manager.IsGameOver);
        if (!playable)
        {
            if (IsBusy) Cancel();
            return;
        }
        if (Time.timeScale <= 0f) return;
        if (damage && damage.IsInHitstun)
        {
            if (IsBusy) InterruptForHit();
            return;
        }

        bool grounded = controller.IsOnGround;
        if (grounded && (!IsBusy || Phase == BattleSpecialPhase.Helpless))
        {
            RecoverySpent = DashSpent = false;
            if (Phase == BattleSpecialPhase.Helpless) Finish();
        }
        if (wasGrounded && !grounded && Kind == BattleSpecialKind.Dash) DashSpent = true;
        wasGrounded = grounded;

        Keyboard keyboard = Keyboard.current;
        if (!IsBusy && combatInput && combatInput.SpecialPressed && !controller.IsLedgeHanging && !controller.IsLedgeClimbing &&
            !(keyboard != null && keyboard.sKey.isPressed && keyboard.spaceKey.wasPressedThisFrame))
        {
            BattleSpecialKind requested = ReadKind(keyboard);
            bool permitted = !(requested == BattleSpecialKind.Counter && !grounded) &&
                !(requested == BattleSpecialKind.Rise && RecoverySpent) &&
                !(requested == BattleSpecialKind.Dash && !grounded && DashSpent);
            if (permitted && (!attack || !attack.IsMovementLocked || attack.TryCancelComboForAction()))
            {
                chargeFromMouse = combatInput.SpecialPressFromMouse;
                Begin(requested, keyboard, grounded);
            }
        }
        if (!IsBusy) return;
        if (Kind == BattleSpecialKind.Charge && (!combatInput || !combatInput.ChargeHeld(chargeFromMouse))) chargeReleased = true;
        TickState();
        Vfx?.Tick(Kind, PhaseNames[(int)Phase], Charge01, FacingDirection);
    }

    // Vertical intent wins; conflicting keys cancel only their own axis.
    public static BattleSpecialKind ReadKind(Keyboard keyboard)
    {
        if (keyboard == null) return BattleSpecialKind.Charge;
        bool up = keyboard.wKey.isPressed, down = keyboard.sKey.isPressed;
        if (up != down) return up ? BattleSpecialKind.Rise : BattleSpecialKind.Counter;
        return keyboard.aKey.isPressed != keyboard.dKey.isPressed ? BattleSpecialKind.Dash : BattleSpecialKind.Charge;
    }

    void Begin(BattleSpecialKind kind, Keyboard keyboard, bool grounded)
    {
        Kind = kind; Charge01 = chargeTime = 0f;
        chargeReleased = strikeConsumed = CounterSucceeded = LastContactLanded = false;
        dashSweepReady = false;
        counterTarget = null; launchPending = false;
        BeamHitCount = beamContactCount = 0; beamWallImpactShown = false;
        for (int i = 0; i < beamHitTargets.Length; i++) beamHitTargets[i] = null;
        FacingDirection = keyboard != null && keyboard.aKey.isPressed != keyboard.dKey.isPressed
            ? (keyboard.dKey.isPressed ? 1f : -1f) : controller.FacingDirection;
        controller.CancelDefence(); controller.FaceLaunchDirection(FacingDirection);
        attack?.ResetComboForSpecial();
        switch (kind)
        {
            case BattleSpecialKind.Charge: Enter(BattleSpecialPhase.Startup, .12f); break;
            case BattleSpecialKind.Dash:
                DashSpent |= !grounded;
                dashSpeed = 14f; SetContinuousCollision();
                Enter(BattleSpecialPhase.Startup, .15f); break;
            case BattleSpecialKind.Rise:
                RecoverySpent = true; SetContinuousCollision();
                Enter(BattleSpecialPhase.Startup, .10f); break;
            case BattleSpecialKind.Counter:
                controller.SetSpecialDefencePose(true);
                Enter(BattleSpecialPhase.Startup, .08f); break;
        }
    }

    // Every phase has a bounded duration; no real-time coroutine continues behind pause menus.
    void TickState()
    {
        if (Phase == BattleSpecialPhase.Helpless) return;
        if (Phase == BattleSpecialPhase.Charging)
        {
            chargeTime += Time.deltaTime; Charge01 = Mathf.Clamp01(chargeTime);
            phaseTimer = Mathf.Max(0f, 1f - chargeTime);
            if (chargeReleased || Charge01 >= 1f) ReleaseCharge();
            return;
        }
        if (Phase == BattleSpecialPhase.Active)
        {
            if (Kind == BattleSpecialKind.Charge) ResolveBeam();
            if (Kind == BattleSpecialKind.Dash) ResolveDashTravel();
            if (Kind == BattleSpecialKind.Rise) ResolveStrike(.22f, .76f, new Vector3(.72f, .68f, .60f), 4f, .65f, .25f, .3f, 1f);
            if (Kind == BattleSpecialKind.Counter && CounterSucceeded) ResolveCounterStrike();
        }
        phaseTimer -= Time.deltaTime;
        if (phaseTimer > 0f) return;
        switch (Phase)
        {
            case BattleSpecialPhase.Startup:
                if (Kind == BattleSpecialKind.Charge)
                {
                    Enter(BattleSpecialPhase.Charging, 1f);
                    if (chargeReleased) ReleaseCharge();
                }
                else if (Kind == BattleSpecialKind.Dash)
                {
                    Enter(BattleSpecialPhase.Active, .30f);
                    dashPreviousRoot = body.position; dashSweepReady = true;
                    Vfx?.PlayRelease(Kind, transform.position, FacingDirection, 1f);
                    ResolveDashTravel(); // A touching opponent must not be passed before contact opens.
                }
                else if (Kind == BattleSpecialKind.Rise)
                { launchPending = true; Enter(BattleSpecialPhase.Active, .18f); Vfx?.PlayRelease(Kind, transform.position, FacingDirection, 1f); }
                else Enter(BattleSpecialPhase.Active, CounterSucceeded ? .10f : .22f);
                break;
            case BattleSpecialPhase.Active:
                if (Kind == BattleSpecialKind.Charge) Vfx?.ClearBeam();
                if (Kind == BattleSpecialKind.Counter && !CounterSucceeded) Vfx?.PlayCounter(false, transform.position, FacingDirection);
                controller.SetSpecialDefencePose(false);
                Enter(BattleSpecialPhase.Recovery, Kind == BattleSpecialKind.Charge ? Mathf.Lerp(.35f, .45f, Charge01) : Kind == BattleSpecialKind.Dash ? .40f + (LastContactLanded ? 0f : .12f) : Kind == BattleSpecialKind.Rise ? .28f : .40f);
                break;
            case BattleSpecialPhase.Recovery:
                if (Kind == BattleSpecialKind.Rise && !controller.IsOnGround) Enter(BattleSpecialPhase.Helpless, 0f);
                else Finish();
                break;
        }
    }

    void ReleaseCharge()
    {
        BeamMaxLength = Mathf.Lerp(4f, 14f, Charge01);
        BeamRadius = Mathf.Lerp(.12f, .42f, Charge01);
        Enter(BattleSpecialPhase.Active, .22f);
        ResolveBeam();
        Vfx?.PlayRelease(Kind, BeamOrigin, FacingDirection, Charge01);
    }

    // The short anchored beam follows the actual muzzle, but its facing is locked at startup.
    // Its collision sweep uses the displayed radius, clips at the first solid wall and remembers
    // every touched fighter for this release, including guarded contacts, to prevent repeat hits.
    void ResolveBeam()
    {
        Vector3 direction = Vector3.right * FacingDirection;
        Vector3 bodyOrigin = transform.position + Vector3.up * .22f;
        BeamOrigin = bodyOrigin + direction * .85f;
        BeamEnd = BeamOrigin;

        int overlapCount = Physics.OverlapSphereNonAlloc(bodyOrigin, BeamRadius, beamContacts, 1 << 0, QueryTriggerInteraction.Ignore);
        if (overlapCount == beamContacts.Length) { CollapseBeam(bodyOrigin); return; }
        for (int i = 0; i < overlapCount; i++)
            if (IsBeamWall(beamContacts[i])) { CollapseBeam(bodyOrigin); return; }

        // A hand/muzzle offset is not permission to emit on the other side of a close wall.
        int count = Physics.SphereCastNonAlloc(bodyOrigin, BeamRadius, direction, beamWalls, .85f, 1 << 0, QueryTriggerInteraction.Ignore);
        if (count == beamWalls.Length) { CollapseBeam(bodyOrigin); return; }
        float muzzleDistance = float.PositiveInfinity; Vector3 muzzleContact = default;
        for (int i = 0; i < count; i++)
        {
            if (!IsBeamWall(beamWalls[i].collider) || beamWalls[i].distance >= muzzleDistance) continue;
            muzzleDistance = beamWalls[i].distance; muzzleContact = beamWalls[i].point;
        }
        if (muzzleDistance < float.PositiveInfinity)
        {
            CollapseBeam(bodyOrigin + direction * Mathf.Max(0f, muzzleDistance - .015f));
            ShowBeamWallImpact(muzzleContact); return;
        }

        overlapCount = Physics.OverlapSphereNonAlloc(BeamOrigin, BeamRadius, beamContacts, 1 << 0, QueryTriggerInteraction.Ignore);
        if (overlapCount == beamContacts.Length) { CollapseBeam(BeamOrigin); return; }
        for (int i = 0; i < overlapCount; i++)
            if (IsBeamWall(beamContacts[i])) { CollapseBeam(BeamOrigin); return; }

        // The full-radius wall sweep trims the entire beam, not merely its thin center line.
        count = Physics.SphereCastNonAlloc(BeamOrigin, BeamRadius, direction, beamWalls, BeamMaxLength, 1 << 0, QueryTriggerInteraction.Ignore);
        if (count == beamWalls.Length) { CollapseBeam(BeamOrigin); return; }
        float effectiveLength = BeamMaxLength;
        bool clipped = false; Vector3 wallContact = default;
        for (int i = 0; i < count; i++)
        {
            if (!IsBeamWall(beamWalls[i].collider)) continue;
            float safeDistance = Mathf.Max(0f, beamWalls[i].distance - .015f);
            if (safeDistance >= effectiveLength) continue;
            effectiveLength = safeDistance; wallContact = beamWalls[i].point; clipped = true;
        }
        BeamEnd = BeamOrigin + direction * effectiveLength;
        if (effectiveLength <= .015f) { Vfx?.ClearBeam(); if (clipped) ShowBeamWallImpact(wallContact); return; }

        int targets = Physics.OverlapCapsuleNonAlloc(BeamOrigin, BeamEnd, BeamRadius, beamContacts, ~0, QueryTriggerInteraction.Ignore);
        if (targets == beamContacts.Length) { CollapseBeam(BeamOrigin); return; }
        Vfx?.SetBeam(BeamOrigin, BeamEnd, BeamRadius, Charge01);
        if (clipped) ShowBeamWallImpact(wallContact);
        for (int i = 0; i < targets; i++)
        {
            Collider hit = beamContacts[i]; EnemyDamage enemy = hit ? hit.GetComponentInParent<EnemyDamage>() : null;
            if (!enemy || !enemy.enabled || !enemy.gameObject.activeInHierarchy || !enemy.CompareTag("Enemy")) continue;
            bool alreadyTouched = false;
            for (int j = 0; j < beamContactCount; j++) if (beamHitTargets[j] == enemy) { alreadyTouched = true; break; }
            if (alreadyTouched || beamContactCount >= beamHitTargets.Length) continue;

            float along = Mathf.Clamp(Vector3.Dot(hit.bounds.center - BeamOrigin, direction), 0f, effectiveLength);
            Vector3 point = hit.ClosestPoint(BeamOrigin + direction * along);
            float contactAlong = Vector3.Dot(point - BeamOrigin, direction);
            // Capsule overlap has round caps; reject contacts outside the visible flat beam ends.
            if (contactAlong < -.001f || contactAlong > effectiveLength + .001f)
            {
                Vector3 endPlane = contactAlong < 0f ? BeamOrigin : BeamEnd;
                if (!TryFlatBeamCapsuleContact(hit as CapsuleCollider, endPlane, out point)) continue;
            }
            if (WorldBetween(BeamOrigin, point, enemy.transform)) continue;
            beamHitTargets[beamContactCount++] = enemy;
            bool blocked = enemy.TakeHit(FacingDirection, Mathf.Lerp(4f, 10f, Charge01), Mathf.Lerp(.34f, 1.05f, Charge01),
                Mathf.Lerp(.20f, .34f, Charge01), -1, .97f, .19f, transform, false);
            if (!blocked)
            {
                BeamHitCount++; LastContactLanded = true;
                Vfx?.PlayImpact(point, FacingDirection, Mathf.Lerp(.4f, 1f, Charge01));
                PresentHit(enemy,point,Charge01);
            }
            Debug.Log("Player K Charge beam " + (blocked ? "blocked." : "connected."));
        }
    }

    bool IsBeamWall(Collider hit) => hit && !hit.transform.IsChildOf(transform) && !hit.GetComponentInParent<BattleDamageReceiver>();

    // A capsule's nearest point to the axis can lie beyond a flat beam end even
    // while its lower/upper curved edge really intersects that end disk. Solve
    // the vertical capsule's exact section in the end's X plane; never accept
    // the broad-phase beam's invisible round cap or increase its radius/range.
    bool TryFlatBeamCapsuleContact(CapsuleCollider target, Vector3 endPlane, out Vector3 contact)
    {
        contact = default;
        if (!target || !target.enabled) return false;
        Vector3 localAxis = target.direction == 0 ? Vector3.right : target.direction == 2 ? Vector3.forward : Vector3.up;
        Vector3 scaledAxis = target.transform.TransformVector(localAxis);
        float axisScale = scaledAxis.magnitude;
        if (axisScale <= .00001f) return false;
        Vector3 worldAxis = scaledAxis / axisScale;
        // Side A fighters have vertical physics capsules, regardless of the visual rig's rotation.
        // Tilted/different shapes remain conservatively rejected rather than using wrong geometry.
        if (Mathf.Abs(worldAxis.x) > .0001f || Mathf.Abs(worldAxis.z) > .0001f) return false;

        Vector3 scale = target.transform.lossyScale;
        float radialScale = target.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)) :
            target.direction == 2 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float worldRadius = target.radius * radialScale;
        if (worldRadius <= .00001f) return false;
        Vector3 center = target.transform.TransformPoint(target.center);
        float planeOffset = endPlane.x - center.x;
        if (Mathf.Abs(planeOffset) > worldRadius) return false;
        float sectionRadius = Mathf.Sqrt(Mathf.Max(0f, worldRadius * worldRadius - planeOffset * planeOffset));
        float segmentHalf = Mathf.Max(0f, target.height * axisScale * .5f - worldRadius);
        float segmentY = Mathf.Clamp(endPlane.y, center.y - segmentHalf, center.y + segmentHalf);
        Vector2 fromSegmentToBeam = new Vector2(endPlane.y - segmentY, endPlane.z - center.z);
        float radialDistance = fromSegmentToBeam.magnitude;
        float gap = Mathf.Max(0f, radialDistance - sectionRadius);
        if (gap > BeamRadius) return false;

        if (radialDistance <= sectionRadius || radialDistance <= .00001f)
            contact = endPlane;
        else
        {
            Vector2 sectionPoint = fromSegmentToBeam * (sectionRadius / radialDistance);
            contact = new Vector3(endPlane.x, segmentY + sectionPoint.x, center.z + sectionPoint.y);
        }
        return true;
    }

    void CollapseBeam(Vector3 position) { BeamOrigin = BeamEnd = position; Vfx?.ClearBeam(); }

    void ShowBeamWallImpact(Vector3 position)
    {
        if (beamWallImpactShown) return;
        beamWallImpactShown = true;
        Vfx?.PlayImpact(position, FacingDirection, .3f, true);
    }

    // Called only by the movement controller: physics owns displacement and stage collisions remain solid.
    public void ApplyMotion(Rigidbody rb, float moveInput, float moveSpeed)
    {
        if (!IsBusy || !rb) return;
        Vector3 velocity = rb.linearVelocity;
        velocity.z = 0f;
        if (Kind == BattleSpecialKind.Charge)
            velocity.x = Phase == BattleSpecialPhase.Recovery || Phase == BattleSpecialPhase.Active ? 0f : moveInput * moveSpeed * .24f;
        else if (Kind == BattleSpecialKind.Dash)
        {
            // Inspect the last completed physics displacement, not a predicted
            // future endpoint. This also catches several fixed steps per render.
            if (Phase == BattleSpecialPhase.Active) ResolveDashTravel();
            velocity.x = Phase == BattleSpecialPhase.Active ? FacingDirection * dashSpeed : 0f;
            if (Phase == BattleSpecialPhase.Active && IsWorldBlockingMotion(Vector3.right * FacingDirection, dashSpeed * Time.fixedDeltaTime + .06f))
            {
                velocity.x = 0f;
                Vfx?.PlayImpact(transform.position + Vector3.right * FacingDirection * .6f, FacingDirection, .4f, true);
                Enter(BattleSpecialPhase.Recovery, .52f);
            }
        }
        else if (Kind == BattleSpecialKind.Rise)
        {
            if (launchPending)
            { velocity.y = RiseHeightVelocity; launchPending = false; }
            velocity.x = Phase == BattleSpecialPhase.Startup ? 0f : moveInput * moveSpeed * .58f;
        }
        else velocity.x = 0f;
        rb.linearVelocity = velocity;
    }

    bool IsWorldBlockingMotion(Vector3 direction, float distance)
    {
        if (!capsule || !capsule.enabled) return false;
        Bounds bounds = capsule.bounds;
        float radius = Mathf.Max(.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) - .035f);
        float halfSegment = Mathf.Max(0f, bounds.extents.y - radius - .035f);
        int count = Physics.CapsuleCastNonAlloc(bounds.center + Vector3.up * halfSegment, bounds.center - Vector3.up * halfSegment,
            radius, direction, motionHits, distance, 1 << 0, QueryTriggerInteraction.Ignore);
        if (count == motionHits.Length) return true;
        for (int i = 0; i < count; i++)
        {
            Collider hit = motionHits[i].collider;
            if (!hit || hit.transform.IsChildOf(transform) || motionHits[i].normal.y > .55f || hit.bounds.max.y <= bounds.min.y + .06f) continue;
            return true;
        }
        return false;
    }

    void ResolveDashTravel()
    {
        if (!body || Phase != BattleSpecialPhase.Active) return;
        Vector3 current = body.position;
        Vector3 previous = dashSweepReady ? dashPreviousRoot : current;
        dashPreviousRoot = current; dashSweepReady = true;
        if (strikeConsumed) return;
        Vector3 offset = new Vector3(FacingDirection * .80f, -.12f, 0);
        Vector3 half = new Vector3(.65f, .48f, .60f);
        Vector3 start = previous + offset;
        // Unity shape casts omit existing overlaps, so test the starting box too.
        int count = Physics.OverlapBoxNonAlloc(start, half, contacts, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        if (count == contacts.Length) return;
        for (int i = 0; i < count; i++)
            if (TryDashContact(contacts[i], previous, start)) return;
        Vector3 travel = current - previous;
        float distance = travel.magnitude;
        if (distance <= .00001f) return;
        int swept = Physics.BoxCastNonAlloc(start, half, travel / distance, dashContactHits,
            Quaternion.identity, distance, ~0, QueryTriggerInteraction.Ignore);
        if (swept == dashContactHits.Length) return;
        for (int i = 0; i < swept; i++)
        {
            Vector3 contactCenter = start + travel.normalized * dashContactHits[i].distance;
            if (TryDashContact(dashContactHits[i].collider, previous, contactCenter)) return;
        }
    }

    bool TryDashContact(Collider hit, Vector3 sweptFromRoot, Vector3 center)
    {
        EnemyDamage enemy = hit ? hit.GetComponentInParent<EnemyDamage>() : null;
        if (!enemy || !enemy.enabled || !enemy.CompareTag("Enemy")) return false;
        // Compare with the start of the actual segment: after a physics step
        // the same legitimate target may already be behind the current root.
        if ((enemy.transform.position.x - sweptFromRoot.x) * FacingDirection <= .05f) return false;
        Vector3 point = hit.ClosestPoint(center);
        if (WorldBetween(center, point, enemy.transform, sweptFromRoot)) return false;
        strikeConsumed = true;
        bool blocked = enemy.TakeHit(FacingDirection, 7f, .88f, .28f, -1, 1f, .18f, transform, false);
        LastContactLanded = !blocked;
        if (!blocked) { Vfx?.PlayImpact(point, FacingDirection, .7f); PresentHit(enemy, point, .7f); }
        Debug.Log("Player K Dash" + (blocked ? " blocked." : " connected."));
        return true;
    }

    // Melee specials use compact directional volumes during their actual contact phase only.
    void ResolveStrike(float x, float y, Vector3 half, float amount, float knockback, float hitstun, float horizontalWeight, float verticalWeight)
    {
        if (strikeConsumed) return;
        Vector3 center = transform.position + new Vector3(FacingDirection * x, y, 0f);
        int count = Physics.OverlapBoxNonAlloc(center, half, contacts, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        if (count == contacts.Length) return;
        for (int i = 0; i < count; i++)
        {
            Collider hit = contacts[i]; EnemyDamage enemy = hit ? hit.GetComponentInParent<EnemyDamage>() : null;
            if (!enemy || !enemy.enabled || !enemy.CompareTag("Enemy")) continue;
            if (Kind == BattleSpecialKind.Dash && (enemy.transform.position.x - transform.position.x) * FacingDirection <= .05f) continue;
            Vector3 point = hit.ClosestPoint(center);
            if (WorldBetween(center, point, enemy.transform)) continue;
            strikeConsumed = true;
            bool blocked = enemy.TakeHit(FacingDirection, amount, knockback, hitstun, -1, horizontalWeight, verticalWeight, transform, false);
            LastContactLanded = !blocked;
            if (!blocked)
            {
                Vfx?.PlayImpact(point, FacingDirection, Kind == BattleSpecialKind.Dash ? .7f : .45f);
                PresentHit(enemy,point,.7f);
            }
            Debug.Log("Player K " + Kind + (blocked ? " blocked." : " connected."));
            return;
        }
    }

    bool WorldBetween(Vector3 from, Vector3 to, Transform target, Vector3? sweptRoot = null)
    {
        // A forward hitbox can already straddle a thin wall while the actual capsule stays outside.
        // Unity rays starting inside that wall can miss it, so first check from the fighter's
        // real X/Z at the same contact height. Keep the local hitbox ray as a second safeguard.
        Vector3 root = sweptRoot ?? transform.position;
        Vector3 bodyOrigin = new Vector3(root.x, from.y, root.z);
        if ((bodyOrigin - from).sqrMagnitude > .0001f && BlocksStrikePath(bodyOrigin, to, target))
            return true;
        return BlocksStrikePath(from, to, target);
    }

    bool BlocksStrikePath(Vector3 from, Vector3 to, Transform target)
    {
        return Physics.Linecast(from, to, out RaycastHit obstruction, 1 << 0, QueryTriggerInteraction.Ignore) &&
            !obstruction.collider.transform.IsChildOf(transform) && !obstruction.collider.transform.IsChildOf(target);
    }

    // Only a real nearby frontal enemy source can consume a counter window; no air swing/proximity trigger.
    public bool TryCounterHit(float hitDirection, Transform source)
    {
        if (!IsCounterWindow || !source || !controller.IsOnGround || Time.timeScale <= 0f || damage && damage.IsInHitstun) return false;
        EnemyDamage enemy = source.GetComponentInParent<EnemyDamage>();
        if (!enemy || !enemy.enabled || !enemy.CompareTag("Enemy")) return false;
        Vector3 offset = enemy.transform.position - transform.position;
        if (FacingDirection * hitDirection >= -.5f || offset.x * FacingDirection <= .05f || Mathf.Abs(offset.y) > 1.1f || offset.sqrMagnitude > 2.25f * 2.25f) return false;
        Vector3 contact = enemy.transform.position + Vector3.up * .1f;
        if (WorldBetween(transform.position + Vector3.up * .1f, contact, enemy.transform)) return false;
        counterTarget = enemy; CounterSucceeded = true; strikeConsumed = false;
        controller.SetSpecialDefencePose(false);
        Vfx?.PlayCounter(true, Vector3.Lerp(transform.position, contact, .5f), FacingDirection);
        Enter(BattleSpecialPhase.Startup, .10f);
        Debug.Log("Player K Counter caught real frontal contact.");
        return true;
    }

    // The follow-up must still connect locally: a successful guard never remotely damages its attacker.
    void ResolveCounterStrike()
    {
        if (strikeConsumed || !counterTarget || !counterTarget.enabled) return;
        Vector3 center = transform.position + new Vector3(FacingDirection * .80f, .15f, 0f);
        int count = Physics.OverlapBoxNonAlloc(center, new Vector3(.75f, .65f, .65f), contacts, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        if (count == contacts.Length) return;
        for (int i = 0; i < count; i++)
        {
            if (contacts[i].GetComponentInParent<EnemyDamage>() != counterTarget) continue;
            Vector3 point = contacts[i].ClosestPoint(center);
            if ((counterTarget.transform.position.x - transform.position.x) * FacingDirection <= .05f || WorldBetween(center, point, counterTarget.transform)) continue;
            strikeConsumed = true;
            LastContactLanded = !counterTarget.TakeHit(FacingDirection, 9f, 1.05f, .32f, -1, .95f, .25f, transform, false);
            if (LastContactLanded)
            {
                Vfx?.PlayImpact(point, FacingDirection, .8f);
                PresentHit(counterTarget,point,1f);
            }
            return;
        }
    }

    // The opponent may notice the visible charging hands, never the future release input.
    void PresentHit(BattleDamageReceiver target,Vector3 point,float power)
    {
        BattleCombatFeedback.PlayKHitStop(Kind,Charge01);
        if(Kind==BattleSpecialKind.Charge && Charge01>.8f)
            BattleCameraController.RequestDirectionalImpact(point,2.6f,FacingDirection);
        else BattleCameraController.RequestPresentationShake(point,Mathf.Lerp(1f,3.6f,power),.12f,
            Kind==BattleSpecialKind.Rise?new Vector2(.45f,1):new Vector2(1,.55f));
        target.NotifyPresentation(power);
        var reaction=target.GetComponent<BattleJHitReaction>();
        if(!reaction)reaction=target.gameObject.AddComponent<BattleJHitReaction>();
        reaction.Play(Kind==BattleSpecialKind.Rise?BattleJMove.Upper:BattleJMove.Side,FacingDirection,power>.8f?2:1);
    }

    public bool TryGetVisibleChargeThreat(Vector3 target, float horizontalRange, out Vector3 position)
    {
        position = transform.position + new Vector3(FacingDirection * .85f, .22f, 0f);
        if (!IsCharging || Charge01 < .12f) return false;
        Vector3 offset = target - position;
        if (offset.x * FacingDirection <= 0f || Mathf.Abs(offset.x) > horizontalRange || Mathf.Abs(offset.y) > 1.05f) return false;
        return !WorldBetween(position, target, transform);
    }

    public void OnLedgeGrabbed() { if (AllowLedgeGrab) Finish(); }
    public void OnSafeLedgeClimbCompleted() { RecoverySpent = DashSpent = false; }

    void Enter(BattleSpecialPhase phase, float duration)
    { Phase = phase; phaseDuration = phaseTimer = Mathf.Max(0f, duration); }

    void SetContinuousCollision()
    {
        if (!body || continuousCollisionOverride) return;
        previousCollisionMode = body.collisionDetectionMode;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        continuousCollisionOverride = true;
    }

    void Finish()
    {
        controller?.SetSpecialDefencePose(false);
        if (body && continuousCollisionOverride) body.collisionDetectionMode = previousCollisionMode;
        continuousCollisionOverride = false; Kind = BattleSpecialKind.None;
        Enter(BattleSpecialPhase.Ready, 0f); launchPending = false; counterTarget = null; chargeFromMouse = false; dashSweepReady = false;
        BeamEnd = BeamOrigin; Vfx?.ClearBeam();
        Vfx?.ClearContinuous();
    }

    // Getting hurt cancels both charging and an anchored active beam immediately.
    public void InterruptForHit() { GetComponent<BattleAfterimages>()?.Clear(); Finish(); }

    /// <summary>Menus discard an unreleased charge, never an already fired beam or an airborne recovery.</summary>
    public void CancelUnreleasedCharge()
    {
        if (Kind == BattleSpecialKind.Charge &&
            (Phase == BattleSpecialPhase.Startup || Phase == BattleSpecialPhase.Charging))
            Cancel();
    }

    // Round/travel/disable cleanup removes the anchored beam and every continuous effect state.
    public void Cancel()
    {
        GetComponent<BattleAfterimages>()?.Clear();
        Finish();
        Vfx?.Clear();
    }

    void OnDisable() { Cancel(); RecoverySpent = DashSpent = false; }
    void OnDestroy()
    {
        Cancel();
    }
}
