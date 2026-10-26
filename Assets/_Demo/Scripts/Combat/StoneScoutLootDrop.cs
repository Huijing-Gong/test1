using System.Collections;
using UnityEngine;

// A small, temporary heal drop. It does not add inventory or alter saved progress.
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class StoneScoutLootDrop : MonoBehaviour
{
    [SerializeField] private Material shardMaterial;
    [SerializeField] private AudioClip pickupClip;
    [SerializeField, Min(1)] private int healAmount = 15;
    [SerializeField, Min(1f)] private float lifetimeSeconds = 25f;

    private Health health;
    private StoneScoutVitalPickup activeDrop;

    private void Awake() { health = GetComponent<Health>(); }
    private void OnEnable() { health.Died += OnDied; }

    private void OnDisable()
    {
        if (health != null) health.Died -= OnDied;
        ClearDrop();
    }

    private void OnDied(Health target, DamageInfo damage)
    {
        if (activeDrop == null) StartCoroutine(SpawnAfterFall());
    }

    private IEnumerator SpawnAfterFall()
    {
        yield return new WaitForSeconds(0.7f);
        if (health.IsDead && activeDrop == null)
            activeDrop = StoneScoutVitalPickup.Spawn(transform.position + Vector3.up * 0.65f,
                shardMaterial, healAmount, lifetimeSeconds, pickupClip);
    }

    public void ClearDrop()
    {
        StopAllCoroutines();
        if (activeDrop == null) return;
        activeDrop.gameObject.SetActive(false);
        Destroy(activeDrop.gameObject);
        activeDrop = null;
    }
}
