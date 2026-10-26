using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Presents a reward that the caller has already recorded. Never grants progress.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(400)]
public sealed class BaselineRewardPresentation : MonoBehaviour
{
    [Tooltip("Use Assets/Art/Rewards/HologramSword/HologramSword.prefab.")]
    public GameObject swordPrefab;

    private const int PreviewLayer = 31;
    private const float RewardIconBobAmplitude = 5.5f;
    private const float RewardIconBobSpeed = 2.1f;
    private const float RewardIconSwayAmplitude = .65f;
    private const float RewardIconSwaySpeed = 1.65f;
    private readonly List<Material> previewMaterials = new List<Material>();
    private PlayerMovement player;
    private ThirdPersonCamera look;
    private DialogueUI dialogue;
    private bool previousMovementLock;
    private bool previousLookLock;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool ownsInput;
    private bool closing;
    private bool blankPress;
    private bool continuePress;
    private int openedFrame;
    private int dismissalFrame;
    private float elapsed;
    private Action onClosed;
    private GameObject modal;
    private Texture2D acquiredCard;
    private Texture2D acquiredCardBackground;
    private Texture2D acquiredSwordIcon;
    private bool islandRelicCard;
    private bool ridgeCharmCard;
    private bool runicGuardCard;
    private bool SmallFloatingCard => islandRelicCard || ridgeCharmCard || runicGuardCard;
    private CanvasGroup modalCanvasGroup;
    private RectTransform cardContent;
    private RectTransform floatingSword;
    private Vector2 cardStartPosition;
    private Vector2 swordRestPosition;
    private GameObject previewRoot;
    private Transform rotor;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private RectTransform protectedArea;
    private RectTransform continueButtonArea;

    // Remains true during the dismissal frame, until gameplay can safely resume.
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Returns false when busy or unavailable. A successfully opened presentation invokes
    /// the callback once on dismissal or cancellation (including disable/scene exit).
    /// The callback finalizes visuals only; the caller records progress before Show.
    /// Setup failure returns false without invoking the callback.
    /// </summary>
    public bool Show(string title, string description, bool sword, Action onClosed)
    {
        if (!isActiveAndEnabled || IsOpen || ownsInput) return false;
        islandRelicCard = !sword && string.Equals(title, "Island relic", StringComparison.OrdinalIgnoreCase);
        ridgeCharmCard = !sword && string.Equals(title, "Ridge charm", StringComparison.OrdinalIgnoreCase);
        runicGuardCard = !sword && string.Equals(title, "Runic guard", StringComparison.OrdinalIgnoreCase);
        if (sword)
        {
            acquiredCardBackground = Resources.Load<Texture2D>("P3Rewards/PracticeSwordCardBase");
            acquiredSwordIcon = Resources.Load<Texture2D>("P3Rewards/PracticeSwordIcon");
            acquiredCard = Resources.Load<Texture2D>("P3Rewards/PracticeSwordAcquired");
        }
        else if (islandRelicCard)
        {
            acquiredCardBackground = Resources.Load<Texture2D>("P3Rewards/IslandRelicCardBase");
            acquiredSwordIcon = Resources.Load<Texture2D>("P3Rewards/IslandRelicIconV6");
        }
        else if (ridgeCharmCard)
        {
            acquiredCardBackground = Resources.Load<Texture2D>("P3Rewards/RidgeCharmCardBase");
            acquiredSwordIcon = Resources.Load<Texture2D>("P3Rewards/RidgeCharmIconV6");
        }
        else if (runicGuardCard)
        {
            acquiredCardBackground = Resources.Load<Texture2D>("P3Rewards/RunicGuardCardBase");
            acquiredSwordIcon = Resources.Load<Texture2D>("P3Rewards/RunicGuardIconV6");
        }
        bool hasLayeredCard = acquiredCardBackground && acquiredSwordIcon;
        if (!isActiveAndEnabled || IsOpen || ownsInput || (sword && !hasLayeredCard && !acquiredCard && !swordPrefab)) return false;
        player = FindFirstObjectByType<PlayerMovement>();
        dialogue = FindFirstObjectByType<DialogueUI>();
        Camera main = Camera.main;
        look = main ? main.GetComponent<ThirdPersonCamera>() : null;
        if (!player || !player.isActiveAndEnabled || !dialogue || !dialogue.isActiveAndEnabled)
            return false;
        if (!dialogue.BeginRewardPresentation()) return false;

        previousMovementLock = player.IsGameplayInputLocked;
        previousLookLock = look && look.IsLookInputLocked;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        ownsInput = true;
        player.SetGameplayInputLocked(true);
        if (look) look.SetLookInputLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        this.onClosed = onClosed;
        openedFrame = Time.frameCount;
        elapsed = 0;
        closing = blankPress = continuePress = false;
        IsOpen = true;

        try
        {
            if (sword && !hasLayeredCard && !acquiredCard) CreateSwordPreview();
            CreateModal(title, description, sword);
            return true;
        }
        catch (Exception exception)
        {
            // The caller can still finish its recorded reward if presentation setup fails.
            Finish(false);
            Debug.LogException(exception, this);
            return false;
        }
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (closing)
        {
            UpdateDismissAnimation();
            return;
        }
        if (!player || !player.isActiveAndEnabled || !dialogue || !dialogue.isActiveAndEnabled)
        {
            Finish(true);
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (modalCanvasGroup)
        {
            modalCanvasGroup.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .28f));
            if (cardContent)
            {
                float intro = EaseOutBack(Mathf.Clamp01(elapsed / .52f));
                float scale = Mathf.Lerp(.92f, 1f, intro);
                cardContent.localScale = new Vector3(scale, scale, 1f);
                cardContent.anchoredPosition = Vector2.Lerp(cardStartPosition, Vector2.zero, intro);
            }
        }
        if (floatingSword)
        {
            // Every acquired-item card uses the same restrained hover motion so changing
            // the reward artwork never changes the perceived weight or animation timing.
            floatingSword.anchoredPosition = swordRestPosition +
                Vector2.up * (Mathf.Sin(elapsed * RewardIconBobSpeed) * RewardIconBobAmplitude);
            floatingSword.localRotation = Quaternion.Euler(0, 0,
                Mathf.Sin(elapsed * RewardIconSwaySpeed) * RewardIconSwayAmplitude);
        }
        if (rotor)
        {
            rotor.localRotation = Quaternion.Euler(0, elapsed * 32f, 0);
            rotor.localPosition = Vector3.up * (Mathf.Sin(elapsed * 1.8f) * .07f);
        }
        if (Time.frameCount <= openedFrame) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
        {
            Dismiss();
            return;
        }
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 pointer = mouse.position.ReadValue();
        bool outside = !RectTransformUtility.RectangleContainsScreenPoint(protectedArea, pointer);
        bool onContinue = continueButtonArea && RectTransformUtility.RectangleContainsScreenPoint(continueButtonArea, pointer);
        if (mouse.leftButton.wasPressedThisFrame)
        {
            blankPress = outside;
            continuePress = onContinue;
        }
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            bool dismiss = (blankPress && outside) || (continuePress && onContinue);
            blankPress = continuePress = false;
            if (dismiss) Dismiss();
        }
    }

    /// <summary>Plays a short exit motion while keeping the player's input locked.</summary>
    public void Dismiss()
    {
        if (!IsOpen || closing) return;
        closing = true;
        dismissalFrame = Time.frameCount;
        elapsed = 0f;
    }

    private void UpdateDismissAnimation()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / .16f);
        if (modalCanvasGroup) modalCanvasGroup.alpha = 1f - t;
        if (cardContent)
        {
            cardContent.localScale = Vector3.one * Mathf.Lerp(1f, .965f, t);
            cardContent.anchoredPosition = Vector2.Lerp(Vector2.zero, new Vector2(0, -20), t);
        }
    }

    private void LateUpdate()
    {
        // Keep both Update and LateUpdate consumers locked through the exit motion, so
        // the dismiss click/key cannot leak into gameplay when the modal disappears.
        if (closing && Time.frameCount > dismissalFrame && elapsed >= .16f) Finish(true);
    }

    private void CreateSwordPreview()
    {
        previewRoot = new GameObject("Baseline Reward Preview") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(previewRoot, gameObject.scene);
        previewRoot.SetActive(false);
        previewRoot.transform.position = new Vector3(0, -10000, 0);
        rotor = new GameObject("Rotating Sword").transform;
        rotor.SetParent(previewRoot.transform, false);
        GameObject sword = Instantiate(swordPrefab, rotor, false);
        sword.name = "Hologram Sword Reward";

        // Reuse the real mesh and hologram material; do not run its world-display H shortcut.
        foreach (MonoBehaviour behaviour in sword.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (RewardSwordDisplay display in sword.GetComponentsInChildren<RewardSwordDisplay>(true))
        {
            display.hologram = true;
            display.ApplyAppearance();
        }
        foreach (Collider collider in sword.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("The reward sword prefab has no renderer.");

        var instances = new Dictionary<Material, Material>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material source = materials[slot];
                if (!source) continue;
                if (!instances.TryGetValue(source, out Material instance))
                {
                    instance = new Material(source) { name = source.name + " (Reward Preview)", hideFlags = HideFlags.DontSave };
                    // A transparent render texture is composited a second time by the UI.
                    if (instance.HasProperty("_Opacity"))
                        instance.SetFloat("_Opacity", Mathf.Max(.72f, instance.GetFloat("_Opacity")));
                    instances.Add(source, instance);
                    previewMaterials.Add(instance);
                }
                materials[slot] = instance;
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        sword.transform.localRotation = Quaternion.Euler(0, 0, -12) * sword.transform.localRotation;
        Bounds bounds = ChestDissolveController.CalculateLocalBounds(rotor, renderers);
        sword.transform.localPosition -= bounds.center;
        float radius = Mathf.Max(bounds.extents.y, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
        float distance = Mathf.Max(3, bounds.extents.magnitude * 3);
        foreach (Transform child in previewRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = PreviewLayer;

        var cameraObject = new GameObject("Reward Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(previewRoot.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -distance);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = Mathf.Max(.5f, radius * 1.2f + .1f);
        previewCamera.nearClipPlane = .05f;
        previewCamera.farClipPlane = distance * 2 + 2;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        previewCamera.useOcclusionCulling = false;
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.requiresColorTexture = false;
        data.requiresDepthTexture = false;
        data.volumeLayerMask = 0;
        previewTexture = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32)
        {
            name = "Baseline Reward Preview Texture",
            hideFlags = HideFlags.DontSave,
            antiAliasing = 1
        };
        previewTexture.Create();
        previewCamera.targetTexture = previewTexture;
        previewRoot.SetActive(true);
    }

    private void CreateModal(string title, string description, bool sword)
    {
        modal = new GameObject("Baseline Reward Modal", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        modal.hideFlags = HideFlags.DontSave;
        SceneManager.MoveGameObjectToScene(modal, gameObject.scene);
        Canvas canvas = modal.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        CanvasScaler scaler = modal.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        modalCanvasGroup = modal.AddComponent<CanvasGroup>();
        modalCanvasGroup.alpha = 0f;

        if ((sword || SmallFloatingCard) && ((acquiredCardBackground && acquiredSwordIcon) || acquiredCard))
        {
            CreateAcquiredCard(acquiredCardBackground && acquiredSwordIcon);
            return;
        }

        RectTransform shade = Rect("Background", modal.transform, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        Image background = shade.gameObject.AddComponent<Image>();
        background.color = new Color(.012f, .023f, .04f, .94f);
        background.raycastTarget = false;
        protectedArea = Rect("Reward Content", modal.transform, new Vector2(1040, sword ? 890 : 430), Vector2.zero);
        AddText("REWARD ACQUIRED", protectedArea, new Vector2(0, sword ? 410 : 150), new Vector2(960, 55), 25, new Color(.42f, .85f, 1));
        if (sword)
        {
            RectTransform imageRect = Rect("Hologram Sword", protectedArea, new Vector2(610, 610), new Vector2(0, 40));
            RawImage image = imageRect.gameObject.AddComponent<RawImage>();
            image.texture = previewTexture;
            image.raycastTarget = false;
        }
        AddText(title ?? "Reward", protectedArea, new Vector2(0, sword ? -300 : 45), new Vector2(1000, 84), 43, Color.white);
        AddText(description ?? string.Empty, protectedArea, new Vector2(0, sword ? -380 : -60), new Vector2(980, 100), 25, new Color(.78f, .86f, .93f));
        AddText("Click an empty area or press Enter / Esc to continue", modal.transform, new Vector2(0, -490), new Vector2(1200, 48), 23, new Color(.65f, .75f, .83f));
    }

    private void CreateAcquiredCard(bool layered)
    {
        RectTransform shade = Rect("World Dim", modal.transform, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        Image background = shade.gameObject.AddComponent<Image>();
        background.color = new Color(.025f, .055f, .045f, .54f);
        background.raycastTarget = false;

        protectedArea = Rect("Practice Sword Acquired Card", modal.transform,
            new Vector2(1550, 873), Vector2.zero);
        cardContent = protectedArea;
        cardStartPosition = new Vector2(0, -56);
        cardContent.anchoredPosition = cardStartPosition;
        cardContent.localScale = Vector3.one * .92f;
        RawImage card = protectedArea.gameObject.AddComponent<RawImage>();
        card.texture = layered ? acquiredCardBackground : acquiredCard;
        card.color = Color.white;
        card.raycastTarget = false;

        if (layered)
        {
            // The sword remains a separate layer so its idle hover can animate independently.
            floatingSword = Rect(islandRelicCard ? "Floating Island Relic" : ridgeCharmCard ? "Floating Ridge Charm" : runicGuardCard ? "Floating Runic Guard" : "Floating Practice Sword", protectedArea,
                new Vector2(278, 278), new Vector2(-270, -13));
            swordRestPosition = floatingSword.anchoredPosition;
            RawImage swordImage = floatingSword.gameObject.AddComponent<RawImage>();
            swordImage.texture = acquiredSwordIcon;
            swordImage.color = Color.white;
            swordImage.raycastTarget = false;
        }

        // Continue button bounds mapped from the baked 1671x941 artwork onto 1550x873.
        continueButtonArea = Rect("Continue Button Hit Area", protectedArea,
            new Vector2(332, 70), new Vector2(168, -153));
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.45f;
        float shifted = t - 1f;
        return 1f + (overshoot + 1f) * shifted * shifted * shifted + overshoot * shifted * shifted;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private void AddText(string value, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        Text text = Rect(value, parent, size, position).gameObject.AddComponent<Text>();
        text.font = dialogue.PresentationFont ? dialogue.PresentationFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
    }

    private void DestroyPresentation()
    {
        if (previewCamera) { previewCamera.enabled = false; previewCamera.targetTexture = null; }
        if (previewRoot) { previewRoot.SetActive(false); Destroy(previewRoot); }
        if (modal) { modal.SetActive(false); Destroy(modal); }
        if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
        foreach (Material material in previewMaterials) if (material) Destroy(material);
        previewMaterials.Clear();
        previewCamera = null;
        previewRoot = null;
        rotor = null;
        modal = null;
        modalCanvasGroup = null;
        cardContent = null;
        floatingSword = null;
        acquiredCard = null;
        acquiredCardBackground = null;
        acquiredSwordIcon = null;
        islandRelicCard = false;
        ridgeCharmCard = false;
        runicGuardCard = false;
        protectedArea = null;
        continueButtonArea = null;
        previewTexture = null;
    }

    private void Finish(bool notify)
    {
        Action callback = notify ? onClosed : null;
        onClosed = null;
        DestroyPresentation();
        IsOpen = closing = blankPress = continuePress = false;
        if (ownsInput)
        {
            ownsInput = false;
            if (dialogue) dialogue.HideRewardPresentation();
            if (player) player.SetGameplayInputLocked(previousMovementLock);
            if (look) look.SetLookInputLocked(previousLookLock);
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
        if (callback != null)
        {
            try { callback(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    // Finish already clears the callback before invoking it, so disable followed by
    // destroy cannot replay it. Collected chests must also finalize when a modal is cancelled.
    private void OnDisable() => Finish(true);
    private void OnDestroy() => Finish(true);
}
