using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Keeps every chest part in the same scan space in the editor and player.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ChestScanSpace : MonoBehaviour
{
    private static readonly int WorldToChestId = Shader.PropertyToID("_WorldToChest");
    private static readonly int BoundsMinId = Shader.PropertyToID("_BoundsMin");
    private static readonly int BoundsMaxId = Shader.PropertyToID("_BoundsMax");

    [SerializeField] private Renderer[] targets;
    [SerializeField] private Vector3 boundsMin;
    [SerializeField] private Vector3 boundsMax;
    [SerializeField] private bool configured;

    private MaterialPropertyBlock properties;
    private readonly List<Material> sharedMaterials = new List<Material>();

    public void Configure(Renderer[] renderers, Vector3 minimum, Vector3 maximum)
    {
        ValidateConfiguration(renderers, minimum, maximum);
        targets = (Renderer[])renderers.Clone();
        boundsMin = minimum;
        boundsMax = maximum;
        configured = true;
        RefreshCoordinates();
    }

    private void OnEnable()
    {
        if (configured) RefreshCoordinates();
    }

    private void LateUpdate()
    {
        // ExecuteAlways invokes this after editor scene changes as well as in Play mode.
        if (configured) RefreshCoordinates();
    }

    private void RefreshCoordinates()
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        Matrix4x4 worldToChest = transform.worldToLocalMatrix;
        foreach (Renderer target in targets)
        {
            // A child may be removed independently during the object's lifetime.
            if (target == null) continue;
            target.GetPropertyBlock(properties);
            SetCoordinates(worldToChest);
            target.SetPropertyBlock(properties);

            // Per-slot blocks override the whole renderer block. Preserve their
            // existing values and add the coordinates to those blocks as well.
            target.GetSharedMaterials(sharedMaterials);
            int slotCount = sharedMaterials.Count;
            for (int slot = 0; slot < slotCount; slot++)
            {
                target.GetPropertyBlock(properties, slot);
                if (properties.isEmpty) continue;
                SetCoordinates(worldToChest);
                target.SetPropertyBlock(properties, slot);
            }
        }
    }

    private void SetCoordinates(Matrix4x4 worldToChest)
    {
        properties.SetMatrix(WorldToChestId, worldToChest);
        properties.SetVector(BoundsMinId, boundsMin);
        properties.SetVector(BoundsMaxId, boundsMax);
    }

    private static void ValidateConfiguration(Renderer[] renderers, Vector3 minimum, Vector3 maximum)
    {
        if (renderers == null || renderers.Length == 0)
            throw new ArgumentException("ChestScanSpace requires at least one chest renderer.", nameof(renderers));
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index] == null)
                throw new ArgumentException("ChestScanSpace renderer at index " + index + " is missing.", nameof(renderers));
        }
        if (!IsFinite(minimum) || !IsFinite(maximum) || minimum.x >= maximum.x || minimum.y >= maximum.y || minimum.z >= maximum.z)
            throw new ArgumentException("ChestScanSpace requires finite minimum/maximum bounds with positive size on every axis.", nameof(maximum));
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
