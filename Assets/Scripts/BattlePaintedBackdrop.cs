using UnityEngine;
using UnityEngine.Rendering;

// Fixed-size distant scenery: camera zoom must also zoom the painting.
// A restrained translation parallax prevents scenery feeling screen-locked.
[ExecuteAlways, DefaultExecutionOrder(100), RequireComponent(typeof(Camera))]
public sealed class BattlePaintedBackdrop : MonoBehaviour
{
    public Transform backdrop;
    public float imageAspect = 16f / 9f;
    [Range(1f, 1.1f)] public float overscan = 1.015f;
    [Range(0f, 1f)] public float translationParallax = .35f;
    Camera view;
    Transform wash;
    Transform referenceTarget;
    Vector3 referenceCameraPosition;
    float referenceDepth;
    float referenceOrthographicSize;
    void OnEnable()
    {
        view = GetComponent<Camera>();
        referenceTarget = null;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        FitNow();
    }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= BeforeCamera; }
    void LateUpdate() { FitNow(); }
    void BeforeCamera(ScriptableRenderContext context, Camera camera) { if (camera == view) FitNow(); }
    public void FitNow()
    {
        if (!view) view = GetComponent<Camera>();
        if (!backdrop) backdrop = transform.Find("BattleBackdrop");
        if (!backdrop || !view) return;
        if (referenceTarget != backdrop)
        {
            referenceTarget = backdrop;
            referenceCameraPosition = transform.position;
            referenceDepth = Mathf.Max(1, backdrop.localPosition.z);
            referenceOrthographicSize = view.orthographicSize;
        }
        float aspect = Mathf.Max(.1f, view.aspect);
        var controller = GetComponent<BattleCameraController>();
        float depth = SafeBackdropDepth(controller);
        float depthRatio = depth / referenceDepth;
        // Same maximum-FOV coverage principle as the original branch's
        // BattleCleanBackdropSetupUtility. Never cancel ordinary camera zoom.
        float coverageFov = Mathf.Max(view.fieldOfView, controller ? controller.maximumFramingFieldOfView : 55f);
        float visibleHeight = view.orthographic ? view.orthographicSize * 2 : 2 * depth * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
        float height = (view.orthographic ? Mathf.Max(referenceOrthographicSize, view.orthographicSize) * 2 :
            2 * depth * Mathf.Tan(coverageFov * .5f * Mathf.Deg2Rad)) * overscan;
        float width = height * Mathf.Max(.1f, imageAspect);
        if (width < height * aspect) { width = height * aspect; height = width / Mathf.Max(.1f, imageAspect); }
        backdrop.localScale = new Vector3(width, height, 1);
        Vector3 drift = transform.InverseTransformVector(transform.position - referenceCameraPosition) * -translationParallax;
        if (!view.orthographic) drift *= depthRatio;
        // Keep a half-percent safety border even at maximum zoom-out/wide aspect.
        float horizontalRoom = Mathf.Max(0, (width - visibleHeight * aspect * 1.005f) * .5f);
        float verticalRoom = Mathf.Max(0, (height - visibleHeight * 1.005f) * .5f);
        backdrop.localPosition = new Vector3(Mathf.Clamp(drift.x, -horizontalRoom, horizontalRoom),
            Mathf.Clamp(drift.y, -verticalRoom, verticalRoom), depth);
        SynchronizeWash();
    }
    float SafeBackdropDepth(BattleCameraController controller)
    {
        // Camera-space depth, not world Z: this also handles the 8-degree tilt.
        // Expand the arena to include columns, trim and hanging banners.
        Bounds arena = controller && controller.ArenaCompositionBounds.HasValue
            ? controller.ArenaCompositionBounds.Value
            : new Bounds(new Vector3(controller ? controller.stageCentreX : -2, 3.2f, 0), new Vector3(32, 6.4f, 5.8f));
        arena.Expand(new Vector3(4, 20, 10));
        float depth = referenceDepth;
        for (int i=0;i<8;i++)
        {
            Vector3 corner = arena.center + Vector3.Scale(arena.extents,
                new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            depth = Mathf.Max(depth, transform.InverseTransformPoint(corner).z + 4f);
        }
        if (controller)
        {
            if (controller.playerTarget) depth = Mathf.Max(depth, transform.InverseTransformPoint(controller.playerTarget.position).z + 4f);
            if (controller.enemyTarget) depth = Mathf.Max(depth, transform.InverseTransformPoint(controller.enemyTarget.position).z + 4f);
        }
        return Mathf.Min(depth, view.farClipPlane - .5f);
    }
    void SynchronizeWash()
    {
        // The existing camera creates its subtle tint wash at Awake. It must
        // cover precisely the same image area at every zoom, not a fixed patch.
        if (!wash) wash = transform.Find("BattleBackdropWash");
        if (!wash) return;
        wash.localScale = backdrop.localScale;
        wash.localRotation = backdrop.localRotation;
        wash.localPosition = backdrop.localPosition + Vector3.back * .05f;
    }
    void OnValidate() { FitNow(); }
}
