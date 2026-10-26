using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Guardian's brief entrance exchange uses the supplied animated panel.
// It is separate from nearby NPC interactions because it starts on room entry.
[DisallowMultipleComponent]
public sealed class GuardianIntroDialogue : MonoBehaviour
{
    private static readonly string[] Lines =
    {
        "So... you made it to the tower.",
        "Home is close. Two worlds are on the line.",
        "Come, then. Fight for your way back."
    };

    private readonly Texture2D[] frames = new Texture2D[33];
    private GameObject canvasObject;
    private RawImage panel;
    private Material smoothPanelMaterial;
    private Text dialogueText;
    private Text continuePrompt;
    private PlayerMovement movement;
    private ThirdPersonCamera look;
    private Coroutine animation;
    private bool previousMovementLock, previousLookLock, opening, closing;
    private int lineIndex, openedFrame;

    public bool IsShowing { get; private set; }
    public string FinalPrompt { get; set; } = "[E] BEGIN FIGHT";

    public bool Show(Transform player)
    {
        if (IsShowing || player == null || !LoadFrames()) return false;
        if (canvasObject == null) BuildCanvas();
        movement = player.GetComponent<PlayerMovement>();
        look = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        var combat = player.GetComponent<PlayerCombat>();
        if (combat) combat.CancelForTransition();
        previousMovementLock = movement && movement.IsGameplayInputLocked;
        previousLookLock = look && look.IsLookInputLocked;
        if (movement) movement.SetGameplayInputLocked(true);
        if (look) look.SetLookInputLocked(true);

        lineIndex = 0;
        openedFrame = Time.frameCount;
        IsShowing = opening = true;
        closing = false;
        SetLine(0);
        dialogueText.gameObject.SetActive(false);
        continuePrompt.gameObject.SetActive(false);
        canvasObject.SetActive(true);
        animation = StartCoroutine(PlayOpening());
        return true;
    }

    private bool LoadFrames()
    {
        for (int i = 0; i < frames.Length; i++)
        {
            if (!frames[i]) frames[i] = Resources.Load<Texture2D>("GuardianDialogue/frame_" + i.ToString("00"));
            if (frames[i]) continue;
            Debug.LogWarning("Guardian dialogue frame missing: " + i, this);
            return false;
        }
        return true;
    }

    private void BuildCanvas()
    {
        canvasObject = new GameObject("Guardian dialogue UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;

        var imageObject = new GameObject("Animated Guardian panel", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = imageObject.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, 0f);
        panelRect.pivot = new Vector2(.5f, 0f);
        // Sit close to the bottom edge while keeping the panel fully visible.
        panelRect.sizeDelta = new Vector2(1040f, 310f);
        panelRect.anchoredPosition = new Vector2(0f, 12f);
        panel = imageObject.GetComponent<RawImage>();
        panel.raycastTarget = false;
        Shader smoothShader = Resources.Load<Shader>("GuardianDialogueSmooth");
        if (smoothShader)
        {
            smoothPanelMaterial = new Material(smoothShader);
            panel.material = smoothPanelMaterial;
        }

        var textObject = new GameObject("Guardian line", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(panelRect, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(.27f, .34f);
        textRect.anchorMax = new Vector2(.89f, .64f);
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        dialogueText = textObject.GetComponent<Text>();
        DialogueUI existingDialogue = FindFirstObjectByType<DialogueUI>();
        dialogueText.font = existingDialogue && existingDialogue.PresentationFont
            ? existingDialogue.PresentationFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        dialogueText.fontSize = 24;
        dialogueText.color = new Color32(248, 239, 220, 255);
        dialogueText.alignment = TextAnchor.MiddleLeft;
        dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
        dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
        dialogueText.resizeTextForBestFit = false;
        dialogueText.raycastTarget = false;

        var promptObject = new GameObject("Continue hint", typeof(RectTransform), typeof(Text));
        promptObject.transform.SetParent(panelRect, false);
        RectTransform promptRect = promptObject.GetComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(.67f, .32f);
        promptRect.anchorMax = new Vector2(.89f, .40f);
        promptRect.offsetMin = promptRect.offsetMax = Vector2.zero;
        continuePrompt = promptObject.GetComponent<Text>();
        continuePrompt.font = dialogueText.font;
        continuePrompt.fontSize = 16;
        continuePrompt.fontStyle = FontStyle.Bold;
        continuePrompt.color = new Color32(248, 239, 220, 255);
        continuePrompt.alignment = TextAnchor.MiddleRight;
        continuePrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        continuePrompt.raycastTarget = false;
    }

    private void SetLine(int index)
    {
        dialogueText.text = Lines[index];
        continuePrompt.text = index == Lines.Length - 1 ? FinalPrompt : "[E] CONTINUE";
    }

    public bool TryInterfaceBack()
    {
        if(!IsShowing)return false;
        if(!closing&&Time.frameCount!=openedFrame)BeginClosing();
        return true;
    }
    private void Update()
    {
        if (!IsShowing || closing || Time.frameCount == openedFrame) return;
        Keyboard keys = Keyboard.current;
        if (keys == null) return;
        if (keys.escapeKey.wasPressedThisFrame) { BeginClosing(); return; }
        if (!WorldInputBindings.Pressed("Interact")) return;
        if (opening)
        {
            if (animation != null) StopCoroutine(animation);
            animation = null;
            ShowFrame(22);
            dialogueText.gameObject.SetActive(true);
            continuePrompt.gameObject.SetActive(true);
            opening = false;
            return;
        }
        lineIndex++;
        if (lineIndex < Lines.Length) SetLine(lineIndex);
        else BeginClosing();
    }

    private IEnumerator PlayOpening()
    {
        for (int i = 0; i <= 22; i++)
        {
            ShowFrame(i);
            yield return new WaitForSecondsRealtime(i == 0 ? .1f : .05f);
        }
        ShowFrame(22);
        dialogueText.gameObject.SetActive(true);
        continuePrompt.gameObject.SetActive(true);
        opening = false;
        animation = null;
    }

    private void BeginClosing()
    {
        if (animation != null) StopCoroutine(animation);
        animation = null;
        opening = false;
        closing = true;
        dialogueText.gameObject.SetActive(false);
        continuePrompt.gameObject.SetActive(false);
        animation = StartCoroutine(PlayClosing());
    }

    private IEnumerator PlayClosing()
    {
        for (int i = 23; i < frames.Length; i++)
        {
            ShowFrame(i);
            yield return new WaitForSecondsRealtime(i == frames.Length - 1 ? .4f : .05f);
        }
        animation = null;
        DismissImmediately();
    }

    private void ShowFrame(int index) => panel.texture = frames[index];

    public void DismissImmediately()
    {
        if (animation != null) StopCoroutine(animation);
        animation = null;
        if (!IsShowing) return;
        IsShowing = opening = closing = false;
        if (canvasObject) canvasObject.SetActive(false);
        var flow=FindFirstObjectByType<BaselineFlow>();
        // Being in the arena is not a modal; preserve only a real overlay takeover.
        bool modal=flow && (flow.MenuOpen || flow.MapOpen || flow.DeathOpen || flow.VictoryOpen || flow.TitleScreenVisible ||
            (flow.Tutorial && flow.Tutorial.ModalOpen) || (flow.rewards && flow.rewards.IsOpen) || flow.GetComponent<BaselineWorld>().JournalOpen);
        if (movement) movement.SetGameplayInputLocked(previousMovementLock || modal);
        if (look) look.SetLookInputLocked(previousLookLock || modal);
        movement = null;
        look = null;
    }

    private void OnDisable() => DismissImmediately();

    private void OnDestroy()
    {
        DismissImmediately();
        if (smoothPanelMaterial) Destroy(smoothPanelMaterial);
        if (canvasObject) Destroy(canvasObject);
    }
}
