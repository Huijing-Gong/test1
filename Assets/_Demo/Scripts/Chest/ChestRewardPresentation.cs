using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>A visual reward only. Does not modify inventory, equipment or progression.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class ChestRewardPresentation : MonoBehaviour
{
    private ChestDissolveController chest;
    private PlayerMovement player;
    private ThirdPersonCamera look;
    private DialogueUI ui;
    private GameObject visual;
    private Material material;
    private Mesh crystal;
    private Vector3 anchor;
    private bool wasMovementLocked;
    private bool wasLookLocked;
    private float elapsed;
    private int openedFrame;
    private GameObject previewRoot;
    private GameObject modal;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private RectTransform itemArea;
    private bool blankPress;
    private bool closeRequested;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    public int RewardGrantCount { get; private set; }
    public bool HasPresented => RewardGrantCount > 0;
    public bool IsOpen { get; private set; }

    public bool Show(ChestDissolveController owner, PlayerMovement interactionPlayer)
    {
        if (HasPresented || !owner || !interactionPlayer) return false;
        ui = FindFirstObjectByType<DialogueUI>();
        var sword = FindFirstObjectByType<RewardSwordDisplay>();
        if (!ui || !sword || !sword.hologramMaterial) return false;
        if (!ui.BeginRewardPresentation()) return false;
        chest = owner;
        player = interactionPlayer;
        look = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        wasMovementLocked = player.IsGameplayInputLocked;
        wasLookLocked = look && look.IsLookInputLocked;
        player.SetGameplayInputLocked(true);
        if (look) look.SetLookInputLocked(true);
        // The display-only reward is granted once, on opening, not on dismissal.
        RewardGrantCount = 1;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsOpen = true;
        openedFrame = Time.frameCount;
        elapsed = 0;
        material = new Material(sword.hologramMaterial) { name = "Chest Energy Core (runtime)" };
        material.SetColor("_BodyColor", new Color(.08f, .85f, 1f));
        material.SetFloat("_Opacity", .78f);
        material.SetFloat("_ScanDensity", 18);
        material.SetFloat("_ScanIntensity", .55f);
        owner.SetProgress(0);
        previewRoot = new GameObject("Reward Preview Stage") { hideFlags = HideFlags.DontSave };
        // An isolated, unlit preview is rendered to the modal instead of the game world.
        anchor = new Vector3(0, -10000, 0);
        previewRoot.transform.position = anchor;
        CreateVisual();
        CreateModal();
        return true;
    }

    private void CreateVisual()
    {
        visual = new GameObject("Energy Core Reward");
        visual.transform.SetParent(previewRoot.transform, false);
        visual.transform.position = anchor;
        visual.transform.localScale = Vector3.zero;
        var body = new GameObject("Core", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(visual.transform, false);
        Vector3[] points = { Vector3.up * .65f, Vector3.down * .65f,
            Vector3.right * .36f, Vector3.forward * .36f, Vector3.left * .36f, Vector3.back * .36f };
        crystal = new Mesh { name = "Energy Core Crystal" };
        crystal.vertices = points;
        crystal.triangles = new[] { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
        crystal.uv = new[] { new Vector2(.5f,1), new Vector2(.5f,0), new Vector2(0,.5f), new Vector2(.25f,.5f), new Vector2(.5f,.5f), new Vector2(.75f,.5f) };
        crystal.colors = new[] { Color.black, Color.black, Color.black, Color.black, Color.black, Color.black };
        crystal.RecalculateNormals();
        crystal.RecalculateBounds();
        body.GetComponent<MeshFilter>().sharedMesh = crystal;
        body.GetComponent<MeshRenderer>().sharedMaterial = material;
        body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        for (int ringIndex = 0; ringIndex < 2; ringIndex++)
        {
            var ring = new GameObject("Orbit", typeof(LineRenderer));
            ring.transform.SetParent(visual.transform, false);
            ring.transform.localRotation = Quaternion.Euler(ringIndex == 0 ? 25 : -45, 0, ringIndex * 55);
            var line = ring.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 64;
            line.widthMultiplier = .025f;
            line.sharedMaterial = material;
            line.startColor = line.endColor = ringIndex == 0 ? Color.black : Color.white;
            line.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2 / 64;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .65f);
            }
        }
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (!chest || !chest.isActiveAndEnabled || !player || !ui || !ui.isActiveAndEnabled)
        {
            Close(false);
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        if (visual)
        {
            visual.transform.position = anchor + Vector3.up * Mathf.Sin(elapsed * 2.2f) * .09f;
            visual.transform.localScale = Vector3.one * Mathf.SmoothStep(0, 1, elapsed / .45f);
            visual.transform.rotation = Quaternion.Euler(0, elapsed * 35, 0);
        }
        var mouse = Mouse.current;
        if (Time.frameCount > openedFrame && mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            bool outside = !RectTransformUtility.RectangleContainsScreenPoint(itemArea, position);
            if (mouse.leftButton.wasPressedThisFrame) blankPress = outside;
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                closeRequested = blankPress && outside;
                blankPress = false;
            }
        }
    }

    private void LateUpdate()
    {
        // Consume the click before releasing gameplay input, avoiding click-through attacks.
        if (IsOpen && closeRequested) Close(true);
    }

    private void CreateModal()
    {
        foreach (var child in previewRoot.GetComponentsInChildren<Transform>()) child.gameObject.layer = 31;
        var cameraObject = new GameObject("Reward Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(previewRoot.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, .04f, -3);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << 31;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = .95f;
        previewCamera.nearClipPlane = .1f;
        previewCamera.farClipPlane = 8;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;
        data.volumeLayerMask = 0;
        previewTexture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32)
            { name = "Reward Modal Preview", antiAliasing = 1 };
        previewTexture.Create();
        previewCamera.targetTexture = previewTexture;

        modal = new GameObject("Chest Reward Modal", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        modal.hideFlags = HideFlags.DontSave;
        var canvas = modal.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = modal.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var shade = new GameObject("Background Shade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(modal.transform, false);
        var shadeRect = shade.GetComponent<RectTransform>();
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(.015f, .025f, .04f, .80f);
        shade.GetComponent<Image>().raycastTarget = false;

        itemArea = CenteredRect("Protected Item Area", modal.transform, new Vector2(640, 720), new Vector2(0, 20));
        var imageRect = CenteredRect("Hologram", itemArea, new Vector2(560, 560), new Vector2(0, 40));
        var image = imageRect.gameObject.AddComponent<RawImage>();
        image.texture = previewTexture;
        image.raycastTarget = false;
        AddText("REWARD ACQUIRED", itemArea, new Vector2(0, 335), 22, new Color(.45f, .86f, 1));
        AddText("Energy Core", itemArea, new Vector2(0, -275), 42, Color.white);
        AddText("Click an empty area to continue", modal.transform, new Vector2(0, -420), 25, new Color(.8f, .85f, .9f));
    }

    private static RectTransform CenteredRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private void AddText(string value, Transform parent, Vector2 position, int size, Color color)
    {
        var rect = CenteredRect(value, parent, new Vector2(900, 64), position);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = ui.PresentationFont;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
    }

    private void Close(bool dissolve)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (ui) ui.HideRewardPresentation();
        if (player) player.SetGameplayInputLocked(wasMovementLocked);
        if (look) look.SetLookInputLocked(wasLookLocked);
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        if (previewCamera) previewCamera.targetTexture = null;
        if (previewRoot) Destroy(previewRoot);
        if (modal) Destroy(modal);
        if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
        if (material) Destroy(material);
        if (crystal) Destroy(crystal);
        if (dissolve && chest && chest.isActiveAndEnabled) chest.BeginDissolve();
    }

    private void OnDisable() => Close(false);
}
