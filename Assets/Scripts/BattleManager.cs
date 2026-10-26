using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class BattleManager : MonoBehaviour
{
    private static readonly int IsMovingHash =
        Animator.StringToHash("IsMoving");

    private static readonly int IsWalkingHash =
        Animator.StringToHash("IsWalking");

    private static readonly int IsGroundedHash =
        Animator.StringToHash("IsGrounded");

    private static readonly int JumpTriggerHash =
        Animator.StringToHash("Jump");

    private static readonly int YesTriggerHash =
        Animator.StringToHash("Yes");

    private const int GroundLayerMask = 1 << 0;
    private const float GroundCheckDistance = 1.1f;
    private const float FrontFacingRotationY = 180f;
    private const float ControlsHintVisibleDuration = 4.5f;
    private const float ControlsHintFadeDuration = 0.75f;

    [Header("Battle HUD")]
    public TMP_Text gameOverText;
    public TMP_Text battleTimerText;
    public TMP_Text countdownText;
    public Button pauseButton;
    public GameObject pauseOverlay;
    public Button closePauseButton;
    [Header("Trial selection and shared menus")]
    public BattleTrialMenu trialMenu;
    private bool selectionOpen;
    public bool SelectionOpen => selectionOpen;
    public float BattleDuration => elapsedBattleTime;
    public BattleTutorial Tutorial { get; private set; }
    public bool IsTutorialActive => Tutorial && Tutorial.IsRunning;
    private static bool tutorialReplayRequested;
    private static bool quickGuideShownThisVisit;

    [Header("Battle Intro")]
    public Vector3 playerIntroPosition =
        new Vector3(-13f, 8f, 0f);

    public Vector3 enemyIntroPosition =
        new Vector3(9f, 8f, 0f);

    [Min(0.1f)]
    public float countdownStepDuration = 0.8f;

    [Min(0f)]
    public float introInitialFallSpeed = 2.5f;

    [Min(1f)]
    public float introFallGravityMultiplier = 4.5f;

    [Min(0.1f)]
    public float maximumLandingWait = 4f;

    [Min(0f)]
    public float landingSettleDuration = 0.25f;

    [Min(0.1f)]
    public float goDisplayDuration = 0.65f;

    private bool gameOver = false;
    private bool isPaused = false;
    private bool manualPaused;
    private bool helpOpen;
    private bool awaitingIntroduction;
    private bool playerWon;
    private float pauseResumeTimeScale = 1f;
    private static bool introductionSeenThisVisit;
    private GUIStyle helpTitleStyle, helpTextStyle, helpButtonStyle;
    private bool battleActive;
    private bool introPrepared;
    private BattleIntroPresentation introPresentation;
    private float elapsedBattleTime;
    private float controlsHintElapsedTime;
    private int displayedCentiseconds = -1;
    private Coroutine introCoroutine;
    private bool preparingSelected;
    private SideALoadingScreen selectionLoading;
    private CanvasGroup controlsHintCanvasGroup;
    private TMP_Text controlsHintText;
    private BattleSpecialMoves controlsHintSpecials;
    private int displayedControlsHintSignature = -1;
    private KOZone bottomKoFallback;
    private float bottomKoFallbackY;

    private GameObject playerFighter;
    private GameObject enemyFighter;
    private Rigidbody playerBody;
    private Rigidbody enemyBody;
    private Animator playerAnimator;
    private Animator enemyAnimator;
    private BattlePlayerController playerController;
    private BattleAttack playerAttack;
    private EnemyMovement enemyMovement;
    private EnemyAttack enemyAttack;
    private Vector3 originalPlayerPosition;
    private Vector3 originalEnemyPosition;
    private CollisionDetectionMode originalPlayerCollisionMode;
    private CollisionDetectionMode originalEnemyCollisionMode;
    private bool playerControllerWasEnabled;
    private bool playerAttackWasEnabled;
    private bool enemyMovementWasEnabled;
    private bool enemyAttackWasEnabled;

    public bool IsGameOver => gameOver;
    public bool IsPaused => isPaused;
    public bool IsBattleActive => battleActive;
    public bool HelpOpen => helpOpen;
    public bool IsAwaitingIntroduction => awaitingIntroduction;
    public bool PlayerHasWon => gameOver && playerWon;

    public int CurrentTrialIndex => GetComponent<BattleArenaStages>() is BattleArenaStages stages ? stages.ActiveIndex : 0;
    public static void ResetForNewVisit()
    { introductionSeenThisVisit = false; tutorialReplayRequested = quickGuideShownThisVisit = false; BattleArenaStages.ResetVisit(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTutorialRequest() => tutorialReplayRequested = quickGuideShownThisVisit = false;

    void Awake()
    {
        SideALoadingScreen.EnsureLoadedSceneCover();
        // Time scale persists between scene loads, so every battle starts
        // from a known playable state.
        Time.timeScale = 1f;

        if (!GetComponent<SideAAudio>()) gameObject.AddComponent<SideAAudio>();

        PrepareBattleIntro();
        // The browser owns its normal right-click menu everywhere except the
        // active Side A canvas. The helper releases its listener on scene exit.
        if (!GetComponent<BattleWebGLMouseControls>())
            gameObject.AddComponent<BattleWebGLMouseControls>();
    }

    void Start()
    {
        if (gameOverText == null)
        {
            Debug.LogError(
                "BattleManager requires a GameOverText reference."
            );
        }
        else
        {
            gameOverText.text = "";
            gameOverText.gameObject.SetActive(false);
        }

        // Older BattleScene saves do not have the dedicated countdown text
        // yet. Reusing GameOverText keeps the intro functional until the HUD
        // setup utility is run again.
        if (countdownText == null)
        {
            countdownText = gameOverText;
        }

        HideCountdownText();

        if (pauseOverlay != null)
        {
            pauseOverlay.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.interactable = battleActive;
            pauseButton.onClick.AddListener(PauseBattle);
        }

        if (closePauseButton != null)
        {
            closePauseButton.onClick.AddListener(ResumeBattle);
        }

        PrepareControlsHint();
        RefreshBattleTimer();
        Tutorial = GetComponent<BattleTutorial>();
        if (!Tutorial) Tutorial = gameObject.AddComponent<BattleTutorial>();
        Tutorial.Bind(this, playerFighter, enemyFighter);
        var tutorialHud = GetComponent<BattleTutorialHud>();
        if (!tutorialHud) tutorialHud = gameObject.AddComponent<BattleTutorialHud>();
        tutorialHud.Bind(this, Tutorial);
        var tutorialHints = GetComponent<BattleTutorialHints>();
        if (!tutorialHints) tutorialHints = gameObject.AddComponent<BattleTutorialHints>();
        tutorialHints.Bind(this, playerFighter, enemyFighter);

        if (introPrepared)
        {
            if (GetComponent<BattleArenaStages>())
            {
                introPresentation=GetComponent<BattleIntroPresentation>();
                if(!introPresentation) introPresentation=gameObject.AddComponent<BattleIntroPresentation>();
                introPresentation.PrepareHidden(playerFighter.transform,enemyFighter.transform);
            }
            if (trialMenu)
            {
                trialMenu.Bind(this);
                selectionOpen = awaitingIntroduction = true;
                ApplyPauseState();
                trialMenu.OpenSelection();
                if (tutorialReplayRequested)
                {
                    tutorialReplayRequested = false;
                    BattleTrialMenu.ClearReplay();
                    BeginSelectedTutorial();
                }
                else if (BattleTrialMenu.ConsumeReplayRequest()) BeginSelectedTrial();
                else
                {
                    var travel = SideWorldTravel.Instance;
                    if (travel && travel.IsAway && !travel.IsSideWorldB && !quickGuideShownThisVisit)
                    {
                        // An entry overview, not a compulsory playable lesson.
                        // Keep the existing selection pause; finishing returns
                        // to trial selection, never straight into a live duel.
                        quickGuideShownThisVisit = true;
                        trialMenu.OpenQuickGuide(true);
                    }
                }
                return;
            }
            awaitingIntroduction = !introductionSeenThisVisit;
            if (awaitingIntroduction) SetHelpOpen(true);
            else introCoroutine = StartCoroutine(PlayBattleIntro());
        }
    }

    void Update()
    {
        if (SideALoadingScreen.IsVisible) return;
        // The overview owns its navigation keys. The consumed-frame guard also
        // covers Menu.Update running before this method when the last Enter/H
        // closes a card, so it cannot immediately confirm or dismiss another UI.
        if (trialMenu && trialMenu.QuickGuideConsumesInput) return;
        var keyboard = Keyboard.current;
        if (trialMenu && keyboard != null && !gameOver && (!selectionOpen || helpOpen) && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (helpOpen) SetHelpOpen(false);
            else if (manualPaused) ResumeBattle();
            else PauseBattle();
        }
        if (!gameOver && keyboard != null)
        {
            if (helpOpen && keyboard.enterKey.wasPressedThisFrame)
            {
                if (trialMenu && selectionOpen) SetHelpOpen(false);
                else if (awaitingIntroduction) ConfirmIntroduction();
                else SetHelpOpen(false);
            }
            else if (keyboard.hKey.wasPressedThisFrame && !awaitingIntroduction)
                ToggleHelp();
        }
        if (battleActive && !gameOver && !isPaused)
        {
            if (!IsTutorialActive)
            {
                elapsedBattleTime += Time.unscaledDeltaTime;
                UpdateControlsHint();
            }
            RefreshBattleTimer();
            CheckFallenFighterFallback();
        }

        if (gameOver &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            RestartBattle();
        }
    }

    void CheckFallenFighterFallback()
    {
        if (!bottomKoFallback)
        {
            // The trigger and horizontal escape fallback share one authored
            // bottom plane. Never make an off-to-the-side fall wait longer.
            foreach (var zone in FindObjectsByType<KOZone>(FindObjectsSortMode.None))
            {
                var shape = zone.GetComponent<Collider>();
                if (zone.battleManager != this || !shape || !shape.enabled) continue;
                if (!bottomKoFallback || shape.bounds.max.y < bottomKoFallbackY)
                {
                    bottomKoFallback = zone;
                    bottomKoFallbackY = shape.bounds.max.y;
                }
            }
        }
        if (!bottomKoFallback) return;
        if (HasCrossedBottomPlane(playerBody))
            bottomKoFallback.ResolveFallenFighter(playerBody.gameObject);
        else if (HasCrossedBottomPlane(enemyBody))
            bottomKoFallback.ResolveFallenFighter(enemyBody.gameObject);
    }

    bool HasCrossedBottomPlane(Rigidbody fighter)
    {
        if (!fighter) return false;
        var shape = fighter.GetComponent<Collider>();
        return (shape && shape.enabled ? shape.bounds.min.y : fighter.position.y) <= bottomKoFallbackY;
    }

    public void PlayerWon()
    {
        if (IsTutorialActive || gameOver || trialMenu && !battleActive)
            return;

        gameOver = true;
        playerWon = true;
        battleActive = false;
        StopBattleIntroPresentation();
        GetComponent<SideAAudio>()?.Result(true);
        DisablePauseButton();
        ShowGameOver(
            "YOU WIN!",
            new Color32(100, 230, 160, 255)
        );

        Debug.Log("You Win!");
        // Preserve the integrated host world's completion and reward tracking.
        if (SideWorldTravel.Instance && CurrentTrialIndex == 0)
            SideWorldTravel.Instance.MarkTrialACompleted();
    }

    public void PlayerLost()
    {
        if (IsTutorialActive || gameOver || trialMenu && !battleActive)
            return;

        gameOver = true;
        playerWon = false;
        battleActive = false;
        GetComponent<SideAAudio>()?.Result(false);
        StopBattleIntroPresentation();
        DisablePauseButton();
        ShowGameOver(
            "YOU LOSE!",
            new Color32(255, 105, 115, 255)
        );

        Debug.Log("You Lose!");
    }

    void ShowGameOver(string result, Color color)
    {
        if (trialMenu)
        {
            if (gameOverText) gameOverText.gameObject.SetActive(false);
            trialMenu.OpenResult();
            return;
        }
        if (gameOverText == null)
            return;

        gameOverText.gameObject.SetActive(true);
        gameOverText.color = color;
        bool integrated = SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway;
        gameOverText.text =
            "<size=76><b>" + result + "</b></size>\n" +
            (integrated ? "<size=26>R RETRY  ·  RMB BACK / F8 RETURN</size>" : "<size=30>PRESS R TO RESTART</size>");
    }

    void PauseBattle()
    {
        if (!battleActive ||
            gameOver ||
            manualPaused ||
            pauseOverlay == null)
        {
            return;
        }

        manualPaused = true;
        SideAAudio.Interface();
        ApplyPauseState();
        if (trialMenu) trialMenu.OpenPause();
        pauseOverlay.transform.SetAsLastSibling();
    }

    void ResumeBattle()
    {
        if (!manualPaused)
            return;

        manualPaused = false;
        ApplyPauseState();
        if (trialMenu) trialMenu.Hide();
    }

    public void ConfirmIntroduction()
    {
        if (trialMenu && selectionOpen) { trialMenu.StartSelected(); return; }
        if (!awaitingIntroduction || gameOver) return;
        awaitingIntroduction = false;
        introductionSeenThisVisit = true;
        SetHelpOpen(false);
        introCoroutine = StartCoroutine(PlayBattleIntro());
    }

    public void ToggleHelp()
    {
        if (gameOver || awaitingIntroduction) return;
        SetHelpOpen(!helpOpen);
    }

    private void SetHelpOpen(bool value)
    {
        if (helpOpen == value) return;
        helpOpen = value;
        ApplyPauseState();
        if (trialMenu) { if (value) trialMenu.OpenControls(); else trialMenu.CloseControls(); }
    }

    public void OpenTrialControls() { if (!gameOver) SetHelpOpen(true); }
    // Back only unwinds an existing overlay. During a duel RMB remains a special attack.
    public bool TryInterfaceBack()
    {
        if(helpOpen){SetHelpOpen(false);return true;}
        if(manualPaused){ResumeBattle();return true;}
        if(gameOver&&trialMenu){ReturnToTrialSelection();return true;}
        if(selectionOpen&&trialMenu){trialMenu.ReturnToMain();return true;}
        return false;
    }
    public void CloseTrialControls() => SetHelpOpen(false);
    public void ResumeTrialPause() => ResumeBattle();

    public void BeginSelectedTrial()
    {
        BeginSelectionTransition(false);
    }

    void BeginSelectionTransition(bool practice)
    {
        if (preparingSelected || !trialMenu || !selectionOpen || !introPrepared || gameOver || helpOpen || introCoroutine != null) return;
        preparingSelected = true;
        StartCoroutine(PrepareSelection(practice));
    }

    IEnumerator PrepareSelection(bool practice)
    {
        bool ownCover = !SideALoadingScreen.IsVisible;
        var screen = SideALoadingScreen.Show();
        if (ownCover) selectionLoading = screen;
        yield return null; // Render the cover before assembling the selected stage.
        bool ready = false;
        try
        {
            var stages = GetComponent<BattleArenaStages>();
            ready = !stages || stages.Apply(trialMenu.SelectedTrial);
        }
        catch (System.Exception error) { Debug.LogException(error); }
        if (!ready)
        {
            if (ownCover && screen) screen.Abort();
            selectionLoading = null; preparingSelected = false;
            trialMenu.OpenSelection();
            yield break;
        }
        if (ownCover) { screen.SetProgress(1); yield return screen.Finish(); }
        else while (SideALoadingScreen.IsVisible) yield return null;
        selectionLoading = null; preparingSelected = false;
        if (practice) BeginSelectedTutorialReady(); else BeginSelectedTrialReady();
    }

    void BeginSelectedTrialReady()
    {
        if (!trialMenu || !selectionOpen || !introPrepared || gameOver || helpOpen || introCoroutine != null) return;
        selectionOpen = awaitingIntroduction = false;
        introductionSeenThisVisit = true;
        trialMenu.Hide();
        ApplyPauseState();
        introCoroutine = StartCoroutine(PlayBattleIntro());
    }

    // Training uses the actual fighter/input/physics code, but never owns
    // formal results, elapsed time or host progression. Scene reload is the
    // boundary back to a fresh two-stock duel (including every transient VFX).
    public void BeginSelectedTutorial()
    {
        BeginSelectionTransition(true);
    }

    void BeginSelectedTutorialReady()
    {
        if (!trialMenu || !selectionOpen || !introPrepared || gameOver || helpOpen || introCoroutine != null) return;
        selectionOpen = awaitingIntroduction = false;
        introductionSeenThisVisit = true;
        trialMenu.Hide();
        ApplyPauseState();
        StopBattleIntroPresentation();
        elapsedBattleTime = 0;
        battleActive = true;
        Tutorial.Begin();
        RestoreBehaviour(playerController, playerControllerWasEnabled);
        RestoreBehaviour(playerAttack, playerAttackWasEnabled);
        SetBehaviourEnabled(enemyMovement, false);
        SetBehaviourEnabled(enemyAttack, false);
        if (pauseButton) pauseButton.interactable = true;
        if (controlsHintCanvasGroup) controlsHintCanvasGroup.alpha = 0;
        displayedCentiseconds = -1;
        RefreshBattleTimer();
        GetComponent<SideAAudio>()?.StartMusic();
    }

    public void BeginTutorialPractice()
    {
        if (SideWorldTravel.Instance && SideWorldTravel.Instance.Busy) return;
        if (IsTutorialActive)
        {
            if (helpOpen) SetHelpOpen(false);
            if (manualPaused) ResumeBattle();
            Tutorial.RetryStep();
            return;
        }
        if (selectionOpen)
        {
            if (helpOpen) SetHelpOpen(false);
            BeginSelectedTutorial();
            return;
        }
        tutorialReplayRequested = true;
        BattleTrialMenu.ClearReplay();
        ReloadBattle();
    }

    public bool TryHandleTutorialRingOut(GameObject fighter) =>
        IsTutorialActive && Tutorial.HandleRingOut(fighter);

    public void FinishTutorialAndStartTrial()
    {
        if (!IsTutorialActive || SideWorldTravel.Instance && SideWorldTravel.Instance.Busy) return;
        // Close the action gate before EndSession so no final training frame
        // can create an attack, stock loss, result, or queued host reward.
        battleActive = false;
        Tutorial.EndSession();
        SetBehaviourEnabled(playerController, false);
        SetBehaviourEnabled(playerAttack, false);
        SetBehaviourEnabled(enemyMovement, false);
        SetBehaviourEnabled(enemyAttack, false);
        tutorialReplayRequested = false;
        BattleTrialMenu.QueueReplay();
        ReloadBattle();
    }

    private void ApplyPauseState()
    {
        bool nextPaused = manualPaused || helpOpen || selectionOpen;
        if (nextPaused && !isPaused)
        {
            // Programmatic and pointer-opened menus share the keyboard safety
            // boundary. Releasing a held charge while paused cannot queue a shot.
            if (playerFighter) playerFighter.GetComponent<BattleSpecialMoves>()?.CancelUnreleasedCharge();
            pauseResumeTimeScale = Mathf.Max(.01f, BattleCombatFeedback.UnpausedTimeScale);
            Time.timeScale = 0f;
        }
        else if (!nextPaused && isPaused)
            Time.timeScale = pauseResumeTimeScale;
        isPaused = nextPaused;
        GetComponent<SideAAudio>()?.SetPaused(nextPaused);

        if (pauseOverlay != null)
            pauseOverlay.SetActive(!trialMenu && manualPaused && !helpOpen);
    }

    void OnGUI()
    {
        if (trialMenu) return;
        if (!helpOpen && !gameOver) return;
        Matrix4x4 oldMatrix = GUI.matrix;
        int oldDepth = GUI.depth;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        if (scale <= 0f) return;
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960f * scale) * .5f,
            (Screen.height - 540f * scale) * .5f, 0), Quaternion.identity, Vector3.one * scale);
        GUI.depth = -100;
        PrepareHelpStyles();
        if (helpOpen) DrawHelp();
        else if (SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway && !SideWorldTravel.Instance.IsSideWorldB)
            DrawResultActions();
        GUI.matrix = oldMatrix;
        GUI.depth = oldDepth;
    }

    void PrepareHelpStyles()
    {
        if (helpTitleStyle != null) return;
        helpTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        helpTitleStyle.normal.textColor = new Color32(238, 248, 248, 255);
        helpTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        helpTextStyle.normal.textColor = new Color32(225, 237, 243, 255);
        helpButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
    }

    void DrawHelp()
    {
        Color oldColor = GUI.color;
        GUI.color = new Color(0f, .04f, .07f, .72f);
        GUI.DrawTexture(new Rect(0, 0, 960, 540), Texture2D.whiteTexture);
        GUI.color = new Color32(23, 46, 65, 255);
        GUI.DrawTexture(new Rect(220, 28, 520, 484), Texture2D.whiteTexture);
        GUI.color = oldColor;
        GUI.Label(new Rect(245, 45, 475, 34), awaitingIntroduction ? "SIDE A — BEFORE YOU START" : "SIDE A — HELP / PAUSED", helpTitleStyle);
        GUI.Label(new Rect(245, 86, 475, 28), "YOU ARE THE BLUE FIGHTER", helpTextStyle);
        GUI.Label(new Rect(245, 120, 470, 66),
            "WIN: knock the red opponent off the stage.\nDamage % is NOT health. Higher % means further knockback.\nYou can lose by falling off even at 0%.", helpTextStyle);
        GUI.Label(new Rect(245, 190, 470, 178),
            "A / D  Move   ·   Space  Jump (again in air)\nLEFT CLICK / J  Four-hit combo (A / D may stay held)\nW + LEFT  Uppercut   ·   S + LEFT  Low sweep\nRIGHT CLICK  Hold / release: longer, wider beam\nA / D + RIGHT  Dash kick   ·   W + RIGHT  Air leap\nS + RIGHT  Counter (ground, front hits only)\nS + Space  Drop   ·   H  Help / pause   ·   R  Retry\nJ / K still work. Mouse position does NOT aim.", helpTextStyle);
        bool integrated = SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway;
        GUI.Label(new Rect(245, 372, 470, 42),
            "No direction + RIGHT charges the beam.\nHold W / A / S / D BEFORE clicking for another move.", helpTextStyle);
        GUI.Label(new Rect(245, 414, 470, 42), integrated
            ? "Win, return with B, then press Esc in the main world to claim Trial A's reward."
            : "Build damage with LEFT. Save a dash or air leap to recover. A missed counter leaves you open.", helpTextStyle);
        if (GUI.Button(new Rect(245, 466, 470, 34), awaitingIntroduction
            ? "READY — START [ENTER]" : "CLOSE HELP [H / ENTER]", helpButtonStyle))
        {
            if (awaitingIntroduction) ConfirmIntroduction();
            else SetHelpOpen(false);
        }
    }

    void DrawResultActions()
    {
        var travel = SideWorldTravel.Instance;
        bool oldEnabled = GUI.enabled;
        GUI.enabled = !travel.Busy;
        if (GUI.Button(new Rect(306, 425, 164, 34), "RETRY [R]", helpButtonStyle)) RestartBattle();
        if (GUI.Button(new Rect(482, 425, 198, 34), "MAIN WORLD [F8]", helpButtonStyle))
            travel.ReturnToMain();
        GUI.enabled = oldEnabled;
        var noteStyle = new GUIStyle(helpTextStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        Color oldColor = GUI.color;
        GUI.color = new Color(0.06f, .14f, .2f, .92f);
        GUI.DrawTexture(new Rect(282, 462, 426, 38), Texture2D.whiteTexture);
        GUI.color = oldColor;
        GUI.Label(new Rect(285, 464, 420, 36), playerWon
            ? "Trial A cleared. Return, then press Esc to claim your reward."
            : "Ring-out. Stay on the stage — even 0% can fall!", noteStyle);
    }

    void RefreshBattleTimer()
    {
        if (battleTimerText == null)
            return;

        if (IsTutorialActive)
        {
            if (battleTimerText.text != "PRACTICE") battleTimerText.text = "PRACTICE";
            return;
        }

        int totalCentiseconds = Mathf.FloorToInt(
            elapsedBattleTime * 100f
        );

        if (totalCentiseconds == displayedCentiseconds)
            return;

        displayedCentiseconds = totalCentiseconds;

        int minutes = totalCentiseconds / 6000;
        int seconds = totalCentiseconds / 100 % 60;
        int centiseconds = totalCentiseconds % 100;

        battleTimerText.text =
            $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }

    void PrepareControlsHint()
    {
        GameObject controlsHint = GameObject.Find("ControlsHint");

        if (controlsHint == null)
            return;

        controlsHintCanvasGroup =
            controlsHint.GetComponent<CanvasGroup>();
        controlsHintText=controlsHint.GetComponentInChildren<TMP_Text>(true);
        controlsHintSpecials=playerFighter ? playerFighter.GetComponent<BattleSpecialMoves>() : null;
        if(controlsHintText)
        {
            controlsHintText.enableAutoSizing=true;
            controlsHintText.fontSizeMin=11f;
            controlsHintText.fontSizeMax=24f;
            controlsHintText.raycastTarget=false;
        }

        if (controlsHintCanvasGroup == null)
        {
            controlsHintCanvasGroup =
                controlsHint.AddComponent<CanvasGroup>();
        }

        controlsHintElapsedTime = 0f;
        controlsHintCanvasGroup.alpha = 1f;
        controlsHintCanvasGroup.interactable = false;
        controlsHintCanvasGroup.blocksRaycasts = false;
        RefreshMouseControlsHint(false);
    }

    void UpdateControlsHint()
    {
        if (controlsHintCanvasGroup == null)
        {
            return;
        }

        controlsHintElapsedTime += Time.unscaledDeltaTime;
        bool compact = controlsHintElapsedTime >= ControlsHintVisibleDuration;
        RefreshMouseControlsHint(compact);

        float fadeElapsed =
            controlsHintElapsedTime - ControlsHintVisibleDuration;

        if (fadeElapsed <= 0f)
            return;

        // Leave a small, readable current-skill prompt after the opening hint.
        // This is especially useful while moving: A/D + right-click is a dash,
        // not a beam. Do not disappear after the first few seconds.
        controlsHintCanvasGroup.alpha = Mathf.Lerp(1f, .84f,
            Mathf.Clamp01(fadeElapsed / ControlsHintFadeDuration));
    }

    void RefreshMouseControlsHint(bool compact)
    {
        if (!controlsHintText) return;
        // The controller composes specials in Start. Manager.Start may run
        // first, so retry the cache once the player has finished composition.
        if (!controlsHintSpecials && playerFighter)
            controlsHintSpecials = playerFighter.GetComponent<BattleSpecialMoves>();
        var keyboard = Keyboard.current;
        var normal = playerAttack && playerAttack.IsJAttack
            ? playerAttack.CurrentJMove : BattleAttack.ReadJMove(keyboard);
        bool busy = controlsHintSpecials && controlsHintSpecials.IsBusy;
        var special = busy ? controlsHintSpecials.Kind : BattleSpecialMoves.ReadKind(keyboard);
        var phase = busy ? controlsHintSpecials.Phase : BattleSpecialPhase.Ready;
        bool unavailable = !busy && controlsHintSpecials && (
            (special == BattleSpecialKind.Rise && controlsHintSpecials.RecoverySpent) ||
            (special == BattleSpecialKind.Dash && controlsHintSpecials.DashSpent && playerController && !playerController.IsOnGround) ||
            (special == BattleSpecialKind.Counter && playerController && !playerController.IsOnGround));
        int signature = (int)normal | ((int)special << 3) | ((int)phase << 6) |
            (compact ? 1 << 10 : 0) | (unavailable ? 1 << 11 : 0);
        if (signature == displayedControlsHintSignature) return;
        displayedControlsHintSignature = signature;
        string normalName = normal == BattleJMove.Side ? "SIDE STRIKE" :
            normal == BattleJMove.Upper ? "UPPERCUT" :
            normal == BattleJMove.Low ? "LOW SWEEP" : "COMBO";
        string specialName = special == BattleSpecialKind.Dash ? "DASH KICK" :
            special == BattleSpecialKind.Rise ? "AIR LEAP" :
            special == BattleSpecialKind.Counter ? "COUNTER" : "HOLD / RELEASE BEAM";
        if (unavailable) specialName += special == BattleSpecialKind.Counter ? " (LAND FIRST)" : " (USED IN AIR)";
        else if (busy) specialName += phase == BattleSpecialPhase.Charging ? " (CHARGING)" :
            phase == BattleSpecialPhase.Recovery || phase == BattleSpecialPhase.Helpless ? " (RECOVERING)" : " (LOCKED)";
        string actionLine = "LEFT CLICK: " + normalName + "     <color=#A9EDFF>RIGHT CLICK: " + specialName + "</color>";
        controlsHintText.text = compact
            ? actionLine + "     H HELP / PAUSE     J / K BACKUP"
            : "A / D MOVE     SPACE JUMP     " + actionLine + "     H HELP";
    }

    void DisablePauseButton()
    {
        manualPaused = false;
        helpOpen = false;
        ApplyPauseState();

        if (pauseButton != null)
        {
            pauseButton.interactable = false;
        }
    }

    private void PrepareBattleIntro()
    {
        playerFighter = GameObject.FindGameObjectWithTag("Player");
        enemyFighter = GameObject.FindGameObjectWithTag("Enemy");

        if (playerFighter == null || enemyFighter == null)
        {
            Debug.LogWarning(
                "Battle intro was skipped because a fighter is missing."
            );
            battleActive = true;
            return;
        }

        playerBody = playerFighter.GetComponent<Rigidbody>();
        enemyBody = enemyFighter.GetComponent<Rigidbody>();

        if (playerBody == null || enemyBody == null)
        {
            Debug.LogWarning(
                "Battle intro was skipped because both fighters need a Rigidbody."
            );
            battleActive = true;
            return;
        }

        playerController =
            playerFighter.GetComponent<BattlePlayerController>();
        playerAttack = playerFighter.GetComponent<BattleAttack>();
        enemyMovement = enemyFighter.GetComponent<EnemyMovement>();
        enemyAttack = enemyFighter.GetComponent<EnemyAttack>();

        playerControllerWasEnabled =
            playerController != null && playerController.enabled;
        playerAttackWasEnabled =
            playerAttack != null && playerAttack.enabled;
        enemyMovementWasEnabled =
            enemyMovement != null && enemyMovement.enabled;
        enemyAttackWasEnabled =
            enemyAttack != null && enemyAttack.enabled;

        SetBehaviourEnabled(playerController, false);
        SetBehaviourEnabled(playerAttack, false);
        SetBehaviourEnabled(enemyMovement, false);
        SetBehaviourEnabled(enemyAttack, false);

        originalPlayerPosition = playerBody.position;
        originalEnemyPosition = enemyBody.position;
        var arenaStages = GetComponent<BattleArenaStages>();
        if (arenaStages)
        {
            if (!playerFighter.GetComponent<BattleStockLives>()) playerFighter.AddComponent<BattleStockLives>();
            if (!enemyFighter.GetComponent<BattleStockLives>()) enemyFighter.AddComponent<BattleStockLives>();
            Physics.SyncTransforms();
            Vector3 left = BattleArenaStages.StarCourtPlatformPosition(0);
            Vector3 right = BattleArenaStages.StarCourtPlatformPosition(2);
            playerIntroPosition = AirIntroPosition(playerBody, left, 2.2f);
            enemyIntroPosition = AirIntroPosition(enemyBody, right, 2.2f);
            originalPlayerPosition = AirIntroPosition(playerBody, left, 0);
            originalEnemyPosition = AirIntroPosition(enemyBody, right, 0);
        }
        originalPlayerCollisionMode =
            playerBody.collisionDetectionMode;
        originalEnemyCollisionMode =
            enemyBody.collisionDetectionMode;

        playerAnimator =
            playerFighter.GetComponentInChildren<Animator>();
        enemyAnimator =
            enemyFighter.GetComponentInChildren<Animator>();

        SuspendFighter(
            playerFighter,
            playerBody,
            playerAnimator,
            playerIntroPosition
        );

        SuspendFighter(
            enemyFighter,
            enemyBody,
            enemyAnimator,
            enemyIntroPosition
        );

        Physics.SyncTransforms();
        introPrepared = true;
        battleActive = false;
    }

    private static Vector3 AirIntroPosition(Rigidbody body, Vector3 platformTop, float hoverHeight)
    {
        var capsule = body.GetComponent<CapsuleCollider>();
        float footOffset = capsule ? body.position.y - capsule.bounds.min.y : 1f;
        return platformTop + Vector3.up * (footOffset + .02f + hoverHeight);
    }

    private static void SuspendFighter(
        GameObject fighter,
        Rigidbody fighterBody,
        Animator fighterAnimator,
        Vector3 introPosition
    )
    {
        fighterBody.linearVelocity = Vector3.zero;
        fighterBody.angularVelocity = Vector3.zero;
        fighterBody.isKinematic = true;
        fighterBody.detectCollisions = true;
        fighterBody.position = introPosition;

        if (fighterAnimator == null)
            return;

        fighterAnimator.applyRootMotion = false;
        ResetAnimatorTriggers(fighterAnimator);
        SetAnimatorBoolIfPresent(
            fighterAnimator,
            IsMovingHash,
            false
        );
        SetAnimatorBoolIfPresent(
            fighterAnimator,
            IsWalkingHash,
            false
        );
        SetAnimatorBoolIfPresent(
            fighterAnimator,
            IsGroundedHash,
            true
        );

        FaceCamera(fighter);
        SetAnimatorTriggerIfPresent(fighterAnimator, YesTriggerHash);
    }

    private IEnumerator PlayBattleIntro()
    {
        while (SideALoadingScreen.IsVisible) yield return null;
        GetComponent<SideAAudio>()?.StartMusic();
        elapsedBattleTime = 0f;
        displayedCentiseconds = -1;
        RefreshBattleTimer();

        if (GetComponent<BattleArenaStages>())
        {
            introPresentation=GetComponent<BattleIntroPresentation>();
            if(!introPresentation) introPresentation=gameObject.AddComponent<BattleIntroPresentation>();
            introPresentation.Begin(playerFighter.transform,enemyFighter.transform,countdownText,Mathf.Max(1.1f,countdownStepDuration));
        }
        float introStep=introPresentation ? Mathf.Max(1.1f,countdownStepDuration) : countdownStepDuration;
        yield return AnimateCountdownValue(
            "3",
            new Color32(104, 211, 255, 255),
            introStep
        );

        yield return AnimateCountdownValue(
            "2",
            new Color32(255, 214, 92, 255),
            introStep
        );

        yield return AnimateCountdownValue(
            "1",
            new Color32(255, 104, 118, 255),
            introStep
        );

        introPresentation?.End();
        HideCountdownText();
        ReleaseIntroFighters();

        yield return WaitForIntroLanding();

        if (landingSettleDuration > 0f)
        {
            yield return new WaitForSeconds(landingSettleDuration);
        }

        BeginActiveBattle();

        yield return AnimateCountdownValue(
            "GO!",
            new Color32(103, 239, 158, 255),
            goDisplayDuration
        );

        HideCountdownText();
        introCoroutine = null;
    }

    private IEnumerator AnimateCountdownValue(
        string value,
        Color colour,
        float duration
    )
    {
        float safeDuration = Mathf.Max(0.1f, duration);
        GetComponent<SideAAudio>()?.Countdown(value == "GO!");

        if (countdownText == null)
        {
            float remaining = safeDuration;
            while (remaining > 0f)
            {
                if (!isPaused) remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
            yield break;
        }

        countdownText.gameObject.SetActive(true);
        countdownText.text = value;
        countdownText.transform.SetAsLastSibling();

        float elapsed = 0f;

        while (elapsed < safeDuration)
        {
            if (isPaused) { yield return null; continue; }
            float progress = Mathf.Clamp01(elapsed / safeDuration);
            if(value=="3" || value=="2" || value=="1") introPresentation?.Tick(value,progress);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            float scale = Mathf.Lerp(1.5f, 0.88f, easedProgress);
            float alpha = progress < 0.7f
                ? 1f
                : Mathf.InverseLerp(1f, 0.7f, progress);

            countdownText.rectTransform.localScale =
                Vector3.one * scale;
            countdownText.color = new Color(
                colour.r,
                colour.g,
                colour.b,
                alpha
            );

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void ReleaseIntroFighters()
    {
        BeginFighterDrop(
            playerFighter,
            playerBody,
            playerAnimator,
            1f
        );

        BeginFighterDrop(
            enemyFighter,
            enemyBody,
            enemyAnimator,
            -1f
        );
    }

    private void BeginFighterDrop(
        GameObject fighter,
        Rigidbody fighterBody,
        Animator fighterAnimator,
        float facingDirection
    )
    {
        FaceDirection(fighter, facingDirection);
        ResetAnimatorTriggers(fighterAnimator);
        SetAnimatorBoolIfPresent(
            fighterAnimator,
            IsGroundedHash,
            false
        );
        SetAnimatorTriggerIfPresent(fighterAnimator, JumpTriggerHash);

        fighterBody.isKinematic = false;
        fighterBody.detectCollisions = true;
        fighterBody.useGravity = true;
        fighterBody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        fighterBody.linearVelocity = new Vector3(
            0f,
            -Mathf.Max(0f, introInitialFallSpeed),
            0f
        );
        fighterBody.WakeUp();
    }

    private IEnumerator WaitForIntroLanding()
    {
        WaitForFixedUpdate waitForFixedUpdate =
            new WaitForFixedUpdate();

        float elapsed = 0f;
        bool playerLanded = false;
        bool enemyLanded = false;
        float safeTimeout = Mathf.Max(0.1f, maximumLandingWait);

        while (elapsed < safeTimeout)
        {
            ApplyIntroFallGravity(playerBody, playerLanded);
            ApplyIntroFallGravity(enemyBody, enemyLanded);

            yield return waitForFixedUpdate;

            playerLanded = IsFighterGrounded(playerBody);
            enemyLanded = IsFighterGrounded(enemyBody);

            if (playerLanded && enemyLanded)
                break;

            elapsed += Time.fixedDeltaTime;
        }

        if (!playerLanded)
        {
            RecoverFighter(playerBody, originalPlayerPosition);
        }

        if (!enemyLanded)
        {
            RecoverFighter(enemyBody, originalEnemyPosition);
        }

        FinishFighterLanding(
            playerBody,
            playerAnimator,
            originalPlayerCollisionMode
        );

        FinishFighterLanding(
            enemyBody,
            enemyAnimator,
            originalEnemyCollisionMode
        );

        Physics.SyncTransforms();
    }

    private void ApplyIntroFallGravity(
        Rigidbody fighterBody,
        bool isGrounded
    )
    {
        if (fighterBody == null ||
            isGrounded ||
            fighterBody.linearVelocity.y >= 0f)
        {
            return;
        }

        float extraGravity =
            Mathf.Max(1f, introFallGravityMultiplier) - 1f;

        fighterBody.AddForce(
            Physics.gravity * extraGravity,
            ForceMode.Acceleration
        );
    }

    private static bool IsFighterGrounded(Rigidbody fighterBody)
    {
        if (fighterBody == null ||
            fighterBody.linearVelocity.y > 0.1f)
        {
            return false;
        }

        return Physics.Raycast(
            fighterBody.position,
            Vector3.down,
            GroundCheckDistance,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }

    private static void RecoverFighter(
        Rigidbody fighterBody,
        Vector3 safePosition
    )
    {
        fighterBody.position = safePosition;
        fighterBody.linearVelocity = Vector3.zero;
        fighterBody.angularVelocity = Vector3.zero;
    }

    private static void FinishFighterLanding(
        Rigidbody fighterBody,
        Animator fighterAnimator,
        CollisionDetectionMode originalCollisionMode
    )
    {
        fighterBody.linearVelocity = Vector3.zero;
        fighterBody.angularVelocity = Vector3.zero;
        fighterBody.collisionDetectionMode = originalCollisionMode;

        SetAnimatorBoolIfPresent(
            fighterAnimator,
            IsGroundedHash,
            true
        );
    }

    private void BeginActiveBattle()
    {
        elapsedBattleTime = 0f;
        displayedCentiseconds = -1;
        RefreshBattleTimer();

        battleActive = true;

        RestoreBehaviour(
            playerController,
            playerControllerWasEnabled
        );
        RestoreBehaviour(playerAttack, playerAttackWasEnabled);
        RestoreBehaviour(enemyMovement, enemyMovementWasEnabled);
        RestoreBehaviour(enemyAttack, enemyAttackWasEnabled);

        if (pauseButton != null)
        {
            pauseButton.interactable = true;
        }
    }

    private void HideCountdownText()
    {
        if (countdownText == null)
            return;

        countdownText.rectTransform.localScale = Vector3.one;
        Color colour = countdownText.color;
        colour.a = 1f;
        countdownText.color = colour;
        countdownText.gameObject.SetActive(false);
    }

    private void StopBattleIntroPresentation()
    {
        introPresentation?.End();
        if (introCoroutine != null)
        {
            StopCoroutine(introCoroutine);
            introCoroutine = null;
        }

        HideCountdownText();
    }

    private static void FaceCamera(GameObject fighter)
    {
        BattleFighterUtility.FaceCamera(fighter, FrontFacingRotationY);
    }

    private static void FaceDirection(
        GameObject fighter,
        float direction
    )
    {
        BattleFighterUtility.FaceDirection(fighter, direction);
    }

    private static Transform GetVisualRoot(GameObject fighter)
    {
        return BattleFighterUtility.GetVisualRoot(fighter);
    }

    private static void SetAnimatorBoolIfPresent(
        Animator animator,
        int parameterHash,
        bool value
    )
    {
        BattleAnimatorUtility.SetBoolIfPresent(
            animator,
            parameterHash,
            value
        );
    }

    private static void SetAnimatorTriggerIfPresent(
        Animator animator,
        int triggerHash
    )
    {
        BattleAnimatorUtility.SetTriggerIfPresent(animator, triggerHash);
    }

    private static void ResetAnimatorTriggers(Animator animator)
    {
        BattleAnimatorUtility.ResetTriggers(animator);
    }

    private static void SetBehaviourEnabled(
        Behaviour behaviour,
        bool isEnabled
    )
    {
        if (behaviour != null)
        {
            behaviour.enabled = isEnabled;
        }
    }

    private static void RestoreBehaviour(
        Behaviour behaviour,
        bool wasEnabled
    )
    {
        if (behaviour != null)
        {
            behaviour.enabled = wasEnabled;
        }
    }

    public void RestartBattle()
    {
        if (IsTutorialActive) { Tutorial.RetryStep(); return; }
        tutorialReplayRequested = false;
        if (trialMenu) BattleTrialMenu.QueueReplay();
        ReloadBattle();
    }

    public void ReturnToTrialSelection()
    {
        tutorialReplayRequested = false;
        BattleTrialMenu.ClearReplay();
        ReloadBattle();
    }

    void ReloadBattle()
    {
        battleActive = false;
        Tutorial?.EndSession();
        StopBattleIntroPresentation();
        Time.timeScale = 1f;

        // An additive trial must retry without destroying its suspended host.
        if (SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway)
        {
            SideWorldTravel.Instance.RetryBattle();
            return;
        }

        SideALoadingScreen.Reload(SceneManager.GetActiveScene().name);
    }

    void OnDestroy()
    {
        if (selectionLoading) selectionLoading.Abort();
        Tutorial?.EndSession();
        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(PauseBattle);
        }

        if (closePauseButton != null)
        {
            closePauseButton.onClick.RemoveListener(ResumeBattle);
        }

        if (isPaused)
        {
            Time.timeScale = 1f;
        }
    }
}
