using UnityEngine;

public sealed class SpikeHazard2D : MonoBehaviour
{
    [SerializeField] private int damage = 5;
    [SerializeField] private float repeatDelay = 1f;

    private float nextDamageTime;

    public void Configure(int damageAmount, float damageRepeatDelay)
    {
        damage = Mathf.Max(0, damageAmount);
        repeatDelay = Mathf.Max(0.05f, damageRepeatDelay);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void TryDamage(Collider2D other)
    {
        if (Time.time < nextDamageTime)
            return;

        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead)
            return;

        nextDamageTime = Time.time + repeatDelay;
        player.TakeDamage(damage);
        PlayerController2D controller = player.GetComponent<PlayerController2D>();
        if (controller != null && !player.IsDead)
        {
            float awayDirection = player.transform.position.x - transform.position.x;
            controller.ApplyKnockback(awayDirection, 7.5f, 5.4f);
        }
    }
}
