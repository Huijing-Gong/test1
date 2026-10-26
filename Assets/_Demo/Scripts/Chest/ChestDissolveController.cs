using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A standalone visual interaction; it does not grant or alter game progress.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(ChestScanSpace))]
public sealed class ChestDissolveController : MonoBehaviour
{
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int ProximityId = Shader.PropertyToID("_Proximity");

    [SerializeField] private Renderer[] chestRenderers;
    [SerializeField] private Collider[] bodyColliders;
    [SerializeField] private Bounds localBounds;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField, Min(0.01f)] private float dissolveDuration = 2f;
    [SerializeField, Min(0.1f)] private float interactionDistance = 2.6f;
    [SerializeField] private bool showInteractionPrompt = true;

    private readonly List<Material> ownedMaterials = new List<Material>();
    private readonly Dictionary<Collider, bool> initialColliderStates = new Dictionary<Collider, bool>();
    private Renderer[] materialRenderers;
    private Material[][] originalMaterials;
    private PlayerInteraction playerInteraction;
    private DialogueUI dialogueUI;
    private ChestRewardPresentation rewardPresentation;
    private bool initialized;
    private bool dialogueActiveAtFrameStart;
    private bool canInteract;
    private bool isDissolving;
    private float progress;

    public float Progress => progress;
    public bool IsDissolving => isDissolving;
    public bool IsDissolved => progress >= 1f;
    public Bounds LocalBounds => localBounds;
    public Renderer[] ChestRenderers => chestRenderers == null ? Array.Empty<Renderer>() : (Renderer[])chestRenderers.Clone();

    /// <summary>Call after assigning the chest's meshes and ArtLab materials.</summary>
    public void Configure(PlayerMovement interactionPlayer = null)
    {
        if (initialized) RestoreAndDestroyMaterials();
        chestRenderers = GetComponentsInChildren<Renderer>(true);
        localBounds = CalculateLocalBounds(transform, chestRenderers);
        ValidateMaterials();
        GetComponent<ChestScanSpace>().Configure(chestRenderers, localBounds.min, localBounds.max);

        List<Collider> solidColliders = new List<Collider>();
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger) continue;
            solidColliders.Add(collider);
            if (Application.isPlaying && !initialColliderStates.ContainsKey(collider))
                initialColliderStates.Add(collider, collider.enabled);
        }
        bodyColliders = solidColliders.ToArray();
        if (interactionPlayer != null) playerMovement = interactionPlayer;
        if (Application.isPlaying) ResolvePlayer();

        if (initialized)
        {
            CreateMaterialInstances();
            ApplyProgress(progress);
        }
    }

    /// <summary>Transform all eight local bound corners of every renderer into the root space.</summary>
    public static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (renderers == null || renderers.Length == 0)
            throw new ArgumentException("The chest needs at least one renderer.", nameof(renderers));

        Bounds result = default;
        bool hasPoint = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) throw new ArgumentException("A chest renderer is missing.", nameof(renderers));
            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.localToWorldMatrix;
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                Vector3 point = toRoot.MultiplyPoint3x4(localCorner);
                if (!hasPoint)
                {
                    result = new Bounds(point, Vector3.zero);
                    hasPoint = true;
                }
                else result.Encapsulate(point);
            }
        }
        return result;
    }

    private void Awake() => EnsureInitialized();

    private void Update()
    {
        // This runs before PlayerInteraction.Update. Remember a dialogue that an
        // E press might finish, so that same press cannot also dissolve the chest.
        ResolvePlayer();
        dialogueActiveAtFrameStart = playerInteraction != null && playerInteraction.IsDialogueActive;
    }

    private void LateUpdate()
    {
        if (isDissolving)
            ApplyProgress(progress + Time.deltaTime / Mathf.Max(0.01f, dissolveDuration));

        // All NPC Update calls have finished before this independent E interaction.
        canInteract = !IsDissolved && !isDissolving && !dialogueActiveAtFrameStart
            && (rewardPresentation == null || !rewardPresentation.HasPresented)
            && playerMovement != null && playerMovement.isActiveAndEnabled
            && playerMovement.CanStartInteraction
            && (playerInteraction == null || (!playerInteraction.IsDialogueActive && playerInteraction.SelectedNPC == null))
            && (playerMovement.transform.position - transform.position).sqrMagnitude <= interactionDistance * interactionDistance;
        SetProximity(canInteract ? 1f : 0f);
        if (canInteract && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            var hero=playerMovement.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
            if(hero){hero.PresentInteraction("INTERACT_CHEST",()=>{if(this&&!IsDissolved&&!isDissolving&&playerMovement.CanStartInteraction&&(playerMovement.transform.position-transform.position).sqrMagnitude<=interactionDistance*interactionDistance)ShowReward();});}
            else ShowReward();
        }
        if (showInteractionPrompt && canInteract)
        {
            if (dialogueUI == null) dialogueUI = FindFirstObjectByType<DialogueUI>();
            if (dialogueUI != null) dialogueUI.OfferPrompt("[E] Chest", 10);
        }
    }

    void ShowReward()
    {
            if (rewardPresentation == null) rewardPresentation = gameObject.AddComponent<ChestRewardPresentation>();
            if (rewardPresentation.Show(this, playerMovement))
            {
                canInteract = false;
                SetProximity(0f);
            }
    }

    public void BeginDissolve()
    {
        if (!Application.isPlaying) return;
        if (rewardPresentation != null && rewardPresentation.IsOpen) return;
        EnsureInitialized();
        if (IsDissolved || isDissolving) return;
        isDissolving = true;
        canInteract = false;
        SetProximity(0f);
    }

    public void ResetDissolve() => SetProgress(0f);

    /// <summary>Hold a chosen state for validation without changing source material assets.</summary>
    public void SetProgress(float value)
    {
        if (!Application.isPlaying) return;
        if (rewardPresentation != null && rewardPresentation.IsOpen) value = 0;
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Progress must be finite.");
        EnsureInitialized();
        isDissolving = false;
        canInteract = false;
        SetProximity(0f);
        ApplyProgress(value);
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        Configure();
        CreateMaterialInstances();
        initialized = true;
        ApplyProgress(0f);
    }

    private void ValidateMaterials()
    {
        foreach (Renderer renderer in chestRenderers)
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                throw new InvalidOperationException(renderer.name + " needs an ArtLab/Chest Dissolve material.");
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null || material.shader.name != "ArtLab/Chest Dissolve")
                    throw new InvalidOperationException(renderer.name + " must use ArtLab/Chest Dissolve in every material slot.");
            }
        }
    }

    private void CreateMaterialInstances()
    {
        Dictionary<Material, Material> instances = new Dictionary<Material, Material>();
        materialRenderers = (Renderer[])chestRenderers.Clone();
        originalMaterials = new Material[materialRenderers.Length][];
        for (int index = 0; index < materialRenderers.Length; index++)
        {
            Material[] sources = materialRenderers[index].sharedMaterials;
            originalMaterials[index] = sources;
            Material[] assigned = new Material[sources.Length];
            for (int slot = 0; slot < sources.Length; slot++)
            {
                Material source = sources[slot];
                if (!instances.TryGetValue(source, out Material instance))
                {
                    instance = new Material(source) { name = source.name + " (Chest Instance)" };
                    instances.Add(source, instance);
                    ownedMaterials.Add(instance);
                }
                assigned[slot] = instance;
            }
            materialRenderers[index].sharedMaterials = assigned;
        }
    }

    private void ApplyProgress(float value)
    {
        bool wasDissolved = IsDissolved;
        progress = Mathf.Clamp01(value);
        foreach (Material material in ownedMaterials) material.SetFloat(ProgressId, progress);
        if (IsDissolved)
        {
            isDissolving = false;
            foreach (Collider collider in bodyColliders)
                if (collider != null) collider.enabled = false;
        }
        else if (wasDissolved)
        {
            foreach (Collider collider in bodyColliders)
                if (collider != null && initialColliderStates.TryGetValue(collider, out bool wasEnabled))
                    collider.enabled = wasEnabled;
        }
        // Renderers remain enabled at 1: the shader clips colour, depth and shadows.
    }

    private void SetProximity(float value)
    {
        foreach (Material material in ownedMaterials) material.SetFloat(ProximityId, value);
    }

    private void ResolvePlayer()
    {
        if (playerMovement == null) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null && (playerInteraction == null || playerInteraction.gameObject != playerMovement.gameObject))
            playerInteraction = playerMovement.GetComponent<PlayerInteraction>();
    }

    private void OnDisable()
    {
        canInteract = false;
        SetProximity(0f);
    }

    private void OnDestroy() => RestoreAndDestroyMaterials();

    private void RestoreAndDestroyMaterials()
    {
        if (materialRenderers != null && originalMaterials != null)
        {
            for (int index = 0; index < materialRenderers.Length; index++)
                if (materialRenderers[index] != null) materialRenderers[index].sharedMaterials = originalMaterials[index];
        }
        foreach (Material material in ownedMaterials)
            if (material != null) Destroy(material);
        ownedMaterials.Clear();
        materialRenderers = null;
        originalMaterials = null;
    }
}
