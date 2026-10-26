using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health), typeof(CapsuleCollider))]
public sealed class TrainingDummy : MonoBehaviour
{
    private static readonly int DamageHash = Animator.StringToHash("Damage");
    private static readonly int DeadHash = Animator.StringToHash("Dead");

    private Health health;
    private Animator animator;
    private CapsuleCollider bodyCollider;
    private bool healthEventsSubscribed;
    private bool hasStarted;

    public Health Health => health;
    public Animator Animator => animator;
    public Collider BodyCollider => bodyCollider;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        CacheReferences();
        if (bodyCollider != null)
        {
            if (hasStarted)
            {
                SyncBodyCollider();
            }
            else
            {
                bodyCollider.enabled = true;
            }
        }

        if (healthEventsSubscribed || health == null) return;
        health.HealthChanged += OnHealthChanged;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        healthEventsSubscribed = true;
    }

    private void Start()
    {
        hasStarted = true;
        SyncBodyCollider();
    }

    private void OnDisable()
    {
        if (healthEventsSubscribed && health != null)
        {
            health.HealthChanged -= OnHealthChanged;
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        healthEventsSubscribed = false;
    }

    private void CacheReferences()
    {
        if (health == null) health = GetComponent<Health>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider>();
    }

    private void SyncBodyCollider()
    {
        if (bodyCollider != null && health != null)
        {
            bodyCollider.enabled = !health.IsDead;
        }
    }

    private void OnHealthChanged(Health changedHealth, int previousHealth, int currentHealth)
    {
        if (bodyCollider != null && currentHealth > 0)
        {
            bodyCollider.enabled = true;
        }
    }

    private void OnDamaged(Health damagedHealth, DamageInfo damage)
    {
        if (animator == null || damagedHealth.IsDead) return;
        animator.ResetTrigger(DamageHash);
        animator.SetTrigger(DamageHash);
    }

    private void OnDied(Health deadHealth, DamageInfo damage)
    {
        if (bodyCollider != null) bodyCollider.enabled = false;
        if (animator == null) return;
        animator.ResetTrigger(DamageHash);
        animator.SetBool(DeadHash, true);
    }
}
