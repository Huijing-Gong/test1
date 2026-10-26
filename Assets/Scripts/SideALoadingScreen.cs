using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Persistent transition UI, independent of scene time scale and save/progression.
// Progress is supplied by the real async operation, never a timed pretend load.
public sealed class SideALoadingScreen : MonoBehaviour
{
    public static SideALoadingScreen Instance { get; private set; }
    public static bool IsVisible => Instance && Instance.gameObject.activeInHierarchy;
    public float Progress { get; private set; }
    public int LitDiamonds => Progress >= 1 ? 5 : Mathf.FloorToInt(Progress * 5);
    CanvasGroup group;
    RectTransform crest;
    SideALoadingGraphic[] diamonds;
    bool finishing;
    float shownAt;
    public const float MinimumDisplaySeconds = 1f;
    Camera coverCamera;
    static readonly Color Ink = new Color32(23, 58, 50, 255);
    static readonly Color Gold = new Color32(207, 165, 73, 255);
    static readonly string[] Tips = {
        "The higher the damage, the farther the knockback.",
        "A second jump can bring you back.",
        "Two lives. Stay on the stage.",
        "Tap special for a short beam. Hold it to charge." };
    static int tipIndex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; tipIndex = 0; }

    public static SideALoadingScreen Show()
    {
        if (Instance) return Instance;
        var root = new GameObject("Side A Cream Loading", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(SideALoadingScreen));
        DontDestroyOnLoad(root);
        Instance = root.GetComponent<SideALoadingScreen>();
        Instance.shownAt = Time.realtimeSinceStartup;
        Instance.Build();
        return Instance;
    }

    public static void EnsureLoadedSceneCover()
    {
        if (IsVisible) return;
        var screen = Show();
        screen.StartCoroutine(screen.LoadedScene());
    }

    IEnumerator LoadedScene()
    { yield return null; SetProgress(1); yield return Finish(); }

    public static void Reload(string scene)
    {
        if (IsVisible) return;
        var screen = Show();
        screen.StartCoroutine(screen.ReloadRoutine(scene));
    }

    IEnumerator ReloadRoutine(string scene)
    {
        yield return null; // Paint the opaque screen before scene IO/unload.
        AsyncOperation operation = null;
        try { operation = SceneManager.LoadSceneAsync(scene); }
        catch (System.Exception error) { Debug.LogException(error); }
        if (operation == null) { Abort(); yield break; }
        while (!operation.isDone) { SetProgress(.08f + .82f * Mathf.Clamp01(operation.progress / .9f)); yield return null; }
        yield return null; // Give the new scene Start/layout a frame.
        SetProgress(1); yield return Finish();
    }

    public void SetProgress(float value) { Progress = Mathf.Max(Progress, Mathf.Clamp01(value)); }

    public IEnumerator Finish()
    {
        if (finishing) yield break;
        finishing = true; SetProgress(1);
        Canvas.ForceUpdateCanvases();
        yield return null;
        // Readability floor requested by design; this is not fabricated IO progress.
        while (Time.realtimeSinceStartup - shownAt < MinimumDisplaySeconds) yield return null;
        coverCamera.enabled = false; // Reveal the destination camera underneath the fading UI.
        float age = 0;
        while (age < .22f) { age += Time.unscaledDeltaTime; group.alpha = 1 - Mathf.Clamp01(age / .22f); yield return null; }
        Abort();
    }

    public void Abort()
    {
        if (Instance == this) Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    void Update()
    {
        float wave = BattlePresentationSettings.ReducedMotion ? .5f : .5f + .5f * Mathf.Sin(Time.unscaledTime * 2.1f);
        crest.localScale = Vector3.one * (BattlePresentationSettings.ReducedMotion ? 1 : 1 + wave * .012f);
        for (int i = 0; i < diamonds.Length; i++)
        {
            bool lit = i < LitDiamonds;
            diamonds[i].filled = lit;
            diamonds[i].color = lit ? Gold : new Color(Ink.r, Ink.g, Ink.b, i == LitDiamonds ? .35f + .35f * wave : .45f);
            diamonds[i].SetVerticesDirty();
        }
    }

    void Build()
    {
        var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        group = GetComponent<CanvasGroup>(); group.blocksRaycasts = true; group.interactable = true;
        // Host cameras are suspended before additive IO; UGUI still needs a
        // rendering camera on that frame. This camera renders no world objects.
        var cameraObject = new GameObject("Loading backdrop camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        coverCamera = cameraObject.GetComponent<Camera>(); coverCamera.cullingMask = 0;
        coverCamera.clearFlags = CameraClearFlags.SolidColor; coverCamera.backgroundColor = new Color32(248,241,218,255);
        coverCamera.depth = 1000; coverCamera.allowHDR = coverCamera.allowMSAA = false;
        coverCamera.useOcclusionCulling = false;
        var background = new GameObject("Opaque cream", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(transform, false);
        var rect = background.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color32(248, 241, 218, 255);
        // A fixed-aspect layout scales down to fit both narrow and ultrawide displays.
        var layout = new GameObject("Safe artwork", typeof(RectTransform), typeof(AspectRatioFitter));
        layout.transform.SetParent(transform, false);
        var fit = layout.GetComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 16f / 9f;
        var safe = layout.GetComponent<RectTransform>();
        var watermark = Graphic("Compass watermark", safe, new Vector2(.5f,.61f), new Vector2(660,660), SideALoadingGraphic.Shape.Compass);
        watermark.color = new Color(Gold.r, Gold.g, Gold.b, .10f);
        var emblem = Graphic("Three platform crest", safe, new Vector2(.5f,.61f), new Vector2(280,280), SideALoadingGraphic.Shape.Crest);
        emblem.color = Ink; crest = emblem.rectTransform;
        var tag = Graphic("Side A chip", safe, new Vector2(.5f,.38f), new Vector2(286,62), SideALoadingGraphic.Shape.Chip);
        tag.color = Ink;
        Label("SIDE A", tag.transform, new Vector2(.5f,.5f), new Vector2(240,50), 27, new Color32(248,241,218,255), 7);
        Label("THE ARENA AWAITS", safe, new Vector2(.5f,.295f), new Vector2(1400,72), 40, Ink, 12);
        Label(Tips[tipIndex++ % Tips.Length], safe, new Vector2(.5f,.21f), new Vector2(1500,62), 25, new Color32(83,111,94,255), 1);
        var left = Graphic("Left rule", safe, new Vector2(.30f,.12f), new Vector2(460,1), SideALoadingGraphic.Shape.Rule); left.color = Ink * new Color(1,1,1,.65f);
        var right = Graphic("Right rule", safe, new Vector2(.70f,.12f), new Vector2(460,1), SideALoadingGraphic.Shape.Rule); right.color = left.color;
        diamonds = new SideALoadingGraphic[5];
        for (int i=0;i<5;i++)
        {
            diamonds[i] = Graphic("Loading diamond " + (i+1),safe,new Vector2(.5f+(i-2)*.030f,.12f),new Vector2(23,23),SideALoadingGraphic.Shape.Diamond);
            diamonds[i].color = new Color(Ink.r,Ink.g,Ink.b,.45f);
        }
        Label("LOADING",safe,new Vector2(.5f,.063f),new Vector2(500,40),18,new Color32(83,111,94,255),8);
    }

    static SideALoadingGraphic Graphic(string name, Transform parent, Vector2 anchor, Vector2 size, SideALoadingGraphic.Shape shape)
    {
        var go = new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(SideALoadingGraphic));go.transform.SetParent(parent,false);
        var graphic=go.GetComponent<SideALoadingGraphic>(); Place(graphic.rectTransform,anchor,size);graphic.shape=shape;graphic.raycastTarget=false;return graphic;
    }
    static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
    {rect.anchorMin=rect.anchorMax=anchor;rect.pivot=Vector2.one*.5f;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=size;}
    static void Label(string text,Transform parent,Vector2 anchor,Vector2 size,float fontSize,Color color,float spacing)
    {
        var go=new GameObject(text,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var label=go.GetComponent<TextMeshProUGUI>();Place(label.rectTransform,anchor,size);
        label.font=TMP_Settings.defaultFontAsset;label.text=text;label.fontSize=fontSize;label.color=color;label.characterSpacing=spacing;
        label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;label.enableWordWrapping=true;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}
