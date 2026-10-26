using UnityEngine;

/// <summary>Owns only the existing chest's visuals; BaselineWorld owns its opened state.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-90)]
public sealed class BaselineChestVisual : MonoBehaviour
{
    public ChestDissolveController dissolve;
    public Transform lid;

    private Renderer[] renderers;
    private Collider[] colliders;
    private bool[] rendererStates;
    private bool[] colliderStates;
    private NPCDialogue npc;
    private bool npcEnabled;
    private Quaternion closedLidRotation;
    private bool captured;
    private bool dissolving;

    private void Awake()
    {
        Capture();
        // The world owns chest eligibility. Suppress the controller's standalone E flow.
        if (dissolve) dissolve.enabled = false;
    }

    private void Capture()
    {
        if (captured) return;
        if (!dissolve) dissolve = GetComponent<ChestDissolveController>();
        if (!lid)
        {
            BaselineInteraction interaction = GetComponent<BaselineInteraction>();
            if (interaction) lid = interaction.lid;
        }
        closedLidRotation = lid ? lid.localRotation : Quaternion.identity;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        rendererStates = new bool[renderers.Length];
        colliderStates = new bool[colliders.Length];
        for (int i = 0; i < renderers.Length; i++) rendererStates[i] = renderers[i].enabled;
        for (int i = 0; i < colliders.Length; i++) colliderStates[i] = colliders[i].enabled;
        npc = GetComponent<NPCDialogue>();
        npcEnabled = npc && npc.enabled;
        captured = true;
    }

    /// <summary>Apply saved state without replaying or granting a reward.</summary>
    public void Restore(bool opened)
    {
        Capture();
        dissolving = false;
        if (dissolve)
        {
            dissolve.enabled = false;
            dissolve.SetProgress(opened ? 1 : 0);
        }
        if (lid) lid.localRotation = opened ? closedLidRotation * Quaternion.Euler(-65, 0, 0) : closedLidRotation;
        ApplyVisibility(!opened);
    }

    /// <summary>Call after the recorded reward's modal closes, or if it cannot be shown.</summary>
    public void PlayAfterReward()
    {
        Capture();
        if (dissolving) return;
        if (!isActiveAndEnabled || !dissolve || dissolve.IsDissolved)
        {
            Restore(true);
            return;
        }
        if (lid) lid.localRotation = closedLidRotation * Quaternion.Euler(-65, 0, 0);
        StopInteraction();
        dissolve.enabled = true;
        dissolve.BeginDissolve();
        dissolving = true;
    }

    private void LateUpdate()
    {
        // The dissolve controller runs earlier and maintains its own duration/materials.
        if (dissolving && (!dissolve || dissolve.IsDissolved)) Restore(true);
    }

    private void StopInteraction()
    {
        if (npc) npc.enabled = false;
        // Disabling a trigger does not guarantee an OnTriggerExit callback this frame.
        foreach (PlayerInteraction player in FindObjectsByType<PlayerInteraction>(FindObjectsSortMode.None))
            if (npc) player.UnregisterCandidate(npc);
        foreach (Collider collider in colliders) if (collider && collider.isTrigger) collider.enabled = false;
    }

    private void ApplyVisibility(bool visible)
    {
        for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = visible && rendererStates[i];
        for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = visible && colliderStates[i];
        if (visible)
        {
            if (npc) npc.enabled = npcEnabled;
        }
        else StopInteraction();
    }

    private void OnDisable()
    {
        // A scene suspension during the animation must not leave a collected chest visible.
        if (dissolving) Restore(true);
    }
}
