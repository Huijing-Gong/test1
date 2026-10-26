using System.Collections;
using UnityEngine;

public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private PlayerHealthHud hud;

    private int currentHealth;
    private bool dead;
    private SpriteRenderer spriteRenderer;
    private Color normalColor;

    public bool IsDead => dead;

    public void SetMaximumHealth(int health)
    {
        maxHealth = Mathf.Max(10, health);
        currentHealth = maxHealth;
        dead = false;
        if (hud != null)
        {
            hud.SetHeartCount(Mathf.CeilToInt(maxHealth / 10f));
            hud.SetHealth(1f);
        }
    }

    public void Configure(PlayerHealthHud healthHud, int health)
    {
        hud = healthHud;
        maxHealth = health;
        currentHealth = maxHealth;
        hud.SetHealth(1f);
    }

    private void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            normalColor = spriteRenderer.color;
    }

    public void TakeDamage(int amount)
    {
        if (dead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        GameAudio.Play(GameSound.PlayerHurt);
        if (hud != null)
            hud.SetHealth(currentHealth / (float)maxHealth);

        if (currentHealth == 0)
        {
            GameAudio.Play(GameSound.PlayerDeath);
            GameAudio.PauseMusicForDeath();
            dead = true;
            StopAllCoroutines();
            GetComponent<PlayerCombat2D>().enabled = false;
            FighterMeleeCombat2D fighterCombat = GetComponent<FighterMeleeCombat2D>();
            if (fighterCombat != null)
                fighterCombat.enabled = false;
            GetComponent<PlayerController2D>().Die();
            if (hud != null)
                hud.ShowFailure();
            return;
        }

        GetComponent<PlayerController2D>().PlayHurt(0.25f);
        StartCoroutine(HitFlash());
    }

    public bool Heal(int amount)
    {
        if (dead || currentHealth >= maxHealth)
            return false;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        GameAudio.Play(GameSound.Heal);
        if (hud != null)
            hud.SetHealth(currentHealth / (float)maxHealth);
        StopAllCoroutines();
        StartCoroutine(HealFlash());
        return true;
    }

    private IEnumerator HitFlash()
    {
        if (spriteRenderer == null)
            yield break;
        spriteRenderer.color = new Color(1f, 0.35f, 0.5f, 1f);
        yield return new WaitForSeconds(0.14f);
        spriteRenderer.color = normalColor;
    }

    private IEnumerator HealFlash()
    {
        if (spriteRenderer == null)
            yield break;
        spriteRenderer.color = new Color(0.35f, 1f, 0.4f, 1f);
        yield return new WaitForSeconds(0.35f);
        spriteRenderer.color = normalColor;
    }
}
