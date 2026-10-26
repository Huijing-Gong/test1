using UnityEngine;

// Per-instance appearance only. Never edits the main world's shared materials/textures.
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public sealed class MinigameMaterialStyle : MonoBehaviour
{
    public Color color = Color.white;
    [ColorUsage(false, true)] public Color emission = Color.black;
    public Texture texture;
    public Vector2 tiling = Vector2.one;
    Material original, instance;
    Renderer target;
    void OnEnable() => Apply();
    void OnValidate() => Apply();
    public void Apply()
    {
        target = GetComponent<Renderer>();
        if (!target || !target.sharedMaterial) return;
        if (Application.isPlaying && emission.maxColorComponent > 0 && !instance)
        {
            original = target.sharedMaterial;
            instance = new Material(original) { hideFlags = HideFlags.DontSave };
            instance.EnableKeyword("_EMISSION");
            target.sharedMaterial = instance;
        }
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        properties.SetColor("_EmissionColor", emission);
        if (texture) properties.SetTexture("_BaseMap", texture);
        properties.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, 0, 0));
        target.SetPropertyBlock(properties);
    }
    void OnDisable()
    {
        if (target) { target.SetPropertyBlock(null); if (original) target.sharedMaterial = original; }
        if (instance) { if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance); }
        instance = null;
    }
}
