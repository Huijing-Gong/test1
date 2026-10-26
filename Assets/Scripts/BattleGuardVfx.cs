using UnityEngine;
using UnityEngine.Rendering;

public class BattleGuardVfx : MonoBehaviour
{
    private const int ArcPointCount = 36;
    private const int ImpactParticleCount = 32;
    private const float ArcRadius = 1.22f;
    private const float ArcCentreOffset = 0.18f;
    private const float ArcHalfAngle = 78f;
    private const float ImpactDuration = 0.55f;
    private const float OuterWidth = 0.22f;
    private const float MiddleWidth = 0.095f;
    private const float InnerWidth = 0.035f;
    private const float AmbientParticleInterval = 0.11f;

    private static readonly Color OuterColour =
        new Color(1f, 1f, 1f, 0.46f);

    private static readonly Color MiddleColour =
        new Color(1f, 1f, 1f, 0.66f);

    private static readonly Color InnerColour =
        new Color(1f, 1f, 1f, 0.88f);

    private Transform effectRoot;
    private Mesh shieldFillMesh;
    private MeshRenderer shieldFillRenderer;
    private MaterialPropertyBlock fillProperties;
    private LineRenderer outerArc;
    private LineRenderer middleArc;
    private LineRenderer innerArc;
    private LineRenderer impactRipple;
    private LineRenderer secondaryImpactRipple;
    private Transform impactFlashTransform;
    private Mesh impactFlashMesh;
    private MeshRenderer impactFlashRenderer;
    private MaterialPropertyBlock impactFlashProperties;
    private ParticleSystem impactParticles;
    private Material visualMaterial;
    private float facingDirection = 1f;
    private float visibility;
    private float impactTimer;
    private float ambientParticleTimer;
    private bool isGuarding;

    void Awake()
    {
        BuildVisuals();
        HideImmediately();
    }

    void Update()
    {
        if (effectRoot == null)
            return;

        if (impactTimer > 0f)
        {
            impactTimer -= Time.deltaTime;
        }

        float targetVisibility =
            isGuarding || impactTimer > 0f ? 1f : 0f;

        float fadeSpeed = targetVisibility > visibility ? 14f : 10f;
        visibility = Mathf.MoveTowards(
            visibility,
            targetVisibility,
            fadeSpeed * Time.deltaTime
        );

        if (visibility <= 0f && targetVisibility <= 0f)
        {
            effectRoot.gameObject.SetActive(false);
            return;
        }

        effectRoot.gameObject.SetActive(true);
        UpdateAppearance();

        if (isGuarding)
        {
            ambientParticleTimer -= Time.deltaTime;

            if (ambientParticleTimer <= 0f)
            {
                EmitAmbientParticle();
                ambientParticleTimer = AmbientParticleInterval;
            }
        }
        else
        {
            ambientParticleTimer = 0f;
        }
    }

    public void SetGuarding(bool guarding, float direction)
    {
        float normalisedDirection = direction >= 0f ? 1f : -1f;

        if (!Mathf.Approximately(
                facingDirection,
                normalisedDirection
            ))
        {
            facingDirection = normalisedDirection;
            UpdateArcPositions();
        }

        isGuarding = guarding;

        if (guarding && effectRoot != null)
        {
            effectRoot.gameObject.SetActive(true);
        }
    }

    public void PlayBlockImpact(float direction)
    {
        SetGuarding(isGuarding, direction);

        if (effectRoot == null || impactParticles == null)
            return;

        effectRoot.gameObject.SetActive(true);
        visibility = 1f;
        impactTimer = ImpactDuration;
        impactParticles.Play();

        for (int index = 0;
             index < ImpactParticleCount;
             index++)
        {
            float impactSpread = index < 23
                ? 28f
                : ArcHalfAngle;

            float angle = Random.Range(
                -impactSpread,
                impactSpread
            ) * Mathf.Deg2Rad;

            Vector3 outward = new Vector3(
                facingDirection * Mathf.Cos(angle),
                Mathf.Sin(angle),
                0f
            ).normalized;

            ParticleSystem.EmitParams emitParams =
                new ParticleSystem.EmitParams
                {
                    position = GetArcPoint(angle),
                    velocity = outward * Random.Range(3f, 6f),
                    startLifetime = Random.Range(0.42f, 0.78f),
                    startSize = Random.Range(0.09f, 0.21f),
                    startColor = Color.Lerp(
                        InnerColour,
                        Color.white,
                        Random.Range(0.25f, 0.8f)
                    )
                };

            impactParticles.Emit(emitParams, 1);
        }
    }

    void BuildVisuals()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning(
                "Guard VFX could not find the built-in " +
                "Sprites/Default shader. Defence still works."
            );
            enabled = false;
            return;
        }

        visualMaterial = new Material(shader)
        {
            name = "Runtime Guard VFX Material",
            hideFlags = HideFlags.HideAndDontSave
        };

        GameObject rootObject = new GameObject("Guard VFX");
        rootObject.hideFlags = HideFlags.DontSave;
        effectRoot = rootObject.transform;
        effectRoot.SetParent(transform, false);
        effectRoot.localPosition = new Vector3(0f, 0f, -0.6f);

        CreateShieldFill();

        outerArc = CreateArcRenderer(
            "Outer Energy Arc",
            OuterWidth,
            40
        );

        middleArc = CreateArcRenderer(
            "Middle Energy Arc",
            MiddleWidth,
            41
        );

        innerArc = CreateArcRenderer(
            "Inner Energy Arc",
            InnerWidth,
            42
        );

        impactRipple = CreateArcRenderer(
            "Guard Impact Ripple",
            0.075f,
            43
        );
        impactRipple.enabled = false;

        secondaryImpactRipple = CreateArcRenderer(
            "Secondary Guard Impact Ripple",
            0.045f,
            44
        );
        secondaryImpactRipple.enabled = false;

        CreateImpactFlash();
        CreateImpactParticles();
        UpdateArcPositions();
    }

    void CreateShieldFill()
    {
        GameObject fillObject = new GameObject("Shield Energy Fill");
        fillObject.hideFlags = HideFlags.DontSave;
        fillObject.transform.SetParent(effectRoot, false);
        fillObject.transform.localPosition = new Vector3(0f, 0f, 0.015f);

        MeshFilter meshFilter = fillObject.AddComponent<MeshFilter>();
        shieldFillRenderer = fillObject.AddComponent<MeshRenderer>();

        shieldFillMesh = new Mesh
        {
            name = "Runtime Guard Shield Fill",
            hideFlags = HideFlags.HideAndDontSave
        };

        meshFilter.sharedMesh = shieldFillMesh;
        shieldFillRenderer.sharedMaterial = visualMaterial;
        shieldFillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        shieldFillRenderer.receiveShadows = false;
        shieldFillRenderer.sortingOrder = 39;
        fillProperties = new MaterialPropertyBlock();
    }

    void CreateImpactFlash()
    {
        GameObject flashObject = new GameObject(
            "Guard Contact Flash"
        );
        flashObject.hideFlags = HideFlags.DontSave;
        impactFlashTransform = flashObject.transform;
        impactFlashTransform.SetParent(effectRoot, false);
        impactFlashTransform.localPosition = new Vector3(0f, 0f, -0.03f);

        MeshFilter meshFilter = flashObject.AddComponent<MeshFilter>();
        impactFlashRenderer = flashObject.AddComponent<MeshRenderer>();

        const int rayCount = 24;
        Vector3[] vertices = new Vector3[rayCount + 2];
        Color[] colours = new Color[vertices.Length];
        int[] triangles = new int[rayCount * 3];

        vertices[0] = Vector3.zero;
        colours[0] = Color.white;

        for (int index = 0; index <= rayCount; index++)
        {
            float angle = index / (float)rayCount * Mathf.PI * 2f;
            float radius = index % 2 == 0 ? 0.62f : 0.25f;

            vertices[index + 1] = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f
            );
            colours[index + 1] = new Color(1f, 1f, 1f, 0f);

            if (index >= rayCount)
                continue;

            int triangleIndex = index * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = index + 1;
            triangles[triangleIndex + 2] = index + 2;
        }

        impactFlashMesh = new Mesh
        {
            name = "Runtime Guard Contact Flash",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            colors = colours,
            triangles = triangles
        };
        impactFlashMesh.RecalculateBounds();

        meshFilter.sharedMesh = impactFlashMesh;
        impactFlashRenderer.sharedMaterial = visualMaterial;
        impactFlashRenderer.shadowCastingMode = ShadowCastingMode.Off;
        impactFlashRenderer.receiveShadows = false;
        impactFlashRenderer.sortingOrder = 46;
        impactFlashProperties = new MaterialPropertyBlock();
        impactFlashRenderer.enabled = false;
    }

    LineRenderer CreateArcRenderer(
        string objectName,
        float width,
        int sortingOrder
    )
    {
        GameObject arcObject = new GameObject(objectName);
        arcObject.hideFlags = HideFlags.DontSave;
        arcObject.transform.SetParent(effectRoot, false);

        LineRenderer line = arcObject.AddComponent<LineRenderer>();
        line.sharedMaterial = visualMaterial;
        line.useWorldSpace = false;
        line.loop = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.positionCount = ArcPointCount;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 5;
        line.numCornerVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = sortingOrder;
        return line;
    }

    void CreateImpactParticles()
    {
        GameObject particleObject = new GameObject(
            "Guard Impact Particles"
        );
        particleObject.hideFlags = HideFlags.DontSave;
        particleObject.transform.SetParent(effectRoot, false);

        impactParticles = particleObject.AddComponent<ParticleSystem>();
        impactParticles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        ParticleSystem.MainModule main = impactParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.startLifetime = 0.3f;
        main.startSpeed = 0f;
        main.startSize = 0.08f;
        main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission =
            impactParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = impactParticles.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer =
            particleObject.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = visualMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 47;

        impactParticles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );
    }

    void UpdateArcPositions()
    {
        if (outerArc == null ||
            middleArc == null ||
            innerArc == null ||
            impactRipple == null ||
            secondaryImpactRipple == null)
        {
            return;
        }

        for (int index = 0; index < ArcPointCount; index++)
        {
            float t = index / (ArcPointCount - 1f);
            float angle = Mathf.Lerp(
                -ArcHalfAngle,
                ArcHalfAngle,
                t
            ) * Mathf.Deg2Rad;

            Vector3 point = GetArcPoint(angle);
            outerArc.SetPosition(index, point);
            middleArc.SetPosition(index, point);
            innerArc.SetPosition(index, point);
            impactRipple.SetPosition(index, point);
            secondaryImpactRipple.SetPosition(index, point);
        }

        if (impactFlashTransform != null)
        {
            impactFlashTransform.localPosition = new Vector3(
                facingDirection * (ArcCentreOffset + ArcRadius),
                0f,
                -0.03f
            );
        }

        UpdateShieldFillMesh();
    }

    void UpdateShieldFillMesh()
    {
        if (shieldFillMesh == null)
            return;

        Vector3[] vertices = new Vector3[ArcPointCount + 1];
        Color[] colours = new Color[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(ArcPointCount - 1) * 3];

        vertices[0] = new Vector3(
            facingDirection * ArcCentreOffset,
            0f,
            0f
        );
        colours[0] = new Color(1f, 1f, 1f, 0.035f);
        uv[0] = new Vector2(0.5f, 0.5f);

        for (int index = 0; index < ArcPointCount; index++)
        {
            float t = index / (ArcPointCount - 1f);
            float angle = Mathf.Lerp(
                -ArcHalfAngle,
                ArcHalfAngle,
                t
            ) * Mathf.Deg2Rad;

            vertices[index + 1] = GetArcPoint(angle);

            float centreEmphasis = 1f - Mathf.Abs(t * 2f - 1f);
            colours[index + 1] = new Color(
                1f,
                1f,
                1f,
                Mathf.Lerp(0.08f, 0.17f, centreEmphasis)
            );

            uv[index + 1] = new Vector2(
                Mathf.InverseLerp(
                    -ArcRadius,
                    ArcRadius,
                    vertices[index + 1].x
                ),
                Mathf.InverseLerp(
                    -ArcRadius,
                    ArcRadius,
                    vertices[index + 1].y
                )
            );

            if (index >= ArcPointCount - 1)
                continue;

            int triangleIndex = index * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = index + 1;
            triangles[triangleIndex + 2] = index + 2;
        }

        shieldFillMesh.Clear();
        shieldFillMesh.vertices = vertices;
        shieldFillMesh.colors = colours;
        shieldFillMesh.uv = uv;
        shieldFillMesh.triangles = triangles;
        shieldFillMesh.RecalculateBounds();
    }

    void EmitAmbientParticle()
    {
        if (impactParticles == null)
            return;

        float angle = Random.Range(
            -ArcHalfAngle,
            ArcHalfAngle
        ) * Mathf.Deg2Rad;

        Vector3 point = GetArcPoint(angle);
        Vector3 inward = new Vector3(
            -facingDirection,
            0f,
            0f
        );

        ParticleSystem.EmitParams emitParams =
            new ParticleSystem.EmitParams
            {
                position = point,
                velocity = Vector3.up * Random.Range(0.25f, 0.55f) +
                    inward * Random.Range(0.05f, 0.18f),
                startLifetime = Random.Range(0.42f, 0.7f),
                startSize = Random.Range(0.025f, 0.06f),
                startColor = new Color(1f, 1f, 1f, 0.55f)
            };

        impactParticles.Play();
        impactParticles.Emit(emitParams, 1);
    }

    Vector3 GetArcPoint(float angle)
    {
        return new Vector3(
            facingDirection * (
                ArcCentreOffset + Mathf.Cos(angle) * ArcRadius
            ),
            Mathf.Sin(angle) * ArcRadius,
            0f
        );
    }

    void UpdateAppearance()
    {
        float impactAmount = Mathf.Clamp01(
            impactTimer / ImpactDuration
        );
        float impactProgress = 1f - impactAmount;

        float breathingScale = 1f +
            Mathf.Sin(Time.time * 4.5f) * 0.035f;

        float impactPunch = impactTimer > 0f
            ? Mathf.Sin(impactProgress * Mathf.PI) * 0.2f -
                impactAmount * 0.06f
            : 0f;

        effectRoot.localScale = Vector3.one * (
            breathingScale + impactPunch
        );

        outerArc.startWidth =
            OuterWidth * (1f + impactAmount * 0.45f);
        outerArc.endWidth = outerArc.startWidth;
        middleArc.startWidth =
            MiddleWidth * (1f + impactAmount * 0.4f);
        middleArc.endWidth = middleArc.startWidth;
        innerArc.startWidth =
            InnerWidth * (1f + impactAmount * 0.35f);
        innerArc.endWidth = innerArc.startWidth;

        Color outer = Color.Lerp(
            OuterColour,
            Color.white,
            impactAmount * 0.85f
        );
        outer.a = 0.38f * visibility;

        Color middle = Color.Lerp(
            MiddleColour,
            Color.white,
            impactAmount * 0.92f
        );
        middle.a = 0.62f * visibility;

        Color inner = Color.Lerp(
            InnerColour,
            Color.white,
            impactAmount
        );
        inner.a = 0.9f * visibility;

        outerArc.startColor = outer;
        outerArc.endColor = outer;
        middleArc.startColor = middle;
        middleArc.endColor = middle;
        innerArc.startColor = inner;
        innerArc.endColor = inner;

        if (shieldFillRenderer != null && fillProperties != null)
        {
            Color fillColour = Color.white;
            fillColour.a = 0.16f * visibility;
            fillProperties.SetColor("_Color", fillColour);
            shieldFillRenderer.SetPropertyBlock(fillProperties);
        }

        if (impactRipple != null)
        {
            bool showRipple = impactTimer > 0f;
            impactRipple.enabled = showRipple;

            if (showRipple)
            {
                float rippleScale = Mathf.Lerp(
                    0.82f,
                    1.75f,
                    Mathf.SmoothStep(0f, 1f, impactProgress)
                );
                impactRipple.transform.localScale =
                    Vector3.one * rippleScale;

                Color rippleColour = Color.white;
                rippleColour.a =
                    (1f - impactProgress) * 0.95f * visibility;
                impactRipple.startColor = rippleColour;
                impactRipple.endColor = rippleColour;
            }
        }

        if (secondaryImpactRipple != null)
        {
            float secondaryProgress = Mathf.Clamp01(
                (impactProgress - 0.12f) / 0.88f
            );
            bool showSecondary = impactTimer > 0f &&
                impactProgress >= 0.12f;
            secondaryImpactRipple.enabled = showSecondary;

            if (showSecondary)
            {
                float rippleScale = Mathf.Lerp(
                    0.9f,
                    2.2f,
                    Mathf.SmoothStep(0f, 1f, secondaryProgress)
                );
                secondaryImpactRipple.transform.localScale =
                    Vector3.one * rippleScale;

                Color secondaryColour = Color.white;
                secondaryColour.a =
                    Mathf.Sin(secondaryProgress * Mathf.PI) *
                    0.82f * visibility;
                secondaryImpactRipple.startColor = secondaryColour;
                secondaryImpactRipple.endColor = secondaryColour;
            }
        }

        if (impactFlashRenderer != null &&
            impactFlashTransform != null &&
            impactFlashProperties != null)
        {
            bool showFlash = impactTimer > 0f;
            impactFlashRenderer.enabled = showFlash;

            if (showFlash)
            {
                float flashProgress = Mathf.Clamp01(
                    impactProgress / 0.72f
                );
                float flashScale = Mathf.Lerp(
                    0.2f,
                    1.55f,
                    1f - Mathf.Pow(1f - flashProgress, 3f)
                );

                impactFlashTransform.localScale =
                    Vector3.one * flashScale;
                impactFlashTransform.localRotation =
                    Quaternion.Euler(0f, 0f, impactProgress * 55f);

                Color flashColour = Color.white;
                flashColour.a =
                    (1f - flashProgress) * visibility;
                impactFlashProperties.SetColor(
                    "_Color",
                    flashColour
                );
                impactFlashRenderer.SetPropertyBlock(
                    impactFlashProperties
                );
            }
        }
    }

    void HideImmediately()
    {
        isGuarding = false;
        visibility = 0f;
        impactTimer = 0f;
        ambientParticleTimer = 0f;

        if (impactRipple != null)
        {
            impactRipple.enabled = false;
            impactRipple.transform.localScale = Vector3.one;
        }

        if (secondaryImpactRipple != null)
        {
            secondaryImpactRipple.enabled = false;
            secondaryImpactRipple.transform.localScale = Vector3.one;
        }

        if (impactFlashRenderer != null)
        {
            impactFlashRenderer.enabled = false;
        }

        if (impactFlashTransform != null)
        {
            impactFlashTransform.localScale = Vector3.one;
            impactFlashTransform.localRotation = Quaternion.identity;
        }

        if (impactParticles != null)
        {
            impactParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }

        if (effectRoot != null)
        {
            effectRoot.localScale = Vector3.one;
            effectRoot.gameObject.SetActive(false);
        }
    }

    void OnDisable()
    {
        HideImmediately();
    }

    void OnDestroy()
    {
        if (visualMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(visualMaterial);
            }
            else
            {
                DestroyImmediate(visualMaterial);
            }
        }

        if (shieldFillMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(shieldFillMesh);
            }
            else
            {
                DestroyImmediate(shieldFillMesh);
            }
        }

        if (impactFlashMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(impactFlashMesh);
            }
            else
            {
                DestroyImmediate(impactFlashMesh);
            }
        }
    }
}
