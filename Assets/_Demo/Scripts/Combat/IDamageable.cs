using UnityEngine;

public interface IDamageable
{
    bool IsDead { get; }
    bool CanReceiveDamageFrom(GameObject source);
    bool TryApplyDamage(in DamageInfo damage);
}
