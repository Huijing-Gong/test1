using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class VictoryPortalTrigger2D : MonoBehaviour
{
    private bool showingVictory;
    public bool HasWon => showingVictory;
    private GUIStyle eyebrowStyle;
    private GUIStyle titleStyle;
    private GUIStyle detailStyle;
    private GUIStyle difficultyStyle;
    private GUIStyle rewardLabelStyle;
    private GUIStyle rewardValueStyle;
    private GUIStyle noteStyle;
    private GUIStyle achievementStyle;
    private GUIStyle buttonStyle;
    private Collider2D closedDoorBlocker;
    private bool allDiamondsCollected;
    private int collectedDiamondTotal;
    private SideGameBLevel completedDifficulty;
    private bool loadingNextDifficulty;

    private void Awake()
    {
        if (transform.parent != null) closedDoorBlocker = transform.parent.GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (showingVictory || (closedDoorBlocker != null && closedDoorBlocker.enabled) ||
            other.GetComponentInParent<PlayerController2D>() == null) return;
        DiamondCounterHud counter = DiamondCounterHud.Instance != null
            ? DiamondCounterHud.Instance
            : FindFirstObjectByType<DiamondCounterHud>();
        allDiamondsCollected = counter != null && counter.HasCollectedAll;
        collectedDiamondTotal = counter != null ? counter.TotalCount : 0;
        completedDifficulty = SideGameBDifficulty.Selected;
        showingVictory = true;
        if (SideWorldTravel.Instance) SideWorldTravel.Instance.MarkTrialBCompleted((int)completedDifficulty);
        Time.timeScale = 0f;
    }

    private void OnGUI()
    {
        if (!showingVictory) return;
        EnsureStyles();
        PixelRect(new Rect(0, 0, Screen.width, Screen.height), new Color32(4, 15, 27, 210));

        var previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float viewWidth = Screen.width / scale;
        float viewHeight = Screen.height / scale;
        int bonus = ((int)completedDifficulty + 1) * 10;
        string difficulty = completedDifficulty.ToString().ToUpperInvariant();
        Color32 accent = completedDifficulty == SideGameBLevel.Easy
            ? new Color32(105, 224, 169, 255)
            : completedDifficulty == SideGameBLevel.Normal
                ? new Color32(255, 202, 91, 255)
                : new Color32(255, 116, 105, 255);

        bool canIncreaseDifficulty = completedDifficulty != SideGameBLevel.Hard;
        float panelHeight = (allDiamondsCollected ? 362f : 310f) + (canIncreaseDifficulty ? 54f : 0f);
        var panel = new Rect((viewWidth - 550) * .5f, (viewHeight - panelHeight) * .5f, 550, panelHeight);
        PixelRect(new Rect(panel.x + 10, panel.y + 11, panel.width, panel.height), new Color32(0, 8, 16, 170));
        PixelRect(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), new Color32(102, 215, 185, 255));
        PixelRect(panel, new Color32(12, 43, 58, 255));
        PixelRect(new Rect(panel.x + 5, panel.y + 5, panel.width - 10, panel.height - 10), new Color32(15, 58, 68, 255));

        PixelRect(new Rect(panel.x + 5, panel.y + 5, panel.width - 10, 49), new Color32(10, 39, 55, 255));
        PixelRect(new Rect(panel.x + 22, panel.y + 24, 8, 8), accent);
        GUI.Label(new Rect(panel.x + 39, panel.y + 15, 300, 27), "SIDE WORLD B  /  TRIAL COMPLETE", eyebrowStyle);
        var badge = new Rect(panel.x + 422, panel.y + 14, 102, 28);
        PixelRect(badge, new Color32(7, 27, 39, 255));
        PixelRect(new Rect(badge.x, badge.y, 4, badge.height), accent);
        difficultyStyle.normal.textColor = accent;
        GUI.Label(badge, difficulty, difficultyStyle);

        GUI.Label(new Rect(panel.x + 25, panel.y + 67, panel.width - 50, 48), "VICTORY!", titleStyle);
        string completionDetail = completedDifficulty == SideGameBLevel.Easy
            ? "CHALLENGE UNLOCKED — NORMAL AND HARD"
            : completedDifficulty == SideGameBLevel.Normal
                ? "CHALLENGE UNLOCKED — HARD"
                : "THE FINAL CHALLENGE IS COMPLETE";
        GUI.Label(new Rect(panel.x + 25, panel.y + 111, panel.width - 50, 25),
            completionDetail, detailStyle);

        var rewards = new Rect(panel.x + 32, panel.y + 151, panel.width - 64, 72);
        PixelRect(rewards, new Color32(7, 34, 48, 255));
        PixelRect(new Rect(rewards.x, rewards.y, rewards.width, 2), new Color32(72, 155, 145, 255));
        PixelRect(new Rect(rewards.center.x - 1, rewards.y + 12, 2, rewards.height - 24), new Color32(43, 107, 105, 255));
        GUI.Label(new Rect(rewards.x, rewards.y + 9, rewards.width * .5f, 20), "MAX HP", rewardLabelStyle);
        GUI.Label(new Rect(rewards.x, rewards.y + 31, rewards.width * .5f, 31), "+" + bonus, rewardValueStyle);
        GUI.Label(new Rect(rewards.center.x, rewards.y + 9, rewards.width * .5f, 20), "STAMINA", rewardLabelStyle);
        GUI.Label(new Rect(rewards.center.x, rewards.y + 31, rewards.width * .5f, 31), "+" + bonus, rewardValueStyle);

        float noteY = panel.y + 229;
        float buttonY = panel.y + 254;
        if (allDiamondsCollected)
        {
            var achievement = new Rect(panel.x + 32, panel.y + 231, panel.width - 64, 43);
            PixelRect(new Rect(achievement.x + 3, achievement.y + 4, achievement.width, achievement.height),
                new Color32(0, 15, 24, 190));
            PixelRect(achievement, new Color32(29, 79, 88, 255));
            PixelRect(new Rect(achievement.x, achievement.y, 5, achievement.height), accent);
            PixelRect(new Rect(achievement.x + achievement.width - 5, achievement.y, 5, achievement.height), accent);
            achievementStyle.normal.textColor = accent;
            GUI.Label(achievement,
                $"◆  ACHIEVEMENT UNLOCKED — ALL {collectedDiamondTotal} DIAMONDS COLLECTED  ◆",
                achievementStyle);
            noteY = panel.y + 281;
            buttonY = panel.y + 306;
        }

        GUI.Label(new Rect(panel.x + 25, noteY, panel.width - 50, 18),
            "REWARDS WILL BE APPLIED IN THE MAIN WORLD", noteStyle);
        if (canIncreaseDifficulty)
        {
            bool threeButtons = completedDifficulty == SideGameBLevel.Easy;
            buttonStyle.fontSize = threeButtons ? 12 : 15;
            var returnButton = threeButtons
                ? new Rect(panel.x + 25, buttonY, 154, 40)
                : new Rect(panel.x + 31, buttonY, 231, 42);
            DrawButton(returnButton, new Color32(35, 125, 113, 255));
            if (!loadingNextDifficulty && GUI.Button(returnButton, "KEEP +" + bonus + " & RETURN", buttonStyle))
                ReturnToMainWorld();

            if (completedDifficulty == SideGameBLevel.Easy)
            {
                var normalButton = new Rect(panel.x + 198, buttonY, 154, 40);
                var hardButton = new Rect(panel.x + 371, buttonY, 154, 40);
                DrawButton(normalButton, new Color32(169, 125, 43, 255));
                DrawButton(hardButton, new Color32(169, 70, 55, 255));
                if (!loadingNextDifficulty && GUI.Button(normalButton, "TRY NORMAL  +20", buttonStyle))
                    ChallengeNextDifficulty(SideGameBLevel.Normal);
                if (!loadingNextDifficulty && GUI.Button(hardButton, "TRY HARD  +30", buttonStyle))
                    ChallengeNextDifficulty(SideGameBLevel.Hard);
            }
            else
            {
                var hardButton = new Rect(panel.x + 288, buttonY, 231, 42);
                DrawButton(hardButton, new Color32(169, 70, 55, 255));
                if (!loadingNextDifficulty && GUI.Button(hardButton, "CHALLENGE HARD  +30", buttonStyle))
                    ChallengeNextDifficulty(SideGameBLevel.Hard);
            }
            GUI.Label(new Rect(panel.x + 25, buttonY + 46, panel.width - 50, 18),
                "WIN A HIGHER DIFFICULTY TO REPLACE THIS REWARD WITH THE HIGHER BONUS", noteStyle);
        }
        else
        {
            buttonStyle.fontSize = 15;
            var button = new Rect(panel.x + 92, buttonY, panel.width - 184, 42);
            DrawButton(button, new Color32(35, 125, 113, 255));
            if (GUI.Button(button, "RETURN TO MAIN WORLD   >", buttonStyle)) ReturnToMainWorld();
        }
        GUI.matrix = previousMatrix;
    }

    private void DrawButton(Rect button, Color32 baseColor)
    {
        bool hover = button.Contains(Event.current.mousePosition);
        PixelRect(new Rect(button.x + 4, button.y + 5, button.width, button.height), new Color32(3, 24, 31, 210));
        PixelRect(button, hover ? Color.Lerp(baseColor, Color.white, .2f) : baseColor);
        PixelRect(new Rect(button.x, button.y, button.width, 3), hover
            ? new Color32(245, 255, 217, 255) : new Color32(145, 231, 190, 255));
        PixelRect(new Rect(button.x, button.y + button.height - 3, button.width, 3), new Color32(10, 69, 72, 255));
    }

    private void ChallengeNextDifficulty(SideGameBLevel next)
    {
        if (loadingNextDifficulty) return;
        loadingNextDifficulty = true;
        Time.timeScale = 1f;
        if(!SideGameBDifficulty.PlayLevel(next)){loadingNextDifficulty=false;Time.timeScale=0;}
    }

    public void CancelDifficultyTransition(){loadingNextDifficulty=false;}

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        eyebrowStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        eyebrowStyle.normal.textColor = new Color32(160, 242, 217, 255);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 40,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        Color titleColor = new Color32(239, 253, 239, 255);
        titleStyle.normal.textColor = titleColor;
        detailStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            wordWrap = false
        };
        detailStyle.normal.textColor = new Color32(222, 246, 229, 255);
        WayfarerUITheme.KeepLabelColor(eyebrowStyle);
        WayfarerUITheme.KeepLabelColor(titleStyle);
        WayfarerUITheme.KeepLabelColor(detailStyle);
        difficultyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        rewardLabelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        rewardLabelStyle.normal.textColor = new Color32(151, 207, 194, 255);
        rewardValueStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 27,
            fontStyle = FontStyle.Bold
        };
        rewardValueStyle.normal.textColor = new Color32(240, 255, 226, 255);
        noteStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            fontStyle = FontStyle.Bold
        };
        noteStyle.normal.textColor = new Color32(114, 174, 166, 255);
        achievementStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        foreach (var style in new[] { difficultyStyle, rewardLabelStyle, rewardValueStyle, noteStyle, achievementStyle })
            WayfarerUITheme.KeepLabelColor(style);
        buttonStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        buttonStyle.normal.textColor = new Color32(244, 255, 240, 255);
        buttonStyle.hover.textColor = Color.white;
    }
    private static void PixelRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    public void ReturnToMainWorld()
    {
        var travel = SideWorldTravel.Instance;
        if (travel && travel.IsAway)
        {
            if (travel.IsSideWorldB) travel.ReturnToMain();
            return;
        }
        // Standalone scene testing has no suspended host to restore.
        Time.timeScale = 1f;
        if (SceneManager.sceneCountInBuildSettings > 0)
            SceneManager.LoadScene(0);
        else
            Debug.LogError("Cannot return to the main world because Build Settings has no entry scene.");
    }

    private void OnDisable()
    {
        if (showingVictory) Time.timeScale = 1f;
    }
}
