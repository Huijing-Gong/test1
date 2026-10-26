using UnityEngine;

// Independent charging encounter. All displacement uses swept, terrain-checked movement.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class RailBeetleAI : MonoBehaviour
{
    public enum BehaviourState { Patrol, Seek, Windup, Charge, Recover, WallStun, Return, Dead }
    [SerializeField] private RailBeetleVisual visual;
    [SerializeField, Tooltip("Root solid collider enclosing the moving legs; not an expanded damage radius.")]
    private BoxCollider clearanceBox;
    [SerializeField, Min(1f)] private float noticeRange = 14f;
    [SerializeField, Min(1f), Tooltip("Patrol boundary only; a detected player is not lost at this distance.")]
    private float leashRange = 18f;
    [SerializeField, Min(.1f)] private float walkSpeed = 2.5f, chargeSpeed = 14f, chargeDistance = 16f;
    [SerializeField, Min(.1f)] private float chaseSpeed = 5f;
    [SerializeField, Min(5f), Tooltip("Give up only after failing to reach a fixed last-known position, or waiting at water.")]
    private float unreachableSeconds = 15f;
    [SerializeField, Min(.1f)] private float windupSeconds = .9f, aimLockSeconds = .25f;
    [SerializeField, Min(.1f)] private float recoverSeconds = 2.2f, wallStunSeconds = 2.8f, cooldownSeconds = .8f;
    [SerializeField, Min(.5f)] private float chargeMinRange = 3f, chargeMaxRange = 8f;
    [SerializeField, Min(1)] private int attackDamage = 14;
    [SerializeField] private LayerMask groundMask = ~0;

    private Health health, playerHealth;
    private Rigidbody body;
    private CapsuleCollider bodyCollider;
    private PlayerVitals player;
    private PlayerMovement playerMovement;
    private CharacterController playerBody;
    private BaselineFlow flow;
    private EnemyGroundQuery groundQuery;
    private StoneScoutLootDrop loot;
    private CombatFeedback feedback;
    private EnemyAwareness awareness;
    private Collider[] rootColliders;
    private WaterVolume[] waters;
    private Vector3 home, patrolTarget, chargeHeading, lastKnownTarget, pursuitGoal, detourHeading;
    private Quaternion homeRotation;
    private BehaviourState state;
    private float timer, cooldown, nextSearch, chargeTravel, currentSpeed, blockedTime, lostSightTime;
    private float blockedRetryTimer, closePressureTime, activeChargeDistance;
    private float missedChargeDistance;
    private float pursuitFailureTime, pursuitBestRange, detourTimer;
    private bool chargeHitAttempted, aimLocked, patrolTravelling, engaged;
    private Vector3 workingPosition, escapeDirection, routeHeading;
    private Quaternion workingRotation;
    private bool poseTickActive, poseTurned, poseMoved, escaping, detouring, navigationTurning;
    private float escapeRemaining, escapeBlockedTime, detourTravel;
    private int escapeOption;
    private int side = 1;
    private readonly RaycastHit[] castHits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];
    private readonly EnemyNavigationRoute navigation = new EnemyNavigationRoute();
    private readonly Vector3[] warningPath = new Vector3[65];
    private int warningPathCount;
    private Vector3 warningHeading;
    private float nextWarningSample;
    private const float Skin = .025f;
    public BehaviourState CurrentState => state;
    public bool IsCharging => state == BehaviourState.Charge;
    public float PlannedChargeDistance => activeChargeDistance;
    public float WarningHalfWidth => (HasClearanceBox ? clearanceBox.size.x*.5f : bodyCollider.radius) * Mathf.Abs(transform.lossyScale.x);
    private Vector3 PosePosition => poseTickActive ? workingPosition : body.position;
    private Quaternion PoseRotation => poseTickActive ? workingRotation : body.rotation;

    private void Awake()
    {
        health = GetComponent<Health>(); body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>(); loot = GetComponent<StoneScoutLootDrop>();
        if (!clearanceBox) clearanceBox = GetComponent<BoxCollider>();
        rootColliders = GetComponents<Collider>();
        feedback = GetComponent<CombatFeedback>();
        awareness = EnemyAwareness.Ensure(this);
        if (!visual) visual = GetComponentInChildren<RailBeetleVisual>(true);
        body.isKinematic = true; body.useGravity = false;
        home = body.position; homeRotation = body.rotation;
        activeChargeDistance = chargeDistance;
        groundQuery = new EnemyGroundQuery(transform);
        waters = FindObjectsByType<WaterVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        timer = 1.1f; side = (GetInstanceID() & 1) == 0 ? 1 : -1;
        EnemyImpactVfx.Ensure(gameObject);
    }

    private void OnEnable()
    {
        if (!health) return;
        health.Damaged += OnDamaged; health.Died += OnDied;
        foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        if (flow) flow.WorldEncountersReset += ResetForJourney;
    }

    private void OnDisable()
    {
        if (health) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        if (flow) flow.WorldEncountersReset -= ResetForJourney;
        ClearNavigationPose();
        StopVisual();
    }

    // Menus can pause FixedUpdate entirely; Update cancels telegraphs, but preserves sword hit-stop.
    private void Update()
    {
        if (state != BehaviourState.Dead && player && !player.EnemyCombatActive)
        {
            if(awareness.CanRetainEncounter){awareness.Suspend();return;}
            SuspendCombat();
        }
    }

    private void FixedUpdate()
    {
        if (!health || health.IsDead || state == BehaviourState.Dead) return;
        float dt = Time.fixedDeltaTime;
        if (!player || !playerHealth || !playerBody || player.gameObject.scene != gameObject.scene)
        {
            if (player && player.gameObject.scene != gameObject.scene)
            {
                SuspendCombat(); player = null; playerHealth = null; playerBody = null; playerMovement = null;
            }
            nextSearch -= dt;
            if (nextSearch <= 0f)
            {
                nextSearch = 1f;
                foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
                playerHealth = player ? player.GetComponent<Health>() : null;
                playerBody = player ? player.GetComponent<CharacterController>() : null;
                playerMovement = player ? player.GetComponent<PlayerMovement>() : null;
                if (player && playerMovement && !player.GetComponent<RailBeetlePlayerImpact>())
                    player.gameObject.AddComponent<RailBeetlePlayerImpact>();
            }
        }
        if (!player || !playerHealth || !playerBody || playerHealth.IsDead || !player.EnemyCombatActive)
        {
            if(awareness.CanRetainEncounter){awareness.Suspend();return;}
            SuspendCombat(); return;
        }
        if (Time.timeScale <= 0f) return;
        BeginPoseTick();
        try
        {
            cooldown = Mathf.Max(0f, cooldown - dt);
            if (visual) visual.SetMotion(0f);
            Vector3 target = playerBody.transform.position;
            // Detection range is for initial acquisition, never a leash on an active pursuit.
            awareness.Tick(player, FlatDistance(body.position,target) <= noticeRange * 1.45f && HasSight(), noticeRange, dt);
            bool visible = awareness.Visible;
            lostSightTime = visible ? 0f : lostSightTime + dt;
            if (visible && engaged) lastKnownTarget = target;

            switch (state)
            {
                case BehaviourState.Patrol:
                    if (awareness.Ready && visible && FlatDistance(body.position, target) <= noticeRange)
                    {
                        AcquireTarget(target);
                        state = BehaviourState.Seek; blockedTime = lostSightTime = 0f;
                        closePressureTime = blockedRetryTimer = 0f; patrolTravelling = false;
                    }
                    else if (awareness.Curious) TryFace(awareness.InterestPosition - PosePosition,dt);
                    else Patrol(dt);
                    break;
                case BehaviourState.Seek:
                    if (!awareness.Engaged) { AbandonUnreachableTarget(); break; }
                    Seek(engaged && !visible ? lastKnownTarget : target, visible, dt); break;
                case BehaviourState.Windup: TickWindup(dt); break;
                case BehaviourState.Charge: TickCharge(dt); break;
                case BehaviourState.Recover:
                case BehaviourState.WallStun:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        if (visual) visual.SetStunned(false);
                        cooldown = cooldownSeconds; state = engaged ? BehaviourState.Seek : BehaviourState.Return;
                    }
                    break;
                case BehaviourState.Return:
                    awareness.Clear();
                    if (FlatDistance(body.position, home) < .15f)
                    {
                        TryFace(homeRotation * Vector3.forward, dt); state = BehaviourState.Patrol;
                        patrolTravelling = false; timer = 1.1f; blockedTime = 0f;
                    }
                    else if (!WalkHome(dt))
                    {
                        blockedTime = navigationTurning ? 0f : blockedTime + dt;
                        if (blockedTime > 2f) { state = BehaviourState.Patrol; timer = 1.5f; patrolTravelling = false; }
                    }
                    else blockedTime = 0f;
                    break;
            }
        }
        finally { CommitPoseTick(); }
    }

    private void Seek(Vector3 target, bool visible, float dt)
    {
        if (engaged && pursuitFailureTime >= unreachableSeconds) { AbandonUnreachableTarget(); return; }
        float range = FlatDistance(body.position, target);
        blockedRetryTimer = Mathf.Max(0f, blockedRetryTimer - dt);
        closePressureTime = visible && range < chargeMinRange ? closePressureTime + dt : 0f;
        bool shortCharge = closePressureTime >= 1.2f;
        bool targetOnLand = !playerMovement || !playerMovement.SwimmingActive;
        bool hasRoute = navigation.Direction(gameObject.scene, EnemyNavigationWorld.Profile.RailBeetle,
            PosePosition, target, dt, out Vector3 routeDirection);
        if (escaping)
        {
            bool advanced = TickEscape(dt);
            RegisterPursuitProgress(target, visible, advanced, dt); return;
        }
        if (navigation.Available && !hasRoute)
        {
            StopVisual(); RegisterPursuitProgress(target, visible, false, dt); return;
        }
        if (navigation.Available) { detouring = false; detourTravel = 0f; }
        Vector3 targetHeading = ChargeHeading(PosePosition, target, PoseRotation * Vector3.forward);
        if (detouring && visible && targetOnLand && range <= chargeMaxRange &&
            Vector3.Dot(PoseRotation * Vector3.forward, targetHeading) > .97f && BodyClear(PosePosition, PoseRotation))
        { detouring = false; detourTravel = 0f; }
        // NavMesh polygons can add corners even on a physically clear, gently curved slope.
        // Use the real full-body terrain/sweep test for a straight attack lane, not corner count.
        bool safeAttackRoute = !navigation.Available || (navigation.Complete && SafeChargeApproach(target));
        if (safeAttackRoute && !detouring && visible && targetOnLand && cooldown <= 0f && (range >= chargeMinRange || shortCharge) && range <= chargeMaxRange)
        {
            Vector3 heading = ChargeHeading(PosePosition, target, PoseRotation * Vector3.forward);
            bool turned = TryFace(heading, dt);
            if (turned && Vector3.Dot(PoseRotation * Vector3.forward, heading) > .97f)
            {
                state = BehaviourState.Windup; timer = windupSeconds; aimLocked = false;
                warningPathCount = 0; nextWarningSample = 0f;
                chargeHeading = heading; currentSpeed = chargeTravel = 0f; chargeHitAttempted = false;
                blockedTime = 0f;
                detourTimer = 0f; detouring = false;
                activeChargeDistance = shortCharge ? Mathf.Min(chargeDistance, Mathf.Max(1.2f, range + .65f)) : chargeDistance;
                missedChargeDistance = 0f;
                closePressureTime = blockedRetryTimer = 0f;
                if (visual) visual.SetAttack(0f, false);
            }
            else
            {
                blockedTime += dt;
                if (!turned && blockedTime >= .2f)
                {
                    BeginEscape(heading);
                }
                RegisterPursuitProgress(target, visible, false, dt);
            }
            return;
        }
        Vector3 direction = ChargeHeading(PosePosition, target, PoseRotation * Vector3.forward);
        // Recovery is a readable pause, not a perpetual circle around the player.
        if (safeAttackRoute && !detouring && visible && range < chargeMaxRange && cooldown > 0f)
        { RegisterPursuitProgress(target, visible, false, dt); return; }
        // A close-range run-up is an explicit short reverse step, never a 180-degree sideways slide.
        if (safeAttackRoute && !detouring && visible && range < chargeMinRange + .3f && !shortCharge)
        {
            Vector3 before = PosePosition;
            TryMove(-(PoseRotation * Vector3.forward), walkSpeed * dt, false, out _);
            bool retreated = FlatDistance(before, PosePosition) > .001f;
            if (visual) visual.SetMotion(retreated ? walkSpeed : 0f);
            if (!retreated) { blockedTime += dt; if (blockedTime >= .2f) BeginEscape(direction); }
            else blockedTime = 0f;
            RegisterPursuitProgress(target, visible, retreated, dt); return;
        }
        if (!visible && range < 1.5f)
        { RegisterPursuitProgress(target, visible, false, dt); return; }
        bool moved = Walk(navigation.Available ? routeDirection : direction, dt);
        blockedTime = moved || navigationTurning ? 0f : blockedTime + dt;
        if (moved) blockedRetryTimer = 0f;
        else if (blockedTime >= .2f && !escaping) BeginEscape(direction);
        RegisterPursuitProgress(target, visible, moved, dt);
    }

    private void AcquireTarget(Vector3 target)
    {
        engaged = true; lastKnownTarget = pursuitGoal = target;
        pursuitBestRange = FlatDistance(body.position, target);
        pursuitFailureTime = detourTimer = 0f;
        escaping = detouring = false;
    }

    private void RegisterPursuitProgress(Vector3 target, bool visible, bool moved, float dt)
    {
        if (!engaged) return;
        float range = FlatDistance(lastMovePosition, target);
        bool swimming = playerMovement && playerMovement.SwimmingActive;
        bool newGoal = FlatDistance(pursuitGoal, target) > .75f;
        bool closer = moved && range < pursuitBestRange - .15f;
        if (!swimming && (newGoal || closer))
        {
            pursuitGoal = target; pursuitBestRange = range; pursuitFailureTime = 0f;
        }
        else
        {
            if (newGoal) { pursuitGoal = target; pursuitBestRange = range; }
            pursuitFailureTime += dt;
        }
        if (pursuitFailureTime < unreachableSeconds) return;
        AbandonUnreachableTarget();
    }

    private void AbandonUnreachableTarget()
    {
        awareness?.Clear();
        engaged = false; state = BehaviourState.Return;
        blockedTime = closePressureTime = blockedRetryTimer = detourTimer = 0f;
        escaping = detouring = false;
        StopVisual();
    }

    private void TickWindup(float dt)
    {
        if (!playerBody) { state = BehaviourState.Return; StopVisual(); return; }
        if (!aimLocked && timer > Mathf.Min(aimLockSeconds, windupSeconds))
        {
            // Occlusion retains the last seen point; it does not grant aim through a wall.
            TryFace((engaged ? lastKnownTarget : playerBody.transform.position) - PosePosition, dt);
            chargeHeading = ChargeHeading(Vector3.zero, PoseRotation * Vector3.forward, Vector3.forward);
        }
        else if (!aimLocked)
        {
            aimLocked = true;
            chargeHeading = ChargeHeading(Vector3.zero, PoseRotation * Vector3.forward, Vector3.forward);
            missedChargeDistance = Mathf.Min(activeChargeDistance,
                FlatDistance(PosePosition, engaged ? lastKnownTarget : playerBody.transform.position) + 1.8f);
        }
        timer -= dt;
        float progress = 1f - Mathf.Clamp01(timer / Mathf.Max(.1f, windupSeconds));
        if (visual)
        {
            visual.SetAttack(progress, false, windupSeconds);
            nextWarningSample -= dt;
            if (warningPathCount < 2 || nextWarningSample <= 0f || Vector3.Dot(warningHeading, chargeHeading) < .995f)
            { BuildWarningPath(); nextWarningSample = .15f; }
            visual.SetWarningPath(warningPath, warningPathCount);
        }
        if (timer <= 0f)
        {
            aimLocked = true; chargeTravel = currentSpeed = 0f; chargeHitAttempted = false;
            state = BehaviourState.Charge;
            if (visual) { visual.SetAttack(1f, true); visual.SetWarning(Vector3.zero, Vector3.zero, false); }
        }
    }

    private void TickCharge(float dt)
    {
        if (!health || health.IsDead || state == BehaviourState.Dead || !bodyCollider.enabled) return;
        // A side dodge shortens the run-through, not the heading. Straight retreat retains full reach.
        // No bonus damage or homing: every hit still requires the same actual swept collider contact.
        if (missedChargeDistance > 0f && playerBody && HasSight())
        {
            Vector3 offset = playerBody.transform.position - PosePosition; offset.y = 0f;
            float lateral = Vector3.Cross(chargeHeading, offset).magnitude;
            float halfWidth = (HasClearanceBox ? clearanceBox.size.x * .5f : bodyCollider.radius) * Mathf.Abs(transform.lossyScale.x);
            float playerRadius = playerBody.radius * Mathf.Max(Mathf.Abs(playerBody.transform.lossyScale.x), Mathf.Abs(playerBody.transform.lossyScale.z));
            if (lateral > halfWidth + playerRadius + .25f)
                activeChargeDistance = Mathf.Min(activeChargeDistance, missedChargeDistance);
        }
        currentSpeed = Mathf.MoveTowards(currentSpeed, chargeSpeed, chargeSpeed / .18f * dt);
        float step = LimitedChargeStep(currentSpeed, dt, activeChargeDistance - chargeTravel);
        Vector3 before = PosePosition;
        bool moved = step > 0f && TryMove(chargeHeading, step, true, out _);
        // A zero step is normal range exhaustion, not a wall stun.
        bool wallHit = false;
        if (!moved && step > 0f) wallHit = lastMoveHitWall;
        chargeTravel += FlatDistance(before, lastMovePosition);
        if (visual)
        {
            visual.SetMotion(currentSpeed);
            visual.SetChargeTrail(lastMovePosition - chargeHeading * Mathf.Min(2.4f, chargeTravel),
                lastMovePosition, true);
        }
        if (!moved || chargeHitAttempted || chargeTravel >= activeChargeDistance - .001f)
        {
            // Repeated point-blank wall charges are not progress toward an unreachable ledge.
            // Keep their failure budget across windup/recovery, without aborting a single readable attack.
            if (engaged && wallHit && chargeTravel < .35f)
                pursuitFailureTime += windupSeconds + wallStunSeconds;
            else if (chargeTravel > .35f || chargeHitAttempted)
                pursuitFailureTime = 0f;
            state = wallHit ? BehaviourState.WallStun : BehaviourState.Recover;
            timer = wallHit ? wallStunSeconds : recoverSeconds; currentSpeed = 0f;
            side = -side;
            if (visual)
            {
                visual.SetAttack(0f, false); visual.SetMotion(0f);
                visual.SetChargeTrail(Vector3.zero, Vector3.zero, false);
                visual.SetStunned(true, wallHit); // Wall stun looks stronger than ordinary recovery.
                if (wallHit) visual.PlayImpact(lastImpactPoint, true);
            }
        }
    }

    private Vector3 lastMovePosition, lastImpactPoint;
    private bool lastMoveHitWall;

    private void BuildWarningPath()
    {
        warningPathCount = 1; warningHeading = chargeHeading;
        Vector3 previous = PosePosition;
        warningPath[0] = previous + Vector3.up * .035f;
        // Same supported compound body and solid sweeps as movement, sampled at a bounded 7 Hz.
        // This is a telegraph only: real movement still checks the complete sweep every physics tick.
        for (float travelled = .5f; travelled <= activeChargeDistance + .001f && warningPathCount < warningPath.Length; travelled += .5f)
        {
            Vector3 candidate = PosePosition + chargeHeading * travelled; candidate.y = previous.y;
            if (!GroundForFacing(candidate, PoseRotation, out float height)) break;
            candidate.y = height;
            if (!TranslationClear(previous, candidate, PoseRotation) || !BodyClear(candidate, PoseRotation)) break;
            warningPath[warningPathCount++] = candidate + Vector3.up * .035f;
            previous = candidate;
        }
    }
    private bool Walk(Vector3 direction, float dt)
    {
        navigationTurning = false;
        direction = ChargeHeading(Vector3.zero, direction, PoseRotation * Vector3.forward);
        float speed = state == BehaviourState.Seek ? chaseSpeed : walkSpeed;
        if (escaping) return TickEscape(dt);
        // A detour ends when the route is clear, not because an arbitrary timer expired.
        if (detouring && detourTravel >= .65f && DirectStepClear(direction, .7f))
        { detouring = false; detourTravel = 0f; }
        Vector3 desired = detouring ? detourHeading : direction;
        if (!TryFace(desired, dt))
        {
            if (state != BehaviourState.Patrol) BeginEscape(direction);
            if (visual) visual.SetMotion(0f);
            return false;
        }
        Vector3 forward = PoseRotation * Vector3.forward;
        float alignment = Mathf.Max(0f, Vector3.Dot(forward, desired));
        if (alignment < .7f)
        {
            navigationTurning = true;
            if (visual) visual.SetMotion(0f);
            return false;
        }
        Vector3 before = PosePosition;
        TryMove(forward, speed * alignment * alignment * dt, false, out _);
        bool moved = FlatDistance(before, PosePosition) > .001f;
        if (moved)
        {
            if (detouring) detourTravel += FlatDistance(before, PosePosition);
        }
        else if (state != BehaviourState.Patrol) BeginEscape(direction);
        if (visual) visual.SetMotion(moved ? speed * alignment * alignment : 0f);
        return moved;
    }

    private bool WalkHome(float dt)
    {
        bool routed = navigation.Direction(gameObject.scene, EnemyNavigationWorld.Profile.RailBeetle,
            PosePosition, home, dt, out Vector3 direction);
        if (navigation.Available && !routed) { StopVisual(); return false; }
        return Walk(navigation.Available ? direction : home - PosePosition, dt);
    }

    private bool SafeChargeApproach(Vector3 target)
    {
        Vector3 heading = ChargeHeading(PosePosition, target, PoseRotation * Vector3.forward);
        Quaternion facing = Quaternion.LookRotation(heading);
        Vector3 cursor = PosePosition;
        float length = Mathf.Max(0f, FlatDistance(cursor, target) - 1.5f);
        for (float travelled = 0f; travelled < length;)
        {
            float step = Mathf.Min(.35f, length - travelled);
            Vector3 next = cursor + heading * step;
            if (!GroundForFacing(next, facing, out float y)) return false;
            next.y = y;
            if (!DirectRouteClear(cursor, next, facing)) return false;
            cursor = next; travelled += step;
        }
        return true;
    }

    private void BeginEscape(Vector3 desiredRoute)
    {
        if (escaping) return;
        if (navigation.Available) navigation.Blocked(PosePosition, desiredRoute);
        escaping = true; detouring = false; escapeOption = 0;
        escapeDirection = -(PoseRotation * Vector3.forward);
        routeHeading = ChargeHeading(Vector3.zero, desiredRoute, PoseRotation * Vector3.forward);
        escapeRemaining = .5f; escapeBlockedTime = 0f; blockedTime = 0f;
    }

    private bool TickEscape(float dt)
    {
        Vector3 before = PosePosition;
        // Explicit reverse/side steps keep the current orientation and remain fully swept.
        TryMove(escapeDirection, Mathf.Min(walkSpeed * dt, escapeRemaining), false, out _);
        float advanced = FlatDistance(before, PosePosition);
        if (advanced > .001f)
        {
            escapeRemaining -= advanced; escapeBlockedTime = 0f;
            if (escapeRemaining <= .015f)
            {
                escaping = false; detouring = true; detourTravel = 0f;
                detourHeading = Quaternion.Euler(0f, side * 70f, 0f) * routeHeading;
            }
        }
        else
        {
            escapeBlockedTime += dt;
            if (escapeBlockedTime >= .25f)
            {
                escapeBlockedTime = 0f; escapeOption++;
                Vector3 right = PoseRotation * Vector3.right;
                escapeDirection = escapeOption % 4 == 1 ? right * side :
                    escapeOption % 4 == 2 ? -right * side :
                    escapeOption % 4 == 3 ? PoseRotation * Vector3.forward : -(PoseRotation * Vector3.forward);
                if (escapeOption % 4 == 0) side = -side;
            }
        }
        if (visual) visual.SetMotion(advanced > .001f ? advanced / Mathf.Max(.001f, dt) : 0f);
        return advanced > .001f;
    }

    private bool DirectStepClear(Vector3 direction, float length)
    {
        Quaternion facing = Quaternion.LookRotation(direction);
        if (!GroundForFacing(PosePosition, facing, out float y)) return false;
        Vector3 start = PosePosition; start.y = y;
        if (!BodyClear(start, facing)) return false;
        Vector3 end = start + direction * length;
        if (!GroundForFacing(end, facing, out y)) return false;
        end.y = y;
        return DirectRouteClear(start, end, facing);
    }

    private void Patrol(float dt)
    {
        timer -= dt;
        if (timer > 0f)
        {
            TryFace(homeRotation*Quaternion.Euler(0f,Mathf.Sin(Time.time*.65f+side)*28f,0f)*Vector3.forward,dt);
            return;
        }
        if (!patrolTravelling)
        { patrolTarget = home + homeRotation * (Vector3.right * side * 1.25f); side = -side; patrolTravelling = true; }
        if (FlatDistance(body.position, patrolTarget) < .15f ||
            (!Walk(patrolTarget - body.position, dt) && !navigationTurning))
        { patrolTravelling = false; timer = 1.3f; }
    }

    // Substeps validate ground; each substep sweeps the entire body, never just its final point.
    private bool TryMove(Vector3 direction, float length, bool attacking, out bool wallHit)
    {
        wallHit = lastMoveHitWall = false; lastMovePosition = PosePosition;
        if (length <= 0f || direction.sqrMagnitude < .0001f) return false;
        direction = ChargeHeading(Vector3.zero, direction, PoseRotation * Vector3.forward);
        Vector3 cursor = PosePosition;
        Quaternion facing = PoseRotation;
        int steps = Mathf.CeilToInt(length / .16f);
        float stride = length / steps;
        for (int n = 0; n < steps; n++)
        {
            Vector3 candidate = cursor + direction * stride;
            if ((state == BehaviourState.Patrol && FlatDistance(candidate, home) > leashRange + .1f) ||
                !TryGround(candidate, out float y)) break;
            candidate.y = y;
            CapsuleAt(cursor, facing, out Vector3 p1, out Vector3 p2, out float radius);
            // CapsuleCast omits initial overlaps. Never tunnel from an already touching collider.
            Collider initialPlayer = null; bool initialBlocked = false, hardWall = false;
            bool overflow = false;
            for (int shapeIndex = 0; shapeIndex < (HasClearanceBox ? 2 : 1); shapeIndex++)
            {
                Collider shape = shapeIndex == 0 ? (Collider)bodyCollider : clearanceBox;
                int contacts = ShapeOverlaps(shape, cursor, facing);
                if (contacts == overlaps.Length) { overflow = true; break; }
                for (int i = 0; i < contacts; i++)
                {
                    Collider obstacle = overlaps[i];
                    if (IgnoredForBody(obstacle, cursor, facing, shape)) continue;
                    if (IsPlayer(obstacle))
                    {
                        if (Vector3.Dot(direction, obstacle.bounds.center - cursor) >= 0f) initialPlayer = obstacle;
                        continue; // Moving away from touching the player is safe and necessary.
                    }
                    initialBlocked = true; hardWall |= !obstacle.GetComponentInParent<Health>();
                    lastImpactPoint = obstacle.ClosestPoint(cursor);
                }
            }
            if (overflow) break;
            if (initialBlocked) { wallHit = lastMoveHitWall = hardWall; break; }
            if (initialPlayer)
            {
                if (attacking) TryContactDamage(initialPlayer, initialPlayer.ClosestPoint((p1 + p2) * .5f), direction);
                break;
            }
            Vector3 travel = candidate - cursor;
            float nearest = float.PositiveInfinity; Collider blocker = null; RaycastHit nearestHit = default;
            float nearestNonPlayer = float.PositiveInfinity;
            RaycastHit primaryPlayerHit = default; bool primaryPlayerContact = false;
            for (int shapeIndex = 0; shapeIndex < (HasClearanceBox ? 2 : 1); shapeIndex++)
            {
                Collider shape = shapeIndex == 0 ? (Collider)bodyCollider : clearanceBox;
                int hits;
                if (shapeIndex == 0)
                    hits = Physics.CapsuleCastNonAlloc(p1, p2, radius, travel.normalized, castHits,
                        travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
                else
                {
                    BoxAt(cursor, facing, out Vector3 centre, out Vector3 half);
                    hits = Physics.BoxCastNonAlloc(centre, half, travel.normalized, castHits, facing,
                        travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
                }
                if (hits == castHits.Length) { overflow = true; break; }
                for (int i = 0; i < hits; i++)
                {
                    RaycastHit hit = castHits[i];
                    if (!hit.collider || hit.collider.transform.IsChildOf(transform) ||
                        (IsSupportSurface(hit.collider) && hit.normal.y >= .848f)) continue;
                    // PhysX box sweeps can report a zero-distance hit for an existing overlap.
                    // Ignore only a verified player contact whose penetration this move reduces.
                    if (SeparatingPlayerContact(shape, cursor, candidate, facing, hit.collider)) continue;
                    if (!IsPlayer(hit.collider)) nearestNonPlayer = Mathf.Min(nearestNonPlayer, hit.distance);
                    if (shapeIndex == 0 && IsPlayer(hit.collider) && hit.distance <= travel.magnitude + .001f &&
                        (!primaryPlayerContact || hit.distance < primaryPlayerHit.distance))
                    { primaryPlayerHit = hit; primaryPlayerContact = true; }
                    if (hit.distance < nearest) { nearest = hit.distance; blocker = hit.collider; nearestHit = hit; }
                }
            }
            if (overflow) break;
            if (blocker)
            {
                cursor += travel.normalized * Mathf.Clamp(nearest - Skin, 0f, travel.magnitude);
                lastImpactPoint = nearestHit.point;
                if (IsPlayer(blocker))
                {
                    // Skin-only proximity is not a hit: wait until the swept body actually reaches it.
                    if (attacking && nearest <= travel.magnitude + .001f)
                        TryContactDamage(blocker, primaryPlayerContact && primaryPlayerHit.distance <= nearestNonPlayer + .001f
                            ? primaryPlayerHit.point : nearestHit.point, direction);
                }
                else wallHit = lastMoveHitWall = !blocker.GetComponentInParent<Health>();
                break;
            }
            if (!BodyClear(candidate, facing, true, cursor)) break;
            cursor = candidate;
        }
        lastMovePosition = cursor;
        bool advanced = (cursor - PosePosition).sqrMagnitude > .000001f;
        if (advanced) MoveBody(cursor);
        return advanced && !wallHit && !chargeHitAttempted;
    }

    private void MoveBody(Vector3 destination)
    {
        if (poseTickActive) { workingPosition = destination; poseMoved = true; return; }
#if UNITY_EDITOR
        if (!Application.isPlaying) { body.position = destination; return; }
#endif
        body.MovePosition(destination);
    }

    private void CapsuleAt(Vector3 position, Quaternion rotation, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Vector3 scale = transform.lossyScale;
        Vector3 absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        int axis = bodyCollider.direction;
        float along = absolute[axis], across = axis == 0 ? Mathf.Max(absolute.y, absolute.z) :
            axis == 1 ? Mathf.Max(absolute.x, absolute.z) : Mathf.Max(absolute.x, absolute.y);
        radius = bodyCollider.radius * across;
        Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        Vector3 half = rotation * localAxis * Mathf.Max(0f, bodyCollider.height * along * .5f - radius);
        Vector3 centre = position + rotation * Vector3.Scale(bodyCollider.center, scale);
        p1 = centre - half; p2 = centre + half;
    }

    private bool BodyClear(Vector3 position, Quaternion rotation, bool allowSeparating = false, Vector3 previous = default)
    {
        return ShapeClear(bodyCollider, position, rotation, allowSeparating, previous) &&
            (!HasClearanceBox || ShapeClear(clearanceBox, position, rotation, allowSeparating, previous));
    }

    private bool HasClearanceBox => clearanceBox && clearanceBox.enabled && clearanceBox.transform == transform;
    private void BoxAt(Vector3 position, Quaternion rotation, out Vector3 centre, out Vector3 half)
    {
        Vector3 scale = transform.lossyScale;
        centre = position + rotation * Vector3.Scale(clearanceBox.center, scale);
        half = Vector3.Scale(clearanceBox.size * .5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    private int ShapeOverlaps(Collider shape, Vector3 position, Quaternion rotation)
    {
        if (shape == clearanceBox)
        {
            BoxAt(position, rotation, out Vector3 centre, out Vector3 half);
            return Physics.OverlapBoxNonAlloc(centre, half, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        }
        CapsuleAt(position, rotation, out Vector3 p1, out Vector3 p2, out float radius);
        return Physics.OverlapCapsuleNonAlloc(p1, p2, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
    }

    private bool ShapeClear(Collider shape, Vector3 position, Quaternion rotation, bool allowSeparating, Vector3 previous)
    {
        int count = ShapeOverlaps(shape, position, rotation);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = overlaps[i];
            if (IgnoredForBody(obstacle, position, rotation, shape)) continue;
            // A contact at the start must not trap a backing-away beetle until it teleports clear.
            if (allowSeparating && SeparatingPlayerContact(shape, previous, position, rotation, obstacle)) continue;
            return false;
        }
        return true;
    }

    private bool SeparatingPlayerContact(Collider shape, Vector3 previous, Vector3 candidate,
        Quaternion rotation, Collider obstacle)
    {
        if (!IsPlayer(obstacle)) return false; // Walls and other enemies always remain solid.
        Vector3 travel = candidate - previous, toward = obstacle.bounds.center - previous;
        travel.y = toward.y = 0f;
        if (travel.sqrMagnitude < .000001f || Vector3.Dot(travel, toward) > 0f ||
            (toward - travel).sqrMagnitude <= toward.sqrMagnitude + .000001f) return false;
        if (!Physics.ComputePenetration(shape, previous, rotation, obstacle,
                obstacle.transform.position, obstacle.transform.rotation, out _, out float oldDepth) || oldDepth <= 0f)
            return false; // A newly encountered player must never be skipped by a sweep.
        return !Physics.ComputePenetration(shape, candidate, rotation, obstacle,
                   obstacle.transform.position, obstacle.transform.rotation, out _, out float newDepth) ||
               newDepth < oldDepth - .0001f;
    }

    private bool IgnoredForBody(Collider obstacle, Vector3 position, Quaternion rotation, Collider shape)
    {
        if (!obstacle || obstacle.transform.IsChildOf(transform)) return true;
        // A terrain mesh also contains vertical banks. Only shallow upward support contact
        // is harmless; a cliff side blocks the body even when this mesh is navigable elsewhere.
        return IsSupportSurface(obstacle) &&
            Physics.ComputePenetration(shape, position, rotation, obstacle,
                obstacle.transform.position, obstacle.transform.rotation, out Vector3 push, out float depth) &&
            push.y >= .848f && depth <= .04f;
    }
    private bool IsSupportSurface(Collider obstacle) => groundQuery.IsSurface(obstacle);
    private bool IsPlayer(Collider obstacle) => playerHealth && obstacle.GetComponentInParent<Health>() == playerHealth;

    private bool TryGround(Vector3 candidate, out float rootY)
        => GroundForFacing(candidate, PoseRotation, out rootY);

    private bool GroundForFacing(Vector3 candidate, Quaternion facing, out float rootY)
    {
        rootY = 0f; float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
        float size = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        Vector3 forward = facing * Vector3.forward *
            ((HasClearanceBox ? Mathf.Max(.56f, clearanceBox.size.z * .5f - .03f) : .56f) * size);
        Vector3 right = facing * Vector3.right *
            ((HasClearanceBox ? Mathf.Max(.29f, clearanceBox.size.x * .5f - .03f) : .29f) * size);
        Vector3 centrePoint = default, centreNormal = Vector3.up;
        bool continuousPlane = true;
        for (int i = 0; i < (HasClearanceBox ? 9 : 5); i++)
        {
            Vector3 offset = i == 1 ? forward : i == 2 ? -forward : i == 3 ? right : i == 4 ? -right :
                i == 5 ? forward + right : i == 6 ? forward - right : i == 7 ? -forward + right :
                i == 8 ? -forward - right : Vector3.zero;
            Vector3 probe = candidate + offset;
            if (!groundQuery.TrySample(probe, .9f, Mathf.Max(.1f, 3f * size - .9f),
                31.5f, out RaycastHit floor, groundMask) || IsSubmerged(floor.point)) return false;
            if (i == 0) { centrePoint = floor.point; centreNormal = floor.normal; }
            else
            {
                float planeY = centrePoint.y - Vector3.Dot(centreNormal, offset) / centreNormal.y;
                if (Mathf.Abs(floor.point.y - planeY) > .12f * size) continuousPlane = false;
            }
            lowest = Mathf.Min(lowest, floor.point.y); highest = Mathf.Max(highest, floor.point.y);
        }
        rootY = highest + .015f;
        float planeSpan = 2f * (Mathf.Abs(Vector3.Dot(centreNormal, forward)) +
            Mathf.Abs(Vector3.Dot(centreNormal, right))) / centreNormal.y;
        // Preserve the authored curved-terrain envelope. Only extend its height span when every
        // probe actually fits a continuous shallow plane (e.g. a 22-degree diagonal slope).
        bool supportedSpan = highest - lowest <= .72f * size ||
            (continuousPlane && highest - lowest <= planeSpan + .24f * size);
        return supportedSpan && Mathf.Abs(rootY - candidate.y) <= .42f * size;
    }

    private bool IsSubmerged(Vector3 point)
    {
        if (waters == null) return false;
        foreach (var water in waters)
        {
            if (!water || water.gameObject.scene != gameObject.scene || !water.IsValid || point.y >= water.WaterSurfaceY - .08f) continue;
            Vector3 probe = new Vector3(point.x, water.WaterSurfaceY - .05f, point.z);
            if (water.VolumeCollider.bounds.Contains(probe) &&
                (water.VolumeCollider.ClosestPoint(probe) - probe).sqrMagnitude < .0001f) return true;
        }
        return false;
    }

    private bool HasSight()
    {
        Vector3 start = bodyCollider.bounds.center + Vector3.up * .1f;
        Vector3 ray = playerBody.bounds.center - start;
        if (ray.magnitude < .001f) return true;
        int count = Physics.RaycastNonAlloc(start, ray.normalized, castHits, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == castHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (castHits[i].collider && !castHits[i].collider.transform.IsChildOf(transform) && !IsPlayer(castHits[i].collider)) return false;
        return true;
    }

    private bool TryFace(Vector3 direction, float dt)
    {
        if (poseTickActive && poseTurned) return false;
        direction = ChargeHeading(Vector3.zero, direction, PoseRotation * Vector3.forward);
        Quaternion initialRotation = PoseRotation;
        Quaternion facing = Quaternion.RotateTowards(initialRotation, Quaternion.LookRotation(direction), 180f * dt);
        Vector3 initialPosition = PosePosition, cursor = initialPosition;
        float angle = Quaternion.Angle(initialRotation, facing);
        int steps = Mathf.Max(1, Mathf.CeilToInt(angle / 1f));
        for (int i = 1; i <= steps; i++)
        {
            Quaternion sample = Quaternion.Slerp(initialRotation, facing, (float)i / steps);
            if (!GroundForFacing(cursor, sample, out float y)) return false;
            Vector3 supported = cursor; supported.y = y;
            if (!TranslationClear(cursor, supported, sample) || !TurnPoseClear(supported, sample, angle / steps))
                return false;
            cursor = supported;
        }
        if (poseTickActive)
        {
            workingPosition = cursor; workingRotation = facing;
            poseTurned = true; poseMoved |= (cursor - initialPosition).sqrMagnitude > .000001f;
            return true;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying) { body.position = cursor; body.rotation = facing; return true; }
#endif
        body.MovePosition(cursor); body.MoveRotation(facing); return true;
    }

    private bool TurnPoseClear(Vector3 position, Quaternion rotation, float angularStep)
    {
        if (!BodyClear(position, rotation)) return false;
        if (!HasClearanceBox) return true;
        BoxAt(position, rotation, out Vector3 centre, out Vector3 half);
        // Cover the tiny swept arc between angular samples without shrinking the real collider.
        float padding = Mathf.Sqrt(half.x * half.x + half.z * half.z) *
            Mathf.Sin(Mathf.Deg2Rad * angularStep) + .005f;
        Vector3 expanded = half + new Vector3(padding, 0f, padding);
        int count = Physics.OverlapBoxNonAlloc(centre, expanded, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = overlaps[i];
            if (!obstacle || obstacle.transform.IsChildOf(transform)) continue;
            if (IgnoredForBody(obstacle, position, rotation, clearanceBox)) continue;
            return false;
        }
        return true;
    }

    private bool TranslationClear(Vector3 start, Vector3 end, Quaternion facing)
    {
        Vector3 travel = end - start;
        if (travel.sqrMagnitude < .0000001f) return true;
        CapsuleAt(start, facing, out Vector3 p1, out Vector3 p2, out float radius);
        for (int shapeIndex = 0; shapeIndex < (HasClearanceBox ? 2 : 1); shapeIndex++)
        {
            int count;
            if (shapeIndex == 0)
                count = Physics.CapsuleCastNonAlloc(p1, p2, radius, travel.normalized, castHits,
                    travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
            else
            {
                BoxAt(start, facing, out Vector3 centre, out Vector3 half);
                count = Physics.BoxCastNonAlloc(centre, half, travel.normalized, castHits, facing,
                    travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
            }
            if (count == castHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) ||
                    (IsSupportSurface(hit.collider) && hit.normal.y >= .848f)) continue;
                return false;
            }
        }
        return true;
    }

    private bool DirectRouteClear(Vector3 start, Vector3 end, Quaternion facing)
    {
        Vector3 travel = end - start;
        if (travel.sqrMagnitude < .0000001f) return BodyClear(end, facing);
        CapsuleAt(start, facing, out Vector3 p1, out Vector3 p2, out float radius);
        float firstPlayer = float.PositiveInfinity, firstBlocker = float.PositiveInfinity;
        for (int shapeIndex = 0; shapeIndex < (HasClearanceBox ? 2 : 1); shapeIndex++)
        {
            int count;
            if (shapeIndex == 0)
                count = Physics.CapsuleCastNonAlloc(p1, p2, radius, travel.normalized, castHits,
                    travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
            else
            {
                BoxAt(start, facing, out Vector3 centre, out Vector3 half);
                count = Physics.BoxCastNonAlloc(centre, half, travel.normalized, castHits, facing,
                    travel.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore);
            }
            if (count == castHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) ||
                    (IsSupportSurface(hit.collider) && hit.normal.y >= .848f)) continue;
                if (IsPlayer(hit.collider)) firstPlayer = Mathf.Min(firstPlayer, hit.distance);
                else firstBlocker = Mathf.Min(firstBlocker, hit.distance);
            }
        }
        // Contact with the target ends a route; it is not an obstruction requiring endless detours.
        // This is a planning query only. TryMove still stops/damages at real swept physical contact.
        if (firstPlayer < firstBlocker - .001f) return true;
        return float.IsPositiveInfinity(firstBlocker) && BodyClear(end, facing);
    }

    private void BeginPoseTick()
    {
        workingPosition = body.position; workingRotation = body.rotation;
        poseTickActive = true; poseTurned = poseMoved = navigationTurning = false;
        lastMovePosition = workingPosition;
    }

    private void CommitPoseTick()
    {
        if (!poseTickActive) return;
        poseTickActive = false;
        if (poseMoved) body.MovePosition(workingPosition);
        if (poseTurned) body.MoveRotation(workingRotation);
    }

    private void ClearNavigationPose()
    {
        navigation.Clear();
        poseTickActive = poseTurned = poseMoved = escaping = detouring = navigationTurning = false;
        escapeRemaining = escapeBlockedTime = detourTravel = detourTimer = 0f;
        missedChargeDistance = 0f;
        warningPathCount = 0; nextWarningSample = 0f;
    }

    private void TryContactDamage(Collider target, Vector3 point, Vector3 direction)
    {
        if (chargeHitAttempted || !playerHealth || playerHealth.IsDead) return;
        chargeHitAttempted = true; // Block, dodge and invulnerability also consume this charge's one attempt.
        if (playerHealth.TryApplyDamage(new DamageInfo(attackDamage, gameObject, point, direction)) && visual)
            visual.PlayImpact(point, false);
    }

    private void OnDamaged(Health target, DamageInfo damage)
    {
        if (target.IsDead) return;
        if (visual) visual.PlayHit(damage.HitPoint);
        // Ordinary hits are readable but do not stun-lock or magically cancel a committed charge.
        if (state == BehaviourState.Patrol || state == BehaviourState.Return)
        {
            Vector3 attacker = playerBody ? playerBody.transform.position :
                damage.Source ? damage.Source.transform.position : body.position;
            AcquireTarget(attacker); state = BehaviourState.Seek;
        }
    }

    private void OnDied(Health target, DamageInfo damage)
    {
        ClearNavigationPose();
        state = BehaviourState.Dead; currentSpeed = 0f; StopVisual();
        engaged = false; pursuitFailureTime = detourTimer = 0f;
        closePressureTime = blockedRetryTimer = 0f;
        body.MovePosition(body.position); // Replace any charge destination queued in this physics tick.
        foreach (var rootCollider in rootColliders) if (rootCollider) rootCollider.enabled = false;
        if (visual) visual.PlayDeath();
    }

    private void SuspendCombat()
    {
        awareness?.Clear();
        ClearNavigationPose();
        if (state != BehaviourState.Patrol && state != BehaviourState.Dead)
            state = BehaviourState.Return;
        engaged = false; pursuitFailureTime = detourTimer = 0f;
        cooldown = cooldownSeconds;
        closePressureTime = blockedRetryTimer = blockedTime = 0f;
        StopVisual();
    }
    private void StopVisual()
    {
        if (!visual) return;
        visual.SetMotion(0f); visual.SetAttack(0f, false); visual.SetWarning(Vector3.zero, Vector3.zero, false);
        visual.SetStunned(false);
        visual.SetChargeTrail(Vector3.zero, Vector3.zero, false);
    }

    public void ResetForJourney()
    {
        awareness?.Clear();
        if (!body || !health) return;
        ClearNavigationPose();
        if (loot) loot.ClearDrop();
        if (feedback) feedback.ClearFeedback();
        health.RestoreFullHealth();
        foreach (var rootCollider in rootColliders) if (rootCollider) rootCollider.enabled = true;
        body.position = home; body.rotation = homeRotation;
        state = BehaviourState.Patrol; timer = 1.1f;
        cooldown = nextSearch = currentSpeed = chargeTravel = blockedTime = lostSightTime = 0f;
        closePressureTime = blockedRetryTimer = 0f; activeChargeDistance = chargeDistance;
        engaged = false; pursuitFailureTime = detourTimer = 0f;
        lastKnownTarget = pursuitGoal = home; pursuitBestRange = 0f;
        chargeHitAttempted = aimLocked = patrolTravelling = false; lastMovePosition = home;
        if (visual) visual.ResetVisual();
    }

    public static Vector3 ChargeHeading(Vector3 from, Vector3 target, Vector3 fallback)
    {
        Vector3 heading = target - from; heading.y = 0f;
        if (heading.sqrMagnitude < .000001f) { heading = fallback; heading.y = 0f; }
        return heading.sqrMagnitude < .000001f ? Vector3.forward : heading.normalized;
    }
    public static float LimitedChargeStep(float speed, float dt, float remaining) =>
        Mathf.Min(Mathf.Max(0f, speed) * Mathf.Max(0f, dt), Mathf.Max(0f, remaining));
    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
}
