using UnityEngine;

// CombatFeedback restores one cached color per renderer. The mounted model has
// several materials per renderer, so clear that transient override after a flash.
[DisallowMultipleComponent]
public sealed class MountedKnightMaterialRestore : MonoBehaviour
{
    CombatFeedback feedback;
    SkinnedMeshRenderer[] skins;
    bool wasFlashing;
    void OnEnable()
    {
        feedback = GetComponent<CombatFeedback>();
        skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var skin in skins) skin.SetPropertyBlock(null);
        wasFlashing = false;
    }
    void LateUpdate()
    {
        bool flashing = feedback && feedback.IsFlashing;
        if (wasFlashing && !flashing)
            foreach (var skin in skins) if (skin) skin.SetPropertyBlock(null);
        wasFlashing = flashing;
    }
}
