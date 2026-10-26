using UnityEngine;

/// <summary>A one-way encounter entrance; resets only when the scene restarts.</summary>
[DefaultExecutionOrder(100)]
public sealed class ArenaBarrierLock : MonoBehaviour
{
    [SerializeField] private PixelShieldController shield;
    [SerializeField] private GameObject walls;
    [SerializeField] private Vector3 floorCenter;
    [SerializeField] private float radius = 3.75f;
    [SerializeField] private float height = 4.1f;
    [SerializeField, Min(.01f)] private float riseDuration = 1f;
    private PlayerMovement player;
    private CharacterController capsule;
    private float rise;
    public bool IsLocked { get; private set; }

    public void Configure(PixelShieldController visual, GameObject blockers, Vector3 center, float arenaRadius, float barrierHeight)
    {
        shield = visual;
        walls = blockers;
        floorCenter = center;
        radius = arenaRadius;
        height = barrierHeight;
        shield.gameObject.SetActive(false);
        walls.SetActive(false);
    }

    private void Awake()
    {
        IsLocked = false;
        rise = 0;
        if (shield) shield.gameObject.SetActive(false);
        if (walls) walls.SetActive(false);
        player = FindFirstObjectByType<PlayerMovement>();
        if (player) capsule = player.GetComponent<CharacterController>();
    }

    private void LateUpdate()
    {
        if (!player || !capsule || !capsule.enabled || !shield || !walls) return;
        Vector3 offset = player.transform.position - floorCenter;
        Vector3 planar = Vector3.ProjectOnPlane(offset, Vector3.up);
        float safeRadius = Mathf.Max(.1f, radius - capsule.bounds.extents.x - .25f);
        float feet = capsule.bounds.min.y;
        // The complete capsule must be inside the perimeter and at platform height.
        // Approaching from below or standing outside the rim cannot seal the arena.
        if (!IsLocked && planar.sqrMagnitude <= safeRadius * safeRadius
            && feet >= floorCenter.y - .25f && feet <= floorCenter.y + .75f)
        {
            IsLocked = true;
            walls.SetActive(true);
            shield.transform.localScale = new Vector3(1, .001f, 1);
            shield.transform.position = floorCenter;
            shield.gameObject.SetActive(true);
        }
        if (!IsLocked) return;
        rise = Mathf.MoveTowards(rise, 1, Time.deltaTime / riseDuration);
        float visibleHeight = Mathf.Max(.001f, Mathf.SmoothStep(0, 1, rise));
        shield.transform.localScale = new Vector3(1, visibleHeight, 1);
        shield.transform.position = floorCenter + Vector3.up * height * visibleHeight * .5f;

        // Physical walls handle ordinary movement. Keep the capsule inside even after
        // a large movement step, a jump over the wall or a respawn teleport.
        if (planar.sqrMagnitude > safeRadius * safeRadius || feet < floorCenter.y - .5f)
        {
            Vector3 destination = floorCenter + Vector3.ClampMagnitude(planar, safeRadius);
            destination.y = player.transform.position.y + Mathf.Max(0, floorCenter.y + .02f - feet);
            capsule.enabled = false;
            player.transform.position = destination;
            capsule.enabled = true;
        }
    }
}
