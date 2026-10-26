using UnityEngine;

public class BattleAttackSweep : MonoBehaviour
{
    private const float TrailFadeTime = 0.14f;
    private const string CrescentSpriteResource =
        "VFX/GeneratedGoldenAttackCrescent";
    private const string JabSpriteResource =
        "VFX/OpenGameArtHighResSlashes/JabMedium";
    private const string LeapSpriteResource =
        "VFX/OpenGameArtHighResSlashes/LeapMedium";

    private static Sprite cachedCrescentSprite;
    private static Sprite cachedJabSprite;
    private static Sprite cachedLeapSprite;

    private TrailRenderer trail;
    private Transform sweepTransform;
    private SpriteRenderer crescentRenderer;
    private SpriteRenderer crescentCoreRenderer;
    private Transform crescentTransform;
    private Material trailMaterial;
    private Color crescentBaseColor;
    private float crescentBaseScale;
    private float sweepDuration;
    private float sweepElapsed;
    private float direction = 1f;
    private bool usesWeapon;
    private bool isFinisher;
    private bool isUppercut;
    private bool isLedgeStrike;
    private Vector3 ledgeStrikeStart;
    private Vector3 ledgeStrikeEnd;
    private float ledgeStrikeAngle;
    private float ledgeStrikeContactProgress = 0.62f;
    private int currentComboStep = -1;
    private bool isPlaying;
    private bool isEnemy;
    private bool loggedJabVfx;
    private bool loggedLeapVfx;
    private bool isPlayerJ;
    private BattleJMove playerJMove;
    private Vector3 playerJCenter;
    private float playerJContactProgress;

    public void PlayJ(float horizontalDirection,float startup,float active,Vector3 center,
        Vector3 halfExtents,BattleJMove move,int step)
    {
        Play(horizontalDirection,startup+active,move==BattleJMove.Side || move==BattleJMove.Low,step,false);
        isPlayerJ=true; playerJMove=move; playerJCenter=center;
        sweepDuration=startup+active+.12f;
        playerJContactProgress=startup/sweepDuration;
        trail.startWidth=move==BattleJMove.Upper ? .15f : .11f;
        trail.endWidth=.018f; trail.time=.11f;
        ConfigureCrescent();
        trail.Clear(); sweepTransform.localPosition=CalculateSweepPosition(0);
    }

    public void Initialise(bool belongsToEnemy)
    {
        isEnemy = belongsToEnemy;
        EnsureTrail();
        ConfigureColor();
    }

    public void Play(
        float horizontalDirection,
        float duration,
        bool weaponSweep,
        int comboStep,
        bool uppercutSweep = false
    )
    {
        EnsureTrail();
        ConfigureColor();

        direction = horizontalDirection >= 0f ? 1f : -1f;
        float readableVfxTail = comboStep == 0 || comboStep == 1
            ? 0.15f
            : 0f;
        sweepDuration = Mathf.Max(0.16f, duration + readableVfxTail);
        sweepElapsed = 0f;
        usesWeapon = weaponSweep;
        currentComboStep = comboStep;
        isFinisher = comboStep == BattleFourHitCombo.HitCount - 1;
        isUppercut = uppercutSweep;
        isLedgeStrike = false;
        isPlayerJ = false;
        isPlaying = true;

        float enemyWidthMultiplier = isEnemy ? 1.18f : 1f;
        trail.startWidth = (isUppercut
            ? 0.27f
            : isFinisher
                ? 0.18f
                : currentComboStep == 1
                    ? 0.15f
                    : 0.095f) *
            enemyWidthMultiplier;
        trail.endWidth = (isUppercut
            ? 0.055f
            : isFinisher
                ? 0.035f
                : currentComboStep == 1
                    ? 0.026f
                    : 0.016f) *
            enemyWidthMultiplier;
        trail.time = isUppercut
            ? 0.24f
            : isFinisher
                ? 0.19f
                : TrailFadeTime;
        ConfigureCrescent();

        trail.emitting = false;
        trail.Clear();
        sweepTransform.localPosition = CalculateSweepPosition(0f);
        trail.emitting = true;
    }

    public void PlayLedgeStrike(
        float horizontalDirection,
        float startupDuration,
        float activeDuration,
        Vector3 worldOrigin,
        Vector3 worldTarget
    )
    {
        EnsureTrail();
        ConfigureColor();

        direction = horizontalDirection >= 0f ? 1f : -1f;
        sweepDuration = Mathf.Max(
            0.08f,
            startupDuration + activeDuration + 0.15f
        );
        ledgeStrikeContactProgress = Mathf.Clamp(
            startupDuration / sweepDuration,
            0.05f,
            1f
        );
        sweepElapsed = 0f;
        usesWeapon = false;
        currentComboStep = 0;
        isFinisher = false;
        isUppercut = false;
        isLedgeStrike = true;
        isPlayerJ = false;
        isPlaying = true;

        ledgeStrikeStart = transform.InverseTransformPoint(worldOrigin);
        ledgeStrikeStart.z = -0.72f;
        ledgeStrikeEnd = transform.InverseTransformPoint(worldTarget);
        ledgeStrikeEnd.z = -0.72f;
        Vector3 strikeDelta = ledgeStrikeEnd - ledgeStrikeStart;
        ledgeStrikeAngle = Mathf.Atan2(
            strikeDelta.y,
            strikeDelta.x
        ) * Mathf.Rad2Deg;

        float enemyWidthMultiplier = isEnemy ? 1.18f : 1f;
        trail.startWidth = 0.18f * enemyWidthMultiplier;
        trail.endWidth = 0.028f * enemyWidthMultiplier;
        trail.time = 0.20f;
        ConfigureCrescent();

        trail.emitting = false;
        trail.Clear();
        sweepTransform.localPosition = CalculateSweepPosition(0f);
        trail.emitting = true;
    }

    public void SnapLedgeContact(Vector3 worldTarget)
    {
        if (!isPlaying || !isLedgeStrike || sweepTransform == null)
            return;

        ledgeStrikeEnd = transform.InverseTransformPoint(worldTarget);
        ledgeStrikeEnd.z = -0.72f;
        Vector3 strikeDelta = ledgeStrikeEnd - ledgeStrikeStart;
        ledgeStrikeAngle = Mathf.Atan2(
            strikeDelta.y,
            strikeDelta.x
        ) * Mathf.Rad2Deg;
        sweepTransform.localPosition = ledgeStrikeEnd;
        UpdateCrescent(ledgeStrikeContactProgress);
    }

    public void Stop()
    {
        isPlaying = false;
        if (trail != null)
        {
            trail.emitting = false;
        }

        if (crescentRenderer != null)
        {
            crescentRenderer.enabled = false;
        }

        if (crescentCoreRenderer != null)
        {
            crescentCoreRenderer.enabled = false;
        }
    }

    public void SetDirection(float horizontalDirection)
    {
        if (!isPlaying || Mathf.Abs(horizontalDirection) < 0.01f)
            return;

        float newDirection = horizontalDirection >= 0f ? 1f : -1f;
        if (Mathf.Approximately(newDirection, direction))
            return;

        direction = newDirection;
        trail.emitting = false;
        trail.Clear();
        sweepTransform.localPosition = CalculateSweepPosition(
            Mathf.Clamp01(sweepElapsed / sweepDuration)
        );
        trail.emitting = true;

        ApplyCrescentFlip();
    }

    void Update()
    {
        if (!isPlaying || trail == null)
            return;

        sweepElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(sweepElapsed / sweepDuration);
        sweepTransform.localPosition = CalculateSweepPosition(progress);
        UpdateCrescent(progress);

        if (progress >= 1f)
        {
            Stop();
        }
    }

    Vector3 CalculateSweepPosition(float progress)
    {
        if (isPlayerJ)
        {
            float strike=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress/Mathf.Max(.01f,playerJContactProgress)));
            float x=Mathf.Lerp(.15f,playerJMove==BattleJMove.Side ? 1.48f : 1.12f,strike)*direction;
            float y=playerJCenter.y+Mathf.Sin(strike*Mathf.PI)*.09f;
            if(playerJMove==BattleJMove.Upper) {x=.25f*direction; y=Mathf.Lerp(-.1f,1.45f,strike);}
            return new Vector3(x,y,-.72f);
        }
        if (isLedgeStrike)
        {
            float strikeProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    progress /
                    Mathf.Max(0.01f, ledgeStrikeContactProgress)
                )
            );
            Vector3 controlPoint = Vector3.Lerp(
                ledgeStrikeStart,
                ledgeStrikeEnd,
                0.44f
            ) + Vector3.up * 0.32f +
                Vector3.right * direction * 0.24f;
            float inverse = 1f - strikeProgress;
            return inverse * inverse * ledgeStrikeStart +
                2f * inverse * strikeProgress * controlPoint +
                strikeProgress * strikeProgress * ledgeStrikeEnd;
        }

        if (isUppercut)
        {
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            float uppercutHorizontal = Mathf.Lerp(
                -0.96f * direction,
                0.98f * direction,
                easedProgress
            );
            float uppercutVertical = Mathf.Lerp(
                0.28f,
                2.08f,
                easedProgress
            ) +
                Mathf.Sin(progress * Mathf.PI) * 0.30f;
            return new Vector3(
                uppercutHorizontal,
                uppercutVertical,
                -0.72f
            );
        }

        float reach = isFinisher
            ? 1.72f
            : currentComboStep == 1 || usesWeapon
                ? 1.58f
                : 1.30f;
        float rearReach = isFinisher
            ? 1.08f
            : currentComboStep == 1
                ? 0.90f
                : 0.62f;
        float startX = -rearReach * direction;
        float endX = reach * direction;
        float horizontal = Mathf.Lerp(startX, endX, progress);
        float arcHeight = Mathf.Sin(progress * Mathf.PI) *
            (isFinisher
                ? 0.72f
                : currentComboStep == 1
                    ? 0.68f
                    : 0.34f);
        float vertical = currentComboStep == 1
            ? Mathf.Lerp(0.52f, 1.10f, progress) + arcHeight
            : Mathf.Lerp(0.78f, 0.94f, progress) + arcHeight;
        return new Vector3(horizontal, vertical, -0.72f);
    }

    void EnsureTrail()
    {
        cachedCrescentSprite = LoadEffectSprite(
            cachedCrescentSprite,
            CrescentSpriteResource
        );
        cachedJabSprite = LoadEffectSprite(
            cachedJabSprite,
            JabSpriteResource
        );
        cachedLeapSprite = LoadEffectSprite(
            cachedLeapSprite,
            LeapSpriteResource
        );

        EnsureCrescentRenderers();

        if (trail != null)
            return;

        GameObject trailObject = new GameObject("AttackSweepTrail");
        sweepTransform = trailObject.transform;
        sweepTransform.SetParent(transform, false);
        trail = trailObject.AddComponent<TrailRenderer>();

        Shader trailShader = Shader.Find("Sprites/Default");
        if (trailShader != null)
        {
            trailMaterial = new Material(trailShader);
            trail.material = trailMaterial;
        }

        trail.time = TrailFadeTime;
        trail.minVertexDistance = 0.025f;
        trail.startWidth = 0.12f;
        trail.endWidth = 0.02f;
        trail.numCapVertices = 4;
        trail.numCornerVertices = 4;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.sortingOrder = 208;
        trail.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
    }

    void EnsureCrescentRenderers()
    {
        if (crescentRenderer != null)
            return;

        if (cachedCrescentSprite != null ||
            cachedJabSprite != null ||
            cachedLeapSprite != null)
        {
            GameObject crescentObject = new GameObject("AttackCrescent");
            crescentTransform = crescentObject.transform;
            crescentTransform.SetParent(transform, false);
            crescentRenderer =
                crescentObject.AddComponent<SpriteRenderer>();
            crescentRenderer.sortingOrder = 210;
            crescentRenderer.enabled = false;

            GameObject coreObject = new GameObject(
                "AttackCrescentCore"
            );
            coreObject.transform.SetParent(crescentTransform, false);
            coreObject.transform.localPosition =
                new Vector3(0f, 0f, -0.02f);
            coreObject.transform.localScale = Vector3.one * 0.88f;
            crescentCoreRenderer =
                coreObject.AddComponent<SpriteRenderer>();
            crescentCoreRenderer.sortingOrder = 211;
            crescentCoreRenderer.enabled = false;
        }
    }

    static Sprite LoadEffectSprite(Sprite cachedSprite, string resourcePath)
    {
        if (cachedSprite != null)
            return cachedSprite;

        Sprite importedSprite = Resources.Load<Sprite>(resourcePath);
        if (importedSprite != null)
            return importedSprite;

        Texture2D importedTexture = Resources.Load<Texture2D>(resourcePath);
        if (importedTexture == null)
            return null;

        Sprite runtimeSprite = Sprite.Create(
            importedTexture,
            new Rect(0f, 0f, importedTexture.width, importedTexture.height),
            new Vector2(0.5f, 0.5f),
            160f,
            0u,
            SpriteMeshType.FullRect
        );
        runtimeSprite.name = importedTexture.name + "RuntimeSprite";
        return runtimeSprite;
    }

    void ConfigureColor()
    {
        if (trail == null)
            return;

        Color brightColor = isEnemy
            ? new Color(0.62f, 0.012f, 0.018f, 0.72f)
            : new Color(1f, 0.98f, 0.74f, 0.82f);
        Color middleColor = isEnemy
            ? new Color(0.095f, 0.002f, 0.005f, 0.66f)
            : new Color(1f, 0.58f, 0.06f, 0.62f);
        Color fadedColor = isEnemy
            ? new Color(0.015f, 0f, 0.002f, 0f)
            : new Color(1f, 0.30f, 0.01f, 0f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(brightColor, 0f),
                new GradientColorKey(middleColor, 0.55f),
                new GradientColorKey(fadedColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(brightColor.a, 0f),
                new GradientAlphaKey(0.64f, 0.60f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        trail.colorGradient = gradient;
    }

    void ConfigureCrescent()
    {
        if (crescentRenderer == null)
            return;

        Sprite selectedSprite = currentComboStep == 0 &&
            cachedJabSprite != null
                ? cachedJabSprite
                : currentComboStep == 1 &&
                    cachedLeapSprite != null
                    ? cachedLeapSprite
                    : cachedCrescentSprite;
        if (isPlayerJ && playerJMove!=BattleJMove.Neutral && cachedCrescentSprite)
            selectedSprite=cachedCrescentSprite;
        if (selectedSprite == null)
        {
            Debug.LogWarning(
                "[ComboVFX] Missing sprite for combo step " +
                (currentComboStep + 1) + " on " + name + ".",
                this
            );
            crescentRenderer.enabled = false;
            if (crescentCoreRenderer != null)
            {
                crescentCoreRenderer.enabled = false;
            }
            return;
        }

        if (currentComboStep == 0 && !loggedJabVfx)
        {
            loggedJabVfx = true;
            Debug.Log(
                "[ComboVFX] 1A loaded " + selectedSprite.name +
                ", world bounds " + selectedSprite.bounds.size + ".",
                this
            );
        }
        else if (currentComboStep == 1 && !loggedLeapVfx)
        {
            loggedLeapVfx = true;
            Debug.Log(
                "[ComboVFX] 2A loaded " + selectedSprite.name +
                ", world bounds " + selectedSprite.bounds.size + ".",
                this
            );
        }

        crescentRenderer.sprite = selectedSprite;
        if (crescentCoreRenderer != null)
        {
            crescentCoreRenderer.sprite = selectedSprite;
        }

        float desiredWorldWidth = isLedgeStrike
            ? Mathf.Clamp(
                Vector3.Distance(ledgeStrikeStart, ledgeStrikeEnd) *
                    1.20f,
                2.35f,
                3.75f
            )
            : isUppercut
            ? 4.65f
            : isFinisher
                ? 4.15f
                : currentComboStep == 1
                    ? 4.35f
                    : currentComboStep == 0
                        ? 3.90f
                        : 3.25f;
        if(isPlayerJ) desiredWorldWidth=playerJMove==BattleJMove.Side ? 1.9f : playerJMove==BattleJMove.Upper ? 1.7f : 1.45f;
        crescentBaseScale = desiredWorldWidth /
            Mathf.Max(0.01f, selectedSprite.bounds.size.x);
        if (isEnemy)
        {
            crescentBaseScale *= 1.05f;
        }
        crescentBaseColor = isEnemy
            ? new Color(0.075f, 0.002f, 0.005f, 0.99f)
            : isFinisher || isUppercut
                ? new Color(1f, 1f, 1f, 1f)
                : currentComboStep == 1
                    ? new Color(1f, 0.58f, 0.08f, 1f)
                    : new Color(1f, 0.70f, 0.12f, 1f);

        ApplyCrescentFlip();
        crescentRenderer.color = crescentBaseColor;
        crescentRenderer.enabled = true;

        if (crescentCoreRenderer != null)
        {
            crescentCoreRenderer.flipX = crescentRenderer.flipX;
            crescentCoreRenderer.color = isEnemy
                ? new Color(
                    0.92f,
                    0.018f,
                    0.025f,
                    isFinisher ? 0.98f : 0.90f
                )
                : new Color(1f, 0.98f, 0.82f, 1f);
            crescentCoreRenderer.enabled = true;
        }
        UpdateCrescent(0f);
    }

    void UpdateCrescent(float progress)
    {
        if (crescentRenderer == null || !crescentRenderer.enabled)
            return;

        float expansion = Mathf.Lerp(
            0.66f,
            1f,
            Mathf.SmoothStep(0f, 0.48f, progress)
        );
        crescentTransform.localScale = Vector3.one *
            crescentBaseScale * expansion;
        if (isPlayerJ)
        {
            crescentTransform.localPosition=new Vector3(playerJCenter.x*direction,playerJCenter.y,-.74f);
            crescentTransform.localRotation=Quaternion.Euler(0,0,playerJMove==BattleJMove.Upper ? 70f*direction : playerJMove==BattleJMove.Low ? -8f*direction : 0f);
        }
        else if (isLedgeStrike)
        {
            crescentTransform.localPosition = Vector3.Lerp(
                ledgeStrikeStart,
                ledgeStrikeEnd,
                0.56f
            );
            crescentTransform.localPosition +=
                new Vector3(0f, 0.08f, -0.02f);
            crescentTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                ledgeStrikeAngle
            );
        }
        else if (isUppercut)
        {
            crescentTransform.localPosition = new Vector3(
                Mathf.Lerp(-0.30f, 0.46f, progress) * direction,
                Mathf.Lerp(0.55f, 1.48f, progress),
                -0.74f
            );
            crescentTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Lerp(-34f, 58f, progress) * direction
            );
        }
        else
        {
            if (currentComboStep == 1)
            {
                crescentTransform.localPosition = new Vector3(
                    Mathf.Lerp(-0.72f, 0.94f, progress) * direction,
                    Mathf.Lerp(0.62f, 1.18f, progress),
                    -0.74f
                );
                crescentTransform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(-18f, 27f, progress) * direction
                );
            }
            else
            {
                crescentTransform.localPosition = new Vector3(
                    Mathf.Lerp(-0.62f, 1.02f, progress) * direction,
                    Mathf.Lerp(0.84f, 1.00f, progress),
                    -0.74f
                );
                crescentTransform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(-4f, 4f, progress) * direction
                );
            }
        }

        float fadeStart = isLedgeStrike
            ? 0.76f
            : isFinisher
            ? 0.52f
            : currentComboStep == 1
                ? 0.90f
                : 0.88f;
        float fade = 1f - Mathf.SmoothStep(fadeStart, 1f, progress);
        crescentRenderer.color = new Color(
            crescentBaseColor.r,
            crescentBaseColor.g,
            crescentBaseColor.b,
            crescentBaseColor.a * fade
        );

        if (crescentCoreRenderer != null)
        {
            Color coreColor = crescentCoreRenderer.color;
            float coreMaximumAlpha = isFinisher
                ? 0.98f
                : isEnemy
                    ? 0.90f
                    : 1f;
            coreColor.a = coreMaximumAlpha * fade;
            crescentCoreRenderer.color = coreColor;
        }
    }

    void ApplyCrescentFlip()
    {
        if (crescentRenderer == null)
            return;

        bool sourcePointsLeft = currentComboStep == 0 &&
            cachedJabSprite != null;
        crescentRenderer.flipX = sourcePointsLeft
            ? direction > 0f
            : direction < 0f;

        if (crescentCoreRenderer != null)
        {
            crescentCoreRenderer.flipX = crescentRenderer.flipX;
        }
    }

    void OnDestroy()
    {
        if (trailMaterial != null)
        {
            Destroy(trailMaterial);
        }
    }
}
