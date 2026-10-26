using UnityEngine;

internal static class BattleFighterUtility
{
    public static Transform GetVisualRoot(GameObject fighter)
    {
        if (fighter == null)
            return null;

        Animator animator = fighter.GetComponentInChildren<Animator>();
        if (animator == null)
            return null;

        Transform visualRoot = animator.transform;
        while (visualRoot.parent != null &&
            visualRoot.parent != fighter.transform)
        {
            visualRoot = visualRoot.parent;
        }

        return visualRoot;
    }

    public static void FaceCamera(GameObject fighter, float rotationY)
    {
        Transform visualRoot = GetVisualRoot(fighter);
        if (visualRoot != null)
        {
            visualRoot.localRotation = Quaternion.Euler(
                0f,
                rotationY,
                0f
            );
        }
    }

    public static void FaceDirection(GameObject fighter, float direction)
    {
        Transform visualRoot = GetVisualRoot(fighter);
        if (visualRoot == null)
            return;

        float yRotation = direction >= 0f ? 90f : -90f;
        visualRoot.localRotation = Quaternion.Euler(0f, yRotation, 0f);
    }
}
