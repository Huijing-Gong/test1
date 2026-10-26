using UnityEngine;

public class BattleKnockbackTrail : MonoBehaviour
{
    private const string PuffSpriteResource =
        "VFX/GeneratedKnockbackCloudPuff";
    private const float MinimumKnockbackStrength = 8f;
    private const float FullKnockbackStrength = 34f;

    private static Sprite cachedPuffSprite;

    private Rigidbody fighterBody;
    private Vector3 launchDirection = Vector3.right;
    private Color baseColor;
    private float spawnTimer;
    private float spawnInterval;
    private float spawnElapsed;
    private float maximumSpawnDuration;
    private float puffWidth;
    private float puffLifetime;
    private bool isSpawning;
    private bool upwardLaunchStarted;

    public void Initialise(bool isEnemy)
    {
        fighterBody = GetComponent<Rigidbody>();
        baseColor = isEnemy
            ? new Color(1f, 0.56f, 0.60f, 0.58f)
            : new Color(1f, 1f, 1f, 0.68f);

        EnsureSprite();
    }

    public void Play(
        Vector3 direction,
        float knockbackStrength,
        float damagePercent
    )
    {
        var presentation=GetComponent<BattleMotionPresentation>();
        if(presentation){presentation.Launched(knockbackStrength);return;}
        if (knockbackStrength < MinimumKnockbackStrength)
            return;

        EnsureSprite();
        if (cachedPuffSprite == null)
            return;

        direction.z = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.right;
        }

        launchDirection = direction.normalized;

        float strengthRatio = Mathf.InverseLerp(
            MinimumKnockbackStrength,
            FullKnockbackStrength,
            knockbackStrength
        );
        float damageRatio = Mathf.InverseLerp(
            20f,
            150f,
            damagePercent
        );
        float intensity = Mathf.Clamp01(
            strengthRatio * 0.72f + damageRatio * 0.28f
        );

        spawnInterval = Mathf.Lerp(0.09f, 0.055f, intensity);
        maximumSpawnDuration = Mathf.Lerp(0.24f, 0.55f, intensity);
        puffWidth = Mathf.Lerp(0.62f, 0.96f, intensity) * 2f;
        puffLifetime = Mathf.Lerp(0.34f, 0.58f, intensity);
        spawnTimer = spawnInterval;
        spawnElapsed = 0f;
        upwardLaunchStarted = false;
        isSpawning = true;

        SpawnPuff();
    }

    void Update()
    {
        if (!isSpawning || cachedPuffSprite == null)
            return;

        float deltaTime = Time.deltaTime;
        spawnElapsed += deltaTime;
        spawnTimer -= deltaTime;

        if (fighterBody != null)
        {
            Vector3 velocity = fighterBody.linearVelocity;
            velocity.z = 0f;

            if (velocity.sqrMagnitude > 1f)
            {
                launchDirection = velocity.normalized;
            }

            if (velocity.y > 0.05f)
            {
                upwardLaunchStarted = true;
            }
            else if (upwardLaunchStarted)
            {
                isSpawning = false;
                return;
            }
        }

        if (spawnElapsed >= maximumSpawnDuration)
        {
            isSpawning = false;
            return;
        }

        if (spawnTimer <= 0f)
        {
            SpawnPuff();
            spawnTimer = spawnInterval;
        }
    }

    void EnsureSprite()
    {
        if (cachedPuffSprite != null)
            return;

        cachedPuffSprite = Resources.Load<Sprite>(PuffSpriteResource);

        if (cachedPuffSprite == null)
        {
            Debug.LogWarning(
                "Generated knockback cloud puff sprite could not be loaded."
            );
        }
    }

    void SpawnPuff()
    {
        if (cachedPuffSprite == null)
            return;

        Vector3 perpendicular = new Vector3(
            -launchDirection.y,
            launchDirection.x,
            0f
        );
        Vector3 position = transform.position -
            launchDirection * Random.Range(0.28f, 0.42f) +
            perpendicular * Random.Range(-0.09f, 0.09f);
        position.z = -0.58f;

        BattleKnockbackCloudPuff.Create(
            cachedPuffSprite,
            position,
            baseColor,
            puffWidth * Random.Range(0.88f, 1.08f),
            puffLifetime * Random.Range(0.90f, 1.10f)
        );
    }

    void OnDisable()
    {
        isSpawning = false;
    }
}
