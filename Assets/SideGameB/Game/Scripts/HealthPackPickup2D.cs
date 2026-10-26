using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class HealthPackPickup2D : MonoBehaviour
{
    [SerializeField] private int healAmount = 30;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobSpeed = 2.1f;
    private bool collected;
    private Vector3 startPosition;

    private void Awake()
    {
        startPosition = transform.position;
    }

    private void Update()
    {
        transform.position = startPosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected)
            return;

        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead)
            return;

        player.Heal(healAmount);
        collected = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
