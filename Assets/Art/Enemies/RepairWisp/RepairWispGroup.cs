using UnityEngine;

// Explicit membership: support never searches/heals arbitrary actors across the map.
[DisallowMultipleComponent]
public sealed class RepairWispGroup : MonoBehaviour
{
    [SerializeField] private Health[] guards = new Health[0];
    private float alertUntil;
    public Health[] Guards => guards;
    public bool Alerted => Time.time < alertUntil;
    public Health Leader
    {
        get { foreach (Health guard in guards) if (Eligible(guard)) return guard; return null; }
    }
    private void OnEnable()
    { foreach (Health guard in guards) if (guard) guard.Damaged += OnGuardDamaged; }
    private void OnDisable()
    { foreach (Health guard in guards) if (guard) guard.Damaged -= OnGuardDamaged; }
    private void OnGuardDamaged(Health guard, DamageInfo damage) { Alert(); }
    public void Alert() { alertUntil = Time.time + 8f; }
    public void ClearAlert() { alertUntil = 0f; }
    public bool Contains(Health actor)
    { foreach (Health guard in guards) if (actor == guard) return true; return false; }
    public static bool Eligible(Health actor) => actor && actor.isActiveAndEnabled && !actor.IsDead &&
        actor.Team == HealthTeam.Enemy && !actor.GetComponent<RepairWispAI>() &&
        (actor.GetComponent<StoneScoutRoadsideAI>() || actor.GetComponent<DomainPrototypeEnemy>() || actor.GetComponent<RailBeetleAI>() || actor.GetComponent<AdventureCreatureAI>());
    public void Configure(Health[] members)
    {
        if (isActiveAndEnabled) OnDisable();
        guards = members ?? new Health[0]; ClearAlert();
        if (isActiveAndEnabled) OnEnable();
    }
}
