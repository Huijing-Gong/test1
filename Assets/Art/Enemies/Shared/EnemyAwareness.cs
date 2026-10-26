using UnityEngine;

// Perception is shared; each species still owns its movement, attacks and safe navigation.
// Tick only from the owning AI's simulation clock (never from an independent Update).
[DisallowMultipleComponent]
public sealed class EnemyAwareness : MonoBehaviour
{
    public enum AwarenessState { Unaware, Suspicious, Alerted, Searching }
    [SerializeField, Range(30f, 100f)] float visionHalfAngle = 68f;
    [SerializeField, Min(.1f)] float closeRecognitionSeconds = .32f;
    [SerializeField, Min(.1f)] float distantRecognitionSeconds = 3.8f;
    [SerializeField, Min(.1f)] float searchSeconds = 3.8f;
    MonoBehaviour owner;
    Health health;
    BaselineFlow flow;
    Collider shape;
    PlayerVitals player;
    EnemyAwarenessIndicator indicator;
    float evidence, unseen, confirmedAge, suspectHold;
    public AwarenessState State { get; private set; }
    public float Evidence => evidence;
    public Vector3 InterestPosition { get; private set; }
    public bool Visible { get; private set; }
    public bool Engaged => State == AwarenessState.Alerted || State == AwarenessState.Searching;
    public bool Ready => Engaged && confirmedAge >= .35f;
    public bool Curious => State == AwarenessState.Suspicious || State == AwarenessState.Searching;
    public int ConfirmationCount { get; private set; }
    public float ConfirmationAge => confirmedAge;
    public bool DisplayAllowed => owner && owner.isActiveAndEnabled && health && !health.IsDead &&
        player && player.gameObject.scene == gameObject.scene && player.EnemyCombatActive &&
        player.GetComponent<Health>() && !player.GetComponent<Health>().IsDead;
    // A modal is not a lost target. Retain memory only within the same live world;
    // actual travel, title/death/reset/disabled actors still cancel the encounter.
    public bool CanRetainEncounter => owner && owner.isActiveAndEnabled && health && !health.IsDead &&
        player && player.gameObject.scene==gameObject.scene && CanRetainWorld(player,flow);
    public static bool CanRetainWorld(PlayerVitals target,BaselineFlow host)
    {
        return target && target.isActiveAndEnabled && target.GetComponent<Health>() && !target.GetComponent<Health>().IsDead &&
        (!host || (host.gameObject.scene==target.gameObject.scene && host.Started && !host.TitleScreenVisible && !host.VictoryOpen &&
            !(host.SamBridge && host.SamBridge.IsAway) && !(host.endingRoom && host.endingRoom.IsInside))) &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy));
    }
    public Vector3 HeadPosition => shape && shape.enabled ? new Vector3(transform.position.x,
        Mathf.Max(transform.position.y + .8f, shape.bounds.max.y) + .38f, transform.position.z) : transform.position + Vector3.up * 2f;

    public static EnemyAwareness Ensure(MonoBehaviour ai)
    {
        var result = ai.GetComponent<EnemyAwareness>();
        if (!result) result = ai.gameObject.AddComponent<EnemyAwareness>();
        result.owner = ai;
        return result;
    }
    void Awake()
    {
        health = GetComponent<Health>(); shape = GetComponent<Collider>();
        foreach(var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if(candidate.gameObject.scene==gameObject.scene){flow=candidate;break;}
    }
    void OnEnable() { if (health) { health.Damaged += Damaged; health.Died += Died; } }
    void OnDisable() { if (health) { health.Damaged -= Damaged; health.Died -= Died; } Clear(); }
    void Damaged(Health target, DamageInfo damage)
    {
        // Taking a real player hit is sufficient evidence, including a rear strike.
        var source = damage.Source ? damage.Source.GetComponentInParent<PlayerVitals>() : null;
        if (source && !target.IsDead) Confirm(source);
    }
    void Died(Health target, DamageInfo damage) { Clear(); }
    public void Confirm(PlayerVitals source)
    {
        if (!source || source.gameObject.scene != gameObject.scene) return;
        player = source; InterestPosition = source.transform.position; unseen = 0f;
        if (!Engaged) { confirmedAge = 0f; ConfirmationCount++; }
        evidence = 1f; State = AwarenessState.Alerted;
    }
    public void Clear()
    {
        State = AwarenessState.Unaware; evidence = unseen = suspectHold = confirmedAge = 0f; Visible = false;
        if (indicator) indicator.Hide();
    }
    public void Suspend()
    {
        if (!CanRetainEncounter) { Clear(); return; }
        if (indicator) indicator.Hide(); // Hide HUD, freeze evidence/last-seen/confirmation clocks.
    }
    public void Tick(PlayerVitals target, bool clearLine, float range, float dt, bool omni = false)
    {
        player = target;
        if (!DisplayAllowed) { Suspend(); return; }
        if (dt <= 0f) return;
        Vector3 delta = target.transform.position - transform.position;
        float height = Mathf.Abs(delta.y); delta.y = 0f;
        float distance = delta.magnitude;
        bool inCone = omni || Engaged || distance <= 1.8f || distance < .01f ||
            Vector3.Dot(transform.forward, delta / distance) >= Mathf.Cos(visionHalfAngle * Mathf.Deg2Rad);
        Visible = clearLine && height <= 3f && distance <= range * (Engaged ? 1.45f : 1f) && inCone;
        if (Engaged)
        {
            confirmedAge += dt;
            if (Visible) { unseen = 0f; InterestPosition = target.transform.position; State = AwarenessState.Alerted; }
            else
            {
                unseen += dt;
                if (unseen > .7f) State = AwarenessState.Searching;
                if (unseen > searchSeconds) Clear();
            }
            return;
        }
        if (Visible)
        {
            InterestPosition = target.transform.position; unseen = 0f; suspectHold = .65f;
            float seconds = Mathf.Lerp(closeRecognitionSeconds, distantRecognitionSeconds,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.8f, range, distance)));
            evidence = Mathf.Min(1f, evidence + dt / seconds);
            if (evidence >= 1f) Confirm(target);
            else if (evidence >= .035f) State = AwarenessState.Suspicious;
        }
        else
        {
            unseen += dt; suspectHold = Mathf.Max(0f, suspectHold - dt);
            if (suspectHold <= 0f) evidence = Mathf.MoveTowards(evidence, 0f, dt * .65f);
            if (evidence <= 0f) State = AwarenessState.Unaware;
        }
    }
    void LateUpdate()
    {
        if (!DisplayAllowed) { Suspend(); return; }
        bool show = DisplayAllowed && (Curious || (Engaged && confirmedAge < 1.05f));
        if (show && !indicator) indicator = EnemyAwarenessIndicator.Create(this);
        if (indicator) indicator.Present(show);
    }
}
