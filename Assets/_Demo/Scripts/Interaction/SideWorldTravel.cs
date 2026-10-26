using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Keep the host scene alive so progress, input ownership and the standing point
// survive both native trials. Only the selected side scene is unloaded on return.
public sealed class SideWorldTravel : MonoBehaviour
{
    public const string BattlePath = "Assets/Scenes/BattleScene.unity";
    public const string SideGameBPath = "Assets/SideGameB/Game/Scenes/SideGameB.unity";
    public static SideWorldTravel Instance { get; private set; }
    public bool IsAway { get; private set; }
    public bool IsSideWorldB { get; private set; }
    // Retained for existing inspection callers; B no longer uses a web preview.
    public bool IsWebPreview => false;
    public bool TrialACompleted { get; private set; }
    public bool TrialBCompleted { get; private set; }
    public int TrialBBonus { get; private set; }
    public event System.Action TrialACompletionRecorded;
    public event System.Action TrialBCompletionRecorded;
    public void RestoreTrialACompletion(bool completed) { TrialACompleted = completed; }
    public void RestoreTrialBCompletion(bool completed,int bonus=0) { TrialBCompleted=completed;TrialBBonus=completed?Mathf.Clamp(bonus,0,30):0; }
    public bool Busy { get; private set; }
    public BaselineFlow HostFlow { get; private set; }
    private BattleManager sideABattle;
    private Scene host;
    private readonly List<GameObject> suspended = new List<GameObject>();
    private CursorLockMode cursorLock;
    private bool cursorVisible;
    private float hostTimeScale;
    private bool oldCollision, collisionChanged;
    private string error;
    private bool showControls;
    private GUIStyle pixelTitleStyle, pixelNoteStyle, pixelGoalStyle, pixelButtonStyle;
    private string activeSideGameBPath=SideGameBPath;
    private string SideScenePath => IsSideWorldB ? activeSideGameBPath : BattlePath;
    public bool IsHostSuspended(Scene scene) => IsAway && host.IsValid() && scene == host;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Enter(bool sideWorldB)
    {
        if (Busy || IsAway) return;
        var flow = FindFirstObjectByType<BaselineFlow>();
        var ui = FindFirstObjectByType<DialogueUI>();
        if ((flow && (flow.BlockWorldInput || flow.GetComponent<BaselineWorld>().JournalOpen)) ||
            (ui && (ui.IsDialogueActive || ui.IsRewardPresentationActive))) return;
        string path = sideWorldB ? SideGameBPath : BattlePath;
        if (!Application.CanStreamedLevelBeLoaded(path))
        { error = "Side World " + (sideWorldB ? "B" : "A") + " is not available in this build."; return; }
        var quickGear = flow ? flow.GetComponent<BaselineQuickGearBar>() : null;
        if (quickGear) quickGear.CloseForTransition();
        // Save before suspension; a failed save must not launch a new trial.
        if (flow && !flow.SaveNow()) { error = "Could not save before travel. Please retry saving."; return; }
        HostFlow = flow;
        host = SceneManager.GetActiveScene();
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        hostTimeScale = Time.timeScale;
        suspended.Clear();
        foreach (var root in host.GetRootGameObjects())
            if (root.activeSelf && root != gameObject) suspended.Add(root);
        IsAway = true; IsSideWorldB = sideWorldB; error = null; showControls = false;
        foreach (var root in suspended) root.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (sideWorldB)
        {
            GameControlsTutorial.ResetForNewVisit();
            CharacterSelectionUI.ResetForNewVisit();
            SideGameBDifficulty.ResetForNewVisit();activeSideGameBPath=SideGameBPath;
        }
        if (!sideWorldB) BattleManager.ResetForNewVisit();
        StartCoroutine(LoadSideScene());
    }

    private IEnumerator LoadSideScene()
    {
        Busy = true;
        var loading = IsSideWorldB ? null : SideALoadingScreen.Show();
        if (loading) yield return null;
        if (!IsSideWorldB)
        {
            oldCollision = Physics.GetIgnoreLayerCollision(6, 7);
            Physics.IgnoreLayerCollision(6, 7, true);
            collisionChanged = true;
        }
        AsyncOperation operation = null;
        try { operation = SceneManager.LoadSceneAsync(SideScenePath, LoadSceneMode.Additive); }
        catch (System.Exception exception) { error = "Could not load side world: " + exception.Message; }
        if (operation != null)
            while (!operation.isDone)
            {
                if (loading) loading.SetProgress(.08f + .82f * Mathf.Clamp01(operation.progress / .9f));
                yield return null;
            }
        var side = SceneManager.GetSceneByPath(SideScenePath);
        if (operation != null && side.IsValid() && side.isLoaded)
        {
            SceneManager.SetActiveScene(side); sideABattle = IsSideWorldB ? null : FindFirstObjectByType<BattleManager>();
            if (loading) { yield return null; loading.SetProgress(1); yield return loading.Finish(); }
        }
        else
        {
            if (string.IsNullOrEmpty(error)) error = "Could not load the side world.";
            RestoreCollision(); RestoreHost();
            if (loading) loading.Abort();
        }
        Busy = false;
    }

    public void MarkTrialACompleted()
    {
        if (IsAway && !IsSideWorldB && !Busy && !TrialACompleted)
        { TrialACompleted = true; TrialACompletionRecorded?.Invoke(); }
    }
    public void MarkTrialBCompleted(int difficulty = 2)
    {
        if(difficulty<0||difficulty>2||activeSideGameBPath!=SideGameBDifficulty.ScenePath((SideGameBLevel)difficulty))return;
        int earnedBonus = Mathf.Clamp((difficulty + 1) * 10, 10, 30);
        if (IsAway && IsSideWorldB && !Busy && (!TrialBCompleted || earnedBonus > TrialBBonus))
        {
            TrialBBonus = earnedBonus;
            TrialBCompleted = true;
            TrialBCompletionRecorded?.Invoke();
        }
    }

    public bool SelectSideGameBDifficulty(string scenePath)
    {
        if (!IsAway || !IsSideWorldB || Busy || string.IsNullOrEmpty(scenePath)) return false;
        if(scenePath!=SideGameBDifficulty.EasyScenePath&&scenePath!=SideGameBDifficulty.NormalScenePath&&scenePath!=SideGameBDifficulty.HardScenePath)return false;
        if(scenePath==activeSideGameBPath||!Application.CanStreamedLevelBeLoaded(scenePath))return false;
        StartCoroutine(SwitchSideGameBMap(scenePath));return true;
    }
    private IEnumerator SwitchSideGameBMap(string scenePath)
    {
        Busy = true;
        Time.timeScale = 1f;
        if (!Application.CanStreamedLevelBeLoaded(scenePath))
        {
            error = "The selected Side World B difficulty map is not available in this build.";
            Busy = false;
            yield break;
        }

        // Keep the completed map and its result panel visible while the next map
        // loads. Unloading first exposes the suspended main world for a frame and
        // makes its challenge/reward presentation flash between the two maps.
        Scene current = SceneManager.GetSceneByPath(activeSideGameBPath);
        var oldEvents=new List<UnityEngine.EventSystems.EventSystem>();
        if(current.IsValid() && current.isLoaded) foreach(var root in current.GetRootGameObjects())
            foreach(var item in root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true))
                if(item.isActiveAndEnabled){oldEvents.Add(item);item.enabled=false;}
        AsyncOperation load = null;
        try { load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive); }
        catch (System.Exception exception) { error = "Could not load the selected difficulty: " + exception.Message; }

        if (load == null)
        {
            foreach(var item in oldEvents) if(item) item.enabled=true;
            Busy = false;
            yield break;
        }

        yield return load;
        Scene next = SceneManager.GetSceneByPath(scenePath);
        if (!next.IsValid() || !next.isLoaded)
        {
            error = "Could not load the selected difficulty.";
            foreach(var item in oldEvents) if(item) item.enabled=true;
            Busy = false;
            yield break;
        }

        activeSideGameBPath = scenePath;
        SceneManager.SetActiveScene(next);
        if (current.IsValid() && current.isLoaded)
            yield return SceneManager.UnloadSceneAsync(current);
        Busy = false;
    }

    public void RetryBattle()
    {
        if (IsAway && !IsSideWorldB && !Busy) StartCoroutine(Retry());
    }
    public void RetrySideWorldB()
    {
        if (IsAway && IsSideWorldB && !Busy) StartCoroutine(Retry());
    }
    private IEnumerator Retry()
    {
        Busy = true; Time.timeScale = 1;
        if (!IsSideWorldB) { SideALoadingScreen.Show(); yield return null; }
        if(IsSideWorldB)CharacterSelectionUI.ResetForNewVisit();
        SceneManager.SetActiveScene(host);
        var side = SceneManager.GetSceneByPath(SideScenePath);
        if (side.IsValid() && side.isLoaded) yield return SceneManager.UnloadSceneAsync(side);
        RestoreCollision();
        yield return LoadSideScene();
    }

    public void ReturnToMain()
    {
        if (!IsAway || Busy) return;
        StartCoroutine(Return());
    }
    private IEnumerator Return()
    {
        SideWorldEntrance.CaptureReturnOutcome(IsSideWorldB, SceneManager.GetSceneByPath(SideScenePath));
        Busy = true; Time.timeScale = 1;
        SceneManager.SetActiveScene(host);
        var side = SceneManager.GetSceneByPath(SideScenePath);
        if (side.IsValid() && side.isLoaded) yield return SceneManager.UnloadSceneAsync(side);
        RestoreCollision();
        RestoreHost();
        Busy = false;
    }
    private void RestoreCollision()
    {
        if (!collisionChanged) return;
        Physics.IgnoreLayerCollision(6, 7, oldCollision);
        collisionChanged = false;
    }
    private void RestoreHost()
    {
        if (host.IsValid() && host.isLoaded) SceneManager.SetActiveScene(host);
        foreach (var root in suspended) if (root) root.SetActive(true);
        suspended.Clear();
        Time.timeScale = hostTimeScale;
        Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
        IsAway = false; IsSideWorldB = false; HostFlow = null; sideABattle = null;
    }
    private void Update()
    {
        if (IsAway && !Busy && !SideALoadingScreen.IsVisible && WorldInputBindings.Pressed("ReturnWorld"))
            ReturnToMain();

    }
    public bool TryInterfaceBack()
    {
        if(!IsAway)return false;
        if(Busy)return true;
        if(!IsSideWorldB)return sideABattle&&sideABattle.TryInterfaceBack();
        var tutorial=FindFirstObjectByType<GameControlsTutorial>();
        if(tutorial&&tutorial.TryInterfaceBack())return true;
        var selection=FindFirstObjectByType<CharacterSelectionUI>();
        if(selection&&selection.TryInterfaceBack())return true;
        foreach(var portal in FindObjectsByType<VictoryPortalTrigger2D>(FindObjectsSortMode.None))
            if(portal.HasWon){portal.ReturnToMainWorld();return true;}
        var avatar=FindFirstObjectByType<PlayerHealth>();
        if(!SideGameBDifficulty.HasSelected||(avatar&&avatar.IsDead)){ReturnToMain();return true;}
        return false;
    }
    private void OnGUI()
    {
        WayfarerUITheme.Apply();
        if (!IsAway && string.IsNullOrEmpty(error)) return;
        var previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        if (IsAway && IsSideWorldB)
        {
            if(SideGameBDifficulty.HasSelected){GUI.matrix=previousMatrix;return;}
            // This IMGUI overlay otherwise renders above the side game's Canvas
            // failure shade. Leave the death screen visually and interactively clear.
            var sidePlayer = FindFirstObjectByType<PlayerHealth>();
            if (!sidePlayer || !sidePlayer.IsDead) DrawSideWorldBHud();
            GUI.matrix = previousMatrix;
            return;
        }
        if (IsAway) DrawSideWorldAHud();
        else if (!string.IsNullOrEmpty(error))
        {
            GUILayout.BeginArea(new Rect(8, 60, 310, 90), GUI.skin.box);
            GUILayout.Label(error);
            GUILayout.EndArea();
        }
        GUI.matrix = previousMatrix;
    }
    private void DrawSideWorldAHud()
    {
        // Side A owns help and return actions in its ESC menu. Do not draw
        // a second IMGUI toolbar over gameplay, tutorial objectives or pause.
        // Side B keeps its independent HUD, and the configured return hotkey stays in Update.
        if (Busy) return;
        if (sideABattle && (sideABattle.IsPaused || sideABattle.HelpOpen || sideABattle.IsGameOver || sideABattle.SelectionOpen)) return;
        if (!string.IsNullOrEmpty(error)) GUI.Label(new Rect(8, 90, 270, 30), error);
    }
    private void DrawSideWorldBHud()
    {
        if (pixelTitleStyle == null)
        {
            pixelTitleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            pixelTitleStyle.normal.textColor = new Color32(21, 63, 75, 255);
            pixelNoteStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            pixelNoteStyle.normal.textColor = new Color32(27, 72, 78, 255);
            pixelGoalStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            pixelGoalStyle.normal.textColor = new Color32(20, 94, 86, 255);
            pixelButtonStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            pixelButtonStyle.normal.textColor = new Color32(21, 66, 76, 255);
            pixelButtonStyle.hover.textColor = new Color32(12, 51, 76, 255);
            KeepTextColor(pixelTitleStyle);
            KeepTextColor(pixelNoteStyle);
            KeepTextColor(pixelGoalStyle);
        }

        // A tiny grass-topped sign: chunky pixels echo the platforms while the
        // sky-blue face stays readable over the colorful parallax background.
        PixelRect(new Rect(11, 62, 236, 127), new Color32(13, 54, 69, 255)); // offset shadow
        PixelRect(new Rect(8, 58, 236, 128), new Color32(23, 73, 84, 255));
        PixelRect(new Rect(11, 61, 230, 122), new Color32(248, 239, 190, 255));
        PixelRect(new Rect(14, 64, 224, 116), new Color32(169, 225, 217, 250));
        PixelRect(new Rect(14, 64, 224, 26), new Color32(205, 240, 223, 255));
        PixelRect(new Rect(14, 64, 224, 5), new Color32(95, 174, 70, 255));
        PixelRect(new Rect(25, 69, 11, 3), new Color32(125, 199, 79, 255));
        PixelRect(new Rect(71, 69, 16, 3), new Color32(125, 199, 79, 255));
        PixelRect(new Rect(157, 69, 12, 3), new Color32(125, 199, 79, 255));
        PixelRect(new Rect(214, 69, 15, 3), new Color32(125, 199, 79, 255));
        PixelRect(new Rect(20, 91, 212, 2), new Color32(65, 151, 141, 255));
        PixelRect(new Rect(11, 61, 7, 7), new Color32(249, 244, 197, 255));
        PixelRect(new Rect(234, 176, 7, 7), new Color32(95, 174, 70, 255));
        GUI.Label(new Rect(20, 66, 206, 23), "SIDE WORLD B", pixelTitleStyle);
        GUI.Label(new Rect(20, 99, 40, 18), "GOAL", pixelGoalStyle);
        GUI.Label(new Rect(64, 99, 165, 18), Busy ? "Loading..." : "Reach the final portal", pixelNoteStyle);
        if (!Busy)
            GUI.Label(new Rect(20, 119, 212, 18), "Early return = no win", pixelNoteStyle);
        if (!string.IsNullOrEmpty(error))
            GUI.Label(new Rect(20, 119, 212, 18), error, pixelNoteStyle);

        var button = new Rect(20, 145, 212, 31);
        PixelRect(button, new Color32(26, 82, 96, 255));
        PixelRect(new Rect(button.x + 3, button.y + 3, button.width - 6, button.height - 6), new Color32(106, 195, 224, 255));
        PixelRect(new Rect(button.x + 5, button.y + 3, button.width - 10, 3), new Color32(183, 233, 234, 255));
        PixelRect(new Rect(button.x + 3, button.y + button.height - 5, button.width - 6, 2), new Color32(63, 146, 178, 255));
        if (!Busy && GUI.Button(button, "MAIN WORLD  [F8]", pixelButtonStyle)) ReturnToMain();
    }
    private static void PixelRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
    private static void KeepTextColor(GUIStyle style)
    {
        var color = style.normal.textColor;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
        style.onNormal.textColor = color;
        style.onHover.textColor = color;
        style.onActive.textColor = color;
        style.onFocused.textColor = color;
    }
    private void OnDestroy()
    {
        if (Instance != this) return;
        if (SideALoadingScreen.Instance) SideALoadingScreen.Instance.Abort();
        RestoreCollision();
        Time.timeScale = 1f;
        Instance = null;
    }
}
