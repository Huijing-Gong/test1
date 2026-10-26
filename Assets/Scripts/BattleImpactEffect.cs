using UnityEngine;

public class BattleImpactEffect : MonoBehaviour
{
    private const string ImpactSpriteResource =
        "VFX/GeneratedMeleeImpact";
    private const string FinisherSpriteResource =
        "VFX/GeneratedComboFinisherImpact";

    private static Sprite cachedImpactSprite;
    private static Sprite cachedFinisherSprite;

    private SpriteRenderer impactRenderer;
    private SpriteRenderer coreRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer uppercutEchoNearRenderer;
    private SpriteRenderer uppercutEchoFarRenderer;
    private Color effectColor;
    private float elapsed;
    private float lifetime;
    private float maximumScale;
    private float glowMaximumAlpha;
    private bool isFinisherEffect;
    private bool isUppercutEffect;

    public static void Create(
        Vector3 position,
        float horizontalDirection,
        int comboStep,
        bool isEnemyAttack,
        float targetDamagePercent,
        bool isUppercut = false
    )
    {
        GameObject effectObject = new GameObject("ComboHitImpact");
        position.z = -0.65f;
        effectObject.transform.position = position;
        float rotation = isUppercut
            ? (horizontalDirection >= 0f ? 43f : -43f)
            : comboStep == 0
                ? -8f * horizontalDirection
                : comboStep == 1
                    ? 16f * horizontalDirection
                    : Random.Range(-10f, 10f);
        effectObject.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        BattleImpactEffect effect =
            effectObject.AddComponent<BattleImpactEffect>();
        effect.Build(
            horizontalDirection,
            comboStep,
            isEnemyAttack,
            targetDamagePercent,
            isUppercut
        );
    }

    void Build(
        float horizontalDirection,
        int comboStep,
        bool isEnemyAttack,
        float targetDamagePercent,
        bool isUppercut
    )
    {
        bool isFinisher = comboStep ==
            BattleFourHitCombo.HitCount - 1;
        isFinisherEffect = isFinisher;
        isUppercutEffect = isUppercut;

        if (cachedImpactSprite == null)
        {
            cachedImpactSprite = Resources.Load<Sprite>(
                ImpactSpriteResource
            );
        }

        if (isFinisher && cachedFinisherSprite == null)
        {
            cachedFinisherSprite = Resources.Load<Sprite>(
                FinisherSpriteResource
            );
        }

        Sprite selectedSprite = isFinisher &&
            cachedFinisherSprite != null
                ? cachedFinisherSprite
                : cachedImpactSprite;

        if (selectedSprite == null)
        {
            Debug.LogWarning(
                "Generated combo impact sprite could not be loaded."
            );
            Destroy(gameObject);
            return;
        }

        lifetime = isUppercut
            ? 0.32f
            : isFinisher
                ? 0.42f
            : comboStep == 0
                ? 0.14f
                : 0.18f;
        maximumScale = isUppercut
            ? 2.85f
            : isFinisher
                ? 4.80f
            : comboStep == 0
                ? 0.62f
                : 0.82f;
        effectColor = isEnemyAttack
            ? new Color(0.10f, 0.004f, 0.008f, 0.98f)
            : isFinisher
                ? new Color(1f, 0.66f, 0.08f, 1f)
                : new Color(1f, 0.58f, 0.06f, 0.90f);

        impactRenderer = gameObject.AddComponent<SpriteRenderer>();
        impactRenderer.sprite = selectedSprite;
        impactRenderer.color = effectColor;
        impactRenderer.sortingOrder = 100;
        impactRenderer.flipX = horizontalDirection < 0f;

        if (isFinisher)
        {
            glowRenderer = CreateFinisherLayer(
                "FinisherGoldGlow",
                selectedSprite,
                horizontalDirection,
                99,
                1.28f,
                isEnemyAttack
                    ? new Color(0.42f, 0.004f, 0.01f, 0.62f)
                    : new Color(1f, 0.42f, 0.02f, 0.58f)
            );
            glowMaximumAlpha = glowRenderer.color.a;
            coreRenderer = CreateFinisherLayer(
                "FinisherWhiteCore",
                selectedSprite,
                horizontalDirection,
                101,
                0.66f,
                isEnemyAttack
                    ? new Color(1f, 0.055f, 0.018f, 1f)
                    : new Color(1f, 0.98f, 0.82f, 1f)
            );

            if (isUppercut)
            {
                uppercutEchoNearRenderer = CreateUppercutEcho(
                    "UppercutGoldEchoNear",
                    selectedSprite,
                    horizontalDirection,
                    98,
                    new Vector3(-0.34f, -0.06f, 0f),
                    0.72f,
                    new Color(1f, 0.52f, 0.025f, 0.42f)
                );
                uppercutEchoFarRenderer = CreateUppercutEcho(
                    "UppercutGoldEchoFar",
                    selectedSprite,
                    horizontalDirection,
                    97,
                    new Vector3(-0.65f, -0.11f, 0f),
                    0.48f,
                    new Color(1f, 0.30f, 0.01f, 0.24f)
                );
            }
            if (targetDamagePercent >= 100f)
            {
                BattleFinisherScreenFlash.Create(isEnemyAttack);
            }
        }
        else
        {
            coreRenderer = CreateFinisherLayer(
                isEnemyAttack
                    ? "NormalHitCrimsonCore"
                    : "NormalHitWarmCore",
                selectedSprite,
                horizontalDirection,
                101,
                0.56f,
                isEnemyAttack
                    ? new Color(0.82f, 0.012f, 0.018f, 0.92f)
                    : new Color(1f, 0.98f, 0.76f, 0.94f)
            );
        }

        UpdateVisuals(0f);
    }

    SpriteRenderer CreateFinisherLayer(
        string objectName,
        Sprite sprite,
        float horizontalDirection,
        int sortingOrder,
        float scale,
        Color color
    )
    {
        GameObject layerObject = new GameObject(objectName);
        layerObject.transform.SetParent(transform, false);
        layerObject.transform.localScale = Vector3.one * scale;

        SpriteRenderer layerRenderer =
            layerObject.AddComponent<SpriteRenderer>();
        layerRenderer.sprite = sprite;
        layerRenderer.color = color;
        layerRenderer.sortingOrder = sortingOrder;
        layerRenderer.flipX = horizontalDirection < 0f;
        return layerRenderer;
    }

    SpriteRenderer CreateUppercutEcho(
        string objectName,
        Sprite sprite,
        float horizontalDirection,
        int sortingOrder,
        Vector3 localPosition,
        float scale,
        Color color
    )
    {
        SpriteRenderer echo = CreateFinisherLayer(
            objectName,
            sprite,
            horizontalDirection,
            sortingOrder,
            scale,
            color
        );
        localPosition.x *= horizontalDirection >= 0f ? 1f : -1f;
        echo.transform.localPosition = localPosition;
        return echo;
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(elapsed / lifetime);
        UpdateVisuals(progress);

        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    void UpdateVisuals(float progress)
    {
        if (impactRenderer == null)
            return;

        float fastExpansion = 1f - Mathf.Pow(1f - progress, 3f);
        float displayScale = Mathf.Lerp(
            0.22f,
            maximumScale,
            fastExpansion
        );
        displayScale *= Mathf.Lerp(1f, 0.88f, progress);
        transform.localScale = Vector3.one * displayScale;

        float fade = 1f - Mathf.SmoothStep(0.35f, 1f, progress);
        impactRenderer.color = new Color(
            effectColor.r,
            effectColor.g,
            effectColor.b,
            effectColor.a * fade
        );

        if (coreRenderer != null)
        {
            float coreFade = isFinisherEffect
                ? 1f - Mathf.SmoothStep(0.12f, 0.72f, progress)
                : 1f - Mathf.SmoothStep(0.20f, 0.86f, progress);
            Color coreColor = coreRenderer.color;
            coreColor.a = coreFade;
            coreRenderer.color = coreColor;
        }

        if (!isFinisherEffect)
            return;

        if (glowRenderer != null)
        {
            float glowFade = 1f - Mathf.SmoothStep(0.22f, 1f, progress);
            Color glowColor = glowRenderer.color;
            glowColor.a = glowMaximumAlpha * glowFade;
            glowRenderer.color = glowColor;
        }

        if (isUppercutEffect)
        {
            UpdateUppercutEcho(
                uppercutEchoNearRenderer,
                0.42f,
                progress
            );
            UpdateUppercutEcho(
                uppercutEchoFarRenderer,
                0.24f,
                progress
            );
        }
    }

    void UpdateUppercutEcho(
        SpriteRenderer echoRenderer,
        float maximumAlpha,
        float progress
    )
    {
        if (echoRenderer == null)
            return;

        float fade = 1f - Mathf.SmoothStep(0.04f, 0.78f, progress);
        Color echoColor = echoRenderer.color;
        echoColor.a = maximumAlpha * fade;
        echoRenderer.color = echoColor;
    }
}
