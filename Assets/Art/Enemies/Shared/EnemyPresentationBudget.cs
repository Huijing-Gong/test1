using UnityEngine;

// Presentation only. Never use this to disable AI, hitboxes, damage or attack warnings.
// A single camera lookup shared by every enemy, not Camera.main per part/per frame.
public static class EnemyPresentationBudget
{
    public const float DetailDistance = 24f;
    static Camera camera;
    static float nextSearch;
    static int frame = -1;
    static Vector3 viewPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { camera = null; nextSearch = 0f; frame = -1; }

    public static bool Details(Vector3 position)
    {
        if (!camera || !camera.isActiveAndEnabled || Time.unscaledTime >= nextSearch)
        { camera = Camera.main; nextSearch = Time.unscaledTime + .25f; frame = -1; }
        if (!camera) return true; // Headless/gameplay checks must not depend on a camera.
        if (frame != Time.frameCount)
        { frame = Time.frameCount; viewPosition = camera.transform.position; }
        return (viewPosition - position).sqrMagnitude <= DetailDistance * DetailDistance;
    }
}
