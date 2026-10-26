using UnityEngine;

public sealed class ExplosionDamage2D : MonoBehaviour
{
    [SerializeField] private int damage = 35;
    [SerializeField] private float lifetime = 0.7f;

    private bool applied;
    private EnemyHealth target;

    public void Configure(int newDamage, EnemyHealth hitTarget)
    {
        damage = newDamage;
        target = hitTarget;
    }

    private void Start()
    {
        ApplyDamage();
        Destroy(gameObject, lifetime);
    }

    private void ApplyDamage()
    {
        if (applied)
            return;
        applied = true;

        if (target != null)
            target.TakeDamage(damage);
    }
}
