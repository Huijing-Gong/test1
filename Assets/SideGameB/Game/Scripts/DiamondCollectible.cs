using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class DiamondCollectible : MonoBehaviour
{
    [SerializeField] private float bobHeight = 0.09f;
    [SerializeField] private float bobSpeed = 2.4f;

    private Vector3 startPosition;
    private bool collected;

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
        if (collected || other.GetComponentInParent<PlayerController2D>() == null)
            return;

        collected = true;
        GameAudio.Play(GameSound.DiamondGet);
        DiamondCounterHud counter = DiamondCounterHud.Instance != null
            ? DiamondCounterHud.Instance
            : FindFirstObjectByType<DiamondCounterHud>();
        if (counter != null)
            counter.AddDiamond();
        Destroy(gameObject);
    }
}
