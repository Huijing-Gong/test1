using System.Collections;
using UnityEngine;

public sealed class EnemyArcherAttack2D : MonoBehaviour
{
    [SerializeField] private Sprite arrowSprite;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float horizontalRange = 13f;
    [SerializeField] private float verticalRange = 6.5f;
    [SerializeField] private float cooldown = 1.7f;
    [SerializeField] private float arrowSpeed = 7.5f;
    [SerializeField] private int damage = 5;

    private PlayerHealth player;
    private Animator[] animators;
    private float nextShotTime;
    private bool shooting;
    private BoxCollider2D bodyCollider;
    private bool colliderAligned;
    private bool playerWasInRange;

    public void ConfigureRanges(float allowedHorizontalRange, float allowedVerticalRange)
    {
        horizontalRange = allowedHorizontalRange;
        verticalRange = allowedVerticalRange;
    }

    public void Configure(Sprite projectileSprite, Transform visual)
    {
        arrowSprite = projectileSprite;
        visualRoot = visual;
        animators = GetComponentsInChildren<Animator>(true);
        bodyCollider = GetComponent<BoxCollider2D>();
    }

    private void Awake()
    {
        animators = GetComponentsInChildren<Animator>(true);
        bodyCollider = GetComponent<BoxCollider2D>();
        AlignColliderWithVisibleBody();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        if (!colliderAligned)
            AlignColliderWithVisibleBody();
        if (player == null)
            foreach(var candidate in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                if(candidate.gameObject.scene==gameObject.scene){player=candidate;break;}
        if (player == null || player.IsDead || arrowSprite == null)
            return;

        Vector3 archerPosition = visualRoot != null ? visualRoot.position : transform.position;
        Vector2 delta = player.transform.position - archerPosition;
        bool inRange = Mathf.Abs(delta.x) <= horizontalRange && Mathf.Abs(delta.y) <= verticalRange;
        if (!inRange)
        {
            playerWasInRange = false;
            return;
        }

        if (!playerWasInRange)
        {
            playerWasInRange = true;
            GameAudio.Play(GameSound.EnemyAlert);
        }

        FacePlayer();
        if (!shooting && Time.time >= nextShotTime)
            StartCoroutine(Shoot());
    }

    private void FacePlayer()
    {
        if (visualRoot == null)
            return;
        Vector3 scale = visualRoot.localScale;
        bool playerIsRight = player.transform.position.x > visualRoot.position.x;
        scale.x = Mathf.Abs(scale.x) * (playerIsRight ? -1f : 1f);
        visualRoot.localScale = scale;
    }

    private IEnumerator Shoot()
    {
        shooting = true;
        GameAudio.Play(GameSound.EnemyAttack);
        nextShotTime = Time.time + cooldown;
        foreach (Animator animator in animators)
            if (animator != null && animator.isActiveAndEnabled && HasParameter(animator, "2_Attack"))
                animator.SetTrigger("2_Attack");

        yield return new WaitForSeconds(0.3f);
        if (player != null && !player.IsDead)
            SpawnArrow();
        yield return new WaitForSeconds(0.25f);
        shooting = false;
    }

    private void SpawnArrow()
    {
        GameAudio.Play(GameSound.ArrowShot);
        Vector3 bodyCenter = bodyCollider != null ? bodyCollider.bounds.center :
            (visualRoot != null ? visualRoot.position + Vector3.up * 0.7f : transform.position + Vector3.up * 0.7f);
        Vector2 direction = (player.transform.position + Vector3.up * 0.45f - bodyCenter).normalized;
        Vector3 origin = bodyCenter + (Vector3)(direction * 1.05f);

        GameObject arrow = new GameObject("Enemy Arrow");
        arrow.transform.position = origin;
        SpriteRenderer renderer = arrow.AddComponent<SpriteRenderer>();
        renderer.sprite = arrowSprite;
        renderer.sortingOrder = 30;
        arrow.transform.localScale = Vector3.one * 0.22f;

        Rigidbody2D body = arrow.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        CircleCollider2D collider = arrow.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = arrowSprite.bounds.extents.y * 0.55f;
        arrow.AddComponent<EnemyArrowProjectile2D>().Launch(
            direction, arrowSpeed, damage, player.transform, bodyCollider);
    }

    private void AlignColliderWithVisibleBody()
    {
        if (bodyCollider == null || visualRoot == null)
            return;
        SpriteRenderer[] renderers = visualRoot.GetComponentsInChildren<SpriteRenderer>(true);
        Bounds bounds = default;
        bool found = false;
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        if (!found)
            return;
        Vector3 center = transform.InverseTransformPoint(bounds.center);
        bodyCollider.offset = new Vector2(center.x, center.y);
        bodyCollider.size = new Vector2(Mathf.Clamp(bounds.size.x * 0.65f, 1.05f, 1.6f),
                                        Mathf.Clamp(bounds.size.y * 0.82f, 1.5f, 2.1f));
        bodyCollider.enabled = true;
        bodyCollider.isTrigger = true;
        colliderAligned = true;
    }

    private static bool HasParameter(Animator animator, string parameterName)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == parameterName)
                return true;
        return false;
    }
}
