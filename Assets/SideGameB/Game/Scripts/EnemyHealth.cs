using System.Collections;
using UnityEngine;

public sealed class EnemyHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private WorldHealthBar healthBar;

    private int currentHealth;
    private SpriteRenderer[] spriteRenderers;
    private Color[] normalColors;

    public void Configure(WorldHealthBar bar, int health)
    {
        healthBar = bar;
        maxHealth = health;
        currentHealth = maxHealth;
        if (healthBar != null)
            healthBar.SetHealth(1f);
    }

    private void Awake()
    {
        currentHealth = maxHealth;
        RefreshVisualCache();
    }

    public void RefreshVisualCache()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        normalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            normalColors[i] = spriteRenderers[i].color;
    }

    public void TakeDamage(int amount)
    {
        if (currentHealth <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (healthBar != null)
            healthBar.SetHealth(currentHealth / (float)maxHealth);
        StartCoroutine(HitFlash());

        if (currentHealth == 0)
        {
            Collider2D enemyCollider = GetComponent<Collider2D>();
            if (enemyCollider != null)
                enemyCollider.enabled = false;
            foreach (Animator animator in GetComponentsInChildren<Animator>(true))
                animator.speed = 0f;
            Destroy(gameObject, 0.35f);
        }
    }

    private IEnumerator HitFlash()
    {
        if (spriteRenderers == null)
            yield break;
        foreach (SpriteRenderer renderer in spriteRenderers)
            if (renderer != null)
                renderer.color = new Color(1f, 0.35f, 0.35f, renderer.color.a);
        yield return new WaitForSeconds(0.12f);
        for (int i = 0; i < spriteRenderers.Length; i++)
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = normalColors[i];
    }
}
