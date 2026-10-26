using UnityEngine;
using UnityEngine.Rendering;

namespace KingOfGraphic.MechBossMigration
{
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class PixelShieldController : MonoBehaviour
{
    // Shader menu path used when creating the runtime material.
    const string ShaderName = "MechBossMigration/TransparentBlocks";

    // Geometry settings define the shield shell.
    [SerializeField] float radius = 3.2f;
    [SerializeField] float height = 3.0f;
    [SerializeField] int segments = 96;
    [SerializeField] Material materialTemplate;
    bool rebuildRequested;

    [Header("Shield Appearance")]
    [SerializeField, Range(-0.2f, 0.2f)] float orbitSpeed = 0.035f;
    [SerializeField, Range(0f, 1f)] float pixelDensity = 0.48f;
    [SerializeField, Range(0f, 3f)] float pixelBrightness = 1.25f;
    [SerializeField, Range(0f, 2f)] float pixelWaveSpeed = 0.28f;
    [SerializeField, Range(0f, 1f)] float pixelWaveStrength = 1f;
    [SerializeField, Range(0f, 5f)] float rimBrightness = 2.2f;
    [SerializeField, Range(4, 48)] int pixelRows = 10;

    Mesh shieldMesh;
    Material shieldMaterial;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    void OnEnable()
    {
        // Cache required components and generate the shield when enabled.
        EnsureComponents();
        RebuildShield();
    }

    void OnValidate()
    {
        ClampSettings();
        rebuildRequested = true;
    }

    void Update()
    {
        if (rebuildRequested)
        {
            rebuildRequested = false;
            RebuildShield();
        }
        // GPU animation uses _Time; this only synchronizes material parameters.
        if (shieldMaterial != null)
            ApplyMaterialSettings();
    }

    void OnDisable()
    {
        DestroyGeneratedAssets();
    }

    public void ConfigureGeometry(float shieldRadius, float shieldHeight, int shieldSegments)
    {
        // External systems can configure the shield dimensions through this method.
        radius = shieldRadius;
        height = shieldHeight;
        segments = shieldSegments;
        ClampSettings();
        rebuildRequested = true;
    }

    public void ConfigureMaterial(Material template)
    {
        materialTemplate = template;
        DestroyGeneratedAssets();
        rebuildRequested = true;
    }

    public void ConfigureAppearance(
        float shieldOrbitSpeed,
        float shieldPixelDensity,
        float shieldPixelBrightness,
        float shieldPixelWaveSpeed,
        float shieldPixelWaveStrength,
        float shieldRimBrightness,
        int shieldPixelRows)
    {
        // Update the shader properties for density, brightness, waves and orbit.
        orbitSpeed = shieldOrbitSpeed;
        pixelDensity = shieldPixelDensity;
        pixelBrightness = shieldPixelBrightness;
        pixelWaveSpeed = shieldPixelWaveSpeed;
        pixelWaveStrength = shieldPixelWaveStrength;
        rimBrightness = shieldRimBrightness;
        pixelRows = shieldPixelRows;
        ClampSettings();

        if (shieldMaterial != null)
            ApplyMaterialSettings();
    }

    public void RebuildShield()
    {
        EnsureComponents();
        ClampSettings();
        EnsureMaterial();

        // Geometry changes rebuild the mesh; appearance changes only update material properties.
        if (shieldMesh != null)
        {
            if (Application.isPlaying)
                Destroy(shieldMesh);
            else
                DestroyImmediate(shieldMesh);
        }

        shieldMesh = PixelShieldMeshBuilder.BuildOpenCylinder(radius, height, segments);
        shieldMesh.hideFlags = HideFlags.HideAndDontSave;
        meshFilter.sharedMesh = shieldMesh;
        meshRenderer.sharedMaterial = shieldMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        ApplyMaterialSettings();
    }

    void EnsureComponents()
    {
        // RequireComponent supplies the components; cache references for subsequent use.
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
    }

    void EnsureMaterial()
    {
        if (shieldMaterial != null)
            return;

        // Create a runtime material without a separate material asset.
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError(ShaderName + " shader was not found.", this);
            return;
        }

        shieldMaterial = materialTemplate != null ? new Material(materialTemplate) : new Material(shader);
        shieldMaterial.name = "PixelShield_RuntimeMaterial";
        shieldMaterial.hideFlags = HideFlags.HideAndDontSave;
    }

    void ApplyMaterialSettings()
    {
        if (shieldMaterial == null)
            return;

        // Derive the column count from the aspect ratio to keep cells approximately square.
        int columns = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / height * pixelRows), 8, 128);
        shieldMaterial.SetFloat("_PixelColumns", columns);
        shieldMaterial.SetFloat("_PixelRows", pixelRows);
        shieldMaterial.SetFloat("_BlockFill", pixelDensity);
        shieldMaterial.SetFloat("_BlockStrength", pixelBrightness);
        shieldMaterial.SetFloat("_WaveSpeed", pixelWaveSpeed);
        shieldMaterial.SetFloat("_WaveStrength", pixelWaveStrength);
        shieldMaterial.SetFloat("_RimStrength", rimBrightness);
        shieldMaterial.SetFloat("_OrbitSpeed", orbitSpeed);
    }

    void ClampSettings()
    {
        // Clamp settings to valid geometry and shader parameter ranges.
        radius = Mathf.Max(0.5f, radius);
        height = Mathf.Max(0.5f, height);
        segments = Mathf.Clamp(segments, 12, 192);
        pixelDensity = Mathf.Clamp01(pixelDensity);
        pixelBrightness = Mathf.Clamp(pixelBrightness, 0f, 3f);
        pixelWaveSpeed = Mathf.Clamp(pixelWaveSpeed, 0f, 2f);
        pixelWaveStrength = Mathf.Clamp01(pixelWaveStrength);
        rimBrightness = Mathf.Clamp(rimBrightness, 0f, 5f);
        pixelRows = Mathf.Clamp(pixelRows, 4, 48);
        orbitSpeed = Mathf.Clamp(orbitSpeed, -0.2f, 0.2f);
    }

    void DestroyGeneratedAssets()
    {
        // Release generated temporary meshes and materials when disabled.
        if (shieldMesh != null)
        {
            if (Application.isPlaying)
                Destroy(shieldMesh);
            else
                DestroyImmediate(shieldMesh);
            shieldMesh = null;
        }

        if (shieldMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(shieldMaterial);
            else
                DestroyImmediate(shieldMaterial);
            shieldMaterial = null;
        }
    }
}

}
