using UnityEngine;

// Authoritative movement and attack. Presentation never moves the actor root.
[DisallowMultipleComponent, RequireComponent(typeof(Health), typeof(BoxCollider), typeof(Rigidbody)), DefaultExecutionOrder(110)]
public sealed class GelBlobAI : MonoBehaviour
{
    public enum State { Idle, Approach, Windup, Flight, Recover, Stagger, Return, Dead }
    public const float DefaultBodyScale = 1.2f, DefaultLandingRadius = 1.2f;
    [SerializeField] GelBlobVisual visual;
    [SerializeField, Range(.8f, 1.5f)] float bodyScale = DefaultBodyScale;
    [SerializeField] float detectionRange = 14f, leapRange = 7f, moveSpeed = 2.6f;
    [SerializeField] float windupSeconds = 1f, flightSeconds = .38f, recoverySeconds = 1.35f;
    [SerializeField] float landingRadius = DefaultLandingRadius;
    [SerializeField, Min(.1f)] float landingFollowSpeed = 6f;
    [SerializeField, Min(.01f)] float landingFollowTime = .12f;
    [SerializeField, Min(.05f)] float landingLockSeconds = .6f;
    [SerializeField] int landingDamage = 10;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly Collider[] overlaps = new Collider[32];
    readonly EnemyNavigationRoute route = new EnemyNavigationRoute();
    Health health, playerHealth;
    BoxCollider body;
    PlayerVitals player;
    PlayerMovement playerMovement;
    CharacterController playerBody;
    BaselineFlow flow;
    OverworldV2Layout layout;
    EnemyGroundQuery ground;
    EnemySoundEmitter sound;
    EnemyAwareness awareness;
    float nextPatrol;
    int patrolPoint;
    Vector3 home, launch, landing, pendingPoint, deathFeet, hopStart, hopEnd;
    float age, cooldown, nextSearch, blockedTime, lostTime, nextLandingCheck, lastLandingUpdate;
    bool attackFlight, pendingHit, pendingContact, attackConnected, contactDrop, previouslyActive, emergencyDrop, hopping, lockCueSent;
    float hopAge, hopWait, contactWidth = float.PositiveInfinity;
    Vector3 previousPlayerPosition, playerVelocity, pendingContactPoint;
    const float HopSeconds = .38f, HopHeight = .14f, LeapHeight = 1.15f;
    public State Phase { get; private set; }
    public float Progress => Mathf.Clamp01(age / (Phase == State.Windup ? windupSeconds :
        Phase == State.Flight ? flightSeconds : Phase == State.Recover ? recoverySeconds : 1f));
    public Vector3 LandingPoint => landing;
    public float BodyScale => Mathf.Clamp(bodyScale, .8f, 1.5f);
    public float LandingRadius => landingRadius;
    public bool WarningVisible => Phase == State.Windup || (Phase == State.Flight && attackFlight);
    public bool LandingLocked => Phase != State.Windup || age >= windupSeconds - landingLockSeconds;
    // Appearance follows the actual counterattack window, not an invented weak spot.
    public float RecoveryReadiness => Phase == State.Recover ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.82f, 1f, Progress)) : 1f;
    public int LockCount { get; private set; }
    public int LandingCount { get; private set; }
    public int DamageCount { get; private set; }
    public int ContactCount { get; private set; }
    public bool SimulationRunning => player && player.EnemyCombatActive && playerHealth && !playerHealth.IsDead;
    public bool Hopping => hopping;
    public float HopProgress => Mathf.Clamp01(hopAge / HopSeconds);
    public Vector3 AttackDirection => (landing - launch).sqrMagnitude > .001f ? (landing - launch).normalized : transform.forward;
    public Vector3 Shape
    {
        get
        {
            Vector3 shape = UnconstrainedShape;
            // A body contact can end the leap before the planned landing. Do not
            // widen into the player while settling; retain volume until there is room.
            if (!float.IsPositiveInfinity(contactWidth))
                shape = VolumeScale(Mathf.Max(shape.y, 1f / (contactWidth * contactWidth)));
            return shape;
        }
    }
    Vector3 UnconstrainedShape
    {
        get
        {
            if (Phase == State.Windup) return VolumeScale(Mathf.Lerp(1f, .56f, Mathf.SmoothStep(0f, 1f, Progress / .78f)));
            if (Phase == State.Flight) return FlightShape(Progress);
            if (Phase == State.Recover)
            {
                // Splat and rebound, then HOLD a visibly tired shape until the
                // last part of the real recovery. Don't look idle while unable to act.
                float t = age;
                float y = t < .075f ? .56f : t < .23f ? Mathf.Lerp(.56f, 1.15f, Mathf.SmoothStep(0, 1, (t - .075f) / .155f)) :
                    t < .45f ? Mathf.Lerp(1.15f, .86f, Mathf.SmoothStep(0, 1, (t - .23f) / .22f)) :
                    .86f + .015f * Mathf.Sin((t - .45f) * 20f) * Mathf.Exp(-(t - .45f) * 8f);
                return VolumeScale(Mathf.Lerp(y, 1f, RecoveryReadiness));
            }
            if ((Phase == State.Idle || Phase == State.Approach || Phase == State.Return) && hopping)
                return VolumeScale(.88f + .23f * Mathf.Sin(HopProgress * Mathf.PI));
            if (Phase == State.Stagger) return VolumeScale(1f - .15f * Mathf.Exp(-age * 13f));
            if (Phase == State.Idle) return VolumeScale(1f + Mathf.Sin(age * 3f) * .025f);
            return Vector3.one;
        }
    }
    static Vector3 VolumeScale(float height)
    { float width = 1f / Mathf.Sqrt(height); return new Vector3(width, height, width); }
    static Vector3 FlightShape(float t)
    {
        float y = t < .16f ? Mathf.Lerp(.56f, 1.35f, Mathf.SmoothStep(0, 1, t / .16f)) :
            t < .5f ? Mathf.Lerp(1.35f, 1.04f, Mathf.SmoothStep(0, 1, (t - .16f) / .34f)) :
            Mathf.Lerp(1.04f, 1.26f, Mathf.SmoothStep(0, 1, (t - .5f) / .5f));
        return VolumeScale(y);
    }
    Vector2 Footprint => Vector2.one * (.8f * BodyScale);
    Vector3 Half(Vector3 shape) => new Vector3(.59f * shape.x, .47f * shape.y, .59f * shape.z) * BodyScale;
    Vector3 TravelHalf(Vector3 shape) => new Vector3(.8f, .47f * shape.y, .8f) * BodyScale;
    void Awake()
    {
        health = GetComponent<Health>(); body = GetComponent<BoxCollider>();
        ground = new EnemyGroundQuery(transform); home = transform.position;
        foreach (var candidate in FindObjectsByType<OverworldV2Layout>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { layout = candidate; break; }
        if (!visual) visual = GetComponent<GelBlobVisual>();
        GetComponent<CombatFeedback>()?.UseLocalizedEnemyVisuals();
        sound = EnemySoundEmitter.Ensure(gameObject);
        awareness = EnemyAwareness.Ensure(this);
        EnemyAttackPresentation.Ensure(gameObject);
        FindPlayer(); previousPlayerPosition = player ? player.transform.position : Vector3.zero; UpdateBody();
    }
    void OnEnable()
    {
        health.Damaged += Damaged; health.Died += Died;
        flow = FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += ResetForJourney;
    }
    void OnDisable()
    {
        if (health) { health.Damaged -= Damaged; health.Died -= Died; }
        if (flow) flow.WorldEncountersReset -= ResetForJourney;
        pendingHit = pendingContact = attackFlight = false; sound?.StopAll(); visual?.ClearTransient();
    }
    void FindPlayer()
    {
        nextSearch = Time.unscaledTime + 1f;
        foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        playerHealth = player ? player.GetComponent<Health>() : null;
        playerMovement = player ? player.GetComponent<PlayerMovement>() : null;
        playerBody = player ? player.GetComponent<CharacterController>() : null;
    }
    void Update()
    {
        if (!player && Time.unscaledTime >= nextSearch) FindPlayer();
        bool active = player && player.EnemyCombatActive && playerHealth && !playerHealth.IsDead;
        if (!active)
        {
            if(awareness.CanRetainEncounter)
            {
                awareness.Suspend();
                previousPlayerPosition=player.transform.position;
                return; // Freeze windup/flight/recovery, not a free attack cancellation.
            }
            pendingHit = pendingContact = attackFlight = false;
            playerVelocity = Vector3.zero;
            if (player) previousPlayerPosition = player.transform.position;
            if (previouslyActive && Phase == State.Windup) Enter(State.Recover);
            previouslyActive = false; sound?.StopAll(); visual?.HideWarning(); return;
        }
        previouslyActive = true;
        if (Time.deltaTime <= 0f) return; // Hit-stop freezes rather than cancelling a real attack.
        Vector3 measured = (player.transform.position - previousPlayerPosition) / Mathf.Max(.001f, Time.deltaTime);
        measured.y = 0f;
        // Relocations/new-game are not target velocity; prediction is always bounded.
        playerVelocity = measured.sqrMagnitude > 100f ? Vector3.zero : Vector3.Lerp(playerVelocity, measured, 1f - Mathf.Exp(-20f * Time.deltaTime));
        previousPlayerPosition = player.transform.position;
        if (!float.IsPositiveInfinity(contactWidth) && OccupancyClear(transform.position, Half(VolumeScale(.56f))))
            contactWidth = float.PositiveInfinity;
        if (health.IsDead)
        {
            Vector3 nextFeet = Vector3.MoveTowards(transform.position, deathFeet, Time.deltaTime * 4f);
            if (SweepClear(transform.position, nextFeet, new Vector3(.3f, .2f, .3f) * BodyScale)) transform.position = nextFeet;
            return;
        }
        age += Time.deltaTime; cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
        hopWait = Mathf.Max(0f, hopWait - Time.deltaTime);
        UpdateBody();
        Vector3 toPlayer = player.transform.position - transform.position; toPlayer.y = 0;
        float distance = toPlayer.magnitude;
        awareness.Tick(player, distance <= detectionRange * 1.45f && SeesPlayer(), detectionRange, Time.deltaTime);
        switch (Phase)
        {
            case State.Idle:
                if (hopping) { StepHop(); break; }
                if (awareness.Ready) Enter(State.Approach);
                else if (awareness.Curious) Face(awareness.InterestPosition - transform.position);
                else if (Time.time >= nextPatrol)
                {
                    // A local, terrain-checked hop; never launch a combat leap while idle.
                    nextPatrol=Time.time+Random.Range(1.6f,2.8f);
                    float a=(patrolPoint++%4)*Mathf.PI*.5f;
                    Walk(home+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.1f);
                }
                break;
            case State.Approach:
                if (hopping) { StepHop(); break; }
                if (!awareness.Engaged) { route.Clear(); Enter(State.Return); break; }
                lostTime = distance > detectionRange * 1.8f ? lostTime + Time.deltaTime : 0f;
                if (lostTime > 4f || Vector3.Distance(transform.position, home) > 30f) { route.Clear(); Enter(State.Return); break; }
                Face((awareness.Visible ? player.transform.position : awareness.InterestPosition) - transform.position);
                if (awareness.Visible && cooldown <= 0f && distance <= leapRange && SeesPlayer() && ChooseLanding(out landing))
                { attackConnected = contactDrop = false; Enter(State.Windup); sound?.Cue(EnemySoundCue.GelCharge, seconds: windupSeconds); visual?.SetLanding(landing); }
                else if (distance > 1.1f) Walk(awareness.Visible ? player.transform.position : awareness.InterestPosition);
                break;
            case State.Windup:
                if (LandingLocked && !lockCueSent)
                {
                    lockCueSent = true; LockCount++;
                    visual?.LockLanding();
                    // Reuse the bounded shared warning voice; replace the charge
                    // with one lock cue, never pile up an extra looping sound.
                    sound?.Cue(EnemySoundCue.CoreLock, .7f, seconds: .18f);
                }
                if (awareness.Visible && !LandingLocked && age >= nextLandingCheck)
                {
                    nextLandingCheck = age + .04f;
                    float elapsed = age - lastLandingUpdate; lastLandingUpdate = age;
                    Vector3 wanted = PredictedPlayerPosition; wanted.y = landing.y;
                    Vector3 eased = Vector3.Lerp(landing, wanted, 1f - Mathf.Exp(-elapsed / landingFollowTime));
                    Vector3 follow = Vector3.MoveTowards(landing, eased, landingFollowSpeed * elapsed);
                    // The marked point IS the attack destination. Slow the actual
                    // aim, not just its art, and keep each intermediate point safe.
                    if (TryLandingPoint(follow, out Vector3 updated, false)) { landing = updated; visual?.SetLanding(landing); }
                }
                if (!LandingLocked) Face(landing - transform.position);
                if (age >= windupSeconds)
                {
                    launch = transform.position;
                    if (SafeLanding(landing, true) && ArcClear(launch, landing))
                    { attackFlight = true; emergencyDrop = false; Enter(State.Flight); sound?.StopWarning(); sound?.Cue(EnemySoundCue.GelJump); }
                    else { attackFlight = false; cooldown = .5f; Enter(State.Recover); sound?.StopWarning(); }
                }
                break;
            case State.Flight:
                Vector3 next = emergencyDrop ? Vector3.MoveTowards(transform.position, landing, Time.deltaTime * 3f) : Arc(launch, landing, Progress);
                Vector3 stop = next, contact = default;
                bool touched = attackFlight && PlayerContact(transform.position, next, out stop, out contact);
                // Check obstacles only as far as the first real player contact.
                // The contact pose is held narrow until safe expansion is possible.
                Vector3 destination = touched ? stop : next;
                Vector3 clearance = touched || contactDrop ? Half(Shape) : TravelHalf(Shape);
                if (SweepClear(transform.position, destination, clearance, true))
                {
                    if (touched)
                    {
                        transform.position = stop;
                        pendingContact = true; pendingContactPoint = contact;
                        pendingPoint = stop; ContactCount++;
                        contactWidth = Shape.x;
                        attackFlight = false; emergencyDrop = contactDrop = true;
                        landing = stop;
                        if (ground.TryFootprint(stop, transform.rotation, Vector2.one * (.59f * contactWidth * BodyScale),
                            35f, 1f, 3f, out float contactY, true)) landing.y = contactY + .025f;
                        // Damage is physical contact, not an invisible relocated splash disk.
                        visual?.HideWarning(); sound?.Cue(EnemySoundCue.GelSplat, .8f);
                    }
                    else transform.position = next;
                }
                else
                {
                    // Dynamic blocker: stop horizontal travel, descend safely without attacking.
                    attackFlight = false; visual?.HideWarning();
                    if (!emergencyDrop && ground.TrySample(transform.position, .1f, 3f, 35f, out RaycastHit below))
                    { emergencyDrop = true; landing = new Vector3(transform.position.x, below.point.y + .025f, transform.position.z); }
                }
                if (Phase == State.Flight && (emergencyDrop ? Vector3.Distance(transform.position, landing) < .01f : Progress >= 1f))
                {
                    LandingCount++; pendingHit = attackFlight && !attackConnected;
                    if (pendingHit) pendingPoint = landing;
                    attackFlight = false;
                    Enter(State.Recover); cooldown = recoverySeconds + .22f;
                    if (!contactDrop) sound?.Cue(EnemySoundCue.GelSplat, .8f);
                    visual?.Splash();
                }
                break;
            case State.Recover:
                if (age >= recoverySeconds) Enter(State.Approach);
                break;
            case State.Stagger:
                if (age >= .22f) { cooldown = Mathf.Max(cooldown, .3f); Enter(State.Approach); }
                break;
            case State.Return:
                awareness.Clear();
                if (hopping) { StepHop(); break; }
                if (Vector3.Distance(transform.position, home) < .3f) Enter(State.Idle);
                else Walk(home);
                break;
        }
    }
    void LateUpdate()
    {
        if (pendingContact)
        {
            pendingContact = false;
            // Resolve after player movement. A player who moved clear in this frame
            // is not damaged by a stale cast, and a contacted leap cannot hit twice.
            if (CanHitPlayer() && playerBody.bounds.SqrDistance(body.ClosestPoint(playerBody.bounds.center)) < .0144f)
                ApplyPlayerHit(pendingContactPoint);
        }
        if (!pendingHit) return;
        pendingHit = false;
        // Evaluate AFTER player movement; the jump grace flag is not used as grounding.
        if (!CanHitPlayer() || !playerBody.isGrounded ||
            (playerMovement && (playerMovement.SwimmingActive || playerMovement.ClimbingActive || playerMovement.MantlingActive))) return;
        Vector3 feet = playerBody.bounds.center - Vector3.up * playerBody.bounds.extents.y;
        if (!ground.TrySample(feet, .6f, .7f, playerBody.slopeLimit, out RaycastHit floor) ||
            !ground.TrySample(pendingPoint, 1f, 1f, 35f, out RaycastHit impactFloor) ||
            feet.y - floor.point.y > playerBody.skinWidth + .22f) return;
        // Compare the local slope plane, not two absolute Y values. A continuous
        // hillside is valid; a separate balcony/floor above the splash is not.
        if (Mathf.Abs(Vector3.Dot(impactFloor.normal, floor.point - impactFloor.point)) > .18f) return;
        Vector3 flat = feet - pendingPoint; flat.y = 0f;
        if (flat.magnitude > landingRadius + PlayerRadius || !ClearDamageLine(impactFloor.point + Vector3.up * .3f, floor.point + Vector3.up * .3f)) return;
        ApplyPlayerHit(feet);
    }
    float PlayerRadius => playerBody.radius * Mathf.Max(Mathf.Abs(playerBody.transform.lossyScale.x), Mathf.Abs(playerBody.transform.lossyScale.z));
    // Actual takeoff evades this ground-oriented enemy; never use grounding grace.
    bool CanHitPlayer() => !attackConnected && !health.IsDead && player && player.EnemyCombatActive &&
        playerHealth && !playerHealth.IsDead && playerBody && playerBody.enabled && playerBody.isGrounded &&
        !(playerMovement && (playerMovement.SwimmingActive || playerMovement.ClimbingActive || playerMovement.MantlingActive));
    void ApplyPlayerHit(Vector3 point)
    {
        Vector3 away = playerBody.bounds.center - transform.position; away.y = 0f;
        if (away.sqrMagnitude < .001f) away = transform.forward;
        away.Normalize();
        if (!playerHealth.TryApplyDamage(new DamageInfo(landingDamage, gameObject, point, away))) return;
        attackConnected = true; DamageCount++;
        playerMovement?.ApplyImpact(away * 3.8f, .22f);
        Camera main = Camera.main;
        // A small soft-bodied enemy needs a brief tap, not a large camera jolt.
        // Damage, HUD flash and collision-constrained recoil remain unchanged.
        if (main) main.GetComponent<ThirdPersonCamera>()?.RequestShake(.02f, .085f);
    }
    bool PlayerContact(Vector3 from, Vector3 to, out Vector3 stop, out Vector3 point)
    {
        stop = from; point = default;
        if (!playerBody || attackConnected) return false;
        Vector3 delta = to - from; Vector3 half = Half(Shape);
        int count = Physics.OverlapBoxNonAlloc(from + Vector3.up * (half.y + .03f), half, overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (overlaps[i] == playerBody) { point = body.ClosestPoint(playerBody.bounds.center); return true; }
        if (delta.sqrMagnitude < .000001f) return false;
        int n = Physics.BoxCastNonAlloc(from + Vector3.up * (half.y + .03f), half, delta.normalized,
            hits, transform.rotation, delta.magnitude + .015f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
            if (hits[i].collider == playerBody && hits[i].distance < nearest)
            { nearest = hits[i].distance; point = hits[i].point; }
        if (float.IsPositiveInfinity(nearest)) return false;
        stop = from + delta.normalized * Mathf.Max(0f, nearest - .025f);
        return true;
    }
    void Enter(State state)
    { Phase = state; age = nextLandingCheck = lastLandingUpdate = 0f; lockCueSent = false; UpdateBody(); }
    void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= .001f) return;
        Quaternion rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
        Vector3 half = Half(Shape);
        if (OccupancyClear(transform.position, half, rotation) &&
            (hopping || ground.TryFootprint(transform.position, rotation, new Vector2(half.x, half.z), 35f, .7f, .8f, out _, true)))
            transform.rotation = rotation;
    }
    void UpdateBody()
    {
        Vector3 s = Shape; body.size = Half(s) * 2f;
        body.center = Vector3.up * (.03f + body.size.y * .5f);
    }
    bool SeesPlayer() => ClearDamageLine(transform.position + Vector3.up * .65f, playerBody ? playerBody.bounds.center : player.transform.position + Vector3.up);
    bool ClearDamageLine(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from; int n = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (n == hits.Length) return false;
        for (int i = 0; i < n; i++) if (!Owned(hits[i].collider) && !hits[i].collider.GetComponentInParent<Health>()) return false;
        return true;
    }
    Vector3 PredictedPlayerPosition => player.transform.position + Vector3.ClampMagnitude(playerVelocity * .22f, .65f);
    bool ChooseLanding(out Vector3 point, bool checkArc = true) => TryLandingPoint(PredictedPlayerPosition, out point, checkArc);
    bool TryLandingPoint(Vector3 wanted, out Vector3 point, bool checkArc)
    {
        point = default;
        // Aim at the target, not 1.12m short of it. A swept physical contact stops
        // the actor before penetration; a dodged target leaves a genuine landing disk.
        wanted.y = transform.position.y;
        if (!ground.TrySample(wanted, 2f, 2f, 35f, out RaycastHit floor)) return false;
        point = new Vector3(wanted.x, floor.point.y + .025f, wanted.z);
        if (!ground.TryFootprint(point, transform.rotation, Footprint, 35f, .7f, .8f, out float supportedY, true)) return false;
        point.y = supportedY + .025f;
        // Fast tracking updates validate support/occupancy; the full arc is checked
        // on attack selection and again at launch, not 25 times per second.
        return Vector3.Distance(point, transform.position) <= leapRange && SafeLanding(point, true) &&
            (!checkArc || ArcClear(transform.position, point));
    }
    // A local shoreline rule: this map's rendered lake is not fully represented by
    // physical water volumes. Other enemy types keep their existing navigation policy.
    bool AboveLake(Vector3 point) => !layout || point.y >= layout.LakePosition.y + .15f;
    public bool SafeLanding(Vector3 point) => SafeLanding(point, false);
    bool SafeLanding(Vector3 point, bool targetOccupied) => AboveLake(point) && ground.TryFootprint(point, transform.rotation, Footprint, 35f, .7f, .8f, out _, true) &&
        ground.TryFootprint(point, Quaternion.identity, new Vector2(landingRadius, landingRadius), 35f, 1f, 1f, out _, true) &&
        OccupancyClear(point, TravelHalf(Vector3.one), transform.rotation, targetOccupied);
    bool Owned(Collider c) => !c || c.transform.IsChildOf(transform) || c.gameObject.scene != gameObject.scene;
    bool OccupancyClear(Vector3 feet, Vector3 half)
        => OccupancyClear(feet, half, transform.rotation);
    bool OccupancyClear(Vector3 feet, Vector3 half, Quaternion rotation, bool ignorePlayer = false)
    {
        int n = Physics.OverlapBoxNonAlloc(feet + Vector3.up * (half.y + .03f), half, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (n == overlaps.Length) return false;
        for (int i = 0; i < n; i++) if (!Owned(overlaps[i]) && !(ignorePlayer && overlaps[i] == playerBody) && !ground.AllowsSupportContact(overlaps[i], feet)) return false;
        return true;
    }
    bool SweepClear(Vector3 from, Vector3 to, Vector3 half, bool ignorePlayer = false)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .000001f) return OccupancyClear(to, half, transform.rotation, ignorePlayer);
        int n = Physics.BoxCastNonAlloc(from + Vector3.up * (half.y + .03f), half, delta.normalized, hits, transform.rotation,
            delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (n == hits.Length) return false;
        for (int i = 0; i < n; i++)
            if (!Owned(hits[i].collider) && !(ignorePlayer && hits[i].collider == playerBody) && !(ground.IsSurface(hits[i].collider) && hits[i].normal.y >= .819f)) return false;
        return OccupancyClear(to, half, transform.rotation, ignorePlayer);
    }
    public bool ArcClear(Vector3 from, Vector3 to)
    {
        Vector3 previous = from;
        for (int i = 1; i <= 16; i++)
        {
            float t = i / 16f; Vector3 next = Arc(from, to, t);
            if (!ground.TrySample(next, .25f, LeapHeight + .9f, 35f, out RaycastHit support) || !AboveLake(support.point) ||
                !SweepClear(previous, next, TravelHalf(FlightShape(t)), true)) return false;
            previous = next;
        }
        // The widest contact pose must fit as well as the tall flight pose.
        if (!OccupancyClear(to, Half(VolumeScale(.56f)), transform.rotation, true)) return false;
        return true;
    }
    static Vector3 Arc(Vector3 from, Vector3 to, float t) => Vector3.Lerp(from, to, t) + Vector3.up * (4f * t * (1f - t) * LeapHeight);
    void Walk(Vector3 target)
    {
        if (hopWait > 0f) return;
        Vector3 navFeet = transform.position;
        if (ground.TrySample(navFeet, .6f, .8f, 35f, out RaycastHit support)) navFeet.y = support.point.y + .025f;
        if (!route.Direction(gameObject.scene, EnemyNavigationWorld.Profile.StoneScout, navFeet, target, Time.deltaTime, out Vector3 direction))
        {
            blockedTime += Time.deltaTime;
            if (Phase == State.Approach && (route.WaitingAtEnd || blockedTime > 2.5f)) { blockedTime = 0f; Enter(State.Return); }
            // Can't get home? Hold safely instead of moving through invalid terrain.
            else if (Phase == State.Return && blockedTime > 4f) { blockedTime = 0f; Enter(State.Idle); }
            return;
        }
        direction.y = 0f;
        if (direction.sqrMagnitude < .001f) return;
        Face(direction);
        Vector3 next = transform.position + direction.normalized * (moveSpeed * (HopSeconds + .12f));
        if (ground.TryFootprint(next, transform.rotation, Footprint, 35f, .5f, .6f, out float y, true))
        {
            next.y = y + .025f;
            // Tiny real ground hops, not a sliding actor with a bobbing decoration.
            if (AboveLake(next) && HopClear(transform.position, next))
            { hopStart = transform.position; hopEnd = next; hopAge = 0f; hopping = true; blockedTime = 0f; return; }
        }
        blockedTime += Time.deltaTime;
        if (blockedTime > .65f) { route.Blocked(transform.position, direction); blockedTime = 0f; }
        if (route.WaitingAtEnd) { cooldown = 1f; Enter(State.Return); }
    }
    bool HopClear(Vector3 from, Vector3 to)
    {
        Vector3 previous = from;
        for (int i = 1; i <= 6; i++)
        {
            float t = i / 6f;
            Vector3 next = Vector3.Lerp(from, to, t) + Vector3.up * (4f * t * (1f - t) * HopHeight);
            if (!ground.TryFootprint(next, transform.rotation, Footprint, 35f, .5f, .8f, out float supportedY, true) ||
                !AboveLake(new Vector3(next.x, supportedY, next.z)) ||
                !SweepClear(previous, next, TravelHalf(VolumeScale(.88f + .23f * Mathf.Sin(t * Mathf.PI))))) return false;
            previous = next;
        }
        return true;
    }
    void StepHop()
    {
        hopAge += Time.deltaTime;
        float t = HopProgress;
        Vector3 next = Vector3.Lerp(hopStart, hopEnd, t) + Vector3.up * (4f * t * (1f - t) * HopHeight);
        if (!SweepClear(transform.position, next, TravelHalf(Shape))) { DropHop(); return; }
        transform.position = next; UpdateBody();
        if (t >= 1f) { hopping = false; hopWait = .12f; }
    }
    void DropHop()
    {
        hopping = false; attackFlight = pendingHit = contactDrop = false; emergencyDrop = true;
        landing = transform.position;
        if (ground.TrySample(landing, .1f, 2f, 35f, out RaycastHit below)) landing.y = below.point.y + .025f;
        launch = transform.position; sound?.StopWarning(); Enter(State.Flight);
    }
    void Damaged(Health actor, DamageInfo hit)
    {
        visual?.Hit(hit.HitDirection, hit.HitPoint);
        if (actor.IsDead) return;
        if (hopping) { DropHop(); return; }
        if (Phase != State.Flight && Phase != State.Recover) { pendingHit = false; sound?.StopWarning(); Enter(State.Stagger); }
    }
    void Died(Health actor, DamageInfo hit)
    {
        hopping = pendingHit = pendingContact = attackFlight = false; deathFeet = transform.position;
        if (ground.TrySample(transform.position, .1f, 3f, 35f, out RaycastHit floor)) deathFeet.y = floor.point.y + .025f;
        Enter(State.Dead); body.enabled = false; route.Clear(); visual?.Die();
    }
    public void ResetForJourney()
    {
        awareness?.Clear(); nextPatrol=Time.time+1.2f;
        hopping = pendingHit = pendingContact = attackFlight = attackConnected = contactDrop = emergencyDrop = false;
        contactWidth = float.PositiveInfinity; playerVelocity = Vector3.zero;
        previousPlayerPosition = player ? player.transform.position : Vector3.zero;
        hopAge = hopWait = 0f; route.Clear(); cooldown = .3f; lostTime = blockedTime = 0f;
        sound?.ResetForJourney(); GetComponent<StoneScoutLootDrop>()?.ClearDrop();
        transform.position = home; body.enabled = true; health.RestoreFullHealth(); Enter(State.Idle); visual?.ResetVisual();
    }
}
