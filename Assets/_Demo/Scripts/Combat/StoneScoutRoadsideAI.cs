using System.Collections.Generic;
using UnityEngine;

// Small, self-contained overworld behaviour for the first roadside Stone Scout.
// The visual prefab still owns animation; this component owns movement and one timed hit.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class StoneScoutRoadsideAI : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float noticeRange = 7.5f;
    [SerializeField, Min(1f)] private float leashRange = 10.5f;
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.8f;
    [SerializeField, Min(0.5f)] private float attackRange = 1.05f;
    public float AttackRange => attackRange;
    [SerializeField, Min(0.05f)] private float fistHitRadius = 0.3f;
    [SerializeField, Min(1)] private int attackDamage = 8;
    [SerializeField, Min(0.1f)] private float windupSeconds = 0.38f;
    [SerializeField, Min(0.02f)] private float hitWindowSeconds = 0.16f;
    [SerializeField, Min(0.1f)] private float recoverSeconds = 0.8f;
    [SerializeField, Min(0.1f)] private float staggerSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float lostSightSeconds = 1.2f;
    [SerializeField, Min(30f)] private float turnDegreesPerSecond = 420f;
    [SerializeField, Min(0f)] private float patrolRadius = 0.9f;
    [SerializeField, Min(0.1f)] private float alertSeconds = 0.45f;
    [SerializeField, Min(1f)] private float squadLinkRange = 11f;

    // Only nearby, awake Scouts participate; distant placements keep their own fights.
    private static readonly HashSet<StoneScoutRoadsideAI> ActiveScouts = new HashSet<StoneScoutRoadsideAI>();

    private enum State { Idle, Alert, Chase, Windup, HitWindow, Recover, Stagger, Return, Dead }

    private Health health;
    private Rigidbody body;
    private CapsuleCollider bodyCollider;
    private StoneScoutVisualMotion visual;
    private DemonKingCombatVisual bladeVisual;
    private StoneScoutFeedback feel;
    private EnemyAwareness awareness;
    private PlayerVitals player;
    private Health playerHealth;
    private CharacterController playerBody;
    private EnemyGroundQuery groundQuery;
    private Vector3 previousFist;
    private bool hasFistSample;
    private Vector3 home;
    private Vector3 lastSeenPosition;
    private Quaternion homeFacing;
    private State state;
    private float timer;
    private float playerSearchTimer;
    private float lostSightTimer;
    private float blockedTimer;
    private float attackSettleTime;
    private float patrolWaitTimer;
    private Vector3 patrolTarget;
    private bool hasPatrolTarget;
    private int patrolSide = 1;
    private int patrolCorner;
    private float patrolLookClock;
    private int repositionSide = 1;
    private int detourSide = 1;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private readonly Collider[] nearbyColliders = new Collider[32];
    private readonly EnemyNavigationRoute navigation = new EnemyNavigationRoute();

    private void Awake()
    {
        health = GetComponent<Health>();
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>();
        visual = GetComponentInChildren<StoneScoutVisualMotion>(true);
        bladeVisual = visual as DemonKingCombatVisual;
        feel = GetComponent<StoneScoutFeedback>();
        awareness = EnemyAwareness.Ensure(this);
        body.isKinematic = true;
        body.useGravity = false;
        groundQuery = new EnemyGroundQuery(transform);
        home = transform.position;
        lastSeenPosition = home;
        homeFacing = transform.rotation;
        patrolWaitTimer = Random.Range(0.8f, 2.2f);
        patrolSide = (GetInstanceID() & 1) == 0 ? 1 : -1;
        repositionSide = patrolSide;
        EnemyImpactVfx.Ensure(gameObject);
    }

    private void OnEnable()
    {
        ActiveScouts.Add(this);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        navigation.Clear();
        ActiveScouts.Remove(this);
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        if (visual != null) visual.SetMoving(false);
        if (feel != null) feel.Clear();
        SuspendAttack();
    }

    private void Update()
    {
        // FixedUpdate does not run in menus. Air swings and sword hit-stop remain active combat.
        if (player && (player.gameObject.scene != gameObject.scene || !player.EnemyCombatActive ||
            !playerHealth || playerHealth.IsDead))
        {
            if (awareness.CanRetainEncounter) { hasFistSample=false; awareness.Suspend(); return; }
            SuspendAttack();
        }
    }

    private void SuspendAttack()
    {
        awareness?.Clear();
        hasFistSample = false;
        if (visual != null) { visual.SetMoving(false); visual.CancelAttack(); }
        if (feel != null) feel.EndWindup();
        navigation.Clear();
        if (state != State.Dead && !health.IsDead) state = State.Return;
    }

    private void FixedUpdate()
    {
        if (health.IsDead || state == State.Dead) return;

        if (player == null)
        {
            playerSearchTimer -= Time.fixedDeltaTime;
            if (playerSearchTimer <= 0f)
            {
                playerSearchTimer = 1f;
                foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
                playerHealth = player != null ? player.GetComponent<Health>() : null;
                playerBody = player != null ? player.GetComponent<CharacterController>() : null;
            }
        }

        // Main-world menus, cutscenes and side worlds suspend this prototype.
        if (player == null || playerHealth == null || playerBody == null ||
            player.gameObject.scene != gameObject.scene || !player.EnemyCombatActive || playerHealth.IsDead)
        {
            if (awareness.CanRetainEncounter) { hasFistSample=false; awareness.Suspend(); return; }
            if (visual != null) visual.SetMoving(false);
            if (feel != null) feel.EndWindup();
            if (state == State.Windup || state == State.HitWindow || state == State.Recover)
                state = State.Return;
            // Menus and side worlds pause enemy simulation. If the player is gone,
            // the Scout can still walk back to its original roadside position.
            if (player == null || playerHealth == null || playerHealth.IsDead)
            {
                state = State.Return;
                ReturnHome();
            }
            return;
        }

        Vector3 target = player.transform.position;
        float targetDistance = FlatDistance(body.position, target);
        float targetFromHome = FlatDistance(home, target);
        float fromHome = FlatDistance(home, body.position);
        bool canPursue = targetFromHome <= leashRange && fromHome <= leashRange;
        bool clearSight = targetDistance <= noticeRange * 1.45f && HasClearSight();
        awareness.Tick(player, clearSight && canPursue, noticeRange, Time.fixedDeltaTime);
        bool canSeePlayer = awareness.Visible;

        switch (state)
        {
            case State.Idle:
                if (awareness.Ready && canPursue && canSeePlayer && targetDistance <= noticeRange &&
                    Mathf.Abs(target.y - body.position.y) <= 2f)
                {
                    lostSightTimer = blockedTimer = 0f;
                    lastSeenPosition = target;
                    hasPatrolTarget = false;
                    timer = alertSeconds;
                    state = State.Alert;
                    if (visual != null) visual.SetMoving(false);
                }
                else if (awareness.Curious)
                {
                    // Freeze the walk, then turn towards the last observation, not hidden live coordinates.
                    hasPatrolTarget = false;
                    if (visual) visual.SetMoving(false);
                    Face(awareness.InterestPosition);
                }
                else Patrol();
                break;

            case State.Alert:
                if (!canPursue || !canSeePlayer || targetDistance > noticeRange + 1f)
                {
                    state = State.Return;
                    break;
                }
                Face(target);
                if (visual != null) visual.SetMoving(false);
                timer -= Time.fixedDeltaTime;
                if (timer <= 0f) state = State.Chase;
                break;

            case State.Chase:
                if (!awareness.Engaged || !canPursue || targetDistance > noticeRange + 2f) { state = State.Return; break; }
                if (canSeePlayer) lastSeenPosition = target;
                lostSightTimer = canSeePlayer ? 0f : lostSightTimer + Time.fixedDeltaTime;
                if (lostSightTimer >= (navigation.Available ? Mathf.Max(6f, lostSightSeconds) : lostSightSeconds))
                { state = State.Return; break; }
                if (canSeePlayer && AnotherScoutIsAttacking())
                {
                    WaitForOpening(target, targetDistance);
                    blockedTimer = 0f; // Waiting for a teammate is not failed pathfinding.
                    break;
                }
                // Reach the player capsule before committing to a punch. The old 1.05 m
                // threshold left a gap between the two colliders on some approaches.
                if (canSeePlayer &&
                    targetDistance <= (bladeVisual ? attackRange : Mathf.Min(attackRange, HorizontalContactDistance() + 0.12f)) &&
                    Mathf.Abs(target.y - body.position.y) <= 1.8f)
                {
                    Face(target);
                    if (!Facing(target, 0.65f))
                    {
                        attackSettleTime = 0f;
                        if (visual != null) visual.SetMoving(false);
                        break;
                    }
                    // Stop the feet and settle the last physical step before throwing a punch.
                    if (visual != null) visual.SetMoving(false);
                    attackSettleTime += Time.fixedDeltaTime;
                    if (attackSettleTime < .1f) break;
                    attackSettleTime = 0f;
                    if (visual != null) { visual.SetMoving(false); visual.PlayAttack(); }
                    hasFistSample = false;
                    if (feel != null) feel.BeginWindup(windupSeconds);
                    repositionSide = -repositionSide;
                    timer = windupSeconds;
                    state = State.Windup;
                }
                else if (!MoveToward(canSeePlayer ? target : lastSeenPosition, walkSpeed,
                    canSeePlayer ? (bladeVisual ? attackRange - .08f : HorizontalContactDistance() + 0.08f) : 0.1f))
                {
                    attackSettleTime = 0f;
                    blockedTimer += Time.fixedDeltaTime;
                    if (blockedTimer >= (navigation.Available ? 4f : 1.5f)) state = State.Return;
                }
                else { blockedTimer = 0f; attackSettleTime = 0f; }
                break;

            case State.Windup:
                // Turn during the readable wind-up, then lock direction so a dodge matters.
                if (timer > 0.12f) Face(canSeePlayer ? target : awareness.InterestPosition);
                timer -= Time.fixedDeltaTime;
                if (visual) visual.SetChargeProgress(1f - Mathf.Clamp01(timer / windupSeconds));
                if (timer > 0f) break;
                timer = hitWindowSeconds;
                if (feel) feel.BeginStrike();
                state = State.HitWindow;
                break;

            case State.HitWindow:
                if (visual) visual.SetChargeProgress(0f);
                timer -= Time.fixedDeltaTime;
                if (timer > 0f) break;
                if (feel != null) feel.EndWindup();
                timer = recoverSeconds;
                state = State.Recover;
                break;

            case State.Recover:
                // A miss exposes the same stationary, usable counterattack window as a hit.
                // Repositioning belongs to Chase, never to the unfinished punch pose.
                if (visual != null) visual.SetMoving(false);
                timer -= Time.fixedDeltaTime;
                if (timer <= 0f) state = canPursue ? State.Chase : State.Return;
                break;

            case State.Stagger:
                timer -= Time.fixedDeltaTime;
                if (timer <= 0f) state = canPursue ? State.Chase : State.Return;
                break;

            case State.Return:
                ReturnHome();
                break;
        }
    }

    // Animator updates before LateUpdate, so the fist check uses the visible punch pose.
    private void LateUpdate()
    {
        if (visual == null || player == null || !player.EnemyCombatActive ||
            player.gameObject.scene != gameObject.scene || playerHealth == null || playerHealth.IsDead ||
            playerBody == null || (state != State.Windup && state != State.HitWindow))
        { hasFistSample = false; return; }
        if (Time.deltaTime <= 0f) return; // Preserve the previous pose across sword hit-stop.
        if (state == State.HitWindow && TryStrike())
        {
            timer = recoverSeconds; // Block/dodge also consumes this punch's one contact.
            state = State.Recover;
            if (feel != null) feel.EndWindup();
        }
        previousFist = visual.RightFistWorldPosition;
        hasFistSample = true;
    }

    private bool MoveToward(Vector3 destination, float speed, float stoppingDistance = 0f)
    {
        Vector3 direction = destination - body.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance <= stoppingDistance + 0.0001f)
        {
            if (visual != null) visual.SetMoving(false);
            return true;
        }
        direction.Normalize();
        // Near a visible target, finish the collision-checked approach directly. A rejected
        // NavMesh goal inside the player's body must not keep steering around a reachable punch.
        bool closeApproach = state == State.Chase && distance < 1.6f && HasClearSight();
        bool hasRoute = navigation.Direction(gameObject.scene, EnemyNavigationWorld.Profile.StoneScout,
            body.position, destination, Time.fixedDeltaTime, out Vector3 routeDirection);
        if (navigation.Available && !closeApproach)
        {
            if (!hasRoute) { if (visual != null) visual.SetMoving(false); return navigation.WaitingAtEnd; }
            direction = routeDirection;
        }
        float step = Mathf.Min(speed * Time.fixedDeltaTime, distance - stoppingDistance);
        bool returning = state == State.Return;
        if (!TryStep(direction, step, returning, out Vector3 next))
        {
            if (navigation.Available && !closeApproach)
            {
                navigation.Blocked(body.position, direction);
                if (visual != null) visual.SetMoving(false);
                return false;
            }
            // Navigation brings us to the fight; short collision-checked tangents finish
            // the approach around another Scout instead of two bodies blocking each other.
            Vector3 left = Quaternion.Euler(0f, -75f, 0f) * direction;
            Vector3 right = Quaternion.Euler(0f, 75f, 0f) * direction;
            bool leftClear = TryStep(left, step, returning, out Vector3 leftNext);
            bool rightClear = TryStep(right, step, returning, out Vector3 rightNext);
            if (!leftClear && !rightClear)
            {
                if (visual != null) visual.SetMoving(false);
                return false;
            }
            if (leftClear && rightClear)
            {
                float leftDistance = FlatDistance(leftNext, destination);
                float rightDistance = FlatDistance(rightNext, destination);
                detourSide = Mathf.Abs(leftDistance - rightDistance) < 0.1f
                    ? detourSide : leftDistance < rightDistance ? -1 : 1;
            }
            else detourSide = leftClear ? -1 : 1;
            next = detourSide < 0 ? leftNext : rightNext;
        }

        Face(body.position + direction);
        body.MovePosition(next);
        if (visual != null) visual.SetMoving(true);
        return true;
    }

    private void Patrol()
    {
        if (patrolRadius <= 0f) { if (visual != null) visual.SetMoving(false); return; }
        if (patrolWaitTimer > 0f)
        {
            patrolWaitTimer -= Time.fixedDeltaTime;
            patrolLookClock += Time.fixedDeltaTime;
            Quaternion look = homeFacing * Quaternion.Euler(0f, Mathf.Sin(patrolLookClock*.8f)*42f, 0f);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation,look,55f*Time.fixedDeltaTime));
            if (visual != null) visual.SetMoving(false);
            return;
        }
        if (!hasPatrolTarget)
        {
            float a=patrolCorner++*Mathf.PI*.5f;
            patrolTarget = home + homeFacing * new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a)*.65f)*patrolRadius;
            hasPatrolTarget = true;
        }
        if (FlatDistance(body.position, patrolTarget) <= 0.12f ||
            !MoveToward(patrolTarget, walkSpeed * 0.55f, 0.1f))
        {
            hasPatrolTarget = false;
            patrolWaitTimer = Random.Range(1.1f, 2f);
            if (visual != null) visual.SetMoving(false);
        }
    }

    // Cutscenes pause the world clock. Move only this selected Scout using the
    // same ground/obstacle checks as its normal patrol, without running combat.
    public void TickCinematicPatrol(float delta)
    {
        if(!body || !health || health.IsDead || patrolRadius<=0) return;
        if(!hasPatrolTarget || FlatDistance(body.position,patrolTarget)<.12f)
        {
            patrolTarget=home+homeFacing*(Vector3.right*(patrolSide*patrolRadius));
            patrolSide=-patrolSide; hasPatrolTarget=true;
        }
        Vector3 direction=patrolTarget-body.position; direction.y=0;
        bool moving=false;
        if(direction.sqrMagnitude>.01f && TryStep(direction.normalized,
            walkSpeed*.55f*Mathf.Min(delta,.05f),false,out var position))
        {
            moving=true;
            body.position=position;
            body.rotation=Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(direction),turnDegreesPerSecond*delta);
        }
        else hasPatrolTarget=false;
        if(visual) visual.SetMoving(moving);
        if(Time.timeScale==0)
            foreach(var animator in GetComponentsInChildren<Animator>())
                if(animator.enabled && animator.runtimeAnimatorController) animator.Update(Mathf.Min(delta,.05f));
    }

    public void EndCinematicPatrol()
    {
        if(visual) visual.SetMoving(false);
        hasPatrolTarget=false; patrolWaitTimer=.5f;
    }

    private void RepositionAround(Vector3 target, float targetDistance)
    {
        Vector3 away = body.position - target;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();
        Vector3 direction = targetDistance < HorizontalContactDistance() + 0.35f
            ? away
            : Vector3.Cross(Vector3.up, away) * repositionSide;
        float step = 1.35f * Time.fixedDeltaTime;
        if (TryStep(direction, step, false, out Vector3 next))
        {
            body.MovePosition(next);
            if (visual != null) visual.SetMoving(true);
        }
        else if (visual != null) visual.SetMoving(false);
        Face(target);
    }

    private bool IsNearbyEngagedScout(StoneScoutRoadsideAI other)
    {
        return other != null && other != this && other.isActiveAndEnabled &&
               other.health != null && !other.health.IsDead && other.player == player &&
               other.state != State.Idle && other.state != State.Return &&
               other.state != State.Dead &&
               FlatDistance(home, other.home) <= squadLinkRange &&
               FlatDistance(other.body.position, player.transform.position) <= other.noticeRange + 2f;
    }

    private bool AnotherScoutIsAttacking()
    {
        foreach (StoneScoutRoadsideAI other in ActiveScouts)
            if (IsNearbyEngagedScout(other) &&
                (other.state == State.Windup || other.state == State.HitWindow))
                return true;
        return false;
    }

    private bool IsFlanker()
    {
        // Stable ranking gives at most two engaged Scouts the moving flank role.
        int lowerIdCount = 0;
        foreach (StoneScoutRoadsideAI other in ActiveScouts)
            if (IsNearbyEngagedScout(other) && other.GetInstanceID() < GetInstanceID())
                lowerIdCount++;
        return lowerIdCount < 2;
    }

    private void WaitForOpening(Vector3 target, float targetDistance)
    {
        float desiredDistance = HorizontalContactDistance() + 0.8f;
        if (targetDistance > desiredDistance + 0.35f)
        {
            MoveToward(target, walkSpeed * 0.75f, desiredDistance);
            return;
        }

        Vector3 away = body.position - target;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();

        if (targetDistance < desiredDistance - 0.25f)
        {
            TryWaitingStep(away, target);
            return;
        }

        if (IsFlanker())
        {
            Vector3 side = Vector3.Cross(Vector3.up, away) * repositionSide;
            TryWaitingStep(side, target);
        }
        else
        {
            Face(target);
            if (visual != null) visual.SetMoving(false);
        }
    }

    private void TryWaitingStep(Vector3 direction, Vector3 target)
    {
        if (TryStep(direction, walkSpeed * 0.55f * Time.fixedDeltaTime, false,
                out Vector3 next))
        {
            body.MovePosition(next);
            if (visual != null) visual.SetMoving(true);
        }
        else if (visual != null) visual.SetMoving(false);
        Face(target);
    }

    private bool TryStep(Vector3 direction, float step, bool returning, out Vector3 next)
    {
        Vector3 candidate = body.position + direction * step;
        next = default;
        if ((!returning && FlatDistance(home, candidate) > leashRange) ||
            !TryGround(candidate, out float groundY)) return false;
        candidate.y = groundY + 0.015f;
        if (!BodyClear(candidate)) return false;
        if (!SweptStepClear(candidate))
        { navigation.Blocked(body.position, direction); return false; }
        next = candidate;
        return true;
    }

    private bool BodyClear(Vector3 candidate)
    {
        Vector3 scale = transform.lossyScale;
        float radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = bodyCollider.height * Mathf.Abs(scale.y);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 centre = candidate + transform.TransformVector(bodyCollider.center);
        int count = Physics.OverlapCapsuleNonAlloc(centre - Vector3.up * halfSegment,
            centre + Vector3.up * halfSegment, radius + .02f, nearbyColliders,
            ~0, QueryTriggerInteraction.Ignore);
        if (count == nearbyColliders.Length) return false; // Conservative if the buffer filled.
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = nearbyColliders[i];
            if (obstacle == null || obstacle.transform.IsChildOf(transform) ||
                groundQuery.AllowsSupportContact(obstacle, candidate)) continue;
            return false;
        }
        return true;
    }

    private bool SweptStepClear(Vector3 candidate)
    {
        Vector3 travel = candidate - body.position;
        if (travel.sqrMagnitude < .000001f) return true;
        Vector3 scale = transform.lossyScale;
        float radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float half = Mathf.Max(0f, bodyCollider.height * Mathf.Abs(scale.y) * .5f - radius);
        Vector3 centre = body.position + transform.TransformVector(bodyCollider.center);
        int count = Physics.CapsuleCastNonAlloc(centre - Vector3.up * half, centre + Vector3.up * half,
            radius, travel.normalized, sightHits, travel.magnitude + .02f, ~0, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider hit = sightHits[i].collider;
            if (!hit || hit.transform.IsChildOf(transform)) continue;
            if (groundQuery.IsSurface(hit) && sightHits[i].normal.y >= .819f) continue;
            return false;
        }
        return true;
    }

    private bool HasClearSight()
    {
        if (playerBody == null) return false;
        Vector3 origin = bodyCollider.bounds.center;
        Vector3 toPlayer = playerBody.bounds.center - origin;
        float distance = toPlayer.magnitude;
        if (distance < 0.001f) return true;
        int count = Physics.RaycastNonAlloc(origin, toPlayer / distance, sightHits, distance,
            ~0, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = sightHits[i].collider;
            if (obstacle == null || obstacle.transform.IsChildOf(transform) ||
                obstacle.transform.IsChildOf(player.transform)) continue;
            return false;
        }
        return true;
    }

    private bool Facing(Vector3 destination, float minimumDot)
    {
        Vector3 direction = destination - body.position;
        direction.y = 0f;
        return direction.sqrMagnitude < 0.0001f ||
               Vector3.Dot(transform.forward, direction.normalized) >= minimumDot;
    }

    private void ReturnHome()
    {
        awareness?.Clear();
        if (FlatDistance(home, body.position) <= 0.12f)
        {
            body.MovePosition(new Vector3(home.x, body.position.y, home.z));
            body.MoveRotation(homeFacing);
            if (visual != null) visual.SetMoving(false);
            blockedTimer = lostSightTimer = 0f;
            patrolWaitTimer = Random.Range(1.1f, 2f);
            hasPatrolTarget = false;
            state = State.Idle;
        }
        else if (!MoveToward(home, walkSpeed))
        {
            if (visual != null) visual.SetMoving(false);
            // Do not teleport through scenery if the authored home becomes blocked.
            patrolWaitTimer = 1.5f;
            hasPatrolTarget = false;
            state = State.Idle;
        }
    }

    private bool TryGround(Vector3 candidate, out float groundY)
    {
        float size = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        if (!groundQuery.TryFootprint(candidate - Vector3.up * .015f, body.rotation,
            Vector2.one * (bodyCollider.radius * size), 35f, .4f, .6f, out groundY, true)) return false;
        return Mathf.Abs(groundY + 0.015f - body.position.y) <= 0.35f &&
               groundY >= home.y - 1.5f;
    }

    private void Face(Vector3 destination)
    {
        Vector3 direction = destination - body.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up),
                turnDegreesPerSecond * Time.fixedDeltaTime));
    }

    private bool TryStrike()
    {
        if (visual == null) return false;
        Vector3 target = player.transform.position;
        var evasion = player.GetComponent<PlayerBattleActions>();
        if (evasion && evasion.TryEvadeThreat(gameObject, body.position, Mathf.Min(attackRange + .1f, HorizontalContactDistance() + .2f), .5f)) return false;
        float targetDistance = FlatDistance(body.position, target);
        if (targetDistance > (bladeVisual ? Vector3.Distance(body.position, bladeVisual.BladeTip) + .5f : Mathf.Min(attackRange + 0.1f, HorizontalContactDistance() + 0.2f)) ||
            Mathf.Abs(target.y - body.position.y) > 1.8f) return false;

        Vector3 direction = target - body.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f ||
            Vector3.Dot(transform.forward, direction.normalized) < 0.5f) return false;

        if (bladeVisual)
        {
            if (!HasClearSight() || !bladeVisual.TryBladeContact(playerBody, out var bladePoint)) return false;
            if (playerHealth.TryApplyDamage(new DamageInfo(attackDamage, gameObject, bladePoint, direction)) && feel != null)
                feel.HitPlayer(bladePoint);
            return true;
        }
        Vector3 fist = visual.RightFistWorldPosition;
        Vector3 closest = playerBody.ClosestPoint(fist);
        Vector3 aim = closest - fist;
        if (!HasClearSight()) return false;
        bool contact = aim.sqrMagnitude <= fistHitRadius * fistHitRadius;
        Vector3 travel = hasFistSample ? fist - previousFist : Vector3.zero;
        if (travel.sqrMagnitude > .000001f)
        {
            int count = Physics.SphereCastNonAlloc(previousFist, fistHitRadius, travel.normalized,
                sightHits, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            float nearest = float.PositiveInfinity;
            Collider first = null;
            for (int i = 0; i < count; i++)
                if (sightHits[i].collider && !sightHits[i].collider.transform.IsChildOf(transform) &&
                    sightHits[i].distance < nearest)
                { nearest = sightHits[i].distance; first = sightHits[i].collider; closest = sightHits[i].point; }
            if (first && !first.transform.IsChildOf(player.transform)) return false;
            contact |= first != null;
        }
        if (!contact) return false;
        // An overlap at the fist start/end must not reach through nearby masonry.
        int overlaps = Physics.OverlapSphereNonAlloc(fist, fistHitRadius, nearbyColliders, ~0,
            QueryTriggerInteraction.Ignore);
        if (overlaps == nearbyColliders.Length) return false;
        for (int i = 0; i < overlaps; i++)
            if (nearbyColliders[i] && !nearbyColliders[i].transform.IsChildOf(transform) &&
                !nearbyColliders[i].transform.IsChildOf(player.transform)) return false;
        if (playerHealth.TryApplyDamage(new DamageInfo(attackDamage, gameObject, closest, direction)) && feel != null)
            feel.HitPlayer(closest);
        return true;
    }

    private void OnDamaged(Health target, DamageInfo damage)
    {
        attackSettleTime = 0f;
        hasFistSample = false;
        if (target.IsDead) return;
        if (visual != null) visual.SetMoving(false);
        if (feel != null) feel.EndWindup();
        timer = staggerSeconds;
        blockedTimer = 0f;
        state = State.Stagger; // Cancels a pending punch.
    }

    private void OnDied(Health target, DamageInfo damage)
    {
        hasFistSample = false;
        navigation.Clear();
        if (visual != null) visual.SetMoving(false);
        if (feel != null) feel.EndWindup();
        state = State.Dead;
    }

    public void ResetToHome()
    {
        awareness?.Clear();
        attackSettleTime = 0f;
        hasFistSample = false;
        navigation.Clear();
        body.position = home;
        body.rotation = homeFacing;
        state = State.Idle;
        timer = playerSearchTimer = 0f;
        lostSightTimer = blockedTimer = 0f;
        patrolWaitTimer = Random.Range(0.8f, 2.2f);
        hasPatrolTarget = false;
        lastSeenPosition = home;
        if (visual != null) visual.SetMoving(false);
        if (feel != null) feel.Clear();
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private float HorizontalContactDistance()
    {
        float enemyScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float playerScale = Mathf.Max(Mathf.Abs(playerBody.transform.lossyScale.x),
                                      Mathf.Abs(playerBody.transform.lossyScale.z));
        return bodyCollider.radius * enemyScale + playerBody.radius * playerScale;
    }
}
