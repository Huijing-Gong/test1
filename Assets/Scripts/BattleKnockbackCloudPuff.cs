using UnityEngine;

public class BattleKnockbackCloudPuff : MonoBehaviour
{
    private SpriteRenderer puffRenderer;
    private Color baseColor;
    private Vector3 startingScale;
    private Vector3 driftVelocity;
    private float elapsed;
    private float lifetime;

    public static void Create(
        Sprite sprite,
        Vector3 position,
        Color colour,
        float worldWidth,
        float duration
    )
    {
        GameObject puffObject = new GameObject("KnockbackCloudPuff");
        puffObject.transform.position = position;
        puffObject.transform.rotation = Quaternion.Euler(
            0f,
            0f,
            Random.Range(-8f, 8f)
        );

        BattleKnockbackCloudPuff puff =
            puffObject.AddComponent<BattleKnockbackCloudPuff>();
        puff.Build(sprite, colour, worldWidth, duration);
    }

    void Build(
        Sprite sprite,
        Color colour,
        float worldWidth,
        float duration
    )
    {
        puffRenderer = gameObject.AddComponent<SpriteRenderer>();
        puffRenderer.sprite = sprite;
        puffRenderer.color = colour;
        puffRenderer.sortingOrder = 84;

        float spriteWidth = Mathf.Max(0.001f, sprite.bounds.size.x);
        float scale = worldWidth / spriteWidth;
        startingScale = Vector3.one * scale;
        transform.localScale = startingScale * 0.82f;

        baseColor = colour;
        lifetime = Mathf.Max(0.1f, duration);
        driftVelocity = new Vector3(
            Random.Range(-0.08f, 0.08f),
            Random.Range(0.12f, 0.24f),
            0f
        );
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / lifetime);

        transform.position += driftVelocity * Time.deltaTime;
        transform.localScale = startingScale * Mathf.Lerp(
            0.82f,
            1.16f,
            progress
        );

        float fade = 1f - Mathf.SmoothStep(0.28f, 1f, progress);
        puffRenderer.color = new Color(
            baseColor.r,
            baseColor.g,
            baseColor.b,
            baseColor.a * fade
        );

        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}
