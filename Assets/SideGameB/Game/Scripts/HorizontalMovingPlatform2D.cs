using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class HorizontalMovingPlatform2D : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float distanceEachSide = 3f;
    [SerializeField, Min(0.05f)] private float moveSpeed = 1f;
    private Rigidbody2D body;
    private Collider2D platformCollider;
    private Vector2 center;
    private float direction = 1f;
    private Transform princess;
    private PlayerController2D princessController;
    private Rigidbody2D princessBody;
    private Collider2D princessCollider;

    public void Configure(float newDistanceEachSide, float newMoveSpeed)
    {
        distanceEachSide = Mathf.Max(0.1f, newDistanceEachSide);
        moveSpeed = Mathf.Max(0.05f, newMoveSpeed);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();
        ConfigureOneWayCollision();
        center = body.position;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.useFullKinematicContacts = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.freezeRotation = true;
        PlayerController2D player = FindFirstObjectByType<PlayerController2D>();
        CachePrincess(player);
    }

    private void ConfigureOneWayCollision()
    {
        PlatformEffector2D effector = GetComponent<PlatformEffector2D>();
        if (effector == null) effector = gameObject.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.useSideFriction = false;
        effector.surfaceArc = 160f;
        platformCollider.usedByEffector = true;
    }

    private void FixedUpdate()
    {
        if (!PrincessIsStandingOnPlatform())
        {
            if (princessController != null) princessController.SetMovingPlatformMotion(this, Vector2.zero, false);
            return;
        }
        float left = center.x - distanceEachSide;
        float right = center.x + distanceEachSide;
        float nextX = Mathf.MoveTowards(body.position.x, direction > 0f ? right : left, moveSpeed * Time.fixedDeltaTime);
        Vector2 next = new Vector2(nextX, center.y);
        Vector2 delta = next - body.position;

        // Carry only the princess standing on top; touching the side does not drag her.
        if (princessController != null)
            princessController.SetMovingPlatformMotion(this, delta / Time.fixedDeltaTime, true);
        body.MovePosition(next);
        if (nextX >= right - 0.001f) direction = -1f;
        else if (nextX <= left + 0.001f) direction = 1f;
    }

    private bool PrincessIsStandingOnPlatform()
    {
        if (princess == null)
        {
            PlayerController2D player = FindFirstObjectByType<PlayerController2D>();
            CachePrincess(player);
        }
        if (princessCollider == null || platformCollider == null) return false;
        Bounds playerBounds = princessCollider.bounds;
        Bounds platformBounds = platformCollider.bounds;
        bool horizontallyOverlapping = playerBounds.max.x > platformBounds.min.x + 0.05f &&
                                       playerBounds.min.x < platformBounds.max.x - 0.05f;
        float feetGap = playerBounds.min.y - platformBounds.max.y;
        return horizontallyOverlapping && feetGap >= -0.08f && feetGap <= 0.16f;
    }

    private void CachePrincess(PlayerController2D player)
    {
        if (player == null) return;
        princess = player.transform;
        princessController = player;
        princessBody = player.GetComponent<Rigidbody2D>();
        princessCollider = player.GetComponent<BoxCollider2D>();
    }

    private void OnDisable()
    {
        if (princessController != null) princessController.SetMovingPlatformMotion(this, Vector2.zero, false);
    }
}
