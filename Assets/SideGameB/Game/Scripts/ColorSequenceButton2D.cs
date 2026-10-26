using System.Collections;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class ColorSequenceButton2D : MonoBehaviour
{
    [SerializeField] private PuzzleColor color;
    [SerializeField] private ColorSequencePuzzle puzzle;
    [SerializeField] private Transform visual;
    [SerializeField] private Sprite halfPressedSprite;
    [SerializeField] private Sprite fullyPressedSprite;
    [SerializeField] private float pressDistance = 0.45f;
    [SerializeField] private float moveTime = 0.10f;
    private Vector3 raisedPosition;
    private Sprite raisedSprite;
    private SpriteRenderer visualRenderer;
    private BoxCollider2D topCollider;
    private Vector2 raisedColliderOffset;
    private BoxCollider2D playerMainCollider;
    private Coroutine movement;
    private bool occupied;
    public PuzzleColor Color => color;
    public bool IsLockedDown { get; private set; }

    public void Configure(PuzzleColor newColor, ColorSequencePuzzle newPuzzle, Transform newVisual,
        float newPressDistance = 0.45f, Sprite newHalfPressedSprite = null, Sprite newFullyPressedSprite = null)
    {
        color = newColor; puzzle = newPuzzle; visual = newVisual;
        pressDistance = newPressDistance;
        halfPressedSprite = newHalfPressedSprite;
        fullyPressedSprite = newFullyPressedSprite;
        raisedPosition = visual.localPosition;
        CacheVisual();
    }

    private void Awake()
    {
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null) { raisedPosition = visual.localPosition; CacheVisual(); }
    }

    private void FixedUpdate()
    {
        if (playerMainCollider == null)
        {
            PlayerController2D player = FindFirstObjectByType<PlayerController2D>();
            if (player != null) playerMainCollider = player.GetComponent<BoxCollider2D>();
        }
        if (playerMainCollider == null || topCollider == null) return;
        if (!IsPlayerActuallyNearButton(playerMainCollider))
        {
            occupied = false;
            return;
        }
        Bounds feet = playerMainCollider.bounds;
        Bounds top = topCollider.bounds;
        bool horizontal = feet.max.x > top.min.x + 0.02f && feet.min.x < top.max.x - 0.02f;
        float gap = feet.min.y - top.max.y;
        bool standing = horizontal && gap >= -0.12f && gap <= 0.20f;
        if (standing && !occupied)
        {
            occupied = true;
            puzzle?.Press(this);
        }
        else if (!standing)
        {
            occupied = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryPressWithMainPlayerCollider(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Also evaluate while standing so entering with another child collider cannot swallow the press.
        TryPressWithMainPlayerCollider(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player != null && other == player.GetComponent<BoxCollider2D>()) occupied = false;
    }

    private void TryPressWithMainPlayerCollider(Collider2D other)
    {
        if (occupied) return;
        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null || other != player.GetComponent<BoxCollider2D>()) return;
        if (!IsPlayerActuallyNearButton(other)) return;
        // Only feet arriving from above count; side contact with the button does not.
        if (other.bounds.min.y < transform.position.y - 0.12f) return;
        occupied = true;
        puzzle?.Press(this);
    }

    private bool IsPlayerActuallyNearButton(Collider2D playerCollider)
    {
        if (playerCollider == null) return false;
        Vector3 buttonPosition = visual != null ? visual.position : transform.position;
        Bounds playerBounds = playerCollider.bounds;
        // Never trust a potentially oversized platform/trigger bound by itself.
        // A sequence button can only react to a player physically beside and above
        // this specific button visual.
        float horizontalDistance = Mathf.Abs(playerBounds.center.x - buttonPosition.x);
        float feetDistance = Mathf.Abs(playerBounds.min.y - buttonPosition.y);
        return horizontalDistance <= 1.25f && feetDistance <= 1.35f;
    }

    public void LockDown() { IsLockedDown = true; Animate(true); }
    public void PressTemporarily() { Animate(true); }
    public void ResetUp() { IsLockedDown = false; Animate(false); }

    private void CacheVisual()
    {
        if (visual == null) return;
        visualRenderer = visual.GetComponent<SpriteRenderer>();
        topCollider = visual.GetComponent<BoxCollider2D>();
        if (visualRenderer != null && raisedSprite == null) raisedSprite = visualRenderer.sprite;
        if (topCollider != null)
        {
            Vector2 loweredOffset = topCollider.offset;
            loweredOffset.y = 0.12f;
            topCollider.offset = loweredOffset;
            raisedColliderOffset = loweredOffset;
        }
    }

    private void Animate(bool pressed)
    {
        if (movement != null) StopCoroutine(movement);
        movement = StartCoroutine(PressRoutine(pressed));
    }

    private IEnumerator PressRoutine(bool pressed)
    {
        if (visualRenderer == null) CacheVisual();
        Sprite startSprite = pressed ? raisedSprite : fullyPressedSprite;
        Sprite middleSprite = halfPressedSprite;
        Sprite endSprite = pressed ? fullyPressedSprite : raisedSprite;
        Vector2 startOffset = topCollider != null ? topCollider.offset : Vector2.zero;
        Vector2 endOffset = raisedColliderOffset + Vector2.down * (pressed ? pressDistance : 0f);
        float duration = Mathf.Max(0.08f, moveTime * 1.6f);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            if (visualRenderer != null)
                visualRenderer.sprite = p < 0.5f ? (startSprite != null ? startSprite : visualRenderer.sprite) : (middleSprite != null ? middleSprite : visualRenderer.sprite);
            if (topCollider != null) topCollider.offset = Vector2.Lerp(startOffset, endOffset, p);
            yield return null;
        }
        if (visualRenderer != null && endSprite != null) visualRenderer.sprite = endSprite;
        if (topCollider != null) topCollider.offset = endOffset;
    }
}
