using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class MagicProjectile2D : MonoBehaviour
{
    [SerializeField] private GameObject explosionPrefab;
    [SerializeField] private float speed = 7f;
    [SerializeField] private float lifetime = 2.2f;
    [SerializeField] private int damage = 35;

    private bool hasExploded;
    private float direction = 1f;

    public void SetExplosionPrefab(GameObject prefab)
    {
        explosionPrefab = prefab;
    }

    public void Launch(float launchDirection)
    {
        direction = Mathf.Sign(launchDirection);
        if (direction == 0f)
            direction = 1f;

        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        if (renderer != null)
            renderer.flipX = direction < 0f;
        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        transform.position += Vector3.right * (direction * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            Explode(enemy);
    }

    private void Explode(EnemyHealth enemy)
    {
        if (hasExploded)
            return;
        hasExploded = true;
        CreateExplosion(enemy);
        Destroy(gameObject);
    }

    private void CreateExplosion(EnemyHealth enemy)
    {
        if (explosionPrefab == null)
            return;
        GameObject explosion = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        explosion.GetComponent<ExplosionDamage2D>().Configure(damage, enemy);
    }
}
