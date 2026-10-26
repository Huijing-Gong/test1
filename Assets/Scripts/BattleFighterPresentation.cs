using UnityEngine;

internal sealed class BattleFighterPresentation
{
    private const int GroundLayerMask = 1 << 0;
    private const float MaximumShadowDistance = 5.5f;
    private const int DiscSegments = 28;

    private static readonly int ColorProperty =
        Shader.PropertyToID("_Color");

    private readonly Transform fighterRoot;
    private readonly Renderer[] modelRenderers;
    private readonly bool isEnemy;
    private readonly GameObject markerObject;
    private readonly GameObject shadowObject;
    private readonly Mesh markerMesh;
    private readonly Mesh shadowMesh;
    private readonly Material markerMaterial;
    private readonly Material shadowMaterial;

    private float markerVerticalVelocity;
    private float smoothedMarkerY;
    private bool markerPositionInitialised;
    float flashRemaining,flashPower;
    readonly MaterialPropertyBlock flashProperties=new MaterialPropertyBlock();
    public void Flash(float power){flashPower=power;flashRemaining=.11f;}

    public static BattleFighterPresentation Create(
        Transform fighterRoot,
        bool isEnemy
    )
    {
        if (fighterRoot == null)
            return null;

        return new BattleFighterPresentation(fighterRoot, isEnemy);
    }

    private BattleFighterPresentation(
        Transform fighterRoot,
        bool isEnemy
    )
    {
        this.fighterRoot = fighterRoot;
        this.isEnemy = isEnemy;

        Transform visualRoot = FindVisualRoot(fighterRoot);
        modelRenderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];

        ApplyModelTreatment();

        markerMesh = CreateTriangleMesh();
        shadowMesh = CreateSoftDiscMesh();
        markerMaterial = CreatePresentationMaterial(
            isEnemy
                ? new Color32(255, 76, 91, 255)
                : new Color32(48, 169, 226, 255),
            "Fighter Marker Material"
        );
        shadowMaterial = CreatePresentationMaterial(
            new Color(0.015f, 0.025f, 0.04f, 0.62f),
            "Fighter Contact Shadow Material"
        );

        markerObject = CreatePresentationObject(
            "Fighter Marker",
            markerMesh,
            markerMaterial,
            50
        );
        shadowObject = CreatePresentationObject(
            "Fighter Contact Shadow",
            shadowMesh,
            shadowMaterial,
            20
        );
    }

    public void Tick()
    {
        if (fighterRoot == null)
            return;

        UpdateMarker();
        UpdateContactShadow();
        if(Time.timeScale>0 && flashRemaining>0)
        {
            flashRemaining=Mathf.Max(0,flashRemaining-Mathf.Min(Time.unscaledDeltaTime,.033f));
            foreach(var renderer in modelRenderers)
            {
                if(!renderer || !renderer.sharedMaterial)continue;
                var material=renderer.sharedMaterial;
                int property=material.HasProperty("_BaseColor")?Shader.PropertyToID("_BaseColor"):ColorProperty;
                if(!material.HasProperty(property))continue;
                Color tint=material.GetColor(property)*(isEnemy?1f:.9f);tint.a=material.GetColor(property).a;
                float flash=BattlePresentationSettings.FlashEnabled?flashRemaining/.11f*(.10f+flashPower*.16f):0;
                Color display=tint*(1+flash);display.a=tint.a;
                renderer.GetPropertyBlock(flashProperties);flashProperties.SetColor(property,display);renderer.SetPropertyBlock(flashProperties);
            }
        }
    }

    public void Dispose()
    {
        DestroyRuntimeObject(markerObject);
        DestroyRuntimeObject(shadowObject);
        DestroyRuntimeObject(markerMesh);
        DestroyRuntimeObject(shadowMesh);
        DestroyRuntimeObject(markerMaterial);
        DestroyRuntimeObject(shadowMaterial);
    }

    private void ApplyModelTreatment()
    {
        float tintMultiplier = isEnemy ? 1f : 0.9f;

        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer == null)
                continue;

            modelRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            modelRenderer.receiveShadows = true;

            Material sharedMaterial = modelRenderer.sharedMaterial;

            // The restored source used Built-in shaders; the host now uses URP.
            int colorProperty = sharedMaterial != null && sharedMaterial.HasProperty("_BaseColor")
                ? Shader.PropertyToID("_BaseColor")
                : ColorProperty;

            if (sharedMaterial == null ||
                !sharedMaterial.HasProperty(colorProperty))
            {
                continue;
            }

            Color originalColor = sharedMaterial.GetColor(colorProperty);
            Color displayColor = new Color(
                originalColor.r * tintMultiplier,
                originalColor.g * tintMultiplier,
                originalColor.b * tintMultiplier,
                originalColor.a
            );
            MaterialPropertyBlock properties =
                new MaterialPropertyBlock();
            modelRenderer.GetPropertyBlock(properties);
            properties.SetColor(colorProperty, displayColor);
            modelRenderer.SetPropertyBlock(properties);
        }
    }

    private void UpdateMarker()
    {
        if (markerObject == null)
            return;

        float targetY = fighterRoot.position.y + 1.55f;
        bool foundBounds = false;
        Bounds combinedBounds = default;

        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer == null ||
                !modelRenderer.enabled ||
                !modelRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!foundBounds)
            {
                combinedBounds = modelRenderer.bounds;
                foundBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(modelRenderer.bounds);
            }
        }

        if (foundBounds)
        {
            targetY = combinedBounds.max.y + 0.32f;
        }

        if (!markerPositionInitialised)
        {
            smoothedMarkerY = targetY;
            markerPositionInitialised = true;
        }
        else if (Time.deltaTime > 0f)
        {
            smoothedMarkerY = Mathf.SmoothDamp(
                smoothedMarkerY,
                targetY,
                ref markerVerticalVelocity,
                0.08f
            );
        }

        float bobOffset = Mathf.Sin(
            Time.time * 3.2f + (isEnemy ? 1.7f : 0f)
        ) * 0.035f;

        markerObject.transform.position = new Vector3(
            fighterRoot.position.x,
            smoothedMarkerY + bobOffset,
            fighterRoot.position.z - 0.8f
        );
        markerObject.transform.rotation = Quaternion.identity;
        markerObject.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
    }

    private void UpdateContactShadow()
    {
        if (shadowObject == null || shadowMaterial == null)
            return;

        Vector3 rayOrigin = fighterRoot.position + Vector3.up * 0.45f;
        bool foundGround = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit groundHit,
            MaximumShadowDistance,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        if (!foundGround || groundHit.normal.y < 0.35f)
        {
            shadowObject.SetActive(false);
            return;
        }

        if (!shadowObject.activeSelf)
        {
            shadowObject.SetActive(true);
        }

        float distanceAmount = Mathf.Clamp01(
            groundHit.distance / MaximumShadowDistance
        );
        float width = Mathf.Lerp(1.08f, 0.72f, distanceAmount);
        float height = Mathf.Lerp(0.25f, 0.14f, distanceAmount);
        float alpha = Mathf.Lerp(0.62f, 0.08f, distanceAmount);

        shadowMaterial.color = new Color(
            0.015f,
            0.025f,
            0.04f,
            alpha
        );
        shadowObject.transform.position = new Vector3(
            fighterRoot.position.x,
            groundHit.point.y + 0.035f,
            fighterRoot.position.z - 0.55f
        );
        shadowObject.transform.rotation = Quaternion.identity;
        shadowObject.transform.localScale =
            new Vector3(width, height, 1f);
    }

    private GameObject CreatePresentationObject(
        string objectName,
        Mesh mesh,
        Material material,
        int sortingOrder
    )
    {
        if (mesh == null || material == null)
            return null;

        GameObject presentationObject = new GameObject(objectName);
        presentationObject.hideFlags = HideFlags.DontSave;
        presentationObject.transform.SetParent(fighterRoot, false);

        MeshFilter meshFilter =
            presentationObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer =
            presentationObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage =
            UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage =
            UnityEngine.Rendering.ReflectionProbeUsage.Off;
        meshRenderer.sortingOrder = sortingOrder;

        return presentationObject;
    }

    private static Transform FindVisualRoot(Transform fighterRoot)
    {
        Animator animator = fighterRoot.GetComponentInChildren<Animator>();

        if (animator == null)
            return null;

        Transform visualRoot = animator.transform;

        while (visualRoot.parent != null &&
            visualRoot.parent != fighterRoot)
        {
            visualRoot = visualRoot.parent;
        }

        return visualRoot;
    }

    private static Material CreatePresentationMaterial(
        Color color,
        string materialName
    )
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        return new Material(shader)
        {
            name = materialName,
            color = color,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static Mesh CreateTriangleMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "Fighter Marker Triangle",
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.vertices = new[]
        {
            new Vector3(-0.22f, 0.16f, 0f),
            new Vector3(0.22f, 0.16f, 0f),
            new Vector3(0f, -0.16f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateSoftDiscMesh()
    {
        int innerStart = 1;
        int outerStart = innerStart + DiscSegments;
        Vector3[] vertices = new Vector3[1 + DiscSegments * 2];
        Color[] colours = new Color[vertices.Length];
        int[] triangles = new int[DiscSegments * 9];

        vertices[0] = Vector3.zero;
        colours[0] = Color.white;

        for (int i = 0; i < DiscSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / DiscSegments;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle),
                Mathf.Sin(angle),
                0f
            );
            vertices[innerStart + i] = direction * 0.62f;
            vertices[outerStart + i] = direction;
            colours[innerStart + i] = Color.white;
            colours[outerStart + i] = new Color(1f, 1f, 1f, 0f);

            int next = (i + 1) % DiscSegments;
            int triangleIndex = i * 9;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = innerStart + i;
            triangles[triangleIndex + 2] = innerStart + next;
            triangles[triangleIndex + 3] = innerStart + i;
            triangles[triangleIndex + 4] = outerStart + i;
            triangles[triangleIndex + 5] = outerStart + next;
            triangles[triangleIndex + 6] = innerStart + i;
            triangles[triangleIndex + 7] = outerStart + next;
            triangles[triangleIndex + 8] = innerStart + next;
        }

        Mesh mesh = new Mesh
        {
            name = "Soft Fighter Contact Shadow",
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.vertices = vertices;
        mesh.colors = colours;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target != null)
        {
            UnityEngine.Object.Destroy(target);
        }
    }
}
