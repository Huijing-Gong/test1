using UnityEngine;

// Reusable overworld enemy visuals and attacks. The player's sword and movement stay untouched.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(CapsuleCollider))]
public sealed class DomainPrototypeEnemy : MonoBehaviour
{
    public enum Kind { LeafWisp, RuinPrism }
    public enum PrismPalette { WestCyan, SideAEmber, SideBViolet, TowerGold }

    [SerializeField] private Kind kind;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform muzzle;
    [SerializeField] private LineRenderer warningLine;
    [SerializeField] private GameObject seedProjectile;
    [SerializeField] private float noticeRange = 8f;
    [SerializeField] private float windup = 0.85f;
    [SerializeField] private float cooldown = 2.3f;
    [SerializeField, Range(0.1f, 0.4f)] private float aimLockLeadSeconds = 0.22f;
    [SerializeField] private float pulseRadius = 5.4f;
    [SerializeField] private int damage = 9;
    [SerializeField] private PrismPalette prismPalette;
    [SerializeField, Min(0.5f)] private float wispPreferredRange = 3.2f;
    [SerializeField, Min(0.25f)] private float wispRepositionSpeed = 1.45f;
    [SerializeField, Min(0.5f)] private float wispLeashRadius = 4.5f;
    [SerializeField, Min(.1f)] private float wispPostShotPause = .45f;

    public Kind EnemyKind => kind;
    public PrismPalette Palette => prismPalette;
    public LineRenderer AttackLine => warningLine;
    public Transform AttackMuzzle => muzzle;
    public Color AttackTint => PrismAccent;
    public bool PulseActive => kind == Kind.RuinPrism && pulseVisualTime > 0f;
    public float PulseProgress => 1f - Mathf.Clamp01(pulseVisualTime / PulseDuration);
    public float AttackCharge => state == State.Warning ? 1f - Mathf.Clamp01(timer / AttackWindup) : 0f;

    private Health health;
    private EnemyAwareness awareness;
    private CapsuleCollider body;
    private StoneScoutLootDrop loot;
    private CombatFeedback feedback;
    private BaselineFlow flow;
    private PlayerVitals player;
    private Health playerHealth;
    private Vector3 home;
    private Quaternion homeRotation;
    private Vector3 visualHome;
    private Vector3 visualHomeScale;
    private Transform leftWing;
    private Transform rightWing;
    private Transform lens;
    private Transform lensFrame;
    private Transform chargeCore;
    private Transform prismCore;
    private Vector3 prismCoreHomeScale;
    private readonly Transform[] prismPetals = new Transform[4];
    private readonly Vector3[] prismPetalHomePositions = new Vector3[4];
    private readonly Vector3[] prismPetalDirections = new Vector3[4];
    private readonly Transform[] prismWarningLenses = new Transform[4];
    private readonly Vector3[] prismWarningLensHomeScales = new Vector3[4];
    private Quaternion leftWingHome;
    private Quaternion rightWingHome;
    private Vector3 lensHomeScale;
    private Mesh detailMesh;
    private EnemyGroundQuery groundQuery;
    private BoxCollider solidBase;
    private float groundOffset;
    private Mesh pulseFillMesh;
    private Material pulseFillMaterial;
    private MeshRenderer pulseFillRenderer;
    private Vector3 lockedAimPosition;
    private bool aimLocked;
    private float clock;
    private float timer;
    private float pulseVisualTime;
    private bool pulseHitPlayer;
    private bool pulseHitPending;
    private float pendingPulsePreviousRadius, pendingPulseCurrentRadius;
    private float prismSpin;
    private float nextSearch;
    private float hitVisualTime;
    private float recoilVisualTime;
    private float deathVisualTime;
    private float lostSightTime;
    private float strafeClock;
    private Vector3 wispShotPosition;
    private bool hasWispShotPosition;
    private float wispDecisionTime, wispBlockedTime;
    private float prismStaggerLockout;
    private float prismExposedTime;
    private bool paletteWasFlashing;
    private Renderer[] paletteRenderers;
    private MaterialPropertyBlock paletteBlock;
    private readonly float[] pulseSectorLimits = new float[RingSegments];
    // The cover pass already samples these radial floor points. Reuse them for
    // visuals instead of making 49 physics queries every warning/wave frame.
    private const int PulseSamples = 13;
    private float[] pulseSampleDistances, pulseSampleHeights;
    private Vector3[] pulseSampleNormals, pulseRingPoints;
    private int[] pulseSampleCounts;
    private Vector3[] pulseFillVertices;
    private Vector3 pulseCentre;
    private bool pulseCacheReady;
    private struct PulseFloorFrame { public Transform frame; public Matrix4x4 matrix; }
    private System.Collections.Generic.List<PulseFloorFrame> pulseFloorFrames;
    private Matrix4x4 pulseRootMatrix;
    public int PulseGroundSamples { get; private set; }
    private int strafeSign;
    private bool hasAlerted;
    private LeafWispPresentation wispPresentation;
    private Vector3 wispHitDirection, wispRecoilDirection;
    private float wispRetreatRest, deathGroundY;
    private float wispShotRadius = .13f;
    private bool wispLanded;
    public bool RangedCharging => kind == Kind.LeafWisp && state == State.Warning;
    public bool RangedLocked => RangedCharging && aimLocked;
    public bool RangedRecovering => kind == Kind.LeafWisp && state == State.Recover;
    public float RangedCharge => RangedCharging ? 1f - Mathf.Clamp01(timer / AttackWindup) : 0f;
    public int RangedShots { get; private set; }
    private float WispMoveRadius => body ? body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : .48f;
    private readonly Collider[] moveBlockers = new Collider[24];
    private readonly RaycastHit[] stepHits = new RaycastHit[16];
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private readonly EnemyNavigationRoute navigation = new EnemyNavigationRoute();
    private State state;
    private const int RingSegments = 48;
    private const float PulseDuration = 0.36f;
    // Apply the same modest speed increase to every Prism while preserving its location's tier.
    private const float PrismMoveSpeedMultiplier = 1.2f;
    private const float PrismWindupMultiplier = 0.9f;
    private const float PrismCooldownMultiplier = 0.85f;
    private float AttackWindup => kind == Kind.RuinPrism ? windup * PrismWindupMultiplier : windup;
    private float AttackCooldown => kind == Kind.RuinPrism ? cooldown * PrismCooldownMultiplier : cooldown;
    private EnemySoundEmitter sound;
    private bool plateCuePlayed;
    private bool paletteApplied;
    private State lastPaletteState;
    private PrismPalette lastPalette;
    private float lastPalettePower;
    private bool lastPaletteAimLocked;

    private enum State { Waiting, Alert, Warning, Recover, Staggered, Dead }

    private void Awake()
    {
        if (kind == Kind.RuinPrism)
        {
            pulseSampleDistances = new float[RingSegments * PulseSamples];
            pulseSampleHeights = new float[RingSegments * PulseSamples];
            pulseSampleNormals = new Vector3[RingSegments * PulseSamples];
            pulseSampleCounts = new int[RingSegments]; pulseRingPoints = new Vector3[RingSegments + 1];
            pulseFloorFrames = new System.Collections.Generic.List<PulseFloorFrame>(4);
        }
        health = GetComponent<Health>();
        awareness = EnemyAwareness.Ensure(this);
        body = GetComponent<CapsuleCollider>();
        if (kind == Kind.RuinPrism) solidBase = GetComponentInChildren<BoxCollider>();
        loot = GetComponent<StoneScoutLootDrop>();
        feedback = GetComponent<CombatFeedback>();
        home = transform.position;
        homeRotation = transform.rotation;
        if (visualRoot)
        {
            visualHome = visualRoot.localPosition;
            visualHomeScale = visualRoot.localScale;
            leftWing = visualRoot.Find("left leaf wing");
            rightWing = visualRoot.Find("right leaf wing");
            lens = visualRoot.Find("cyan lens");
            if (leftWing) leftWingHome = leftWing.localRotation;
            if (rightWing) rightWingHome = rightWing.localRotation;
            if (kind == Kind.LeafWisp) CreateWispDetails();
            else CreatePrismDetails();
            if (lens) lensHomeScale = lens.localScale;
        }
        if (warningLine) warningLine.enabled = false;
        groundQuery = new EnemyGroundQuery(transform);
        if (groundQuery.TrySample(home, .4f, 1f, 27f, out RaycastHit floor))
        {
            groundOffset = kind == Kind.RuinPrism ? .015f : Mathf.Max(.015f, home.y - floor.point.y);
            if (kind == Kind.RuinPrism)
            {
                if (TrySupportedPosition(home, out Vector3 supported))
                { transform.position = supported; home = supported; }
            }
        }
        strafeSign = (GetInstanceID() & 1) == 0 ? 1 : -1;
        paletteRenderers = GetComponentsInChildren<Renderer>(true);
        paletteBlock = new MaterialPropertyBlock();
        if (kind == Kind.RuinPrism) CreatePulseFill();
        if (kind == Kind.RuinPrism)
        {
            paletteRenderers = GetComponentsInChildren<Renderer>(true);
            paletteBlock = new MaterialPropertyBlock();
            ApplyPrismPalette();
        }
        EnemyImpactVfx.Ensure(gameObject);
        sound = GetComponent<EnemySoundEmitter>();
        if (kind == Kind.LeafWisp)
        {
            if (seedProjectile && seedProjectile.TryGetComponent(out DomainSeedProjectile dart)) wispShotRadius = dart.CollisionRadius;
            wispPresentation = GetComponent<LeafWispPresentation>();
            if (!wispPresentation) wispPresentation = gameObject.AddComponent<LeafWispPresentation>();
            wispPresentation.Configure(visualRoot, muzzle);
        }
    }

    private void OnEnable()
    {
        if (!health) health = GetComponent<Health>();
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        flow = FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += ResetForJourney;
    }

    private void OnDisable()
    {
        navigation.Clear();
        DomainSeedProjectile.ClearOwned(gameObject);
        if (health)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
        if (flow) flow.WorldEncountersReset -= ResetForJourney;
        if (warningLine) warningLine.enabled = false;
        if (pulseFillRenderer) pulseFillRenderer.enabled = false;
        pulseVisualTime = 0f;
        pulseHitPending = false;
        aimLocked = false;
        if (state != State.Dead) { state = State.Recover; timer = .35f; }
        if (chargeCore) chargeCore.localScale = Vector3.zero;
        if (sound) sound.StopAll();
        if (wispPresentation) wispPresentation.Clear();
        ApplyPrismPalette();
    }

    private void OnDestroy()
    {
        if (pulseFillMesh) Destroy(pulseFillMesh);
        if (pulseFillMaterial) Destroy(pulseFillMaterial);
        if (detailMesh) Destroy(detailMesh);
    }

    private void Update()
    {
        pulseHitPending = false;
        clock += Time.deltaTime;
        prismStaggerLockout = Mathf.Max(0f, prismStaggerLockout - Time.deltaTime);
        prismExposedTime = Mathf.Max(0f, prismExposedTime - Time.deltaTime);
        AnimateVisual();
        if (state == State.Dead) return;
        if (player && (player.gameObject.scene != gameObject.scene || !playerHealth ||
            playerHealth.IsDead || !player.EnemyCombatActive))
        {
            if(awareness.CanRetainEncounter){awareness.Suspend();return;}
            // Runs even with timeScale=0: no stale pulse can resume after a modal screen.
            if (state == State.Warning || state == State.Alert || pulseVisualTime > 0f) CancelWarning();
            DomainSeedProjectile.ClearOwned(gameObject);
            if (wispPresentation) wispPresentation.Clear();
            return;
        }
        // A player's successful sword hit may briefly set timeScale to zero. Freeze this
        // encounter for that hit-stop instead of treating it as a menu/scene transition.
        if (Time.deltaTime <= 0f && player && player.EnemyCombatActive) return;
        if (pulseVisualTime > 0f)
        {
            float previousRadius = pulseRadius * (1f - pulseVisualTime / PulseDuration);
            pulseVisualTime = Mathf.Max(0f, pulseVisualTime - Time.deltaTime);
            float progress = 1f - pulseVisualTime / PulseDuration;
            float currentRadius = pulseRadius * progress;
            if (warningLine && kind == Kind.RuinPrism)
            {
                DrawRing(Mathf.Max(0.08f, currentRadius));
                Color cyan = PrismAccent; cyan.a = 1f - progress;
                warningLine.startColor = warningLine.endColor = cyan;
                if (pulseVisualTime <= 0f) warningLine.enabled = false;
            }
            if (kind == Kind.RuinPrism && Time.deltaTime > 0f)
            {
                // Resolve after PlayerMovement.Update, not against last frame's grounded flag.
                pendingPulsePreviousRadius = previousRadius;
                pendingPulseCurrentRadius = currentRadius;
                pulseHitPending = true;
            }
        }
        if (!player || !playerHealth)
        {
            nextSearch -= Time.deltaTime;
            if (nextSearch <= 0f)
            {
                nextSearch = 1f;
                foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
                playerHealth = player ? player.GetComponent<Health>() : null;
            }
        }
        if (!player || !playerHealth || playerHealth.IsDead || !player.EnemyCombatActive)
        {
            // A menu, tutorial or side world must never finish an invisible attack.
            if (state == State.Warning || state == State.Alert) CancelWarning();
            return;
        }

        Vector3 perceivedDelta=player.transform.position-transform.position;
        float perceptionRange=kind==Kind.RuinPrism?Mathf.Max(noticeRange,pulseRadius+.8f):noticeRange;
        bool perceptionLine=perceivedDelta.sqrMagnitude <= perceptionRange*perceptionRange*2.11f &&
            (kind==Kind.RuinPrism?HasPrismLineOfSight():HasClearShot());
        awareness.Tick(player,perceptionLine,perceptionRange,Time.deltaTime,kind==Kind.RuinPrism);
        if (!awareness.Ready)
        {
            if(state==State.Warning || state==State.Alert) CancelWarning();
            if(state==State.Recover || state==State.Staggered)
            {timer-=Time.deltaTime;if(timer<=0f)state=State.Waiting;}
            if (kind==Kind.LeafWisp && awareness.Curious)
            {
                Vector3 look=awareness.InterestPosition-transform.position;look.y=0f;
                if(look.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(look),140f*Time.deltaTime);
            }
            else if(state==State.Waiting)
            {
                // Ambient motion stays near home and uses the existing ground/obstacle sweeps.
                Vector3 goal=home;
                if(kind==Kind.LeafWisp && Vector3.Distance(transform.position,home)<2f)
                    goal=home+homeRotation*new Vector3(Mathf.Sin(clock*.3f)*1.1f,0f,Mathf.Cos(clock*.3f)*.7f);
                if(Vector3.Distance(transform.position,goal)>.18f)
                    TryHoverMove(goal-transform.position,Time.deltaTime*.32f,navGoal:goal);
                if(kind==Kind.LeafWisp)transform.rotation=Quaternion.RotateTowards(transform.rotation,
                    homeRotation*Quaternion.Euler(0,Mathf.Sin(clock*.55f)*28f,0),55f*Time.deltaTime);
            }
            hasAlerted=false;
            return;
        }

        if (!awareness.Visible && (state==State.Waiting || state==State.Recover || state==State.Alert))
        {
            // Searching must not run the combat repositioner against hidden live coordinates.
            if(state==State.Alert)CancelWarning();
            if(state==State.Recover){timer-=Time.deltaTime;if(timer<=0f)state=State.Waiting;}
            if(state==State.Waiting && Vector3.Distance(transform.position,awareness.InterestPosition)>1f)
                TryHoverMove(awareness.InterestPosition-transform.position,Time.deltaTime*.5f,navGoal:awareness.InterestPosition);
            return;
        }

        if (kind == Kind.LeafWisp && (state == State.Waiting || state == State.Recover))
            RepositionWisp();
        if (kind == Kind.RuinPrism && state == State.Waiting)
            RepositionPrism();

        if (kind == Kind.LeafWisp && state != State.Staggered &&
            !(state == State.Warning && aimLocked))
        {
            Vector3 look = (awareness.Visible?player.transform.position:awareness.InterestPosition) - transform.position;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f && look.sqrMagnitude < noticeRange * noticeRange * 2f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(look), 240f * Time.deltaTime);
        }

        timer -= Time.deltaTime;
        if (state == State.Warning)
        {
            if (kind == Kind.LeafWisp && !aimLocked && !HasClearShot())
            {
                CancelWarning();
                return;
            }
            if (kind == Kind.LeafWisp && !aimLocked && timer <= aimLockLeadSeconds)
                LockAim();
            UpdateWarning();
            if (timer <= 0f) Fire();
        }
        else if (state == State.Alert)
        {
            if (!CanStartAttack()) { CancelWarning(); return; }
            if (timer <= 0f) BeginWarning();
        }
        else if (state == State.Recover || state == State.Staggered)
        {
            if (timer <= 0f) state = State.Waiting;
        }
        else if (timer <= 0f && CanStartAttack())
        {
            if (kind == Kind.LeafWisp && !hasAlerted)
            {
                state = State.Alert;
                timer = 0.25f;
                hasAlerted = true;
            }
            else BeginWarning();
        }
    }

    private void BeginWarning()
    {
        hasWispShotPosition = false;
        state = State.Warning;
        timer = AttackWindup;
        aimLocked = false;
        plateCuePlayed = false;
        if (kind == Kind.RuinPrism)
        {
            MeasurePulseCover();
            RefreshPulseFill();
        }
        PlayPrismCue(false);
        if(wispPresentation) wispPresentation.BeginCharge();
        if (warningLine) warningLine.enabled = kind == Kind.RuinPrism;
        if (pulseFillRenderer) pulseFillRenderer.enabled = true;
        UpdateWarning();
    }

    private void AnimateVisual()
    {
        if (!visualRoot || !visualRoot.gameObject.activeSelf) return;
        if (kind == Kind.LeafWisp && state == State.Dead)
        {
            AnimateWispDeath();
            return;
        }
        if (state == State.Dead)
        {
            deathVisualTime += Time.deltaTime;
            float t = Mathf.Clamp01(deathVisualTime / 0.72f);
            float fall = t * t * (3f - 2f * t);
            visualRoot.localPosition = visualHome + Vector3.down * (0.58f * fall);
            visualRoot.localRotation = Quaternion.Euler(23f * fall, 70f * fall, 95f * fall);
            visualRoot.localScale = visualHomeScale * Mathf.Lerp(1f, 0.08f, fall);
            if (t >= 1f) visualRoot.gameObject.SetActive(false);
            return;
        }

        hitVisualTime = Mathf.Max(0f, hitVisualTime - Time.deltaTime);
        recoilVisualTime = Mathf.Max(0f, recoilVisualTime - Time.deltaTime);
        float hit = hitVisualTime / 0.32f;
        float recoil = recoilVisualTime / 0.23f;
        float charge = state == State.Warning ? 1f - Mathf.Clamp01(timer / Mathf.Max(0.1f, AttackWindup)) :
            state == State.Alert ? 0.22f : 0f;
        float exhausted = kind == Kind.RuinPrism && state == State.Recover
            ? Mathf.Clamp01(timer / Mathf.Max(0.1f, AttackCooldown)) : 0f;
        bool details = EnemyPresentationBudget.Details(transform.position);
        float bob = details ? Mathf.Sin(clock * (kind == Kind.LeafWisp ? 3.3f : 2f)) *
            (kind == Kind.LeafWisp ? 0.075f : 0.04f) : 0f;
        bool staggered = state == State.Staggered;
        visualRoot.localPosition = visualHome + Vector3.up * (bob + charge * 0.09f - hit * 0.09f - (staggered ? .08f : 0f));
        if (kind == Kind.RuinPrism && (details || charge > 0f)) prismSpin += Time.deltaTime * (23f + charge * 90f);
        visualRoot.localRotation = Quaternion.Euler(staggered ? 12f : 0f,
            (kind == Kind.RuinPrism ? prismSpin : details ? Mathf.Sin(clock * 1.4f) * 4f : 0f),
            hit * 18f - recoil * 16f);
        visualRoot.localScale = visualHomeScale * (1f + charge * 0.1f - hit * 0.14f + recoil * 0.06f);
        if (leftWing) leftWing.localRotation = leftWingHome *
            Quaternion.Euler(0, 0, -charge * 24f + (details ? Mathf.Sin(clock * 4f) * 5f : 0f) + hit * 19f);
        if (rightWing) rightWing.localRotation = rightWingHome *
            Quaternion.Euler(0, 0, charge * 24f - (details ? Mathf.Sin(clock * 4f) * 5f : 0f) - hit * 19f);
        if (lens) lens.localScale = lensHomeScale * (1f + charge * 0.46f + recoil * 0.25f);
        if (lensFrame && details) lensFrame.localRotation = Quaternion.Euler(0, 0, clock * 75f);
        if (chargeCore)
        {
            float chargeSize = state == State.Warning
                ? Mathf.Lerp(0.04f, 0.24f, charge) * (1f + 0.08f * Mathf.Sin(clock * 32f))
                : state == State.Alert ? 0.035f : recoilVisualTime > .13f ? .16f * recoil : 0f;
            chargeCore.localScale = Vector3.one * chargeSize;
        }
        if (kind == Kind.LeafWisp)
        {
            // Rigid ceramic: lean away from the actual contact and recoil along the
            // actual shot, rather than uniformly shrinking or rolling sideways.
            Vector3 localHit = transform.InverseTransformDirection(wispHitDirection);
            Vector3 localRecoil = transform.InverseTransformDirection(wispRecoilDirection);
            visualRoot.localPosition = visualHome + Vector3.up * (bob + charge * .06f - (staggered ? .06f : 0f)) +
                localHit * (.075f * hit) + localRecoil * (.13f * recoil);
            visualRoot.localScale = visualHomeScale;
            visualRoot.localRotation = Quaternion.Euler(-localHit.z * hit * 17f + recoil * 12f + charge * 5f,
                details ? Mathf.Sin(clock * 1.4f) * 2f : 0f, -localHit.x * hit * 17f);
            Vector3 centre = Vector3.up * 1.23f;
            visualRoot.localPosition += centre - visualRoot.localRotation * centre;
            if (lensFrame) lensFrame.localRotation = Quaternion.Euler(0, 0, aimLocked ? 0f : clock * 40f);
            if (lensFrame) lensFrame.localScale = Vector3.one * (aimLocked ? .86f : 1f);
        }
        if (prismCore)
            prismCore.localScale = prismCoreHomeScale *
                (1f + charge * 0.27f + recoil * 0.18f - exhausted * 0.1f +
                 (details ? Mathf.Sin(clock * 4f) * 0.035f : 0f));
        for (int i = 0; i < prismPetals.Length; i++)
        {
            if (!prismPetals[i]) continue;
            // Closing/dimming signals recovery, not an unimplemented glowing weak point.
            float opening = charge * 0.19f + recoil * 0.12f - exhausted * 0.03f +
                (details ? Mathf.Sin(clock * 2.7f + i * 1.57f) * 0.015f : 0f);
            prismPetals[i].localPosition = prismPetalHomePositions[i] +
                prismPetalDirections[i] * opening + Vector3.up * (charge * 0.08f);
            if (prismWarningLenses[i])
                prismWarningLenses[i].localScale = prismWarningLensHomeScales[i] *
                    (1f + charge * (0.65f + 0.1f * Mathf.Sin(clock * 24f)) - exhausted * 0.18f);
        }
    }

    private void AnimateWispDeath()
    {
        deathVisualTime += Time.deltaTime;
        float fall = Mathf.SmoothStep(0, 1, Mathf.Clamp01((deathVisualTime - .12f) / .72f));
        visualRoot.localPosition = visualHome + Vector3.down * (.92f * fall);
        visualRoot.localRotation = Quaternion.Euler(22f * fall, 32f * fall, 66f * fall);
        Vector3 centre = Vector3.up * 1.23f;
        visualRoot.localPosition += centre - visualRoot.localRotation * centre;
        visualRoot.localScale = visualHomeScale;
        if (leftWing) leftWing.localRotation = leftWingHome * Quaternion.Euler(0, 0, -48f * fall);
        if (rightWing) rightWing.localRotation = rightWingHome * Quaternion.Euler(0, 0, 48f * fall);
        // Fit the actual rotated visual bounds onto the sampled supported floor.
        // Never translate the live body, disable world collision or fall into water.
        float lowest = float.PositiveInfinity;
        foreach (Renderer item in paletteRenderers)
            if (item && item.enabled && item.transform.IsChildOf(visualRoot) && item.sharedMaterial && item.sharedMaterial.renderQueue < 2500)
                lowest = Mathf.Min(lowest, item.bounds.min.y);
        if (lowest < deathGroundY + .02f) visualRoot.position += Vector3.up * (deathGroundY + .02f - lowest);
        if (!wispLanded && fall >= .99f)
        {
            wispLanded = true;
            GetComponent<EnemyImpactVfx>()?.EmitLanding(new Vector3(transform.position.x, deathGroundY + .03f, transform.position.z));
            if (sound) sound.Cue(EnemySoundCue.StoneFall, .38f, point: transform.position);
        }
        if (deathVisualTime > 1.25f)
            visualRoot.localScale = visualHomeScale * Mathf.Lerp(1f, .08f, Mathf.SmoothStep(0, 1, (deathVisualTime - 1.25f) / .35f));
        if (deathVisualTime >= 1.6f) visualRoot.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (pulseHitPending)
        {
            pulseHitPending = false;
            TryPulseHit(pendingPulsePreviousRadius, pendingPulseCurrentRadius);
        }
        if (paletteRenderers == null) return;
        bool flashing = feedback && feedback.IsFlashing && !feedback.UsesLocalizedEnemyVisuals;
        if (!flashing) ApplyPrismPalette();
        paletteWasFlashing = flashing;
    }

    private Color PrismAccent => prismPalette switch
    {
        PrismPalette.SideAEmber => new Color(1f, 0.34f, 0.09f),
        PrismPalette.SideBViolet => new Color(0.67f, 0.35f, 1f),
        PrismPalette.TowerGold => new Color(1f, 0.84f, 0.21f),
        _ => new Color(0.08f, 0.78f, 0.91f)
    };

    private void ApplyPrismPalette()
    {
        if (paletteRenderers == null || paletteBlock == null) return;
        Color accent = kind == Kind.RuinPrism ? PrismAccent : aimLocked ? new Color(1f,.67f,.16f) : new Color(.08f, .78f, .91f);
        float power = !Application.isPlaying ? 2f : state == State.Dead ? 0f :
            state == State.Recover ? .18f : state == State.Warning ?
            1.2f + 2.3f * (1f - Mathf.Clamp01(timer / AttackWindup)) :
            state == State.Staggered ? EnemyImpactVfx.StunPower(clock) : 1.5f;
        if (Application.isPlaying && paletteApplied && !paletteWasFlashing &&
            lastPaletteState == state && lastPalette == prismPalette && lastPaletteAimLocked == aimLocked && Mathf.Abs(lastPalettePower - power) < .01f) return;
        paletteApplied = true; lastPaletteState = state; lastPalette = prismPalette; lastPalettePower = power;
        lastPaletteAimLocked = aimLocked;
        foreach (Renderer renderer in paletteRenderers)
        {
            if (!renderer || !renderer.sharedMaterial) continue;
            string materialName = renderer.sharedMaterial.name;
            bool glowing = materialName.Contains("CyanCircuit") || materialName.Contains("AmberLens");
            if (kind == Kind.LeafWisp && !glowing) continue;
            float shellBlend = prismPalette == PrismPalette.WestCyan ? .12f : .47f;
            Color tint = materialName.Contains("CyanCircuit") ? accent :
                materialName.Contains("AmberLens") ? Color.Lerp(accent, Color.white, 0.3f) :
                materialName.Contains("IvoryCeramic") ? Color.Lerp(new Color(.79f, .78f, .66f), accent, shellBlend) :
                materialName.Contains("Graphite") ? Color.Lerp(new Color(.12f, .18f, .19f), accent, .3f) : Color.clear;
            if (tint == Color.clear) continue;
            if (kind == Kind.LeafWisp && renderer.sharedMaterial.HasProperty("_BaseColor"))
                tint = aimLocked ? Color.Lerp(renderer.sharedMaterial.GetColor("_BaseColor"),accent,.85f) : renderer.sharedMaterial.GetColor("_BaseColor");
            if (Application.isPlaying && (materialName.Contains("CyanCircuit") || materialName.Contains("AmberLens")))
                tint *= state == State.Dead ? .1f : state == State.Recover ? .3f : 1f;
            renderer.GetPropertyBlock(paletteBlock);
            if (renderer.sharedMaterial.HasProperty("_BaseColor")) paletteBlock.SetColor("_BaseColor", tint);
            else if (renderer.sharedMaterial.HasProperty("_Color")) paletteBlock.SetColor("_Color", tint);
            if (renderer.sharedMaterial.HasProperty("_EmissionColor") &&
                (materialName.Contains("CyanCircuit") || materialName.Contains("AmberLens")))
                paletteBlock.SetColor("_EmissionColor", accent * power);
            renderer.SetPropertyBlock(paletteBlock);
        }
    }

    // Editor preview can apply the same shader overrides without running enemy AI.
    public void RefreshPalettePreview()
    {
        if (kind != Kind.RuinPrism) return;
        if (paletteRenderers == null) paletteRenderers = GetComponentsInChildren<Renderer>(true);
        if (paletteBlock == null) paletteBlock = new MaterialPropertyBlock();
        ApplyPrismPalette();
    }

    private void PlayPrismCue(bool release)
    {
        if (!sound) return;
        if (release) sound.StopWarning();
        sound.Cue(kind == Kind.RuinPrism ? (release ? EnemySoundCue.Pulse : EnemySoundCue.PrismCharge) :
            (release ? EnemySoundCue.DartFire : EnemySoundCue.CoreLock), seconds: release ? 0f : AttackWindup);
    }

    private void RepositionWisp()
    {
        if (groundQuery == null) return;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        Vector3 fromHome = transform.position - home;
        fromHome.y = 0f;
        if (distance > noticeRange + 3f || Mathf.Abs(player.transform.position.y - transform.position.y) > 2.5f)
        {
            lostSightTime += Time.deltaTime;
            if (lostSightTime > 1.2f) hasAlerted = false;
            if (fromHome.magnitude > 0.12f) TryHoverMove(-fromHome, Time.deltaTime, navGoal: home);
            return;
        }

        bool visible = HasClearShot();
        if (visible) lostSightTime = 0f;
        else lostSightTime += Time.deltaTime;
        if (lostSightTime > 1.2f) hasAlerted = false;

        // Remain still immediately after firing. The player can close in during this window.
        if (state == State.Recover && timer > AttackCooldown - wispPostShotPause) return;
        wispRetreatRest = Mathf.Max(0f, wispRetreatRest - Time.deltaTime);
        // Hysteresis: finish a selected short retreat, then hold long enough to
        // fight. A player must be able to close in, not trigger infinite backpedal.
        if (!hasWispShotPosition && visible && wispRetreatRest > 0f) return;
        if (!hasWispShotPosition && visible && distance >= wispPreferredRange - .45f && distance <= noticeRange - .5f)
        { hasWispShotPosition = false; wispBlockedTime = 0f; return; }
        wispDecisionTime = Mathf.Max(0f, wispDecisionTime - Time.deltaTime);
        if (!hasWispShotPosition && wispDecisionTime <= 0f)
        {
            hasWispShotPosition = ChooseWispShotPosition(toPlayer, distance < wispPreferredRange - .45f,
                out wispShotPosition);
            wispDecisionTime = 1.2f; wispBlockedTime = 0f;
        }
        if (!hasWispShotPosition) return;
        Vector3 direction = wispShotPosition - transform.position; direction.y = 0f;
        if (direction.magnitude < .18f)
        { hasWispShotPosition = false; wispRetreatRest = .8f; return; }
        if (TryHoverMove(direction, Time.deltaTime, navGoal: wispShotPosition)) wispBlockedTime = 0f;
        else
        {
            wispBlockedTime += Time.deltaTime;
            if (wispBlockedTime > .35f)
            { hasWispShotPosition = false; wispDecisionTime = .4f; strafeSign = -strafeSign; }
        }
    }

    private bool ChooseWispShotPosition(Vector3 toPlayer, bool retreat, out Vector3 goal)
    {
        goal = default;
        Vector3 away = toPlayer.sqrMagnitude > .01f ? -toPlayer.normalized : -transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, away) * strafeSign;
        float best = float.PositiveInfinity;
        // Five bounded points, ranked once; never random orbiting or an unlimited retreat.
        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = i == 0 ? away * (retreat ? 1.7f : -1.7f) : i == 1 ? side * 1.6f : i == 2 ? -side * 1.6f :
                i == 3 ? (away + side).normalized * 1.8f : (away - side).normalized * 1.8f;
            Vector3 point = transform.position + offset;
            Vector3 fromHome = point - home; fromHome.y = 0f;
            if (fromHome.magnitude > wispLeashRadius - .15f || !TrySupportedPosition(point, out point)) continue;
            float range = Vector2.Distance(new Vector2(point.x, point.z),
                new Vector2(player.transform.position.x, player.transform.position.z));
            // A rear wall should allow a genuine lateral escape, not reject it
            // solely because it gains less than thirty centimetres of range.
            if (retreat && range < toPlayer.magnitude + .08f) continue;
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .82f, point + Vector3.up * 1.5f,
                WispMoveRadius, moveBlockers, ~0, QueryTriggerInteraction.Ignore);
            bool clear = count < moveBlockers.Length;
            for (int j = 0; j < count && clear; j++) if (BlocksHoverStep(moveBlockers[j], point)) clear = false;
            if (!clear || !ClearWispCorridor(point)) continue;
            // Check the muzzle at the candidate's final facing, not at our old
            // heading: a forward-mounted emitter swings sideways while turning.
            Vector3 facing = player.transform.position - point; facing.y = 0f;
            Quaternion orientation = facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing) : transform.rotation;
            Vector3 emitterOffset = muzzle ? Quaternion.Inverse(transform.rotation) * (muzzle.position - transform.position) : Vector3.up;
            Vector3 start = point + orientation * emitterOffset;
            bool shot = ClearLineToPlayer(start);
            if (!retreat && !shot) continue; // Don't shuffle to another occluded shooting point.
            float score = Mathf.Abs(range - (wispPreferredRange + .35f)) + (shot ? 0f : 4f) +
                offset.magnitude * .25f + i * .04f;
            if (score < best) { best = score; goal = point; }
        }
        return best < float.PositiveInfinity;
    }

    private bool ClearWispCorridor(Vector3 point)
    {
        Vector3 travel = point - transform.position;
        int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 1.15f, WispMoveRadius,
            travel.normalized, stepHits, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == stepHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = stepHits[i];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) ||
                (groundQuery.IsSurface(hit.collider) && hit.normal.y >= .891f)) continue;
            return false;
        }
        return true;
    }

    private void RepositionPrism()
    {
        if (groundQuery == null || !player) return;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        Vector3 fromHome = transform.position - home;
        fromHome.y = 0f;
        if (distance > pulseRadius + 3f ||
            Mathf.Abs(player.transform.position.y - transform.position.y) > 2.5f)
        {
            // Do not chase across the overworld; return to guarding the western objective.
            if (fromHome.sqrMagnitude > 0.18f * 0.18f)
                TryHoverMove(-fromHome, Time.deltaTime, 2f, home);
            return;
        }
        strafeClock = Mathf.Max(0f, strafeClock - Time.deltaTime);
        Vector3 side = Vector3.Cross(Vector3.up, toPlayer.normalized) * strafeSign;
        Vector3 direction;
        float speed;
        if (fromHome.magnitude > 3.25f)
        {
            direction = -fromHome;
            speed = 2f;
        }
        else if (distance > 3.4f)
        {
            // Close distance before charging; orbit slightly instead of moving in a straight line.
            direction = toPlayer.normalized + side * 0.25f;
            speed = 2.2f;
        }
        else if (distance < 1.9f)
        {
            direction = -toPlayer.normalized + side * 0.2f;
            speed = 1.8f;
        }
        else
        {
            if (HasPrismLineOfSight()) return; // Hold a good area-control position.
            direction = side; speed = 1.65f;
        }
        Vector3? goal = fromHome.magnitude > 3.25f ? home : distance > 3.4f ? player.transform.position : (Vector3?)null;
        if (!TryHoverMove(direction, Time.deltaTime, speed, goal))
        {
            if (navigation.Available) return;
            if (strafeClock > 0f) return;
            strafeClock = 1.2f; strafeSign = -strafeSign;
            if (!TryHoverMove(-side, Time.deltaTime, speed) && distance > 3.4f)
                TryHoverMove(toPlayer, Time.deltaTime, speed);
        }
    }

    private bool TryHoverMove(Vector3 direction, float deltaTime, float prismSpeed = 2f, Vector3? navGoal = null)
    {
        if (groundQuery == null) return false;
        direction.y = 0f;
        if (deltaTime <= 0f || direction.sqrMagnitude < 0.001f) return false;
        bool prism = kind == Kind.RuinPrism;
        float moveSpeed = prism ? prismSpeed * PrismMoveSpeedMultiplier : wispRepositionSpeed;
        float moveRadius = prism ? 0.95f : WispMoveRadius;
        float leash = prism ? 3.5f : wispLeashRadius;
        Vector3 goal = navGoal ?? transform.position + direction.normalized * 1.8f;
        Vector3 goalOffset = goal - home; goalOffset.y = 0f;
        if (goalOffset.magnitude > leash - .1f)
        { float height = goal.y; goal = home + goalOffset.normalized * (leash - .1f); goal.y = height; }
        bool hasRoute = navigation.Direction(gameObject.scene,
            prism ? EnemyNavigationWorld.Profile.RuinPrism : EnemyNavigationWorld.Profile.LeafWisp,
            transform.position - Vector3.up * groundOffset, goal - Vector3.up * groundOffset,
            deltaTime, out Vector3 routeDirection);
        if (navigation.Available)
        {
            if (!hasRoute) return false;
            direction = routeDirection;
        }
        Vector3 candidate = transform.position + direction.normalized * (moveSpeed * deltaTime);
        Vector3 offset = candidate - home;
        offset.y = 0f;
        if (offset.magnitude > leash) { navigation.Blocked(transform.position, direction); return false; }
        if (!TrySupportedPosition(candidate, out Vector3 supported))
        { navigation.Blocked(transform.position, direction); return false; }
        candidate = supported;
        if (Mathf.Abs(candidate.y - transform.position.y) > 0.18f)
        { navigation.Blocked(transform.position, direction); return false; }
        Vector3 travel = candidate - transform.position;
        if (solidBase && !ClearBaseStep(candidate, travel))
        { navigation.Blocked(transform.position, direction); return false; }
        int swept = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 1.15f,
            moveRadius, travel.normalized, stepHits, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (swept == stepHits.Length) return false;
        for (int i = 0; i < swept; i++)
            if (!(groundQuery.IsSurface(stepHits[i].collider) && stepHits[i].normal.y >= .891f) &&
                BlocksHoverStep(stepHits[i].collider, candidate))
            { navigation.Blocked(transform.position, direction); return false; }
        int count = Physics.OverlapCapsuleNonAlloc(candidate + Vector3.up * (prism ? 0.65f : 0.82f),
            candidate + Vector3.up * (prism ? 1.75f : 1.5f), moveRadius,
            moveBlockers, ~0, QueryTriggerInteraction.Ignore);
        if (count == moveBlockers.Length) return false;
        for (int i = 0; i < count; i++)
            if (BlocksHoverStep(moveBlockers[i], candidate))
            { navigation.Blocked(transform.position, direction); return false; }
        transform.position = candidate;
        return true;
    }

    private bool TrySupportedPosition(Vector3 candidate, out Vector3 supported)
    {
        supported = candidate;
        Vector3 scale = transform.lossyScale;
        Vector2 footprint = new Vector2(body.radius * Mathf.Abs(scale.x), body.radius * Mathf.Abs(scale.z));
        Quaternion facing = transform.rotation;
        if (solidBase)
        {
            footprint = new Vector2(solidBase.size.x * Mathf.Abs(solidBase.transform.lossyScale.x) * .5f,
                solidBase.size.z * Mathf.Abs(solidBase.transform.lossyScale.z) * .5f);
            facing = solidBase.transform.rotation;
        }
        if (!groundQuery.TryFootprint(candidate - Vector3.up * groundOffset, facing, footprint,
            27f, .35f, .6f, out float highest, !solidBase)) return false;
        supported.y = highest + groundOffset;
        return true;
    }

    private bool ClearBaseStep(Vector3 candidate, Vector3 travel)
    {
        Vector3 centre = solidBase.transform.TransformPoint(solidBase.center);
        Vector3 scale = solidBase.transform.lossyScale;
        Vector3 half = Vector3.Scale(solidBase.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Quaternion rotation = solidBase.transform.rotation;
        int count = Physics.BoxCastNonAlloc(centre, half, travel.normalized, stepHits,
            rotation, travel.magnitude + .02f, ~0, QueryTriggerInteraction.Ignore);
        if (count == stepHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (!(groundQuery.IsSurface(stepHits[i].collider) && stepHits[i].normal.y >= .891f) &&
                BlocksHoverStep(stepHits[i].collider, candidate)) return false;
        count = Physics.OverlapBoxNonAlloc(centre + travel, half + Vector3.one * .01f,
            moveBlockers, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == moveBlockers.Length) return false;
        for (int i = 0; i < count; i++) if (BlocksHoverStep(moveBlockers[i], candidate)) return false;
        return true;
    }

    private bool BlocksHoverStep(Collider obstacle, Vector3 candidate)
    {
        if (!obstacle || obstacle.transform == transform ||
            obstacle.transform.IsChildOf(transform)) return false;
        if (groundQuery.AllowsSupportContact(obstacle, candidate - Vector3.up * groundOffset)) return false;
        // If already touching the player, allow only a step that increases separation.
        if (player && obstacle.GetComponentInParent<PlayerVitals>() == player &&
            Vector3.Distance(candidate, player.transform.position) >
            Vector3.Distance(transform.position, player.transform.position) + 0.001f) return false;
        return true;
    }

    private void CreatePrismDetails()
    {
        if (!visualRoot) return;
        visualRoot.localScale = Vector3.one * 1.25f;
        visualHomeScale = visualRoot.localScale;
        if (body)
        {
            body.center = Vector3.up * 1.18f;
            body.height = 2.36f;
            body.radius = 0.78f;
        }
        Transform stoneBase = transform.Find("broken stone base");
        if (stoneBase)
        {
            stoneBase.localPosition = new Vector3(0f, 0.22f, 0f);
            stoneBase.localScale = new Vector3(1.8f, 0.22f, 1.8f);
            BoxCollider baseCollider = stoneBase.GetComponent<BoxCollider>();
            if (!baseCollider) baseCollider = stoneBase.gameObject.AddComponent<BoxCollider>();
            baseCollider.size = new Vector3(1f, 2f, 1f);
        }
        Transform plinth = transform.Find("dark plinth");
        if (plinth)
        {
            plinth.localPosition = new Vector3(0f, 0.51f, 0f);
            plinth.localScale = new Vector3(1.08f, 0.14f, 1.08f);
        }
        prismCore = visualRoot.Find("floating core");
        if (prismCore)
        {
            prismCore.localScale = Vector3.one * 0.66f;
            prismCoreHomeScale = prismCore.localScale;
        }
        for (int i = 0; i < prismPetals.Length; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Transform petal = visualRoot.Find("ceramic prism " + i);
            if (petal)
            {
                // Four separated shards frame the core instead of hiding it behind a white wall.
                petal.localPosition = new Vector3(radial.x * 0.53f, 1.27f, radial.z * 0.53f);
                petal.localScale = new Vector3(0.27f, 0.68f, 0.16f);
                prismPetals[i] = petal;
                prismPetalHomePositions[i] = petal.localPosition;
                prismPetalDirections[i] = radial;
            }
            Transform warningLens = visualRoot.Find("amber warning lens " + i);
            if (warningLens)
            {
                warningLens.localPosition = new Vector3(radial.x * 0.72f, 1.25f,
                    radial.z * 0.72f);
                warningLens.localScale = Vector3.one * 0.15f;
                prismWarningLenses[i] = warningLens;
                prismWarningLensHomeScales[i] = warningLens.localScale;
            }
        }
    }

    private void CreateWispDetails()
    {
        if (!lens) return;
        // Make the dark face and bright eye readable at the normal third-person camera distance.
        Transform bodyCore = visualRoot.Find("dark seed body");
        if (bodyCore) bodyCore.localScale = new Vector3(0.82f, 0.76f, 0.7f);
        Transform socket = visualRoot.Find("front lens socket");
        if (socket) socket.localScale = new Vector3(0.51f, 0.51f, 0.2f);
        lens.localScale = new Vector3(0.3f, 0.3f, 0.15f);
        MeshRenderer lensRenderer = lens.GetComponent<MeshRenderer>();
        if (!lensRenderer || !lensRenderer.sharedMaterial) return;
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Charging seed light";
        core.transform.SetParent(visualRoot, false);
        core.transform.localPosition = new Vector3(0f, 1.25f, 0.66f);
        core.transform.localScale = Vector3.zero;
        Destroy(core.GetComponent<Collider>());
        MeshRenderer coreRenderer = core.GetComponent<MeshRenderer>();
        coreRenderer.sharedMaterial = lensRenderer.sharedMaterial;
        coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        chargeCore = core.transform;
        GameObject frame = new GameObject("Compact rotating lens reticle", typeof(MeshFilter), typeof(MeshRenderer));
        frame.transform.SetParent(visualRoot, false);
        frame.transform.localPosition = new Vector3(0f, 1.25f, 0.47f);
        lensFrame = frame.transform;
        const int segments = 24;
        Vector3[] vertices = new Vector3[segments * 2 + 9];
        int[] triangles = new int[segments * 6 + 9];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float outer = i % 8 == 0 ? 0.29f : 0.245f;
            vertices[i * 2] = new Vector3(Mathf.Cos(angle) * 0.205f, Mathf.Sin(angle) * 0.205f);
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(angle) * outer, Mathf.Sin(angle) * outer);
            int next = (i + 1) % segments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = i * 2 + 1;
            triangles[t + 2] = next * 2 + 1;
            triangles[t + 3] = i * 2;
            triangles[t + 4] = next * 2 + 1;
            triangles[t + 5] = next * 2;
        }
        int accent = segments * 2;
        vertices[accent] = new Vector3(-.32f, -.04f);
        vertices[accent + 1] = new Vector3(-.41f, .02f);
        vertices[accent + 2] = new Vector3(-.31f, .13f);
        vertices[accent + 3] = new Vector3(.32f, -.04f);
        vertices[accent + 4] = new Vector3(.41f, .02f);
        vertices[accent + 5] = new Vector3(.31f, .13f);
        vertices[accent + 6] = new Vector3(-.045f, .3f);
        vertices[accent + 7] = new Vector3(.045f, .3f);
        vertices[accent + 8] = new Vector3(0f, .41f);
        int tail = segments * 6;
        triangles[tail] = accent;
        triangles[tail + 1] = accent + 2;
        triangles[tail + 2] = accent + 1;
        triangles[tail + 3] = accent + 3;
        triangles[tail + 4] = accent + 4;
        triangles[tail + 5] = accent + 5;
        triangles[tail + 6] = accent + 6;
        triangles[tail + 7] = accent + 7;
        triangles[tail + 8] = accent + 8;
        detailMesh = new Mesh { name = "Leaf Wisp lens reticle" };
        detailMesh.vertices = vertices;
        detailMesh.triangles = triangles;
        detailMesh.RecalculateNormals();
        frame.GetComponent<MeshFilter>().sharedMesh = detailMesh;
        frame.GetComponent<MeshRenderer>().sharedMaterial = lensRenderer.sharedMaterial;
        frame.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private bool CanStartAttack()
    {
        if (!awareness.Ready || !awareness.Visible) return false;
        if (kind == Kind.LeafWisp && hasWispShotPosition && wispRetreatRest <= 0f &&
            Vector3.Distance(transform.position, wispShotPosition) > .18f)
            return false; // Finish the bounded escape/cover move, not the first barely-clear corner.
        Vector3 toPlayer = player.transform.position - transform.position;
        float distance = new Vector2(toPlayer.x, toPlayer.z).magnitude;
        float range = kind == Kind.RuinPrism ? pulseRadius - 0.35f : noticeRange;
        return distance < range && Mathf.Abs(toPlayer.y) < 2f &&
            (kind == Kind.RuinPrism ? HasPrismLineOfSight() : HasClearShot());
    }

    private bool HasClearShot()
    {
        Vector3 start = muzzle ? muzzle.position : transform.position + Vector3.up;
        return ClearLineToPlayer(start);
    }

    private bool HasPrismLineOfSight()
    {
        return ClearLineToPlayer(transform.position + Vector3.up * 1.15f);
    }

    private bool ClearLineToPlayer(Vector3 start)
    {
        if (!player) return false;
        Vector3 goal = player.transform.position + Vector3.up * 0.9f;
        Vector3 ray = goal - start;
        if (ray.sqrMagnitude < .001f) return true;
        bool ranged = kind == Kind.LeafWisp;
        // Small aiming clearance only, not a larger damage radius. It tolerates
        // frame-to-frame muzzle rotation and sweep precision at distant map coordinates.
        float clearance = wispShotRadius + .03f;
        if (ranged)
        {
            // SphereCast omits starting overlaps. Match the projectile's initial
            // overlap guard as well as its actual swept width around cover edges.
            int overlap = Physics.OverlapSphereNonAlloc(start, clearance, moveBlockers, ~0, QueryTriggerInteraction.Ignore);
            if (overlap == moveBlockers.Length) return false;
            for (int i=0; i<overlap; i++)
                if (moveBlockers[i] && !moveBlockers[i].transform.IsChildOf(transform) &&
                    moveBlockers[i].GetComponentInParent<PlayerVitals>() != player) return false;
        }
        int count = ranged ? Physics.SphereCastNonAlloc(start, clearance, ray.normalized, sightHits, ray.magnitude,
            ~0, QueryTriggerInteraction.Ignore) : Physics.RaycastNonAlloc(start, ray.normalized, sightHits, ray.magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == sightHits.Length
            ? ranged ? Physics.SphereCastAll(start, clearance, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore) :
                Physics.RaycastAll(start, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore)
            : sightHits;
        if (count == sightHits.Length) count = hits.Length;
        float nearest = float.MaxValue;
        Collider first = null;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            first = hit.collider;
        }
        return first && first.GetComponentInParent<PlayerVitals>() == player;
    }

    private void UpdateWarning()
    {
        if(kind == Kind.LeafWisp)
        {
            // Charge/lock is communicated by the compact eye iris, posture and
            // sound. A continuous line looked like an already-active beam.
            if(warningLine) warningLine.enabled=false;
            return;
        }
        if (kind == Kind.RuinPrism && !plateCuePlayed && timer <= AttackWindup * .65f)
        { plateCuePlayed = true; if (sound) sound.Cue(EnemySoundCue.PlateOpen, .7f); }
        if (!warningLine || !player) return;
        DrawRing(pulseRadius);
        float flash = 0.55f + Mathf.PingPong(clock * 3f, 0.45f);
        Color color = new Color(PrismAccent.r, PrismAccent.g, PrismAccent.b, flash);
        warningLine.startColor = warningLine.endColor = color;
    }

    private void LockAim()
    {
        if (!player) return;
        bool newlyLocked=!aimLocked;
        lockedAimPosition = player.transform.position + Vector3.up * 0.9f;
        aimLocked = true;
        if(kind == Kind.LeafWisp && newlyLocked)
        {
            ApplyPrismPalette(); // Core and iris confirm the lock on the same frame.
            if(wispPresentation) wispPresentation.LockAim();
            if(sound) sound.Cue(EnemySoundCue.CoreLock,.9f,seconds:aimLockLeadSeconds);
        }
    }

    private void CreatePulseFill()
    {
        if (!warningLine || !warningLine.sharedMaterial) return;
        GameObject fill = new GameObject("Pulse warning fill");
        fill.transform.SetParent(transform, false);
        MeshFilter filter = fill.AddComponent<MeshFilter>();
        pulseFillMesh = new Mesh { name = "Terrain-following pulse warning" };
        const int segments = RingSegments;
        Vector3[] vertices = pulseFillVertices = new Vector3[1 + segments * 2];
        int[] triangles = new int[segments * 9];
        // Hidden until BeginWarning, when cover/floor data is measured once.
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int t = i * 9;
            triangles[t] = 0;
            triangles[t + 1] = 1 + next;
            triangles[t + 2] = 1 + i;
            triangles[t + 3] = 1 + i;
            triangles[t + 4] = 1 + segments + next;
            triangles[t + 5] = 1 + segments + i;
            triangles[t + 6] = 1 + i;
            triangles[t + 7] = 1 + next;
            triangles[t + 8] = 1 + segments + next;
        }
        pulseFillMesh.vertices = vertices;
        pulseFillMesh.MarkDynamic();
        pulseFillMesh.triangles = triangles;
        pulseFillMesh.RecalculateNormals();
        filter.sharedMesh = pulseFillMesh;
        pulseFillRenderer = fill.AddComponent<MeshRenderer>();
        pulseFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pulseFillRenderer.receiveShadows = false;
        pulseFillMaterial = new Material(warningLine.sharedMaterial);
        pulseFillMaterial.name = "Pulse warning fill (runtime)";
        Color tint = PrismAccent;
        tint.a = 0.24f;
        if (pulseFillMaterial.HasProperty("_BaseColor")) pulseFillMaterial.SetColor("_BaseColor", tint);
        if (pulseFillMaterial.HasProperty("_Color")) pulseFillMaterial.SetColor("_Color", tint);
        pulseFillMaterial.SetFloat("_Surface", 1f);
        pulseFillMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        pulseFillMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        pulseFillMaterial.SetFloat("_ZWrite", 0f);
        pulseFillMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        pulseFillMaterial.SetOverrideTag("RenderType", "Transparent");
        pulseFillMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        pulseFillRenderer.sharedMaterial = pulseFillMaterial;
        pulseFillRenderer.enabled = false;
    }

    private void RefreshPulseFill()
    {
        if (!pulseFillMesh) return;
        Vector3[] vertices = pulseFillVertices;
        vertices[0] = transform.InverseTransformPoint(CachedPulsePoint(0, 0f, .07f));
        for (int ring = 0; ring < 2; ring++)
        for (int i = 0; i < RingSegments; i++)
        {
            float radius = pulseSectorLimits[i] * (ring == 0 ? 0.5f : 1f);
            vertices[1 + ring * RingSegments + i] = transform.InverseTransformPoint(CachedPulsePoint(i, radius, .07f));
        }
        pulseFillMesh.vertices = vertices;
        // The warning material is unlit; normal regeneration is unnecessary.
        pulseFillMesh.RecalculateBounds();
    }

    private Vector3 CachedPulsePoint(int sector, float radius, float lift)
    {
        float angle = sector * Mathf.PI * 2f / RingSegments;
        Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 point = pulseCentre + direction * radius;
        int offset = sector * PulseSamples, count = pulseSampleCounts[sector];
        if (!pulseCacheReady || count < 1) return point + Vector3.up * lift;
        // Use the sampled floor's plane, not a line through empty air on slopes.
        int index = offset;
        for (int i = 1; i < count; i++)
            if (pulseSampleDistances[offset + i] <= radius) index = offset + i; else break;
        Vector3 normal = pulseSampleNormals[index];
        float along = radius - pulseSampleDistances[index];
        point.y = pulseSampleHeights[index] - Vector3.Dot(normal, direction * along) / Mathf.Max(.01f, normal.y);
        return point + Vector3.up * lift;
    }

    private void DrawRing(float radius)
    {
        if (!warningLine) return;
        bool moved = !pulseCacheReady || pulseRootMatrix != transform.localToWorldMatrix;
        foreach (var floor in pulseFloorFrames)
            if (!floor.frame || floor.frame.localToWorldMatrix != floor.matrix) { moved = true; break; }
        if (moved) { MeasurePulseCover(); RefreshPulseFill(); }
        warningLine.positionCount = RingSegments + 1;
        for (int i = 0; i < RingSegments; i++)
        {
            pulseRingPoints[i] = CachedPulsePoint(i, Mathf.Min(radius, pulseSectorLimits[i]), .09f);
        }
        pulseRingPoints[RingSegments] = pulseRingPoints[0];
        warningLine.SetPositions(pulseRingPoints);
    }

    private void MeasurePulseCover()
    {
        pulseCentre = transform.position;
        pulseRootMatrix = transform.localToWorldMatrix; pulseFloorFrames.Clear();
        Vector3 start = transform.position + Vector3.up * 1.15f;
        RaycastHit centreFloor = default;
        bool supported = groundQuery != null && groundQuery.TrySample(
            transform.position - Vector3.up * groundOffset, .35f, 1.6f, 27f, out centreFloor);
        PulseGroundSamples++;
        if (supported) RememberPulseFloor(centreFloor.collider.transform);
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / RingSegments;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int count = Physics.RaycastNonAlloc(start, direction, sightHits, pulseRadius,
                ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = count == sightHits.Length
                ? Physics.RaycastAll(start, direction, pulseRadius, ~0, QueryTriggerInteraction.Ignore)
                : sightHits;
            if (count == sightHits.Length) count = hits.Length;
            float limit = pulseRadius;
            for (int j = 0; j < count; j++)
            {
                Collider obstacle = hits[j].collider;
                if (!obstacle || obstacle.transform.IsChildOf(transform) ||
                    obstacle.GetComponentInParent<PlayerVitals>()) continue;
                limit = Mathf.Min(limit, hits[j].distance - .12f);
            }
            // A ground wave cannot bridge a cliff, water or a different platform layer.
            float previous = 0f;
            RaycastHit previousFloor = centreFloor;
            int offset = i * PulseSamples;
            pulseSampleCounts[i] = 1;
            pulseSampleDistances[offset] = 0f;
            pulseSampleHeights[offset] = supported ? centreFloor.point.y : transform.position.y - groundOffset;
            pulseSampleNormals[offset] = supported ? centreFloor.normal : Vector3.up;
            for (int j = 1; j <= 12; j++)
            {
                float distance = Mathf.Min(limit, pulseRadius * j / 12f);
                Vector3 point = transform.position + direction * distance - Vector3.up * groundOffset;
                PulseGroundSamples++;
                if (!supported || !groundQuery.TrySample(point, .35f, 1.6f, 27f, out RaycastHit floor))
                { limit = previous; break; }
                float expectedY = previousFloor.point.y - Vector3.Dot(previousFloor.normal,
                    direction * (distance - previous)) / previousFloor.normal.y;
                if (Mathf.Abs(floor.point.y - expectedY) > .15f)
                { limit = previous; break; }
                previousFloor = floor;
                RememberPulseFloor(floor.collider.transform);
                previous = distance;
                int sample = offset + pulseSampleCounts[i]++;
                pulseSampleDistances[sample] = distance;
                pulseSampleHeights[sample] = floor.point.y;
                pulseSampleNormals[sample] = floor.normal;
                if (distance >= limit) break;
            }
            pulseSectorLimits[i] = Mathf.Max(0.12f, limit);
        }
        pulseCacheReady = true;
    }

    private void RememberPulseFloor(Transform frame)
    {
        foreach (var existing in pulseFloorFrames) if (existing.frame == frame) return;
        pulseFloorFrames.Add(new PulseFloorFrame { frame = frame, matrix = frame.localToWorldMatrix });
    }

    private void TryPulseHit(float previousRadius, float currentRadius)
    {
        if (state == State.Dead || pulseHitPlayer || !player || !playerHealth || playerHealth.IsDead ||
            player.gameObject.scene != gameObject.scene ||
            !player.EnemyCombatActive) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        // Ground waves are not tall damage cylinders. Use actual capsule contact, not
        // the movement probe/grace period which may remain grounded briefly off a ledge.
        if (!controller || !controller.enabled || !controller.isGrounded) return;
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement && (movement.SwimmingActive || movement.ClimbingActive || movement.MantlingActive)) return;
        Vector3 diff = player.transform.position - transform.position;
        if (Mathf.Abs(diff.y) > 1.6f) return;
        float flatDistance = new Vector2(diff.x, diff.z).magnitude;
        float targetRadius = controller ? controller.radius * Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x),
            Mathf.Abs(controller.transform.lossyScale.z)) : 0.3f;
        float angle = Mathf.Atan2(diff.z, diff.x);
        if (angle < 0f) angle += Mathf.PI * 2f;
        float previousFront = WaveRadiusAt(angle, previousRadius);
        float currentFront = WaveRadiusAt(angle, currentRadius);
        // Only the advancing wavefront deals damage. Entering a space after it passes is safe.
        if (flatDistance - targetRadius > currentFront ||
            flatDistance + targetRadius < previousFront) return;
        if (!HasPrismLineOfSight()) return;
        pulseHitPlayer = true;
        Vector3 point = controller ? controller.bounds.center :
            player.transform.position + Vector3.up;
        if (playerHealth.TryApplyDamage(new DamageInfo(damage, gameObject, point, diff)))
        {
            Camera camera = Camera.main;
            ThirdPersonCamera follow = camera ? camera.GetComponent<ThirdPersonCamera>() : null;
            if (follow) follow.RequestShake(.07f, .12f);
        }
    }

    private float WaveRadiusAt(float angle, float radius)
    {
        // Use the same line segments as DrawRing, not an unrelated circular damage disk.
        float sector = angle * RingSegments / (Mathf.PI * 2f);
        int i = Mathf.FloorToInt(sector) % RingSegments, next = (i + 1) % RingSegments;
        float a = i * Mathf.PI * 2f / RingSegments, b = (i + 1) * Mathf.PI * 2f / RingSegments;
        Vector2 first = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Min(radius, pulseSectorLimits[i]);
        Vector2 second = new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * Mathf.Min(radius, pulseSectorLimits[next]);
        Vector2 edge = second - first, direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float denominator = direction.x * edge.y - direction.y * edge.x;
        return Mathf.Abs(denominator) < .000001f ? 0f :
            Mathf.Max(0f, (first.x * edge.y - first.y * edge.x) / denominator);
    }

    private void Fire()
    {
        if (warningLine) warningLine.enabled = kind == Kind.RuinPrism;
        if (pulseFillRenderer) pulseFillRenderer.enabled = false;
        state = State.Recover;
        timer = AttackCooldown;
        recoilVisualTime = 0.23f;
        if (kind == Kind.RuinPrism)
        {
            MeasurePulseCover(); // Recheck moved cover before the actual release.
            pulseVisualTime = PulseDuration;
            DrawRing(.08f); // First release frame starts at the source, not the old warning radius.
            prismExposedTime = PulseDuration + .5f;
            pulseHitPlayer = false;
            PlayPrismCue(true);
        }
        if (!player || !player.EnemyCombatActive || playerHealth.IsDead) return;
        if (kind == Kind.LeafWisp)
        {
            if (!seedProjectile) return;
            if (!aimLocked) LockAim();
            Vector3 start = muzzle ? muzzle.position : transform.position + Vector3.up;
            Vector3 direction = (lockedAimPosition - start).normalized;
            var dart = EnemyTransientPool.For(gameObject.scene)?.Spawn(seedProjectile, gameObject, start, direction, damage);
            if (dart)
            {
                RangedShots++;
                wispRecoilDirection = -direction;
                if (wispPresentation) wispPresentation.Fire();
                PlayPrismCue(true);
            }
            aimLocked = false;
        }
    }

    private void CancelWarning()
    {
        if(wispPresentation) wispPresentation.ClearAim();
        if (warningLine) warningLine.enabled = false;
        if (pulseFillRenderer) pulseFillRenderer.enabled = false;
        pulseVisualTime = 0f;
        pulseHitPending = false;
        prismExposedTime = 0f;
        aimLocked = false;
        if (chargeCore) chargeCore.localScale = Vector3.zero;
        if (sound) sound.StopWarning();
        state = State.Recover;
        timer = 0.35f;
    }

    private void OnDamaged(Health target, DamageInfo info)
    {
        if (target.IsDead) return;
        hitVisualTime = 0.32f;
        if (kind == Kind.LeafWisp)
        {
            wispHitDirection = Vector3.ProjectOnPlane(info.HitDirection, Vector3.up).normalized;
            hasWispShotPosition = false; wispRetreatRest = .65f;
        }
        if (kind == Kind.RuinPrism)
        {
            // The first sword hit breaks a charge, but a five-hit combo cannot stun-lock
            // a stone sentry indefinitely. Later hits still show the normal hit feedback.
            if (prismStaggerLockout > 0f) return;
            prismStaggerLockout = 2.8f;
        }
        CancelWarning();
        state = State.Staggered;
        timer = kind == Kind.RuinPrism ? 0.24f : 0.36f;
    }

    private void OnDied(Health target, DamageInfo info)
    {
        navigation.Clear();
        DomainSeedProjectile.ClearOwned(gameObject);
        state = State.Dead;
        if (warningLine) warningLine.enabled = false;
        if (pulseFillRenderer) pulseFillRenderer.enabled = false;
        pulseVisualTime = 0f;
        pulseHitPending = false;
        prismExposedTime = 0f;
        aimLocked = false;
        deathVisualTime = 0f;
        if (kind == Kind.LeafWisp)
        {
            wispLanded = false; deathGroundY = transform.position.y;
            if (groundQuery.TrySample(transform.position, .5f, 2f, 35f, out RaycastHit floor)) deathGroundY = floor.point.y;
        }
        ApplyPrismPalette(); // Extinguish before the collapse, even on a lethal hit mid-charge.
        if (sound) sound.StopWarning(); // Preserve the emitter's actual shutdown sound.
        if (chargeCore) chargeCore.localScale = Vector3.zero;
        if (body) body.enabled = false;
        if (kind == Kind.RuinPrism && info.Source)
        {
            PlayerVitals victor = info.Source.GetComponentInParent<PlayerVitals>();
            if (victor) victor.TryGrantPrismAid();
        }
    }

    public void ResetForJourney()
    {
        awareness?.Clear();
        hasWispShotPosition = false; wispDecisionTime = wispBlockedTime = 0f;
        wispRetreatRest = 0f; wispLanded = false; RangedShots = 0;
        wispHitDirection = wispRecoilDirection = Vector3.zero;
        if (wispPresentation) wispPresentation.Clear();
        navigation.Clear();
        DomainSeedProjectile.ClearOwned(gameObject);
        if (loot) loot.ClearDrop();
        if (feedback) feedback.ClearFeedback();
        if (sound) sound.ResetForJourney();
        transform.SetPositionAndRotation(home, homeRotation);
        health.RestoreFullHealth();
        if (body) body.enabled = true;
        if (visualRoot)
        {
            visualRoot.gameObject.SetActive(true);
            visualRoot.localPosition = visualHome;
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = visualHomeScale;
        }
        if (leftWing) leftWing.localRotation = leftWingHome;
        if (rightWing) rightWing.localRotation = rightWingHome;
        if (lens) lens.localScale = lensHomeScale;
        if (lensFrame) lensFrame.localScale = Vector3.one;
        if (chargeCore) chargeCore.localScale = Vector3.zero;
        if (prismCore) prismCore.localScale = prismCoreHomeScale;
        for (int i = 0; i < prismPetals.Length; i++)
        {
            if (prismPetals[i]) prismPetals[i].localPosition = prismPetalHomePositions[i];
            if (prismWarningLenses[i])
                prismWarningLenses[i].localScale = prismWarningLensHomeScales[i];
        }
        if (warningLine) warningLine.enabled = false;
        if (pulseFillRenderer) pulseFillRenderer.enabled = false;
        pulseVisualTime = 0f;
        pulseHitPending = false;
        prismExposedTime = 0f;
        pulseHitPlayer = false;
        prismSpin = 0f;
        aimLocked = false;
        hasAlerted = false;
        hitVisualTime = recoilVisualTime = deathVisualTime = lostSightTime = strafeClock =
            prismStaggerLockout = 0f;
        state = State.Waiting;
        timer = 0.5f;
        ApplyPrismPalette();
    }
}
