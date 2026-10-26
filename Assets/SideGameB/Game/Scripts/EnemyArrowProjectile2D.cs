using UnityEngine;

public sealed class EnemyArrowProjectile2D : MonoBehaviour
{
    private Vector2 velocity;
    private int damage;
    private bool spent;
    private Transform target;
    private float speed;
    private float launchTime;

    public void Launch(Vector2 direction, float moveSpeed, int damageAmount, Transform trackingTarget,
        Collider2D ownerCollider)
    {
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.right;
        direction.Normalize();
        speed = moveSpeed;
        velocity = direction * speed;
        damage = damageAmount;
        target = trackingTarget;
        launchTime = Time.time;
        Collider2D arrowCollider = GetComponent<Collider2D>();
        if (arrowCollider != null && ownerCollider != null)
            Physics2D.IgnoreCollision(arrowCollider, ownerCollider, true);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        Destroy(gameObject, 4f);
    }

    private void FixedUpdate()
    {
        if (target != null)
        {
            Vector2 desired = ((Vector2)target.position + Vector2.up * 0.45f - (Vector2)transform.position).normalized;
            Vector2 current = velocity.sqrMagnitude > 0.001f ? velocity.normalized : desired;
            // Keep homing readable and projectile-like. Once the target has passed
            // behind the arrow, it continues forward instead of reversing in midair.
            Vector2 steered = current;
            if (Vector2.Dot(current, desired) > 0f)
                steered = Vector3.RotateTowards(current, desired,
                    150f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f).normalized;
            velocity = steered * speed;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(steered.y, steered.x) * Mathf.Rad2Deg);
        }
        transform.position += (Vector3)(velocity * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (spent || other.GetComponentInParent<EnemyHealth>() != null)
            return;

        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player != null)
        {
            spent = true;
            player.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        // Briefly ignore scenery beside the bow so downward shots are not deleted
        // by the archer's own platform the instant they are spawned.
        if (!other.isTrigger && Time.time - launchTime >= 0.22f)
        {
            spent = true;
            Destroy(gameObject);
        }
    }
}
