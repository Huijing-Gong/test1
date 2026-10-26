using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Presentation only: DialogueUI/NPCDialogue remain the owners of rewards and input locks.
public sealed class RioDialoguePresentation : MonoBehaviour
{
    [Serializable] public sealed class Frame { public string file; public int durationMs; public string phase; }
    [Serializable] public sealed class Sequence { public int width; public int height; public Frame[] frames; }
    const string Root = "RioDialogue/";
    const string SideRoot = "SideWorldDialogue/";
    const string FirstLine = "This lake is yours to explore. Take the practice sword; there is no required route.";
    const string SecondLine = "A survey cylinder was lost in the western grove. I will record the clue. Press J if you want directions.";
    const string IntroGuideName = "The Guide";
    readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    Sequence promptSequence, dialogueSequence;
    Sequence magicianSequence, rangerSequence, activeSequence;
    RawImage prompt, dialogue;
    Text bodyText, hintText;
    GameObject liveName;
    Camera worldCamera;
    GameObject canvasObject;
    NPCDialogue target, magicianTarget, rangerTarget, activeTarget, promptTarget;
    string activeRoot;
    bool activeSideDialogue;
    Text speakerText;
    CanvasGroup dialogueOpacity;
    static readonly Vector2 PanelPosition = new Vector2(0, 44);
    Coroutine promptRoutine, dialogueRoutine;
    bool promptWanted, transitioning;
    int page;
    public bool IsPresenting { get; private set; }
    public bool PromptVisible => prompt != null && prompt.gameObject.activeSelf;
    public bool IsTransitioning => transitioning;

    public static RioDialoguePresentation Attach(DialogueUI owner)
    {
        string scenePath = owner.gameObject.scene.path;
        bool supportedScene = scenePath == "Assets/_Demo/Scenes/OverworldBaseline.unity" ||
            scenePath == "Assets/_Demo/Scenes/GameplayFeatureDemoScene.unity" || scenePath == "Assets/StartScene.unity";
        if (!supportedScene)
            foreach (var root in owner.gameObject.scene.GetRootGameObjects())
                if (root.GetComponentInChildren<BaselineFlow>(true)) { supportedScene=true; break; }
        if (!supportedScene) return null;
        Transform intro = null;
        foreach (GameObject root in owner.gameObject.scene.GetRootGameObjects())
            if (root.name == "_Gameplay") intro = root.transform.Find("IntroNPC");
        NPCDialogue npc = intro != null ? intro.GetComponent<NPCDialogue>() : null;
        bool validIntro = npc != null && npc.LineCount == 2 && npc.GetLine(0) == FirstLine && npc.GetLine(1) == SecondLine;
        if (!validIntro) npc = null;
        else
        {
            npc.SetNPCName(IntroGuideName);
            npc.SetInteractionPrompt("[E] " + IntroGuideName);
        }
        TextAsset p = Resources.Load<TextAsset>(Root + "Prompt/timing");
        TextAsset d = validIntro ? Resources.Load<TextAsset>(Root + "Dialogue/timing") : null;
        if (p == null || (validIntro && d == null)) { Debug.LogWarning("Dialogue presentation assets missing; using original dialogue UI.", owner); return null; }
        Sequence ps = JsonUtility.FromJson<Sequence>(p.text), ds = d != null ? JsonUtility.FromJson<Sequence>(d.text) : null;
        if (ps == null || ps.frames == null || ps.frames.Length != 46 ||
            (validIntro && (ds == null || ds.frames == null || ds.frames.Length != 29))) return null;
        var result = owner.gameObject.AddComponent<RioDialoguePresentation>();
        result.target = npc; result.promptSequence = ps; result.dialogueSequence = ds;
        // Preflight/cache before proximity can trigger playback. Invalid imports
        // must leave the original UI available instead of hiding it or pausing play.
        if (!result.LoadFrames(ps, "Prompt") || (validIntro && !result.LoadFrames(ds, "Dialogue")))
        {
            Destroy(result);
            return null;
        }
        GameObject magicianObject = GameObject.Find("SideWorldA_Guide");
        GameObject rangerObject = GameObject.Find("SideWorldB_Guide");
        result.magicianTarget = magicianObject ? magicianObject.GetComponent<NPCDialogue>() : null;
        result.rangerTarget = rangerObject ? rangerObject.GetComponent<NPCDialogue>() : null;
        result.magicianSequence = result.LoadSideSequence("Magician");
        result.rangerSequence = result.LoadSideSequence("Rio");
        if (result.magicianSequence == null) result.magicianTarget = null;
        if (result.rangerSequence == null) result.rangerTarget = null;
        if (result.target == null && result.magicianTarget == null && result.rangerTarget == null)
        {
            Destroy(result);
            return null;
        }
        result.BuildCanvas(owner.PresentationFont);
        return result;
    }

    public bool SupportsJournal(NPCDialogue npc) => npc != null && npc == target && npc.LineCount == 2 &&
        npc.GetLine(0) == FirstLine && npc.GetLine(1) == SecondLine;

    public bool Supports(NPCDialogue npc) => npc != null && npc.LineCount > 0 &&
        (SupportsJournal(npc) ||
         npc == magicianTarget || npc == rangerTarget);

    Sequence LoadSideSequence(string character)
    {
        string folder = SideRoot + character;
        TextAsset timing = Resources.Load<TextAsset>(folder + "/timing");
        if (timing == null) return null;
        Sequence sequence = JsonUtility.FromJson<Sequence>(timing.text);
        if (sequence == null || sequence.frames == null || sequence.frames.Length == 0 || !LoadFrames(sequence, folder)) return null;
        return sequence;
    }

    bool LoadFrames(Sequence sequence, string folder)
    {
        foreach (Frame frame in sequence.frames)
        {
            string key = (folder.StartsWith(SideRoot, StringComparison.Ordinal) ? folder : Root + folder) + "/" + System.IO.Path.ChangeExtension(frame.file, null);
            if (textures.ContainsKey(key)) continue;
            Texture2D texture = Resources.Load<Texture2D>(key);
            if (texture == null)
            {
                Debug.LogWarning("Rio animation texture unavailable: " + key + "; using original dialogue UI.", this);
                return false;
            }
            textures.Add(key, texture);
        }
        return true;
    }

    void BuildCanvas(Font font)
    {
        canvasObject = new GameObject("Rio Animated UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        // A root Canvas owns its reference resolution; nesting under DialogueCanvas
        // would make CanvasScaler inherit the old UI scale instead.
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
        prompt = CreateImage("Rio Interact", new Vector2(120, 50.17f), Vector2.zero);
        prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(.5f, .5f);
        prompt.rectTransform.pivot = new Vector2(.5f, .5f);
        dialogue = CreateImage("Rio Dialogue", new Vector2(840, 216), new Vector2(0, 50));
        dialogueOpacity = dialogue.gameObject.AddComponent<CanvasGroup>();
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bodyText = CreateText("Live dialogue", font, new Vector2(.278f, .34f), new Vector2(.915f, .66f), 18);
        hintText = CreateText("Live controls", font, new Vector2(.278f, .20f), new Vector2(.88f, .32f), 13);
        hintText.alignment = TextAnchor.MiddleRight;
        // Cover only the old raster name on its flat-colour tab.
        liveName = new GameObject("Crisp name backing", typeof(RectTransform), typeof(Image));
        liveName.transform.SetParent(dialogue.transform, false);
        var nameRect = liveName.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(.274f, .717f); nameRect.anchorMax = new Vector2(.419f, .829f);
        nameRect.offsetMin = nameRect.offsetMax = Vector2.zero;
        var backing = liveName.GetComponent<Image>(); backing.color = new Color32(20, 64, 54, 255); backing.raycastTarget = false;
        speakerText = CreateText("Live speaker", font, Vector2.zero, Vector2.one, 14);
        speakerText.transform.SetParent(liveName.transform, false);
        speakerText.rectTransform.offsetMin = speakerText.rectTransform.offsetMax = Vector2.zero;
        speakerText.text = target != null ? target.NPCName : string.Empty; speakerText.fontStyle = FontStyle.Bold;
        speakerText.color = new Color32(244, 234, 202, 255); speakerText.alignment = TextAnchor.MiddleLeft;
        liveName.SetActive(false);
        worldCamera = Camera.main;
    }

    Text CreateText(string name, Font font, Vector2 min, Vector2 max, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(dialogue.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>(); text.font = font; text.fontSize = size;
        text.color = new Color32(22, 60, 51, 255); text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    void LateUpdate()
    {
        NPCDialogue markerTarget = IsPresenting ? activeTarget : promptTarget;
        if (prompt == null || markerTarget == null) return;
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;
        Vector3 screen = worldCamera.WorldToScreenPoint(markerTarget.transform.position + Vector3.up * 1.35f);
        bool onScreen = screen.z > 0 && screen.x > 0 && screen.x < Screen.width && screen.y > 0 && screen.y < Screen.height;
        prompt.enabled = onScreen;
        if (!onScreen) return;
        var canvasRect = (RectTransform)canvasObject.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        local += new Vector2(75, 0);
        local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + 70, canvasRect.rect.xMax - 70);
        local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + 30, canvasRect.rect.yMax - 30);
        prompt.rectTransform.anchoredPosition = local;
    }

    RawImage CreateImage(string name, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(canvasObject.transform, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
        rect.pivot = new Vector2(.5f, 0); rect.sizeDelta = size; rect.anchoredPosition = position;
        var image = go.GetComponent<RawImage>(); image.raycastTarget = false; go.SetActive(false); return image;
    }

    void Display(RawImage image, Sequence sequence, string resourceFolder, int index, bool introDialogue = false)
    {
        if (introDialogue)
        {
            liveName.SetActive(index >= 7 && index <= 23);
            if (activeTarget != null) speakerText.text = activeTarget.NPCName;
            // Reuse the text-free hold frame; render dialogue at the actual screen
            // resolution instead of scaling letters baked into the PNG sequence.
            float opacity = 0;
            int textPage = page;
            if (index >= 8 && index <= 12) { textPage = 0; opacity = Mathf.Clamp01((index - 7) / 4f); }
            else if (index >= 13 && index <= 15) { textPage = 0; opacity = (15 - index) / 3f; }
            else if (index >= 16 && index <= 20) { textPage = 1; opacity = Mathf.Clamp01((index - 15) / 4f); }
            else if (index >= 21 && index <= 23) { textPage = 1; opacity = (23 - index) / 3f; }
            bodyText.text = target.GetLine(textPage);
            hintText.text = textPage == 1 ? "[E] Continue   [J] Tasks   [Esc] Close" : "[E] Continue   [Esc] Close";
            bodyText.canvasRenderer.SetAlpha(opacity); hintText.canvasRenderer.SetAlpha(opacity);
            if (index >= 8 && index <= 23) index = 7;
        }
        else if (activeSideDialogue && image == dialogue)
        {
            // The GIF's other frames contain raster character names. Animate the
            // composed panel instead, so no page/transition can expose those names.
            index = 8;
            liveName.SetActive(true);
            speakerText.text = activeTarget.NPCName;
            bodyText.text = activeTarget != null ? activeTarget.GetLine(page) : string.Empty;
            hintText.text = "[E] Continue   [Esc] Close";
            bodyText.canvasRenderer.SetAlpha(1); hintText.canvasRenderer.SetAlpha(1);
        }
        string file = sequence.frames[index].file;
        string key = resourceFolder + "/" + file.Substring(0, file.LastIndexOf('.'));
        if (!textures.TryGetValue(key, out Texture2D texture))
        {
            texture = Resources.Load<Texture2D>(key); textures[key] = texture;
            if (texture == null) Debug.LogWarning("Rio animation frame unavailable: " + key, this);
        }
        image.texture = texture; image.gameObject.SetActive(texture != null);
    }

    IEnumerator Play(RawImage image, Sequence sequence, string resourceFolder, int start, int end, bool introDialogue = false)
    {
        for (int i = start; i <= end; i++)
        {
            Display(image, sequence, resourceFolder, i, introDialogue);
            yield return new WaitForSecondsRealtime(Mathf.Max(.01f, sequence.frames[i].durationMs / 1000f));
        }
    }

    public void SetPrompt(bool visible, NPCDialogue npc = null)
    {
        visible &= !IsPresenting;
        promptTarget = visible ? npc : null;
        if (visible == promptWanted) return;
        promptWanted = visible;
        if (promptRoutine != null) StopCoroutine(promptRoutine);
        promptRoutine = StartCoroutine(PromptAnimation(visible));
    }

    IEnumerator PromptAnimation(bool visible)
    {
        if (visible)
        {
            yield return Play(prompt, promptSequence, Root + "Prompt", 0, 5);
            while (promptWanted) yield return Play(prompt, promptSequence, Root + "Prompt", 6, 25);
        }
        else { yield return Play(prompt, promptSequence, Root + "Prompt", 37, 45); prompt.gameObject.SetActive(false); }
        promptRoutine = null;
    }

    public void Open(NPCDialogue npc)
    {
        HideImmediately();
        activeTarget = npc;
        activeSideDialogue = npc == magicianTarget || npc == rangerTarget;
        activeSequence = npc == magicianTarget ? magicianSequence : npc == rangerTarget ? rangerSequence : dialogueSequence;
        activeRoot = activeSideDialogue ? SideRoot + (npc == magicianTarget ? "Magician" : "Rio") : Root + "Dialogue";
        if (activeSideDialogue) ConfigureSideDialogueLayout();
        else ConfigureIntroDialogueLayout();
        if (speakerText != null) speakerText.text = npc != null ? npc.NPCName : string.Empty;
        IsPresenting = true; page = 0; transitioning = true;
        dialogueRoutine = StartCoroutine(OpenAnimation());
    }

    void ConfigureSideDialogueLayout()
    {
        // Crop transparent canvas with UVs, not a second image export. This puts
        // both supplied character panels into the same layout as the main guide.
        dialogue.uvRect = new Rect(64f / 1280, 23f / 720, 1136f / 1280, 292f / 720);
        ConfigureReadableLayout();
    }

    void ConfigureIntroDialogueLayout()
    {
        dialogue.uvRect = new Rect(0, 0, 1, 1);
        ConfigureReadableLayout();
    }

    void ConfigureReadableLayout()
    {
        dialogue.rectTransform.sizeDelta = new Vector2(1040, 310);
        dialogue.rectTransform.anchoredPosition = PanelPosition;
        dialogueOpacity.alpha = 1;
        SetTextArea(bodyText, new Vector2(.29f, .40f), new Vector2(.89f, .69f), 24);
        bodyText.alignment = TextAnchor.MiddleLeft;
        bodyText.lineSpacing = 1.12f;
        SetTextArea(hintText, new Vector2(.29f, .285f), new Vector2(.86f, .37f), 17);
        hintText.alignment = TextAnchor.MiddleRight;
        RectTransform name = liveName.GetComponent<RectTransform>();
        name.anchorMin = new Vector2(.275f,.70f); name.anchorMax = new Vector2(.515f,.855f);
        name.offsetMin = name.offsetMax = Vector2.zero;
        speakerText.fontSize = 20;
        speakerText.rectTransform.offsetMin = new Vector2(8, 2);
        speakerText.rectTransform.offsetMax = new Vector2(-8, -2);
    }

    static void SetTextArea(Text text, Vector2 min, Vector2 max, int fontSize)
    {
        text.rectTransform.anchorMin = min; text.rectTransform.anchorMax = max;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.fontSize = fontSize;
        text.resizeTextForBestFit = false;
    }

    IEnumerator OpenAnimation()
    {
        if (activeSideDialogue)
        {
            Display(dialogue, activeSequence, activeRoot, 8);
            yield return AnimateSidePanel(true);
            transitioning = false;
            yield return LoopSideDialogue();
            dialogueRoutine = null;
            yield break;
        }
        // E feedback runs only on an actual key press, never as part of idle.
        yield return Play(prompt, promptSequence, Root + "Prompt", 26, 28);
        prompt.gameObject.SetActive(false);
        yield return Play(dialogue, dialogueSequence, Root + "Dialogue", 0, 11, true);
        ShowHold();
    }
    public void SetPage(int value)
    {
        if (!IsPresenting || value == page) return;
        page = value;
        if (activeSideDialogue)
        {
            Display(dialogue, activeSequence, activeRoot, 8);
            return;
        }
        if (dialogueRoutine != null) StopCoroutine(dialogueRoutine);
        transitioning = true; dialogueRoutine = StartCoroutine(ChangePage());
    }
    IEnumerator ChangePage()
    {
        yield return Play(dialogue, dialogueSequence, Root + "Dialogue", 13, 19, true);
        ShowHold();
    }
    void ShowHold()
    {
        if (activeSideDialogue)
        {
            Display(dialogue, activeSequence, activeRoot, Mathf.Min(8, activeSequence.frames.Length - 1));
            if (activeTarget != null) bodyText.text = activeTarget.GetLine(page);
        }
        else Display(dialogue, dialogueSequence, Root + "Dialogue", page == 0 ? 12 : 20, true);
        transitioning = false; dialogueRoutine = null;
    }
    public bool RevealIfBusy()
    {
        if (!transitioning) return false;
        if (dialogueRoutine != null) StopCoroutine(dialogueRoutine);
        prompt.gameObject.SetActive(false); ShowHold();
        dialogueOpacity.alpha = 1;
        dialogue.rectTransform.anchoredPosition = PanelPosition;
        if (activeSideDialogue) dialogueRoutine = StartCoroutine(LoopSideDialogue());
        return true;
    }
    IEnumerator LoopSideDialogue()
    {
        // Keep the artwork and live text together; replaying GIF entry/exit frames
        // under stationary text makes the nameplate drift away from its label.
        float start = Time.unscaledTime;
        while (IsPresenting && activeSideDialogue)
        {
            dialogue.rectTransform.anchoredPosition = PanelPosition + new Vector2(0, Mathf.Sin((Time.unscaledTime - start) * 1.8f) * 2f);
            yield return null;
        }
        dialogueRoutine=null;
    }
    public void CloseAnimated(Action completed)
    {
        if (dialogueRoutine != null) StopCoroutine(dialogueRoutine);
        prompt.gameObject.SetActive(false); transitioning = true;
        if (activeSideDialogue) { dialogueRoutine = StartCoroutine(CloseSideDialogue(completed)); return; }
        dialogueRoutine = StartCoroutine(CloseAnimation(completed));
    }
    IEnumerator CloseSideDialogue(Action completed)
    {
        yield return AnimateSidePanel(false);
        dialogueRoutine=null;transitioning=false;IsPresenting=false;
        dialogue.gameObject.SetActive(false);liveName.SetActive(false);completed?.Invoke();
    }
    IEnumerator AnimateSidePanel(bool opening)
    {
        float start = Time.unscaledTime;
        float duration = opening ? .26f : .18f;
        float t;
        do
        {
            t = Mathf.Clamp01((Time.unscaledTime - start) / duration);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            float visibility = opening ? eased : 1 - eased;
            dialogueOpacity.alpha = visibility;
            dialogue.rectTransform.anchoredPosition = PanelPosition + Vector2.down * (22 * (1 - visibility));
            yield return null;
        } while (t < 1);
    }
    IEnumerator CloseAnimation(Action completed)
    {
        // Fade the page being read, not a hard-coded second page.
        yield return Play(dialogue, dialogueSequence, Root + "Dialogue", page == 0 ? 13 : 21, page == 0 ? 15 : 23, true);
        yield return Play(dialogue, dialogueSequence, Root + "Dialogue", 24, 28, true);
        dialogueRoutine = null; transitioning = false; IsPresenting = false;
        dialogue.gameObject.SetActive(false); completed?.Invoke();
    }
    public void HideImmediately()
    {
        if (promptRoutine != null) StopCoroutine(promptRoutine);
        if (dialogueRoutine != null) StopCoroutine(dialogueRoutine);
        promptRoutine = dialogueRoutine = null; promptWanted = false; transitioning = false; IsPresenting = false;
        if (prompt != null) prompt.gameObject.SetActive(false);
        if (dialogue != null) dialogue.gameObject.SetActive(false);
        if (liveName != null) liveName.SetActive(false);
        activeTarget = null; promptTarget = null; activeSideDialogue = false;
    }
    void OnDisable() { HideImmediately(); }
    void OnDestroy() { if (canvasObject != null) Destroy(canvasObject); }
}
