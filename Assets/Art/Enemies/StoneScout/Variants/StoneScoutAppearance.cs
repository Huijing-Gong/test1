using System;
using UnityEngine;

// Cosmetic-only selector. All three looks share the same mesh, animation and combat prefab.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class StoneScoutAppearance : MonoBehaviour
{
    public enum Look { Forest, Sand, Awakened }

    [SerializeField] private Look look;
    [SerializeField] private Material forestMaterial;
    [SerializeField] private Material sandMaterial;
    [SerializeField] private Material awakenedMaterial;

    public Look CurrentLook => look;

    public void SetLook(Look value)
    {
        look = value;
        Apply();
    }

    private void OnEnable()
    {
        RestoreLookForPlacedInstance();
        Apply();
    }
    private void OnValidate()
    {
        RestoreLookForPlacedInstance();
        Apply();
    }

    private void RestoreLookForPlacedInstance()
    {
        // The six scene copies saved their variant names and materials before the
        // Look field was recorded. OnValidate can run before OnEnable, so recover
        // from the persisted name before either callback repaints the meshes.
        if (name.StartsWith("StoneScout_Sand_", StringComparison.Ordinal)) look = Look.Sand;
        else if (name.StartsWith("StoneScout_Awakened_", StringComparison.Ordinal)) look = Look.Awakened;
        else if (name.StartsWith("StoneScout_Forest_", StringComparison.Ordinal)) look = Look.Forest;
    }

    public void Apply()
    {
        Material selected = look == Look.Sand ? sandMaterial :
            look == Look.Awakened ? awakenedMaterial : forestMaterial;
        if (selected == null || selected.shader == null)
            return;

        // Only the ten authored body pieces use this shader. Runtime punch glows,
        // impact effects and loot keep their own materials.
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            Material current = renderer.sharedMaterial;
            if (current != null && current.shader == selected.shader && current != selected)
                renderer.sharedMaterial = selected;
        }
    }
}
