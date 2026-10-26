using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BattleCameraController : MonoBehaviour
{
    public static BattleCameraController Instance { get; private set; }

    [Header("Fighter Targets")]
    public Transform playerTarget;
    public Transform enemyTarget;

    [Header("Horizontal Follow")]
    public float stageCentreX = -2f;
    public float horizontalFollowLimit = 6.5f;
    public float cameraHeight = 5f;
    public float cameraDepth = -34f;
    public float positionSmoothTime = 0.2f;

    [Header("Vertical Follow")]
    public float stageFloorY = 1f;
    [Range(0f, 1f)] public float verticalFollowStrength = 1f;
    public float verticalFramingOffset = 0.7f;
    public float minimumCameraHeight = 5f;
    public float maximumCameraHeight = 22f;
    public float verticalRiseSmoothTime = 0.1f;
    public float verticalReturnSmoothTime = 0.28f;

    [Header("Distance Zoom")]
    public float closeSeparation = 6f;
    public float farSeparation = 24f;
    public float closeFieldOfView = 28f;
    public float farFieldOfView = 28f;
    public float zoomOutSmoothTime = 0.1f;
    public float zoomSmoothTime = 0.32f;

    [Header("Vertical Framing")]
    public float fighterHeightPadding = 6f;
    [Range(0.2f, 1f)] public float safeVerticalViewport = 0.62f;
    public float maximumFramingFieldOfView = 55f;

    [Header("Hit Shake")]
    [Min(0f)] public float minimumShakeIntensity = 0.1f;
    [Min(0f)] public float maximumShakeIntensity = 0.42f;
    [Min(0f)] public float minimumShakeDuration = 0.08f;
    [Min(0f)] public float maximumShakeDuration = 0.18f;
    [Min(1f)] public float fullShakeDamagePercent = 150f;
    [Min(1f)] public float shakeFrequency = 30f;

    [Header("Backdrop Readability")]
    public Color backdropWashColor =
        new Color(0.72f, 0.82f, 0.9f, 0.08f);

    private Camera battleCamera;
    private float horizontalVelocity;
    private float verticalVelocity;
    private float fieldOfViewVelocity;
    private float shakeTimer;
    private float shakeDuration;
    private float shakeIntensity;
    private float shakeSeed;
    readonly System.Random shakeRandom=new System.Random(61787);
    private Vector2 shakeAxis=Vector2.one;
    private float presentationShakeClock;
    private Vector3 currentShakeOffset;
    Vector3 impactKick;
    float impactKickTimer;
    const float ImpactKickSeconds=.10f;
    public bool HasImpactKick => impactKickTimer>0;
    private GameObject backdropWash;
    private Material backdropWashMaterial;
    private Bounds? arenaComposition;
    private Quaternion authoredRotation;
    private bool rotationCaptured;
    private float compositionAspect;
    private Vector3 safetyDollyOffset;
    // Roughly 11% larger fighters than the previous 20-degree close shot.
    // Safety framing still widens the lens whenever either fighter needs room.
    public const float CloseCombatFov = 18f;
    public bool IntroOwnsCamera { get; private set; }
    public void BeginIntroCamera(out Vector3 normalPosition, out float normalFov)
    {
        SnapToTargets(); normalPosition = transform.position; normalFov = battleCamera.fieldOfView;
        IntroOwnsCamera = true;
    }
    public void SetIntroCamera(Vector3 position, float fov)
    {
        transform.position = position; battleCamera.fieldOfView = fov;
    }
    public void EndIntroCamera()
    {
        if (!IntroOwnsCamera) return;
        IntroOwnsCamera = false;
        horizontalVelocity = verticalVelocity = fieldOfViewVelocity = 0;
        SnapToTargets();
    }
    float zoomInHold = .18f;
    float lookaheadX;
    Transform sampledPlayer, sampledEnemy;
    Rigidbody playerBody, enemyBody;

    // Stage bounds constrain follow, rather than forcing an always-wide shot.
    public bool HasArenaComposition => arenaComposition.HasValue;
    public Bounds? ArenaCompositionBounds => arenaComposition;
    public void SetArenaComposition(Bounds? bounds)
    {
        if (!rotationCaptured) { authoredRotation = transform.rotation; rotationCaptured = true; }
        arenaComposition = bounds;
        transform.rotation = bounds.HasValue ? Quaternion.Euler(8, 0, 0) : authoredRotation;
        horizontalVelocity = verticalVelocity = fieldOfViewVelocity = 0;
        if (battleCamera && HasTargets()) SnapToTargets();
    }

    void Awake()
    {
        Instance = this;
        battleCamera = GetComponent<Camera>();
        FindMissingTargets();
        CreateBackdropWash();
    }

    void Start()
    {
        SnapToTargets();
    }

    void LateUpdate()
    {
        if (IntroOwnsCamera) return;
        if (Time.timeScale <= 0f)
        {
            // Resizing a paused menu still needs a safe aspect fit. Do not
            // tick follow/shake or alter the legacy stage-1 pause behaviour.
            if (battleCamera && HasTargets() && Mathf.Abs(compositionAspect - battleCamera.aspect) > .001f)
            {
                transform.position -= safetyDollyOffset;
                safetyDollyOffset = Vector3.zero;
                battleCamera.fieldOfView = CalculateTargetFieldOfView();
                EnforceFighterSafety();
                compositionAspect = battleCamera.aspect;
            }
            return;
        }

        if (!HasTargets())
        {
            FindMissingTargets();

            if (!HasTargets())
                return;
        }

        UpdateLookahead(Time.deltaTime);
        float targetX = CalculateTargetX();
        float targetY = CalculateTargetY();
        float targetFieldOfView = CalculateTargetFieldOfView();

        Vector3 cameraPosition = transform.position - currentShakeOffset - safetyDollyOffset;
        float viewHeight = 2f * Mathf.Abs(cameraDepth) * Mathf.Tan(battleCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        targetX = StableFollowTarget(targetX, cameraPosition.x, viewHeight * battleCamera.aspect * .015f);
        targetY = StableFollowTarget(targetY, cameraPosition.y, viewHeight * .008f);
        if (Mathf.Approximately(targetX, cameraPosition.x)) horizontalVelocity = 0;
        if (Mathf.Approximately(targetY, cameraPosition.y)) verticalVelocity = 0;
        cameraPosition.x = Mathf.SmoothDamp(
            cameraPosition.x,
            targetX,
            ref horizontalVelocity,
            positionSmoothTime
        );
        float verticalSmoothTime = targetY > cameraPosition.y
            ? verticalRiseSmoothTime
            : verticalReturnSmoothTime;

        cameraPosition.y = Mathf.SmoothDamp(
            cameraPosition.y,
            targetY,
            ref verticalVelocity,
            verticalSmoothTime
        );
        cameraPosition.z = cameraDepth;
        currentShakeOffset = CalculateShakeOffset();
        transform.position = cameraPosition + currentShakeOffset;

        targetFieldOfView = FilterZoomTarget(targetFieldOfView, battleCamera.fieldOfView, Time.deltaTime);
        float fieldOfViewSmoothTime =
            targetFieldOfView > battleCamera.fieldOfView
                ? zoomOutSmoothTime
                : zoomSmoothTime;

        battleCamera.fieldOfView = Mathf.SmoothDamp(
            battleCamera.fieldOfView,
            targetFieldOfView,
            ref fieldOfViewVelocity,
            fieldOfViewSmoothTime
        );
        EnforceFighterSafety();
    }

    void SnapToTargets()
    {
        if (!HasTargets())
            return;

        currentShakeOffset = safetyDollyOffset = Vector3.zero;
        impactKick=Vector3.zero;impactKickTimer=0;
        lookaheadX = 0;
        zoomInHold = .18f;
        transform.position = new Vector3(
            CalculateTargetX(),
            CalculateTargetY(),
            cameraDepth
        );

        battleCamera.fieldOfView = CalculateTargetFieldOfView();
        EnforceFighterSafety();
        compositionAspect = battleCamera.aspect;
    }

    public static void RequestHitShake(
        float targetDamagePercent,
        float attackKnockbackMultiplier
    )
    {
        if (Instance == null || !Instance.isActiveAndEnabled)
            return;

        Instance.StartHitShake(
            targetDamagePercent,
            attackKnockbackMultiplier
        );
    }

    void StartHitShake(
        float targetDamagePercent,
        float attackKnockbackMultiplier
    )
    {
        float damageAmount = Mathf.Clamp01(
            Mathf.Max(0f, targetDamagePercent) /
            Mathf.Max(1f, fullShakeDamagePercent)
        );
        damageAmount = Mathf.SmoothStep(0f, 1f, damageAmount);

        float attackWeight = Mathf.Lerp(
            0.85f,
            1.15f,
            Mathf.Clamp01(attackKnockbackMultiplier)
        );
        float requestedIntensity = Mathf.Lerp(
            minimumShakeIntensity,
            maximumShakeIntensity,
            damageAmount
        ) * attackWeight;
        float requestedDuration = Mathf.Lerp(
            minimumShakeDuration,
            maximumShakeDuration,
            damageAmount
        );

        // Legacy enemy contacts share the same restrained screen-space ceiling.
        float depth=Mathf.Max(1f,Mathf.Abs(cameraDepth));
        float height=battleCamera.orthographic?battleCamera.orthographicSize*2:2*depth*Mathf.Tan(battleCamera.fieldOfView*Mathf.Deg2Rad*.5f);
        requestedIntensity=Mathf.Min(requestedIntensity,height/720f*3.6f);
        if(requestedIntensity>=shakeIntensity)shakeAxis=Vector2.one;

        shakeIntensity = Mathf.Max(shakeIntensity, requestedIntensity);
        shakeDuration = Mathf.Max(shakeDuration, requestedDuration);
        shakeTimer = Mathf.Max(shakeTimer, requestedDuration);
        shakeSeed = (float)shakeRandom.NextDouble()*1000f;
    }

    // J feedback is capped in projected pixels at 720p, independent of target %.
    // Other attacks retain their existing requests; simultaneous requests merge.
    public static float JImpactPixels(BattleJMove move,int step) => move==BattleJMove.Neutral
        ? (step==3?3.2f:step==2?.35f:0f) : 1.8f;
    public static void RequestJShake(Vector3 contact,BattleJMove move,int step,float direction=1f)
    {
        float pixels=JImpactPixels(move,step);
        if(pixels<=0)return;
        if(move==BattleJMove.Neutral && step==3)
        {
            RequestDirectionalImpact(contact,pixels,direction);
            return;
        }
        bool heavy=move==BattleJMove.Side || move==BattleJMove.Upper;
        RequestPresentationShake(contact,pixels,heavy?.09f:.055f,
            move==BattleJMove.Upper?new Vector2(.45f,1):new Vector2(1,.55f));
    }
    public static void RequestDirectionalImpact(Vector3 contact,float pixels,float direction)
    {
        RequestPresentationShake(contact,.75f,ImpactKickSeconds,new Vector2(1,.15f));
        var owner=Instance;
        if(!owner || !owner.battleCamera || BattlePresentationSettings.ReducedMotion)return;
        var camera=owner.battleCamera;
        float depth=Mathf.Max(.1f,Vector3.Dot(contact-camera.transform.position,camera.transform.forward));
        float height=camera.orthographic?camera.orthographicSize*2:2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
        owner.impactKick=new Vector3(Mathf.Sign(direction),.12f,0)*(height/720f*Mathf.Clamp(pixels,0,3.2f));
        owner.impactKickTimer=ImpactKickSeconds;
    }
    public static void RequestPresentationShake(Vector3 contact,float pixels,float duration,Vector2 axis)
    {
        var owner=Instance;
        if (!owner || !owner.isActiveAndEnabled || !owner.battleCamera) return;
        var camera=owner.battleCamera;
        float depth=Mathf.Max(.1f,Vector3.Dot(contact-camera.transform.position,camera.transform.forward));
        float height=camera.orthographic ? camera.orthographicSize*2 : 2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
        float intensity=height/720f*Mathf.Clamp(pixels,0,5);
        if(intensity>=owner.shakeIntensity)owner.shakeAxis=axis;
        owner.shakeIntensity=Mathf.Max(owner.shakeIntensity,intensity);
        owner.shakeDuration=Mathf.Max(owner.shakeDuration,duration);
        owner.shakeTimer=Mathf.Max(owner.shakeTimer,duration);
        owner.shakeSeed=(float)owner.shakeRandom.NextDouble()*1000f;
    }

    Vector3 CalculateShakeOffset()
    {
        Vector3 kick=Vector3.zero;
        if(impactKickTimer>0)
        {
            if(Time.timeScale>0)impactKickTimer=Mathf.Max(0,impactKickTimer-Mathf.Min(Time.unscaledDeltaTime,.033f));
            if(!BattlePresentationSettings.ReducedMotion)kick=impactKick*Mathf.Pow(impactKickTimer/ImpactKickSeconds,2);
        }
        if (shakeTimer <= 0f || shakeDuration <= 0f)
        {
            shakeTimer = 0f;
            shakeDuration = 0f;
            shakeIntensity = 0f;
            return kick;
        }

        float delta=Time.timeScale>0?Mathf.Min(Time.unscaledDeltaTime,.033f):0;
        shakeTimer -= delta;presentationShakeClock+=delta;
        float remaining = Mathf.Clamp01(shakeTimer / shakeDuration);
        float fade = remaining * remaining;
        float sampleTime = presentationShakeClock * Mathf.Max(1f, shakeFrequency);
        float offsetX = Mathf.PerlinNoise(
            shakeSeed,
            sampleTime
        ) * 2f - 1f;
        float offsetY = Mathf.PerlinNoise(
            shakeSeed + 29.7f,
            sampleTime
        ) * 2f - 1f;

        return kick+new Vector3(offsetX*shakeAxis.x, offsetY*shakeAxis.y, 0f) *
            shakeIntensity * fade*(BattlePresentationSettings.ReducedMotion?.15f:1f);
    }

    float CalculateTargetX()
    {
        float midpoint =
            (playerTarget.position.x + enemyTarget.position.x) * 0.5f + lookaheadX;

        if (arenaComposition.HasValue)
        {
            Bounds arena = arenaComposition.Value;
            float limit = Mathf.Min(horizontalFollowLimit, arena.size.x * .23f);
            return Mathf.Clamp(midpoint, arena.center.x - limit, arena.center.x + limit);
        }

        return Mathf.Clamp(
            midpoint,
            stageCentreX - horizontalFollowLimit,
            stageCentreX + horizontalFollowLimit
        );
    }

    static float StableFollowTarget(float target, float current, float radius)
    {
        float difference = target - current;
        return Mathf.Abs(difference) <= radius ? current : target - Mathf.Sign(difference) * radius;
    }

    float FilterZoomTarget(float desired, float current, float delta)
    {
        // Delay only zoom-IN. Tiny lens variations are ignored; neither this
        // hold nor the dead zone can suppress the final safety-framing pass.
        if (desired >= current - .35f)
        {
            zoomInHold = .18f;
            return Mathf.Max(current, desired);
        }
        zoomInHold = Mathf.Max(0, zoomInHold - delta);
        if (zoomInHold > 0) { fieldOfViewVelocity = 0; return current; }
        return desired;
    }

    void UpdateLookahead(float delta)
    {
        if (sampledPlayer != playerTarget)
        { sampledPlayer = playerTarget; playerBody = playerTarget ? playerTarget.GetComponent<Rigidbody>() : null; }
        if (sampledEnemy != enemyTarget)
        { sampledEnemy = enemyTarget; enemyBody = enemyTarget ? enemyTarget.GetComponent<Rigidbody>() : null; }
        float lead = 0;
        if (!BattlePresentationSettings.ReducedMotion && playerBody && enemyBody)
        {
            float a = playerBody.linearVelocity.x, b = enemyBody.linearVelocity.x;
            // Anticipate shared pursuit, never a single fist, opposing fighters,
            // or a violent one-sided knockback. No vertical prediction.
            if (a * b > 0 && Mathf.Min(Mathf.Abs(a), Mathf.Abs(b)) > .75f && Mathf.Abs(a - b) < 8f && Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)) < 18f)
                lead = Mathf.Clamp((a + b) * .05f, -.5f, .5f);
        }
        lookaheadX = BattlePresentationSettings.ReducedMotion ? 0 :
            Mathf.Lerp(lookaheadX, lead, 1f - Mathf.Exp(-Mathf.Max(0, delta) / .12f));
    }

    float CalculateTargetY()
    {
        float midpoint = (playerTarget.position.y + enemyTarget.position.y) * .5f;
        if (arenaComposition.HasValue)
        {
            // Follow elevated duels too, rather than leaving both fighters at
            // the top of a ground-centred frame. Includes the 8-degree tilt.
            return Mathf.Clamp(midpoint + 5.6f, stageFloorY + 5.6f, maximumCameraHeight);
        }
        return Mathf.Clamp(midpoint + 1.7f, stageFloorY + 1.7f, maximumCameraHeight);
    }

    float CalculateTargetFieldOfView()
    {
        if (arenaComposition.HasValue) return ArenaFieldOfView();
        float horizontalSeparation = Mathf.Abs(
            playerTarget.position.x - enemyTarget.position.x
        );

        float zoomAmount = Mathf.InverseLerp(
            closeSeparation,
            farSeparation,
            horizontalSeparation
        );

        zoomAmount = Mathf.SmoothStep(0f, 1f, zoomAmount);

        float horizontalFieldOfView = Mathf.Lerp(
            CloseCombatFov,
            farFieldOfView,
            zoomAmount
        );

        float verticalSpan = Mathf.Abs(
            playerTarget.position.y - enemyTarget.position.y
        ) + fighterHeightPadding;
        float targetDepth =
            (playerTarget.position.z + enemyTarget.position.z) * 0.5f;
        float cameraDistance = Mathf.Max(
            1f,
            Mathf.Abs(cameraDepth - targetDepth)
        );
        float safeViewFraction = Mathf.Clamp(
            safeVerticalViewport,
            0.2f,
            1f
        );
        float requiredHalfHeight =
            verticalSpan / (2f * safeViewFraction);
        float verticalFieldOfView =
            2f * Mathf.Atan(requiredHalfHeight / cameraDistance) *
            Mathf.Rad2Deg;

        verticalFieldOfView = Mathf.Clamp(
            verticalFieldOfView,
            CloseCombatFov,
            maximumFramingFieldOfView
        );

        return Mathf.Max(FighterSafetyFieldOfView(), Mathf.Max(horizontalFieldOfView, verticalFieldOfView));
    }

    float ArenaFieldOfView()
    {
        float separation = Mathf.Abs(playerTarget.position.x - enemyTarget.position.x);
        float amount = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(closeSeparation, farSeparation, separation));
        return Mathf.Max(Mathf.Lerp(CloseCombatFov, farFieldOfView, amount), FighterSafetyFieldOfView());
    }

    float FighterSafetyFieldOfView()
    {
        // Fit both the destination and the current smoothed camera position.
        // Otherwise fast knockback is clipped while the follow catches up.
        Vector3 destination = new Vector3(CalculateTargetX(), CalculateTargetY(), cameraDepth);
        return Mathf.Max(FighterFieldOfViewAt(destination), FighterFieldOfViewAt(transform.position - currentShakeOffset - safetyDollyOffset));
    }

    float FighterFieldOfViewAt(Vector3 position)
    {
        Quaternion inverse = Quaternion.Inverse(transform.rotation);
        float aspect = battleCamera ? battleCamera.aspect : 16f / 9f;
        float tangent = 0;
        IncludeFighter(playerTarget, position, inverse, aspect, ref tangent);
        IncludeFighter(enemyTarget, position, inverse, aspect, ref tangent);
        return Mathf.Clamp(2f * Mathf.Atan(tangent) * Mathf.Rad2Deg, 1, 150);
    }

    void EnforceFighterSafety()
    {
        // Zoom-in stays smooth. Safety zoom-out cannot wait for SmoothDamp
        // while a fast knockback places a fighter at the edge for several frames.
        safetyDollyOffset = Vector3.zero;
        float required = FighterFieldOfViewAt(transform.position);
        if (required > maximumFramingFieldOfView)
        {
            // Exceptional pre-KO spread: dolly back along the viewing axis
            // instead of exceeding the lens limit or clipping either fighter.
            float ratio = Mathf.Tan(required * .5f * Mathf.Deg2Rad) /
                Mathf.Tan(maximumFramingFieldOfView * .5f * Mathf.Deg2Rad);
            float distance = Mathf.Min(Mathf.Abs(cameraDepth) * 2f,
                Mathf.Abs(cameraDepth) * Mathf.Max(0, ratio - 1f) * 1.15f);
            safetyDollyOffset = -transform.forward * distance;
            transform.position += safetyDollyOffset;
            required = FighterFieldOfViewAt(transform.position);
        }
        float safeFov = Mathf.Min(required, maximumFramingFieldOfView);
        if (battleCamera.fieldOfView < safeFov)
        {
            zoomInHold = .18f;
            battleCamera.fieldOfView = safeFov;
            fieldOfViewVelocity = Mathf.Max(0, fieldOfViewVelocity);
        }
        battleCamera.fieldOfView = Mathf.Min(battleCamera.fieldOfView, maximumFramingFieldOfView);
    }

    static void IncludeFighter(Transform fighter, Vector3 position, Quaternion inverse, float aspect, ref float tangent)
    {
        IncludeFramePoint(fighter.position + new Vector3(-1.25f, -1.1f, 0), position, inverse, aspect, ref tangent);
        IncludeFramePoint(fighter.position + new Vector3(1.25f, 1.65f, 0), position, inverse, aspect, ref tangent);
    }

    static void IncludeFramePoint(Vector3 point, Vector3 position, Quaternion inverse, float aspect, ref float tangent)
    {
        Vector3 local = inverse * (point - position);
        float depth = Mathf.Max(1, local.z);
        // Keep the entire padded fighter inside x=11..89%, y=25..84%.
        tangent = Mathf.Max(tangent, Mathf.Abs(local.x) / (depth * Mathf.Max(.5f, aspect) * .78f));
        tangent = Mathf.Max(tangent, Mathf.Abs(local.y) / (depth * (local.y >= 0 ? .68f : .50f)));
    }

    void FindMissingTargets()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                playerTarget = player.transform;
            }
        }

        if (enemyTarget == null)
        {
            GameObject enemy = GameObject.FindGameObjectWithTag("Enemy");

            if (enemy != null)
            {
                enemyTarget = enemy.transform;
            }
        }
    }

    bool HasTargets()
    {
        return playerTarget != null && enemyTarget != null;
    }

    void CreateBackdropWash()
    {
        Transform backdrop = transform.Find("BattleBackdrop");

        if (backdrop == null || backdropWashColor.a <= 0f)
            return;

        MeshFilter backdropFilter = backdrop.GetComponent<MeshFilter>();

        if (backdropFilter == null || backdropFilter.sharedMesh == null)
            return;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        backdropWash = new GameObject("BattleBackdropWash");
        backdropWash.hideFlags = HideFlags.DontSave;
        backdropWash.transform.SetParent(transform, false);
        backdropWash.transform.localPosition =
            backdrop.localPosition + Vector3.back * 0.05f;
        backdropWash.transform.localRotation =
            backdrop.localRotation;
        backdropWash.transform.localScale = backdrop.localScale;

        MeshFilter washFilter = backdropWash.AddComponent<MeshFilter>();
        washFilter.sharedMesh = backdropFilter.sharedMesh;

        MeshRenderer washRenderer =
            backdropWash.AddComponent<MeshRenderer>();
        backdropWashMaterial = new Material(shader)
        {
            name = "Battle Backdrop Soft Wash",
            color = backdropWashColor,
            hideFlags = HideFlags.HideAndDontSave
        };
        washRenderer.sharedMaterial = backdropWashMaterial;
        washRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        washRenderer.receiveShadows = false;
        washRenderer.lightProbeUsage =
            UnityEngine.Rendering.LightProbeUsage.Off;
        washRenderer.reflectionProbeUsage =
            UnityEngine.Rendering.ReflectionProbeUsage.Off;
        washRenderer.sortingOrder = 1;
    }

    void OnValidate()
    {
        horizontalFollowLimit = Mathf.Max(0f, horizontalFollowLimit);
        positionSmoothTime = Mathf.Max(0.01f, positionSmoothTime);
        verticalFollowStrength = Mathf.Clamp01(verticalFollowStrength);
        verticalFramingOffset = Mathf.Clamp(
            verticalFramingOffset,
            -5f,
            5f
        );
        maximumCameraHeight = Mathf.Max(
            minimumCameraHeight,
            maximumCameraHeight
        );
        verticalRiseSmoothTime = Mathf.Max(
            0.01f,
            verticalRiseSmoothTime
        );
        verticalReturnSmoothTime = Mathf.Max(
            0.01f,
            verticalReturnSmoothTime
        );
        closeSeparation = Mathf.Max(0f, closeSeparation);
        farSeparation = Mathf.Max(
            closeSeparation + 0.01f,
            farSeparation
        );
        closeFieldOfView = Mathf.Clamp(closeFieldOfView, 1f, 179f);
        farFieldOfView = Mathf.Clamp(
            farFieldOfView,
            closeFieldOfView,
            179f
        );
        zoomOutSmoothTime = Mathf.Max(0.01f, zoomOutSmoothTime);
        zoomSmoothTime = Mathf.Max(0.01f, zoomSmoothTime);
        fighterHeightPadding = Mathf.Max(0f, fighterHeightPadding);
        safeVerticalViewport = Mathf.Clamp(
            safeVerticalViewport,
            0.2f,
            1f
        );
        maximumFramingFieldOfView = Mathf.Clamp(
            maximumFramingFieldOfView,
            farFieldOfView,
            179f
        );
        minimumShakeIntensity = Mathf.Max(0f, minimumShakeIntensity);
        maximumShakeIntensity = Mathf.Max(
            minimumShakeIntensity,
            maximumShakeIntensity
        );
        minimumShakeDuration = Mathf.Max(0f, minimumShakeDuration);
        maximumShakeDuration = Mathf.Max(
            minimumShakeDuration,
            maximumShakeDuration
        );
        fullShakeDamagePercent = Mathf.Max(
            1f,
            fullShakeDamagePercent
        );
        shakeFrequency = Mathf.Max(1f, shakeFrequency);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (backdropWashMaterial != null)
        {
            Destroy(backdropWashMaterial);
        }
    }
}
