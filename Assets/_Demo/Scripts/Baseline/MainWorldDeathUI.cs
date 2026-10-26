using UnityEngine;
using UnityEngine.InputSystem;

// Live overlay for overworld and final-room deaths. The supplied GIF is a
// layout/motion reference, not a video backdrop: the current world stays live.
[DisallowMultipleComponent]
public sealed class MainWorldDeathUI : MonoBehaviour
{
    const float Width = 960f;
    const float Height = 540f;
    static readonly Rect RetryRect = new Rect(257f, 279f, 220f, 46f);
    static readonly Rect SafeRect = new Rect(483f, 279f, 220f, 46f);
    static readonly Rect AtlasDefault = new Rect(16f / 2048f, 950f / 1024f, 280f / 2048f, 58f / 1024f);
    static readonly Rect AtlasFocus = new Rect(320f / 2048f, 950f / 1024f, 280f / 2048f, 58f / 1024f);

    BaselineFlow flow;
    PlayerMovement movement;
    ThirdPersonCamera cameraLook;
    Texture2D atlas;
    Texture2D shadePixel;
    GUIStyle titleStyle, selectedStyle, normalStyle;
    float openedAt, previousTimeScale;
    bool previousMovementLock, previousLookLock, previousCursorVisible;
    CursorLockMode previousCursorLock;
    int selected;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        atlas = Resources.Load<Texture2D>("DeathUIAtlas");
        shadePixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        shadePixel.SetPixel(0, 0, Color.white);
        shadePixel.Apply();
    }

    public void Show(BaselineFlow owner, PlayerMovement playerMovement)
    {
        if (IsOpen || !owner || !playerMovement) return;
        flow = owner;
        movement = playerMovement;
        cameraLook = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        previousMovementLock = movement.IsGameplayInputLocked;
        previousLookLock = cameraLook && cameraLook.IsLookInputLocked;
        previousTimeScale = Time.timeScale;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        movement.SetGameplayInputLocked(true);
        if (cameraLook) cameraLook.SetLookInputLocked(true);
        var combat = movement.GetComponent<PlayerCombat>();
        if (combat) combat.CancelForTransition();
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        selected = 0;
        openedAt = Time.unscaledTime;
        IsOpen = true;
    }

    void Update()
    {
        if (!IsOpen || Time.unscaledTime - openedAt < .48f) return;
        Keyboard keys = Keyboard.current;
        if (keys == null) return;
        if (keys.leftArrowKey.wasPressedThisFrame || keys.aKey.wasPressedThisFrame) selected = 0;
        if (keys.rightArrowKey.wasPressedThisFrame || keys.dKey.wasPressedThisFrame) selected = 1;
        if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame)
            Choose(selected);
    }

    void Choose(int choice)
    {
        if (!IsOpen || !flow) return;
        BaselineFlow owner = flow;
        Close();
        bool recovered = choice == 0 ? owner.RetryAfterDeath() : owner.RespawnPlayerAtCamp();
        if (!recovered) Show(owner, movement);
    }

    public void Retry() => Choose(0);
    public void RestoreAtSafePoint() => Choose(1);

    // Explicit loading and a new journey end the prior life's death overlay.
    public void DismissForWorldRestore() => Close();

    void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        if (movement) movement.SetGameplayInputLocked(previousMovementLock);
        if (cameraLook) cameraLook.SetLookInputLocked(previousLookLock);
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    void BuildStyles()
    {
        Font serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman" }, 32);
        if (!serif) serif = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleStyle = new GUIStyle(GUI.skin.label) {
            font = serif, fontSize = 34, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter, wordWrap = false
        };
        titleStyle.normal.textColor = new Color32(249, 240, 216, 255);
        WayfarerUITheme.KeepLabelColor(titleStyle);
        normalStyle = new GUIStyle(GUI.skin.label) {
            font = serif, fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false
        };
        normalStyle.normal.textColor = new Color32(249, 240, 216, 255);
        WayfarerUITheme.KeepLabelColor(normalStyle);
        selectedStyle = new GUIStyle(normalStyle);
        selectedStyle.fontStyle = FontStyle.Bold;
        selectedStyle.normal.textColor = new Color32(26, 58, 52, 255);
        WayfarerUITheme.KeepLabelColor(selectedStyle);
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        WayfarerUITheme.Apply();
        if (titleStyle == null) BuildStyles();
        float elapsed = Time.unscaledTime - openedAt;
        float shade = Mathf.SmoothStep(0f, .68f, Mathf.Clamp01(elapsed / .42f));
        int previousDepth = GUI.depth;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.depth = -1000;
        GUI.color = new Color(12f / 255f, 34f / 255f, 36f / 255f, shade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), shadePixel);
        GUI.color = Color.white;
        float scale = Mathf.Min(Screen.width / Width, Screen.height / Height);
        Vector2 offset = new Vector2((Screen.width - Width * scale) * .5f,
            (Screen.height - Height * scale) * .5f);
        Vector2 pointer = (Event.current.mousePosition - offset) / scale;
        if (RetryRect.Contains(pointer)) selected = 0;
        else if (SafeRect.Contains(pointer)) selected = 1;
        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        float titleAlpha = Mathf.Clamp01((elapsed - .12f) / .27f);
        GUI.color = new Color(1, 1, 1, titleAlpha);
        GUI.Label(new Rect(236, 185, 488, 58), "YOU HAVE FALLEN", titleStyle);
        float buttonsAlpha = Mathf.Clamp01((elapsed - .38f) / .22f);
        GUI.color = new Color(1, 1, 1, buttonsAlpha);
        DrawChoice(RetryRect, "Retry", 0);
        DrawChoice(SafeRect, "Restore at Safe Point", 1);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
        GUI.depth = previousDepth;
    }

    void DrawChoice(Rect rect, string label, int choice)
    {
        bool focused = selected == choice;
        if (atlas) GUI.DrawTextureWithTexCoords(rect, atlas, focused ? AtlasFocus : AtlasDefault, true);
        else GUI.Box(rect, GUIContent.none);
        GUI.Label(rect, label, focused ? selectedStyle : normalStyle);
        if (Time.unscaledTime - openedAt >= .48f && GUI.Button(rect, GUIContent.none, GUIStyle.none))
            Choose(choice);
    }

    void OnDisable() => Close();
    void OnDestroy()
    {
        Close();
        if (shadePixel) Destroy(shadePixel);
    }
}
