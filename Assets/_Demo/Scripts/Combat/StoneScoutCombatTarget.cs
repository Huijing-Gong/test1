using UnityEngine;

// Combat-only adapter for the visual Stone Scout. Enemy movement and attacks
// belong to a separate gameplay controller on this root object.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(CapsuleCollider))]
public sealed class StoneScoutCombatTarget : MonoBehaviour
{
    private Health health;
    private CapsuleCollider bodyCollider;
    private StoneScoutVisualMotion visual;
    private StoneScoutRoadsideAI roadsideAI;
    private StoneScoutLootDrop lootDrop;
    private CombatFeedback feedback;
    private BaselineFlow flow;
    private bool started;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        flow = FindFirstObjectByType<BaselineFlow>();
        if (flow != null) flow.WorldEncountersReset += ResetForJourney;
        if (started)
            bodyCollider.enabled = !health.IsDead;
    }

    private void Start()
    {
        started = true;
        bodyCollider.enabled = !health.IsDead;
    }

    private void OnDisable()
    {
        if (health == null)
            return;
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        if (flow != null) flow.WorldEncountersReset -= ResetForJourney;
        flow = null;
    }

    private void CacheComponents()
    {
        if (health == null) health = GetComponent<Health>();
        if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider>();
        if (visual == null) visual = GetComponentInChildren<StoneScoutVisualMotion>(true);
        if (roadsideAI == null) roadsideAI = GetComponent<StoneScoutRoadsideAI>();
        if (lootDrop == null) lootDrop = GetComponent<StoneScoutLootDrop>();
        if (feedback == null) feedback = GetComponent<CombatFeedback>();
    }

    public void ResetForJourney()
    {
        CacheComponents();
        if (lootDrop != null) lootDrop.ClearDrop();
        if (roadsideAI != null) roadsideAI.ResetToHome();
        health.RestoreFullHealth();
        bodyCollider.enabled = true;
        if (visual != null) visual.ResetVisual();
        if (feedback != null) feedback.ClearFeedback();
    }

    private void OnDamaged(Health target, DamageInfo damage)
    {
        if (!target.IsDead && visual != null)
            visual.PlayHit(damage.HitDirection);
    }

    private void OnDied(Health target, DamageInfo damage)
    {
        bodyCollider.enabled = false;
        if (visual != null)
            visual.PlayDeath();
    }
}
