using System;
using UnityEngine;

public enum HealthTeam
{
    Player,
    Enemy,
    Neutral
}

[DisallowMultipleComponent]
public sealed class Health : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private HealthTeam team = HealthTeam.Neutral;
    [SerializeField] private bool invulnerable;

    public event Action<Health, DamageInfo> Damaged;
    public event Action<Health, DamageInfo> Died;
    public event Action<Health, int, int> HealthChanged;
    public delegate void DamageResolver(ref DamageInfo damage);
    public event DamageResolver ResolvingDamage;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public float NormalizedHealth => (float)CurrentHealth / MaxHealth;
    public bool IsDead => CurrentHealth <= 0;
    public HealthTeam Team => team;
    public bool Invulnerable => invulnerable;

    private void Awake()
    {
        CurrentHealth = MaxHealth;
    }

    public bool TryHeal(int amount)
    {
        if (amount <= 0 || IsDead || CurrentHealth >= MaxHealth) return false;
        int previous = CurrentHealth;
        CurrentHealth = (int)Math.Min(MaxHealth, (long)CurrentHealth + amount);
        HealthChanged?.Invoke(this, previous, CurrentHealth);
        return true;
    }

    // Explicit respawn/new-session restoration; healing cannot revive a dead target.
    public void RestoreFullHealth()
    {
        int previous = CurrentHealth;
        CurrentHealth = MaxHealth;
        if (previous != CurrentHealth) HealthChanged?.Invoke(this, previous, CurrentHealth);
    }

    // Configure a newly spawned target before combat begins.
    public void SetMaximumHealth(int value)
    {
        int previous = CurrentHealth;
        maxHealth = Mathf.Max(1, value);
        CurrentHealth = MaxHealth;
        if (previous != CurrentHealth) HealthChanged?.Invoke(this, previous, CurrentHealth);
    }

    // Capacity-only reward update; never heals or revives an existing actor.
    public void SetMaximumHealthPreservingCurrent(int value)
    {
        int previous=CurrentHealth; maxHealth=Mathf.Max(1,value);
        CurrentHealth=Mathf.Min(CurrentHealth,MaxHealth);
        if(previous!=CurrentHealth)HealthChanged?.Invoke(this,previous,CurrentHealth);
    }

    public bool CanReceiveDamageFrom(GameObject source)
    {
        if (invulnerable || IsDead)
        {
            return false;
        }

        if (source == null)
        {
            return true;
        }

        Health sourceHealth = source.GetComponentInParent<Health>();
        if (sourceHealth == null || team == HealthTeam.Neutral ||
            sourceHealth.team == HealthTeam.Neutral)
        {
            return true;
        }

        return sourceHealth.team != team;
    }

    public bool TryApplyDamage(in DamageInfo damage)
    {
        if(!damage.IsValid||!CanReceiveDamageFrom(damage.Source))return false;
        var evasion=GetComponent<PlayerBattleActions>();
        if(evasion && evasion.TryEvade(in damage))return false;
        var adjusted=damage;
        var creature=GetComponent<AdventureCreatureAI>();
        if(creature&&!creature.ResolveIncoming(ref adjusted))return false;
        ResolvingDamage?.Invoke(ref adjusted);
        var hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        if(hero&&!hero.ResolveIncoming(ref adjusted))return false;
        return ApplyResolvedDamage(adjusted);
    }

    private bool ApplyResolvedDamage(in DamageInfo damage)
    {
        if (!damage.IsValid || !CanReceiveDamageFrom(damage.Source))
        {
            return false;
        }

        int previous = CurrentHealth;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage.Amount);
        HealthChanged?.Invoke(this, previous, CurrentHealth);
        Damaged?.Invoke(this, damage);

        if (CurrentHealth == 0)
        {
            Died?.Invoke(this, damage);
        }

        return true;
    }
}
