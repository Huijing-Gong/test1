using UnityEngine;

public class BattleUppercutBurst : MonoBehaviour
{
    private const float Lifetime = 0.40f;
    private const int StreakCount = 7;
    private const int RingPointCount = 40;

    private readonly Transform[] streakTransforms =
        new Transform[StreakCount];
    private readonly Vector3[] streakVelocities =
        new Vector3[StreakCount];
    private readonly TrailRenderer[] streakTrails =
        new TrailRenderer[StreakCount];
    private readonly Material[] effectMaterials =
        new Material[StreakCount + 1];

    private LineRenderer shockRing;
    private Color energyColor;
    private Color coreColor;
    private float elapsed;

    public static void Create(
        Vector3 position,
        float horizontalDirection,
        bool isEnemyAttack
    )
    {
        GameObject burstObject = new GameObject("UppercutEnergyBurst");
        position.z = -0.70f;
        burstObject.transform.position = position;

        BattleUppercutBurst burst =
            burstObject.AddComponent<BattleUppercutBurst>();
        burst.Build(horizontalDirection, isEnemyAttack);
    }

    void Build(float horizontalDirection, bool isEnemyAttack)
    {
        float direction = horizontalDirection >= 0f ? 1f : -1f;
        energyColor = isEnemyAttack
            ? new Color(0.22f, 0.002f, 0.006f, 0.92f)
            : new Color(1f, 0.46f, 0.015f, 0.94f);
        coreColor = isEnemyAttack
            ? new Color(1f, 0.035f, 0.012f, 1f)
            : new Color(1f, 0.98f, 0.72f, 1f);

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            Destroy(gameObject);
            return;
        }

        CreateShockRing(spriteShader, direction);

        float[] horizontalSpread =
        {
            -0.34f,
            -0.18f,
            -0.04f,
            0.10f,
            0.25f,
            0.43f,
            0.62f
        };
        float[] verticalSpeed =
        {
            5.8f,
            7.2f,
            8.7f,
            10.4f,
            9.2f,
            7.8f,
            6.4f
        };

        for (int i = 0; i < StreakCount; i++)
        {
            CreateEnergyStreak(
                i,
                spriteShader,
                direction,
                horizontalSpread[i],
                verticalSpeed[i]
            );
        }
    }

    void CreateShockRing(Shader shader, float direction)
    {
        GameObject ringObject = new GameObject("UppercutShockRing");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localRotation = Quaternion.Euler(
            0f,
            0f,
            28f * direction
        );

        shockRing = ringObject.AddComponent<LineRenderer>();
        effectMaterials[StreakCount] = new Material(shader);
        shockRing.material = effectMaterials[StreakCount];
        shockRing.useWorldSpace = false;
        shockRing.loop = true;
        shockRing.positionCount = RingPointCount;
        shockRing.startWidth = 0.10f;
        shockRing.endWidth = 0.10f;
        shockRing.numCornerVertices = 3;
        shockRing.sortingOrder = 103;
        shockRing.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        shockRing.receiveShadows = false;

        for (int i = 0; i < RingPointCount; i++)
        {
            float angle = i * Mathf.PI * 2f / RingPointCount;
            shockRing.SetPosition(
                i,
                new Vector3(
                    Mathf.Cos(angle) * 0.92f,
                    Mathf.Sin(angle) * 0.42f,
                    0f
                )
            );
        }

        shockRing.startColor = coreColor;
        shockRing.endColor = energyColor;
        ringObject.transform.localScale = Vector3.one * 0.18f;
    }

    void CreateEnergyStreak(
        int index,
        Shader shader,
        float direction,
        float horizontalSpread,
        float verticalSpeed
    )
    {
        GameObject streakObject = new GameObject(
            "UppercutEnergyStreak_" + index
        );
        Transform streakTransform = streakObject.transform;
        streakTransform.SetParent(transform, false);
        streakTransform.localPosition = new Vector3(
            horizontalSpread * direction * 0.22f,
            -0.12f + index % 2 * 0.08f,
            -0.01f * index
        );

        TrailRenderer trail = streakObject.AddComponent<TrailRenderer>();
        Material material = new Material(shader);
        effectMaterials[index] = material;
        trail.material = material;
        trail.time = Mathf.Lerp(0.13f, 0.22f, index / 6f);
        trail.minVertexDistance = 0.018f;
        trail.startWidth = index == 3 ? 0.24f : 0.11f;
        trail.endWidth = 0.012f;
        trail.numCapVertices = 3;
        trail.numCornerVertices = 3;
        trail.alignment = LineAlignment.View;
        trail.sortingOrder = 102 - index % 2;
        trail.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(coreColor, 0f),
                new GradientColorKey(energyColor, 0.42f),
                new GradientColorKey(energyColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.78f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        trail.colorGradient = gradient;

        streakTransforms[index] = streakTransform;
        streakTrails[index] = trail;
        streakVelocities[index] = new Vector3(
            direction * (1.9f + horizontalSpread * 3.8f),
            verticalSpeed,
            0f
        );
        trail.Clear();
        trail.emitting = true;
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        elapsed += deltaTime;
        float progress = Mathf.Clamp01(elapsed / Lifetime);

        for (int i = 0; i < StreakCount; i++)
        {
            if (streakTransforms[i] == null)
                continue;

            streakTransforms[i].position +=
                streakVelocities[i] * deltaTime;
            streakVelocities[i] += Vector3.down * 5.2f * deltaTime;
            streakVelocities[i] *= Mathf.Pow(0.18f, deltaTime);

            if (progress > 0.58f && streakTrails[i] != null)
            {
                streakTrails[i].emitting = false;
            }
        }

        if (shockRing != null)
        {
            float ringExpansion = 1f - Mathf.Pow(1f - progress, 3f);
            shockRing.transform.localScale = Vector3.one *
                Mathf.Lerp(0.18f, 1.72f, ringExpansion);
            float ringFade = 1f - Mathf.SmoothStep(0.12f, 0.76f, progress);
            shockRing.startColor = new Color(
                coreColor.r,
                coreColor.g,
                coreColor.b,
                ringFade
            );
            shockRing.endColor = new Color(
                energyColor.r,
                energyColor.g,
                energyColor.b,
                energyColor.a * ringFade * 0.70f
            );
        }

        if (elapsed >= Lifetime)
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        foreach (Material material in effectMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
