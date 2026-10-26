using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class RepairWispAI : MonoBehaviour
{
    public enum Phase { Follow, Approach, Prepare, Repair, Cooldown, Stagger, Exhausted, Dead }
    [SerializeField] private RepairWispGroup group;
    [SerializeField] private RepairWispVisual visual;
    [SerializeField] private LineRenderer energyLine;
    [SerializeField] private float repairRange = 4f;
    [SerializeField] private float prepareSeconds = .35f;
    [SerializeField] private float repairSeconds = .75f;
    [SerializeField] private float cooldownSeconds = 4f;
    [SerializeField, Min(.1f)] private float failedRetrySeconds = .65f;
    [SerializeField, Range(.1f, .95f)] private float repairTriggerRatio = .85f;
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float combatSpeed = 3.2f;
    [SerializeField] private float encounterRadius = 9f;
    [SerializeField] private int healAmount = 18;
    [SerializeField] private int maximumRepairs = 3;
    private Health health, target, playerHealth;
    private EnemyAwareness awareness;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private PlayerVitals player;
    private BaselineFlow flow;
    private EnemyGroundQuery groundQuery;
    private StoneScoutLootDrop loot;
    private Vector3 home, followOffset;
    private Quaternion homeRotation;
    private float timer, nextDecision, retryAfter, nextPlayerSearch, approachTime;
    private bool suspended, combat;
    private MaterialPropertyBlock energyBlock;
    private EnemySoundEmitter sound;
    private bool emptyCuePlayed;
    private readonly EnemyNavigationRoute route = new EnemyNavigationRoute();
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];
    public Phase CurrentPhase { get; private set; }
    public int RemainingRepairs { get; private set; }
    public Health RepairTarget => target;
    public int SuccessfulRepairs { get; private set; }
    public bool BeamVisible => energyLine && energyLine.enabled;
    public LineRenderer RepairLine => energyLine;
    public bool CombatAvailable => player && player.gameObject.scene == gameObject.scene &&
        playerHealth && !playerHealth.IsDead && player.EnemyCombatActive;
    public float PhaseProgress => CurrentPhase == Phase.Prepare ? 1f - timer / prepareSeconds :
        CurrentPhase == Phase.Repair ? 1f - timer / repairSeconds : 0f;

    private void Awake()
    {
        health = GetComponent<Health>(); body = GetComponent<Rigidbody>(); capsule = GetComponent<CapsuleCollider>();
        awareness = EnemyAwareness.Ensure(this);
        loot = GetComponent<StoneScoutLootDrop>();
        home = body.position; homeRotation = body.rotation;
        groundQuery = new EnemyGroundQuery(transform);
        foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        if (player) playerHealth = player.GetComponent<Health>();
        RemainingRepairs = Mathf.Max(0, maximumRepairs);
        followOffset = new Vector3(1.7f, 0f, -1.6f); SetBeam(false);
        EnemyImpactVfx.Ensure(gameObject);
        sound = GetComponent<EnemySoundEmitter>();
        if (energyLine && EnemyImpactVfx.SharedEnergyMaterial)
        {
            energyLine.sharedMaterial = EnemyImpactVfx.SharedEnergyMaterial;
            energyLine.textureMode = LineTextureMode.Stretch;
            energyBlock = new MaterialPropertyBlock(); energyBlock.SetFloat("_ImpactRadius",-1f);
            energyLine.SetPropertyBlock(energyBlock);
        }
    }
    private void OnEnable()
    {
        if (!health) health = GetComponent<Health>();
        health.Damaged += OnDamaged; health.Died += OnDied;
        flow = FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += ResetForJourney;
    }
    private void OnDisable()
    {
        if (health) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        if (flow) flow.WorldEncountersReset -= ResetForJourney;
        SetBeam(false); target = null; route.Clear();
        GetComponent<RepairWispSupportVfx>()?.Clear();
        if (sound) sound.StopAll();
    }
    private void FixedUpdate()
    {
        if (health.IsDead) return;
        if (!player && Time.time >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.time + 1f;
            foreach (var candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
            if (player) playerHealth = player.GetComponent<Health>();
        }
        if (!CombatAvailable)
        {
            if(awareness.CanRetainEncounter){awareness.Suspend();return;}
            if (!suspended) { CancelRepair(); route.Clear(); suspended = true; }
            return;
        }
        suspended = false;
        if (CurrentPhase == Phase.Stagger)
        { timer -= Time.fixedDeltaTime; if (timer <= 0) EnterRest(); return; }
        Health leader = group ? group.Leader : null;
        awareness.Tick(player, Flat(player.transform.position-body.position)<17.4f &&
            ClearLine(visual ? visual.CorePosition : body.position + Vector3.up, player.transform.position + Vector3.up, null, true),12f,Time.fixedDeltaTime);
        // Only its explicit guard group can pass an alarm; no global map-wide aggro.
        bool guardAlarm=group && group.Alerted;
        if (!guardAlarm && group)
            foreach(var guard in group.Guards)
            {
                var perception=guard?guard.GetComponent<EnemyAwareness>():null;
                if(perception && perception.Ready && Flat(guard.transform.position-body.position)<9f)
                {guardAlarm=true;break;}
            }
        if(guardAlarm && !awareness.Engaged)awareness.Confirm(player);
        combat = leader && (guardAlarm || awareness.Ready);
        if (CurrentPhase == Phase.Prepare || CurrentPhase == Phase.Repair)
        {
            // Commit only if still alive, injured, in this group, visible and within both leashes.
            if (!combat || !ValidTarget(target) || !InRepairPosition(target)) { CancelRepair(); return; }
            if (CurrentPhase == Phase.Prepare) Face(target.transform.position);
            timer -= Time.fixedDeltaTime;
            if (CurrentPhase == Phase.Prepare && timer <= 0f)
            {
                CurrentPhase = Phase.Repair; timer = repairSeconds; SetBeam(true);
                if (sound) sound.Cue(EnemySoundCue.RepairLink, .85f);
            }
            else if (CurrentPhase == Phase.Repair && timer <= 0f)
            {
                if (ValidTarget(target) && InRepairPosition(target) && target.TryHeal(healAmount))
                {
                    RemainingRepairs--; SuccessfulRepairs++;
                    if (visual) visual.CompletedRepair();
                    if (sound) sound.Cue(EnemySoundCue.RepairSuccess);
                    if (RemainingRepairs == 0 && !emptyCuePlayed)
                    {
                        emptyCuePlayed = true;
                        if (sound) sound.QueueCue(EnemySoundCue.RepairEmpty, .52f, .8f);
                    }
                    CancelRepair(true);
                }
                else CancelRepair();
            }
            return;
        }
        if (CurrentPhase == Phase.Cooldown)
        {
            timer -= Time.fixedDeltaTime;
            if (timer <= 0f) EnterRest();
            else { Follow(leader); return; }
        }
        if (RemainingRepairs <= 0)
        {
            CurrentPhase = Phase.Exhausted;
            if (!emptyCuePlayed)
            { emptyCuePlayed = true; if (sound) sound.Cue(EnemySoundCue.RepairEmpty, .8f); }
            Follow(leader); return;
        }
        if (Time.time >= nextDecision)
        {
            nextDecision = Time.time + .25f;
            target = combat && Time.time >= retryAfter ? SelectTarget() : null;
        }
        if (ValidTarget(target) && combat)
        {
            if (InRepairPosition(target))
            {
                CurrentPhase = Phase.Prepare; timer = prepareSeconds; Face(target.transform.position);
                if (sound) sound.Cue(EnemySoundCue.RepairPrepare, .85f);
            }
            else
            {
                if (CurrentPhase != Phase.Approach) approachTime = 0f;
                approachTime += Time.fixedDeltaTime;
                if (approachTime > 4f) { CancelRepair(); return; }
                CurrentPhase = Phase.Approach;
                // Stop near the target's rear flank, never try to move through its body.
                Vector3 offset = body.position - target.transform.position; offset.y = 0f;
                if (offset.sqrMagnitude < .01f) offset = -target.transform.forward;
                Move(target.transform.position + offset.normalized * 2.5f, combatSpeed);
            }
        }
        else { CurrentPhase = Phase.Follow; Follow(leader); }
    }
    private void LateUpdate()
    {
        // Menus stop FixedUpdate at timeScale=0, but must remove an active link immediately.
        // Sword hit-stop is allowed by EnemyCombatActive and must not cancel a valid channel.
        if (!health.IsDead && !CombatAvailable)
        {
            if(awareness.CanRetainEncounter)
            {
                awareness.Suspend();GetComponent<RepairWispSupportVfx>()?.Clear();return;
            }
            if (!suspended) { CancelRepair(); route.Clear(); suspended = true; }
            GetComponent<RepairWispSupportVfx>()?.Clear();
        }
        if (!BeamVisible || !target || health.IsDead) return;
        // No ray-through-wall line for even one rendered frame after a moving blocker appears.
        if (!ValidTarget(target) || !InRepairPosition(target)) { CancelRepair(); return; }
        energyLine.SetPosition(0, visual.CorePosition); energyLine.SetPosition(1, TargetPoint(target));
        energyLine.widthMultiplier = .05f + .012f * (.5f + .5f * Mathf.Sin(Time.time * 14f));
        Color repair = new Color(.65f, .95f, .15f, .9f);
        energyLine.startColor = energyLine.endColor = repair;
        if (energyBlock != null)
        {
            energyBlock.SetFloat("_LinkPhase",Time.time * 2f);
            energyLine.SetPropertyBlock(energyBlock);
        }
    }
    private Health SelectTarget()
    {
        if (!group) return null;
        Health best = null; float bestRatio = 1f;
        // Keep an acquired target; switching every frame makes the behaviour unreadable.
        if (ValidTarget(target)) return target;
        foreach (Health guard in group.Guards)
        {
            if (!ValidTarget(guard) || guard.NormalizedHealth > repairTriggerRatio ||
                Flat(guard.transform.position - body.position) > 7f) continue;
            if (guard.NormalizedHealth < bestRatio) { best = guard; bestRatio = guard.NormalizedHealth; }
        }
        return best;
    }
    private bool ValidTarget(Health guard) => group && group.Contains(guard) && RepairWispGroup.Eligible(guard) &&
        guard.CurrentHealth < guard.MaxHealth && Flat(guard.transform.position - home) < encounterRadius &&
        guard.gameObject.scene == gameObject.scene;
    private bool InRepairPosition(Health guard) => Flat(guard.transform.position - body.position) <= repairRange &&
        Flat(body.position - home) <= encounterRadius &&
        Mathf.Abs(guard.transform.position.y - body.position.y) <= .65f &&
        (!player || Mathf.Abs(player.transform.position.y - body.position.y) <= 1.1f) &&
        ClearLine(visual.CorePosition, TargetPoint(guard), guard, false);
    private static Vector3 TargetPoint(Health guard)
    {
        CapsuleCollider c = guard.GetComponent<CapsuleCollider>();
        return c ? c.transform.TransformPoint(c.center) : guard.transform.position + Vector3.up * .9f;
    }
    private bool ClearLine(Vector3 start, Vector3 end, Health recipient, bool playerLine)
    {
        Vector3 travel = end - start; float length = travel.magnitude;
        if (length < .01f) return true;
        int n = Physics.RaycastNonAlloc(start, travel / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        if (n == hits.Length) return false; // Dense scenes fail closed, not through-wall.
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (!c || c.transform.IsChildOf(transform)) continue;
            Health actor = c.GetComponentInParent<Health>();
            if (actor == recipient || (playerLine && actor && actor.Team == HealthTeam.Player)) continue;
            return false;
        }
        return true;
    }
    private void Follow(Health leader)
    {
        Vector3 destination = home;
        if (leader)
        {
            // Stable world-space flank; obstacle detours are supplied by the shared route.
            destination = leader.transform.position + followOffset;
            Vector3 fromHome = destination - home; fromHome.y = 0;
            float floorHeight = destination.y;
            destination = home + Vector3.ClampMagnitude(fromHome, encounterRadius - .5f); destination.y = floorHeight;
        }
        if (Flat(destination - body.position) > .6f) Move(destination, combat ? combatSpeed : moveSpeed);
    }
    private void Move(Vector3 destination, float speed)
    {
        Vector3 offset = destination - home; offset.y = 0;
        float floorHeight = destination.y;
        destination = home + Vector3.ClampMagnitude(offset, encounterRadius - .5f); destination.y = floorHeight;
        if (!route.Direction(gameObject.scene, EnemyNavigationWorld.Profile.LeafWisp,
            body.position, destination, Time.fixedDeltaTime, out Vector3 direction)) return;
        Vector3 candidate = body.position + direction * Mathf.Min(speed * Time.fixedDeltaTime, Flat(destination - body.position));
        if (!TryFloor(candidate, out Vector3 floor) || Mathf.Abs(floor.y - body.position.y) > .22f ||
            Flat(floor - home) > encounterRadius || !StepClear(floor))
        { route.Blocked(body.position, direction); return; }
        body.MovePosition(floor); Face(floor + direction);
    }
    private bool TryFloor(Vector3 point, out Vector3 floor)
    {
        floor = default;
        float size = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        if (!groundQuery.TryFootprint(point - Vector3.up * .015f, body.rotation,
            Vector2.one * (capsule.radius * size), 27f, .35f, .6f, out float y, true)) return false;
        floor = new Vector3(point.x, y + .015f, point.z);
        return true;
    }
    private bool StepClear(Vector3 candidate)
    {
        Vector3 start = body.position + capsule.center, travel = candidate - body.position;
        float half = Mathf.Max(0, capsule.height * .5f - capsule.radius);
        int n = Physics.CapsuleCastNonAlloc(start + Vector3.up * half, start - Vector3.up * half,
            capsule.radius + .015f, travel.normalized, hits, travel.magnitude + .015f, ~0, QueryTriggerInteraction.Ignore);
        if (n == hits.Length) return false;
        for (int i = 0; i < n; i++)
            if (!(groundQuery.IsSurface(hits[i].collider) && hits[i].normal.y >= .891f) &&
                Blocks(hits[i].collider, candidate)) return false;
        Vector3 center = candidate + capsule.center;
        n = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * half, center - Vector3.up * half,
            capsule.radius + .01f, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (n == overlaps.Length) return false;
        for (int i = 0; i < n; i++) if (Blocks(overlaps[i], candidate)) return false;
        return true;
    }
    private bool Blocks(Collider c, Vector3 feet) => c && !c.transform.IsChildOf(transform) &&
        !groundQuery.AllowsSupportContact(c, feet);
    private void Face(Vector3 point)
    {
        Vector3 direction = point - body.position; direction.y = 0;
        if (direction.sqrMagnitude > .01f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction), 240f * Time.fixedDeltaTime));
    }
    private void SetBeam(bool enabled)
    {
        if (!energyLine) return;
        if (enabled && target && visual)
        { energyLine.SetPosition(0, visual.CorePosition); energyLine.SetPosition(1, TargetPoint(target)); }
        energyLine.enabled = enabled;
    }
    private void CancelRepair(bool completed = false)
    {
        // No failure sound on a successful heal, idle retry, menu or target search.
        if (!completed && CombatAvailable && (CurrentPhase == Phase.Prepare || CurrentPhase == Phase.Repair))
            if (sound) sound.Cue(EnemySoundCue.RepairBreak, .85f);
        float delay = completed ? cooldownSeconds : failedRetrySeconds;
        SetBeam(false); target = null; nextDecision = 0f; retryAfter = Time.time + delay;
        if (!health || health.IsDead) return;
        CurrentPhase = RemainingRepairs <= 0 ? Phase.Exhausted : Phase.Cooldown; timer = delay;
    }
    private void EnterRest() { CurrentPhase = RemainingRepairs <= 0 ? Phase.Exhausted : Phase.Follow; target = null; }
    private void OnDamaged(Health actor, DamageInfo damage)
    {
        if (actor.IsDead) return;
        CancelRepair(); CurrentPhase = Phase.Stagger; timer = .35f;
        if (group) group.Alert(); if (visual) visual.Hit(damage.HitDirection);
    }
    private void OnDied(Health actor, DamageInfo damage)
    {
        SetBeam(false); target = null; CurrentPhase = Phase.Dead; capsule.enabled = false;
        route.Clear(); if (visual) visual.Die();
    }
    public void ConfigureGroup(RepairWispGroup value) { group = value; target = null; route.Clear(); }
    public void ResetForJourney()
    {
        awareness?.Clear();
        emptyCuePlayed = false;
        if (sound) sound.ResetForJourney();
        SetBeam(false); target = null; route.Clear();
        if (loot) loot.ClearDrop(); health.RestoreFullHealth(); capsule.enabled = true;
        CombatFeedback feedback = GetComponent<CombatFeedback>(); if (feedback) feedback.ClearFeedback();
        body.position = home; body.rotation = homeRotation;
        RemainingRepairs = maximumRepairs; SuccessfulRepairs = 0; timer = nextDecision = retryAfter = 0f;
        suspended = combat = false; CurrentPhase = Phase.Follow;
        if (group) group.ClearAlert(); if (visual) visual.ResetVisual();
    }
    private static float Flat(Vector3 value) { value.y = 0; return value.magnitude; }
}
