using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator), typeof(PlayerCombat2D), typeof(FighterMeleeCombat2D))]
public sealed class CharacterSelectionUI : MonoBehaviour
{
    public enum HeroChoice { None, Princess, Fighter }

    private static HeroChoice selectedThisPlaySession;
    public static void ResetForNewVisit() { selectedThisPlaySession = HeroChoice.None; }

    [SerializeField] private RuntimeAnimatorController princessController;
    [SerializeField] private RuntimeAnimatorController fighterController;

    private Animator animator;
    private PlayerCombat2D princessCombat;
    private FighterMeleeCombat2D fighterCombat;
    private PlayerController2D playerController;
    private PlayerHealth playerHealth;
    private bool visible;
    private GUIStyle titleStyle;
    private GUIStyle heroTitleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle recommendationStyle;
    private GUIStyle buttonStyle;

    public void Configure(RuntimeAnimatorController princess, RuntimeAnimatorController fighter)
    {
        princessController = princess;
        fighterController = fighter;
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        princessCombat = GetComponent<PlayerCombat2D>();
        fighterCombat = GetComponent<FighterMeleeCombat2D>();
        playerController = GetComponent<PlayerController2D>();
        playerHealth = GetComponent<PlayerHealth>();
        fighterCombat.enabled = false;

        if (selectedThisPlaySession != HeroChoice.None)
            ApplyChoice(selectedThisPlaySession);
    }

    private void Start()
    {
        if (SideGameBDifficulty.HasSelected && selectedThisPlaySession == HeroChoice.None)
            StartCoroutine(ShowSelectionAfterMusicStarts());
    }

    private IEnumerator ShowSelectionAfterMusicStarts()
    {
        // Keep the difficulty-to-hero flow continuous: the selected map becomes active,
        // its BGM starts, and only then does the hero choice take over the screen.
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!GameAudio.IsMusicPlaying && Time.realtimeSinceStartup < deadline)
            yield return null;
        ShowSelection();
    }

    public void ShowSelection()
    {
        if (selectedThisPlaySession != HeroChoice.None)
        {
            ApplyChoice(selectedThisPlaySession);
            return;
        }

        visible = true;
        Time.timeScale = 0f;
    }

    private void ApplyChoice(HeroChoice choice)
    {
        selectedThisPlaySession = choice;
        bool fighter = choice == HeroChoice.Fighter;
        animator.runtimeAnimatorController = fighter ? fighterController : princessController;
        princessCombat.enabled = !fighter;
        fighterCombat.enabled = fighter;
        playerController.SetAttackAnimationName(fighter ? "attack" : "skill3");
        playerHealth.SetMaximumHealth(fighter ? 130 : 100);
        animator.Play("idle", 0, 0f);
        visible = false;
        Time.timeScale = 1f;
    }

    public bool TryInterfaceBack()
    {
        if(!visible)return false;
        visible=false;ResetForNewVisit();SideGameBDifficulty.ResetForNewVisit();
        SideGameBDifficulty.Show();return true;
    }
    private void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();

        Color previous = GUI.color;
        GUI.color = new Color(0.02f, 0.025f, 0.04f, 0.96f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;

        float scale = Mathf.Min(1f, Screen.width / 1100f, Screen.height / 700f);
        UpdateStyleSizes(scale);
        Rect panel = new((Screen.width - 900f * scale) * 0.5f, (Screen.height - 520f * scale) * 0.5f,
            900f * scale, 520f * scale);
        GUI.color = new Color(0.11f, 0.12f, 0.16f, 1f);
        GUI.Box(panel, GUIContent.none);
        GUI.color = previous;

        GUI.Label(new Rect(panel.x + 65f * scale, panel.y + 470f * scale, panel.width - 130f * scale, 30f * scale), "RMB · Back to difficulty", bodyStyle);
        GUI.Label(new Rect(panel.x, panel.y + 38f * scale, panel.width, 70f * scale), "CHOOSE YOUR HERO", titleStyle);
        GUI.Label(new Rect(panel.x + 65f * scale, panel.y + 125f * scale, 335f * scale, 48f * scale),
            "PRINCESS", heroTitleStyle);
        GUI.Label(new Rect(panel.x + 65f * scale, panel.y + 185f * scale, 335f * scale, 105f * scale),
            "Ranged magic attacks\nLower damage and health", bodyStyle);

        GUI.Label(new Rect(panel.x + 500f * scale, panel.y + 125f * scale, 335f * scale, 48f * scale),
            "FIGHTER", heroTitleStyle);
        GUI.Label(new Rect(panel.x + 500f * scale, panel.y + 172f * scale, 335f * scale, 34f * scale),
            "RECOMMENDED FOR BEGINNERS", recommendationStyle);
        GUI.Label(new Rect(panel.x + 500f * scale, panel.y + 212f * scale, 335f * scale, 90f * scale),
            "High damage and 13 hearts\nClose-range attacks only", bodyStyle);

        if (GUI.Button(new Rect(panel.x + 85f * scale, panel.y + 355f * scale, 290f * scale, 78f * scale), "PLAY PRINCESS", buttonStyle))
            ApplyChoice(HeroChoice.Princess);
        if (GUI.Button(new Rect(panel.x + 525f * scale, panel.y + 355f * scale, 290f * scale, 78f * scale), "PLAY FIGHTER", buttonStyle))
            ApplyChoice(HeroChoice.Fighter);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 34, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        heroTitleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 27, fontStyle = FontStyle.Bold };
        heroTitleStyle.normal.textColor = Color.white;
        bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 19, fontStyle = FontStyle.Bold, wordWrap = true };
        bodyStyle.normal.textColor = Color.white;
        recommendationStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
        recommendationStyle.normal.textColor = new Color(0.55f, 1f, 0.58f);
        foreach (var style in new[] { titleStyle, heroTitleStyle, bodyStyle, recommendationStyle })
            WayfarerUITheme.KeepLabelColor(style);
        buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
    }

    private void UpdateStyleSizes(float scale)
    {
        titleStyle.fontSize = Mathf.Max(20, Mathf.RoundToInt(34f * scale));
        heroTitleStyle.fontSize = Mathf.Max(17, Mathf.RoundToInt(27f * scale));
        bodyStyle.fontSize = Mathf.Max(13, Mathf.RoundToInt(19f * scale));
        recommendationStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(16f * scale));
        buttonStyle.fontSize = Mathf.Max(13, Mathf.RoundToInt(20f * scale));
    }
}
