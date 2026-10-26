using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SideGameBLevel { Easy = 0, Normal = 1, Hard = 2 }

// Each choice loads a separately authored route assembled from the original map modules.
public sealed class SideGameBDifficulty : MonoBehaviour
{
    public const string EasyScenePath = "Assets/SideGameB/Game/Scenes/SideGameB_Easy.unity";
    public const string NormalScenePath = "Assets/SideGameB/Game/Scenes/SideGameB_Normal.unity";
    public const string HardScenePath = "Assets/SideGameB/Game/Scenes/SideGameB_Hard.unity";
    public static SideGameBLevel Selected { get; private set; } = SideGameBLevel.Hard;
    public static bool HasSelected { get; private set; }
    private bool choosing;
    private string transitionError;
    private SideGameBLevel priorSelected;private bool priorHasSelected;
    private bool transitioning;
    private bool hideTransitionOverlay;
    private GUIStyle title, body, button;

    public static void ResetForNewVisit()
    {
        Selected = SideGameBLevel.Hard;
        HasSelected = false;
    }

    public static void Show()
    {
        if(FindFirstObjectByType<SideGameBDifficulty>())return;
        var chooser = new GameObject("Side Game B Difficulty").AddComponent<SideGameBDifficulty>();
        chooser.choosing = true;
        Time.timeScale = 0f;
    }

    public static bool PlayLevel(SideGameBLevel level)
    {
        SideGameBDifficulty controller = FindFirstObjectByType<SideGameBDifficulty>();
        if (controller == null)
            controller = new GameObject("Side Game B Difficulty Transition").AddComponent<SideGameBDifficulty>();
        // A challenge selected from the victory panel should keep that panel on
        // screen until the next map is ready. Drawing the generic preparation
        // screen here creates a one-frame, unreadable UI flash.
        controller.hideTransitionOverlay = true;
        return controller.Choose(level);
    }

    private void OnGUI()
    {
        if (!choosing && !transitioning) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 29, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            body = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            body.normal.textColor = Color.white;
            button = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            WayfarerUITheme.KeepLabelColor(title);
            WayfarerUITheme.KeepLabelColor(body);
        }
        if(transitioning&&hideTransitionOverlay)return;
        var old = GUI.color;
        GUI.color = new Color(.025f, .03f, .045f, .97f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
        if (transitioning)
        {
            if (hideTransitionOverlay) return;
            GUI.Label(new Rect(0f, Screen.height * .5f - 35f, Screen.width, 70f),
                "PREPARING HERO SELECTION...", title);
            return;
        }
        float scale = Mathf.Min(1f, Screen.width / 930f, Screen.height / 580f);
        var panel = new Rect((Screen.width - 850f * scale) / 2f, (Screen.height - 470f * scale) / 2f, 850f * scale, 470f * scale);
        GUI.Box(panel, GUIContent.none);
        GUI.Label(new Rect(panel.x, panel.y + 22f * scale, panel.width, 52f * scale), "CHOOSE DIFFICULTY", title);
        string[] names = { "EASY", "NORMAL", "HARD" };
        string[] descriptions = {
            "New short route / 8 diamonds\n2 enemies / 2 rotating sections\nHP +10 / stamina +10",
            "New medium route / 11 diamonds\n3 enemies / 2 rotating sections\nHP +20 / stamina +20",
            "Original full route / 20 diamonds\n4 enemies / all hazards\nHP +30 / stamina +30"
        };
        if(string.IsNullOrEmpty(transitionError))GUI.Label(new Rect(panel.x,panel.yMax-30f*scale,panel.width,24f*scale),"RMB · Return to main world",body);
        if(!string.IsNullOrEmpty(transitionError))GUI.Label(new Rect(panel.x,panel.yMax-35,panel.width,28),transitionError,body);
        for (int i = 0; i < 3; i++)
        {
            float x = panel.x + (33f + i * 271f) * scale;
            var card = new Rect(x, panel.y + 104f * scale, 245f * scale, 310f * scale);
            GUI.Box(card, GUIContent.none);
            GUI.Label(new Rect(x + 8f * scale, card.y + 22f * scale, card.width - 16f * scale, 35f * scale), names[i], title);
            GUI.Label(new Rect(x + 12f * scale, card.y + 86f * scale, card.width - 24f * scale, 120f * scale), descriptions[i], body);
            if (GUI.Button(new Rect(x + 19f * scale, card.yMax - 66f * scale, card.width - 38f * scale, 43f * scale), "PLAY " + names[i], button)) Choose((SideGameBLevel)i);
        }
    }

    private bool Choose(SideGameBLevel level)
    {
        if(transitioning||(int)level<0||(int)level>2)return false;
        string path=ScenePath(level);var travel=SideWorldTravel.Instance;
        if(!Application.CanStreamedLevelBeLoaded(path)||(travel&&travel.IsAway&&(!travel.IsSideWorldB||travel.Busy)))
        {transitionError="Difficulty map unavailable; keep current trial.";return false;}
        priorSelected=Selected;priorHasSelected=HasSelected;
        Selected=level;HasSelected=true;choosing=false;transitioning=true;transitionError=null;
        Time.timeScale=1f;
        if(travel&&travel.IsAway)
        {
            if(!travel.SelectSideGameBDifficulty(path))
            {Selected=priorSelected;HasSelected=priorHasSelected;transitioning=false;choosing=!hideTransitionOverlay;Time.timeScale=0;transitionError="Could not start difficulty; keep current trial.";if(hideTransitionOverlay)Destroy(gameObject);return false;}
            GameAudio.PreserveForDifficultyTransition();
            DontDestroyOnLoad(gameObject);StartCoroutine(FinishContinuousTransition(path));
        }
        else
        {
            GameAudio.PreserveForDifficultyTransition();DontDestroyOnLoad(gameObject);
            SceneManager.LoadScene(path);StartCoroutine(FinishContinuousTransition(path));
        }
        return true;
    }

    private IEnumerator FinishContinuousTransition(string path)
    {
        float loadDeadline=Time.realtimeSinceStartup+30f;
        while (!SceneManager.GetSceneByPath(path).isLoaded)
        {
            var travel=SideWorldTravel.Instance;
            if(Time.realtimeSinceStartup>loadDeadline||(travel&&!travel.IsAway)||(travel&&!travel.Busy))
            {
                Selected=priorSelected;HasSelected=priorHasSelected;transitioning=false;choosing=!hideTransitionOverlay;
                transitionError="Difficulty load failed; current trial kept.";Time.timeScale=0;
                foreach(var portal in FindObjectsByType<VictoryPortalTrigger2D>(FindObjectsSortMode.None))portal.CancelDifficultyTransition();
                if(hideTransitionOverlay)Destroy(gameObject);yield break;
            }
            yield return null;
        }

        float deadline = Time.realtimeSinceStartup + 5f;
        while (!GameAudio.IsMusicPlaying && Time.realtimeSinceStartup < deadline)
            yield return null;

        CharacterSelectionUI selection = FindFirstObjectByType<CharacterSelectionUI>();
        if (selection) selection.ShowSelection();
        transitioning = false;
        Destroy(gameObject);
    }

    public static string ScenePath(SideGameBLevel level)
    {
        switch (level)
        {
            case SideGameBLevel.Easy: return EasyScenePath;
            case SideGameBLevel.Normal: return NormalScenePath;
            default: return HardScenePath;
        }
    }
}
