using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Health))]
public sealed class EnemySoundEmitter : MonoBehaviour
{
    public enum Role { Scout, Ranged, Prism, Charger, Repair, Gel }
    public Role EnemyRole { get; private set; }
    Health health;
    PlayerVitals player;
    Health playerHealth;
    BaselineFlow flow;
    float nextPlayerSearch, nextHitTime, landingAt;
    float lastCueAt = -10f;
    EnemySoundCue lastCue;
    EnemySoundCue delayedCue;
    float delayedAt, delayedGain;
    public bool IsDead => health && health.IsDead;
    public bool CanSound => isActiveAndEnabled && gameObject.scene.isLoaded &&
        (!player || (player.gameObject.scene == gameObject.scene && player.EnemyCombatActive && playerHealth && !playerHealth.IsDead));

    public static EnemySoundEmitter Ensure(GameObject actor)
    {
        if (!Application.isPlaying || !actor) return null;
        var emitter = actor.GetComponent<EnemySoundEmitter>();
        if (!emitter) emitter = actor.AddComponent<EnemySoundEmitter>();
        return emitter;
    }
    void Awake()
    {
        health = GetComponent<Health>();
        var domain = GetComponent<DomainPrototypeEnemy>();
        EnemyRole = GetComponent<GelBlobAI>() ? Role.Gel : GetComponent<RepairWispAI>() ? Role.Repair : GetComponent<RailBeetleAI>() ? Role.Charger :
            domain ? (domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism ? Role.Prism : Role.Ranged) : Role.Scout;
        GetComponent<CombatFeedback>()?.UseEnemySoundLibrary();
        FindPlayer();
    }
    void OnEnable()
    {
        health.Damaged += OnDamaged; health.Died += OnDied;
        flow = FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += ResetForJourney;
    }
    void OnDisable()
    {
        if (health) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        if (flow) flow.WorldEncountersReset -= ResetForJourney;
        ResetForJourney();
    }
    void FindPlayer()
    {
        nextPlayerSearch = Time.unscaledTime + 1f;
        foreach (PlayerVitals candidate in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        playerHealth = player ? player.GetComponent<Health>() : null;
    }
    void Update()
    {
        if (!player && Time.unscaledTime >= nextPlayerSearch) FindPlayer();
        if (!CanSound) { landingAt = delayedAt = 0f; EnemyAudio.StopOwner(this); return; }
        if (delayedAt > 0f && Time.time >= delayedAt)
        { delayedAt = 0f; if (!IsDead || EnemyAudio.IsAfterDeath(delayedCue)) Cue(delayedCue, delayedGain); }
        if (landingAt > 0f && Time.time >= landingAt)
        { landingAt = 0f; if (IsDead) Cue(EnemySoundCue.StoneFall, .85f); }
    }
    public bool Cue(EnemySoundCue cue, float gain = 1f, float seconds = 0f, Vector3? point = null)
    {
        if (cue == lastCue && Time.time < lastCueAt + .09f) return false;
        bool warning = EnemyAudio.IsWarning(cue);
        // +/- 3% action pitch; anticipation stays repeatable and easy to recognise.
        float pitch = warning ? 1f : 1f + Random.Range(-.03f, .03f);
        if ((EnemyRole == Role.Charger || EnemyRole == Role.Prism) && cue == EnemySoundCue.ShellHit) pitch *= .86f;
        if (EnemyRole == Role.Repair && cue == EnemySoundCue.ShellHit) pitch *= 1.12f;
        bool played = EnemyAudio.Play(this, cue, point ?? transform.position + Vector3.up, gain, pitch, seconds);
        if (played) { lastCue = cue; lastCueAt = Time.time; }
        return played;
    }
    public void StopWarning() => EnemyAudio.StopOwner(this, true);
    public void StopAction() => EnemyAudio.StopAction(this);
    public void QueueCue(EnemySoundCue cue, float delay, float gain = 1f)
    { delayedCue = cue; delayedGain = gain; delayedAt = Time.time + Mathf.Max(.01f, delay); }
    public void StopAll() { landingAt = delayedAt = 0f; EnemyAudio.StopOwner(this); }
    public void ResetForJourney() { StopAll(); nextHitTime = 0f; lastCueAt = -10f; }
    void OnDamaged(Health actor, DamageInfo damage)
    {
        if(GetComponent<AdventureCreatureVoice>())return;
        if (actor.IsDead || Time.time < nextHitTime) return;
        nextHitTime = Time.time + .12f;
        Cue(EnemyRole == Role.Gel ? EnemySoundCue.GelHit : EnemyRole == Role.Scout || EnemyRole == Role.Charger ? EnemySoundCue.StoneHit : EnemySoundCue.ShellHit,
            .8f, point: damage.HitPoint);
    }
    void OnDied(Health actor, DamageInfo damage)
    {
        if(GetComponent<AdventureCreatureVoice>()){StopAll();return;}
        if (EnemyRole == Role.Gel) { StopAll(); Cue(EnemySoundCue.GelSplat, .8f); QueueCue(EnemySoundCue.CorePowerDown, .2f, .6f); return; }
        StopAll(); Cue(EnemySoundCue.CorePowerDown, EnemyRole == Role.Scout ? .65f : .9f);
        if (EnemyRole == Role.Scout || EnemyRole == Role.Charger) landingAt = Time.time + .65f;
    }
}
