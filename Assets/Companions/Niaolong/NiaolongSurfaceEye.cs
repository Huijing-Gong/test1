using UnityEngine;

// The face markings follow the actual curved body, including during a blink.
// Every instance gets its own deformable mesh; the authored asset stays untouched.
[DefaultExecutionOrder(300)]
public sealed class NiaolongSurfaceEye : MonoBehaviour
{
    public Mesh sourceMesh;
    Mesh instance;
    MeshFilter filter;
    Vector3[] neutral, deformed, normals;
    float[] clearance;
    Vector3 lastScale = Vector3.zero;
    Vector3 lastPosition;

    public static float BodyFront(float x, float y)
    {
        float t = Mathf.Clamp01((y - .035f) / .81f);
        float r = .367f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .34f) * (1 - .20f * t);
        return Mathf.Sqrt(Mathf.Max(0, r * r - x * x)) * .83f;
    }

    void Awake() { UpdateGeometry(); }
    void LateUpdate() { UpdateGeometry(); }

    public void UpdateGeometry()
    {
        if (!sourceMesh) return;
        if (!instance)
        {
            filter = GetComponent<MeshFilter>();
            if (!filter) return;
            instance = Instantiate(sourceMesh);
            instance.name = sourceMesh.name + " (blink instance)";
            instance.hideFlags = HideFlags.DontSave;
            instance.MarkDynamic();
            neutral = sourceMesh.vertices;
            deformed = new Vector3[neutral.Length];
            normals = new Vector3[neutral.Length];
            clearance = new float[neutral.Length];
            var offsets = sourceMesh.uv2;
            for (int i = 0; i < clearance.Length; i++) clearance[i] = offsets[i].x;
            filter.sharedMesh = instance;
        }
        var scale = transform.localScale;
        var position = transform.localPosition;
        if (lastScale == scale && lastPosition == position) return;
        lastScale = scale;
        lastPosition = position;
        float zScale = Mathf.Max(.00001f, Mathf.Abs(scale.z));
        const float h = .0001f;
        for (int i = 0; i < neutral.Length; i++)
        {
            var v = neutral[i];
            float x = position.x + v.x * scale.x;
            float y = position.y + v.y * scale.y;
            deformed[i] = new Vector3(v.x, v.y, (BodyFront(x, y) + clearance[i] - position.z) / zScale);
            float dx = (BodyFront(x + h, y) - BodyFront(x - h, y)) / (2 * h);
            float dy = (BodyFront(x, y + h) - BodyFront(x, y - h)) / (2 * h);
            // Compensate for the blink Transform's non-uniform scale.
            normals[i] = new Vector3(-dx * scale.x, -dy * scale.y, scale.z).normalized;
        }
        instance.vertices = deformed;
        instance.normals = normals;
        instance.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (!instance) return;
        if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
    }
}
