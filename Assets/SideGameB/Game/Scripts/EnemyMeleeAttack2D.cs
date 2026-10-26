using System.Collections;
using UnityEngine;

[RequireComponent(typeof(EnemyPatrol2D))]
public sealed class EnemyMeleeAttack2D : MonoBehaviour
{
    [SerializeField] private float attackRange = 1.35f;
    [SerializeField] private float verticalRange = 1.1f;
    [SerializeField] private float attackCooldown = 1.05f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float detectionRange = 5.5f;
    [SerializeField] private float detectionVerticalRange = 1.8f;
    [SerializeField] private float disengageRange = 7f;
    [SerializeField] private float disengageVerticalRange = 2.4f;

    private EnemyPatrol2D patrol;
    private PlayerHealth player;
    private Collider2D enemyCollider;
    private Animator[] animators;
    private bool attacking;
    private float nextAttackTime;
    private bool bodyColliderAligned;
    private bool alerted;
    private bool playerWasInAttackRange;
    private float nextAlertSoundTime;
    private Collider2D homePlatform;

    public void ConfigureCombat(float horizontalRange, float allowedVerticalRange, int attackDamage)
    {
        attackRange = horizontalRange;
        verticalRange = allowedVerticalRange;
        damage = attackDamage;
        patrol = GetComponent<EnemyPatrol2D>();
        enemyCollider = GetComponent<Collider2D>();
        animators = GetComponentsInChildren<Animator>(true);
    }

    public void ConfigureRanges(float horizontalAttackRange, float allowedAttackVerticalRange,
        float horizontalDetectionRange, float allowedDetectionVerticalRange,
        float horizontalDisengageRange, float allowedDisengageVerticalRange)
    {
        attackRange = horizontalAttackRange;
        verticalRange = allowedAttackVerticalRange;
        detectionRange = horizontalDetectionRange;
        detectionVerticalRange = allowedDetectionVerticalRange;
        disengageRange = Mathf.Max(detectionRange, horizontalDisengageRange);
        disengageVerticalRange = Mathf.Max(detectionVerticalRange, allowedDisengageVerticalRange);
    }

    private void Awake()
    {
        patrol = GetComponent<EnemyPatrol2D>();
        enemyCollider = GetComponent<Collider2D>();
        animators = GetComponentsInChildren<Animator>(true);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        if (!bodyColliderAligned)
            AlignColliderWithVisibleBody();

        if (player == null)
            foreach(var candidate in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                if(candidate.gameObject.scene==gameObject.scene){player=candidate;break;}
        if (player == null || player.IsDead)
            return;

        bool inRange = IsInRange();
        bool detected = IsPlayerDetected();
        if (inRange && !playerWasInAttackRange && Time.time >= nextAlertSoundTime)
        {
            GameAudio.Play(GameSound.EnemyAlert);
            nextAlertSoundTime = Time.time + 1.5f;
        }
        playerWasInAttackRange = inRange;
        if (homePlatform == null)
            homePlatform = FindPlatformBelow(enemyCollider);
        if (!alerted && detected)
        {
            // The player and enemy are necessarily on the same floor when melee
            // range is first entered. This fallback handles composite platforms
            // whose child collider is difficult to find below the enemy pivot.
            if (homePlatform == null)
                homePlatform = FindPlatformBelow(player.GetComponent<Collider2D>());
            alerted = true;
        }

        if (alerted && !IsPlayerWithinDisengageRange())
        {
            alerted = false;
            patrol.StopChasing();
        }

        if (alerted)
        {
            patrol.Chase(player.transform);
            patrol.SetPaused(inRange);
            patrol.FaceTowards(player.transform.position.x);
        }
        else
        {
            patrol.SetPaused(false);
        }

        if (alerted && inRange)
        {
            if (!attacking && Time.time >= nextAttackTime)
                StartCoroutine(Attack());
        }
    }

    private bool IsPlayerOnHomePlatform()
    {
        if (homePlatform == null || player == null)
            return false;
        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider == null)
            return false;

        Bounds playerBounds = playerCollider.bounds;
        bool insideHorizontalSpan = patrol != null && patrol.IsWithinPatrol(playerBounds.center.x, .15f);
        float feetAboveSurface = playerBounds.min.y - homePlatform.bounds.max.y;
        bool nearThisSurface = feetAboveSurface >= -0.2f && feetAboveSurface <= 1.6f;
        return insideHorizontalSpan && nearThisSurface;
    }

    private static Collider2D FindPlatformBelow(Collider2D body)
    {
        if (body == null)
            return null;
        Vector2 origin = new Vector2(body.bounds.center.x, body.bounds.min.y + 0.08f);
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, 2.5f);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.collider == body)
                continue;
            if (IsPlatformCollider(hit.collider.transform))
                return hit.collider;
        }
        return null;
    }

    private static bool IsPlatformCollider(Transform candidate)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current.name.Contains("Platform") || current.name.Contains("Walkable Top"))
                return true;
            current = current.parent;
        }
        return false;
    }

    private void AlignColliderWithVisibleBody()
    {
        BoxCollider2D box = enemyCollider as BoxCollider2D;
        Transform visual = patrol != null ? patrol.VisualRoot : null;
        if (box == null || visual == null)
            return;

        SpriteRenderer[] renderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
        Bounds visibleBounds = default;
        bool found = false;
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null)
                continue;
            if (!found)
            {
                visibleBounds = renderer.bounds;
                found = true;
            }
            else
            {
                visibleBounds.Encapsulate(renderer.bounds);
            }
        }
        if (!found)
            return;

        Vector3 localCenter = transform.InverseTransformPoint(visibleBounds.center);
        float scaleX = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
        float scaleY = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));
        box.offset = new Vector2(localCenter.x, localCenter.y);
        box.size = new Vector2(
            Mathf.Clamp(visibleBounds.size.x / scaleX * 0.62f, 1.05f, 1.6f),
            Mathf.Clamp(visibleBounds.size.y / scaleY * 0.82f, 1.5f, 2.1f));
        box.enabled = true;
        box.isTrigger = true;
        bodyColliderAligned = true;
    }

    private bool IsInRange()
    {
        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (enemyCollider == null || playerCollider == null)
        {
            Vector2 delta = player.transform.position - transform.position;
            return Mathf.Abs(delta.x) <= attackRange && Mathf.Abs(delta.y) <= verticalRange;
        }

        // Measure the visible bodies rather than their transform pivots. This keeps
        // combat correct when a character visual has been repositioned by hand.
        Bounds enemyBounds = enemyCollider.bounds;
        Bounds playerBounds = playerCollider.bounds;
        float horizontalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.x - playerBounds.max.x, playerBounds.min.x - enemyBounds.max.x));
        float verticalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.y - playerBounds.max.y, playerBounds.min.y - enemyBounds.max.y));
        // Melee attacks are only valid on roughly the same floor. A player on a
        // floating platform above the enemy must not be hit through the platform.
        float feetHeightDifference = Mathf.Abs(enemyBounds.min.y - playerBounds.min.y);
        return horizontalGap <= attackRange && verticalGap <= verticalRange && feetHeightDifference <= 0.55f;
    }

    private bool IsPlayerDetected()
    {
        Collider2D playerCollider = player != null ? player.GetComponent<Collider2D>() : null;
        if (enemyCollider == null || playerCollider == null)
        {
            if (player == null) return false;
            Vector2 delta = player.transform.position - transform.position;
            return Mathf.Abs(delta.x) <= detectionRange && Mathf.Abs(delta.y) <= detectionVerticalRange &&
                   IsPlayerInsidePatrolLane(.35f);
        }

        Bounds enemyBounds = enemyCollider.bounds;
        Bounds playerBounds = playerCollider.bounds;
        float horizontalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.x - playerBounds.max.x, playerBounds.min.x - enemyBounds.max.x));
        float verticalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.y - playerBounds.max.y, playerBounds.min.y - enemyBounds.max.y));
        bool closeEnough = horizontalGap <= detectionRange && verticalGap <= detectionVerticalRange;
        return (closeEnough || IsNearCombatAnchor(detectionRange + .85f,
                    Mathf.Max(detectionVerticalRange + .8f, 2.4f))) &&
               IsPlayerInsidePatrolLane(.35f);
    }

    private bool IsPlayerWithinDisengageRange()
    {
        if (player == null || !IsPlayerInsidePatrolLane(.65f))
            return false;

        Collider2D playerCollider = player.GetComponent<Collider2D>();
        if (enemyCollider == null || playerCollider == null)
        {
            Vector2 delta = player.transform.position - transform.position;
            return Mathf.Abs(delta.x) <= disengageRange &&
                   Mathf.Abs(delta.y) <= disengageVerticalRange;
        }

        Bounds enemyBounds = enemyCollider.bounds;
        Bounds playerBounds = playerCollider.bounds;
        float horizontalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.x - playerBounds.max.x, playerBounds.min.x - enemyBounds.max.x));
        float verticalGap = Mathf.Max(0f,
            Mathf.Max(enemyBounds.min.y - playerBounds.max.y, playerBounds.min.y - enemyBounds.max.y));
        return horizontalGap <= disengageRange && verticalGap <= disengageVerticalRange;
    }

    private bool IsPlayerInsidePatrolLane(float margin)
    {
        if (player == null || patrol == null)
            return false;
        Collider2D playerCollider = player.GetComponent<Collider2D>();
        float playerX = playerCollider != null
            ? playerCollider.bounds.center.x
            : player.transform.position.x;
        return patrol.IsWithinPatrol(playerX, margin);
    }

    private bool IsNearCombatAnchor(float horizontalRange, float allowedVerticalRange)
    {
        if (player == null)
            return false;

        Vector3 playerPosition = player.transform.position;
        if (IsPositionNear(playerPosition, transform.position, horizontalRange, allowedVerticalRange))
            return true;

        Transform visual = patrol != null ? patrol.VisualRoot : null;
        return visual != null &&
               IsPositionNear(playerPosition, visual.position, horizontalRange, allowedVerticalRange);
    }

    private static bool IsPositionNear(Vector3 playerPosition, Vector3 enemyPosition,
        float horizontalRange, float allowedVerticalRange)
    {
        return Mathf.Abs(playerPosition.x - enemyPosition.x) <= horizontalRange &&
               Mathf.Abs(playerPosition.y - enemyPosition.y) <= allowedVerticalRange;
    }

    private IEnumerator Attack()
    {
        attacking = true;
        GameAudio.Play(GameSound.EnemyAttack);
        nextAttackTime = Time.time + attackCooldown;
        bool animationPlayed = false;
        foreach (Animator animator in animators)
            if (animator != null && animator.isActiveAndEnabled && HasParameter(animator, "2_Attack"))
            {
                animator.SetTrigger("2_Attack");
                animationPlayed = true;
            }

        // The hit must still work if an imported Animator temporarily has no
        // controller. In that case use the same wind-up without cancelling combat.
        yield return new WaitForSeconds(animationPlayed ? 0.32f : 0.22f);
        if (alerted && player != null && !player.IsDead && IsInRange())
            player.TakeDamage(damage);
        yield return new WaitForSeconds(0.38f);
        attacking = false;
    }

    private static bool HasParameter(Animator animator, string parameterName)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == parameterName)
                return true;
        return false;
    }
}
