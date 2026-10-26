using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class VerticalMovingPlatform2D : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float upwardDistance = 4f;
    [SerializeField, Min(0.05f)] private float moveSpeed = 0.9f;
    private Rigidbody2D body;
    private Collider2D platformCollider;
    private Vector2 bottom;
    private float direction = 1f;
    private Transform princess;
    private Rigidbody2D princessBody;
    private Collider2D princessCollider;

    public void Configure(float newUpwardDistance, float newMoveSpeed)
    {
        upwardDistance = Mathf.Max(0.1f, newUpwardDistance);
        moveSpeed = Mathf.Max(0.05f, newMoveSpeed);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();
        ConfigureOneWayCollision();
        bottom = body.position;
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
        // An upside-down ceiling must block from both sides; a rotated
        // one-way effector otherwise lets the player pass through its stone.
        bool inverted = Vector2.Dot(transform.up, Vector2.up) < 0f;
        effector.enabled = !inverted;
        if (inverted)
        {
            platformCollider.usedByEffector = false;
            return;
        }
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
            return;
        }
        float targetY = direction > 0f ? bottom.y + upwardDistance : bottom.y;
        float nextY = Mathf.MoveTowards(body.position.y, targetY, moveSpeed * Time.fixedDeltaTime);
        Vector2 next = new Vector2(bottom.x, nextY);
        Vector2 delta = next - body.position;
        body.MovePosition(next);
        if (nextY >= bottom.y + upwardDistance - 0.001f) direction = -1f;
        else if (nextY <= bottom.y + 0.001f) direction = 1f;
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
        princessBody = player.GetComponent<Rigidbody2D>();
        princessCollider = player.GetComponent<BoxCollider2D>();
    }
}
