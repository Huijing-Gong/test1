using UnityEngine;

public sealed class EnemyPatrol2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 0.75f;
    [SerializeField] private float leftEdge = 5.8f;
    [SerializeField] private float rightEdge = 11.2f;

    private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform visualRoot;
    private Animator[] animators;
    private int direction = -1;
    private bool paused;
    private Transform chaseTarget;

    public bool IsPaused => paused;
    public Transform VisualRoot => visualRoot;

    public bool IsWithinPatrol(float worldX, float margin = 0f)
    {
        return worldX >= leftEdge - margin && worldX <= rightEdge + margin;
    }

    public void Configure(float left, float right, float speed, Transform visual = null)
    {
        leftEdge = left;
        rightEdge = right;
        moveSpeed = speed;
        visualRoot = visual;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animators = GetComponentsInChildren<Animator>(true);
        foreach (Animator animator in animators)
            if (CanUseAnimator(animator) && HasParameter(animator, "1_Move"))
                animator.SetBool("1_Move", !paused);
        UpdateFacing();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        animators = GetComponentsInChildren<Animator>(true);
        UpdateFacing();
    }

    private void Update()
    {
        if (paused)
            return;

        if (chaseTarget != null)
        {
            float visualX = visualRoot != null ? visualRoot.position.x : transform.position.x;
            float delta = chaseTarget.position.x - visualX;
            if (Mathf.Abs(delta) > 0.08f)
            {
                direction = delta < 0f ? -1 : 1;
                transform.position += Vector3.right * (direction * moveSpeed * Time.deltaTime);
                ClampToPatrolEdges();
                UpdateFacing();
            }
            return;
        }

        transform.position += Vector3.right * (direction * moveSpeed * Time.deltaTime);

        if (transform.position.x <= leftEdge)
        {
            SetX(leftEdge);
            direction = 1;
            UpdateFacing();
        }
        else if (transform.position.x >= rightEdge)
        {
            SetX(rightEdge);
            direction = -1;
            UpdateFacing();
        }
    }

    public void SetPaused(bool value)
    {
        paused = value;
        if (animators == null)
            return;
        foreach (Animator animator in animators)
            if (CanUseAnimator(animator) && HasParameter(animator, "1_Move"))
                animator.SetBool("1_Move", !paused);
    }

    public void FaceTowards(float worldX)
    {
        float visualX = visualRoot != null ? visualRoot.position.x : transform.position.x;
        direction = worldX < visualX ? -1 : 1;
        UpdateFacing();
    }

    public void Chase(Transform target)
    {
        chaseTarget = target;
    }

    public void StopChasing()
    {
        chaseTarget = null;
        paused = false;
        if (animators != null)
        {
            foreach (Animator animator in animators)
                if (CanUseAnimator(animator) && HasParameter(animator, "1_Move"))
                    animator.SetBool("1_Move", true);
        }
    }

    private void ClampToPatrolEdges()
    {
        if (transform.position.x < leftEdge)
            SetX(leftEdge);
        else if (transform.position.x > rightEdge)
            SetX(rightEdge);
    }

    private void SetX(float x)
    {
        Vector3 position = transform.position;
        position.x = x;
        transform.position = position;
    }

    private void UpdateFacing()
    {
        if (spriteRenderer != null)
            spriteRenderer.flipX = direction > 0;
        if (visualRoot != null)
        {
            Vector3 scale = visualRoot.localScale;
            // SPUM characters face left at positive X scale and right at negative X scale.
            scale.x = Mathf.Abs(scale.x) * (direction > 0 ? -1f : 1f);
            visualRoot.localScale = scale;
        }
    }

    private static bool HasParameter(Animator animator, string parameterName)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == parameterName)
                return true;
        return false;
    }

    private static bool CanUseAnimator(Animator animator)
    {
        return animator != null && animator.isActiveAndEnabled &&
               animator.runtimeAnimatorController != null && animator.isInitialized;
    }
}
