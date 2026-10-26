using UnityEngine;
using UnityEngine.InputSystem;

// New-journey introduction; the guide still owns the real sword/task interaction.
[RequireComponent(typeof(BaselineWorld), typeof(BaselineFlow))]
[DefaultExecutionOrder(600)] // Observe movement and Vitals (500) after their real simulation.
public sealed class BaselineCampTutorial : MonoBehaviour
{
    // Append only: existing scene/test references retain their original values.
    public enum Lesson { Look = 0, Move = 1, Approach = 2, Talk = 3, Vista = 4, Track = 5, Swim = 6, ReachIsland = 7,
        Jump = 8, Drop = 9, Heal = 10, RecoverStamina = 11, Climb = 12 }
    [SerializeField] Vector3 arrival = new Vector3(-22, 1.05f, -72);
    [SerializeField] Vector3 destination = new Vector3(0, 1.05f, -43);
    [SerializeField, Min(.5f)] float arrivalRadius = 2;
    [SerializeField] NPCDialogue guide;
    [SerializeField] Vector3[] approach = { new Vector3(-22,1,-66), new Vector3(-12,1,-62) };
    public bool Active { get; private set; }
    public bool CardOpen { get; private set; }
    public BaselineOpeningCamera CameraTour { get; private set; }
    public bool TourPlaying => CameraTour && CameraTour.IsPlaying;
    public bool ModalOpen => CardOpen || TourPlaying;
    public int LastSkipInputFrame { get; private set; } = -1;
    bool tourSeen;
    float nearGuideTime;
    public Lesson CurrentLesson { get; private set; }
    public bool JumpPracticed { get; private set; }
    public bool FallPracticed { get; private set; }
    public bool FieldAidPracticed { get; private set; }
    public bool StaminaRestPracticed { get; private set; }
    public bool ClimbPracticed { get; private set; }
    public bool DropAttemptArmed => dropArmed;
    public bool ClimbMotionObserved => climbSeconds > 0;
    public bool MantleMotionObserved => mantleSeen;
    public Vector3 Destination => destination;
    public Vector3 CurrentTarget
    {
        get
        {
            if (layout)
            {
                if (CurrentLesson == Lesson.Jump) return layout.JumpPracticePosition;
                if (CurrentLesson == Lesson.Drop) return FallPracticed || dropArmed ? layout.DropLandingPosition : layout.DropPlatformPosition;
                if (CurrentLesson == Lesson.Heal)
                    return vitals && vitals.AidCharges == 0 ? layout.CampPosition :
                        health && health.CurrentHealth == health.MaxHealth ? layout.DropPlatformPosition : layout.DropLandingPosition;
                if (CurrentLesson == Lesson.RecoverStamina) return layout.IslandRestPosition;
                if (CurrentLesson == Lesson.Climb) return climbSeconds > 0 ? layout.ClimbTopPosition : layout.ClimbBasePosition;
                if (CurrentLesson == Lesson.Track)
                    return !world.accepted[1] && islandClue ? islandClue.transform.position : layout.IntroShorePosition;
                if (CurrentLesson == Lesson.Swim)
                    return reachedDepartureShore || swimSeconds > 0
                        ? layout.IslandRestPosition : layout.IntroShorePosition;
                if (CurrentLesson == Lesson.ReachIsland)
                    return world.objectives != null && world.objectives.Length > 1 && world.objectives[1]
                        ? world.objectives[1].position : layout.IslandDestination;
            }
            return CurrentLesson == Lesson.Vista ? destination :
                CurrentLesson == Lesson.Approach && waypoint < approach.Length ? approach[waypoint] :
                guide ? guide.transform.position : destination;
        }
    }
    public RenderTexture PreviewTexture => preview ? preview.Texture : null;
    BaselineWorld world;
    BaselineFlow flow;
    PlayerMovement movement;
    PlayerVitals vitals;
    Health health;
    ThirdPersonCamera look;
    DialogueUI dialogue;
    BaselineTutorialPreview preview;
    OverworldV2Layout layout;
    BaselineInteraction islandClue;
    LineRenderer marker;
    GUIStyle title, body, small;
    int waypoint;
    float distanceWalked, angleTurned;
    Vector3 previousPosition;
    Quaternion previousLook;
    bool spokeToGuide;
    float swimSeconds;
    bool reachedDepartureShore, reachedIslandShore;
    bool suspendedForTravel;
    bool PracticeReady => layout && layout.TutorialPracticeReady && vitals && health;
    bool jumpArmed, jumpRose, dropArmed, dropAirborne, fallDamageObserved, mantleSeen;
    float jumpStartY, jumpPeak, jumpStartedAt, dropPeak, climbSeconds;
    int lastAidCharges, healEventFrame = -100, rInputFrame = -100;
    Vector3 actionPreviousPosition;
    bool actionPositionKnown, actionWasGrounded;
    bool OtherUI => flow.TitleScreenVisible || flow.MenuOpen || flow.VictoryOpen || world.JournalOpen ||
        (flow.rewards && flow.rewards.IsOpen) ||
        (dialogue && (dialogue.IsDialogueActive || dialogue.IsRewardPresentationActive));
    bool Visible => Active && flow.Started && !OtherUI &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy));
    void Awake()
    {
        world = GetComponent<BaselineWorld>(); flow = GetComponent<BaselineFlow>();
        movement = world.player.GetComponent<PlayerMovement>();
        vitals = world.player.GetComponent<PlayerVitals>(); health = world.player.GetComponent<Health>();
        if (health) { health.Damaged += OnPlayerDamaged; health.Died += OnPlayerDied; health.HealthChanged += OnHealthChanged; }
        dialogue = FindFirstObjectByType<DialogueUI>(); look = Camera.main.GetComponent<ThirdPersonCamera>();
        if (guide) guide.DialogueCompleted += OnGuideCompleted;
    }
    void OnGuideCompleted(DialogueUI ui) { if (Active) spokeToGuide = true; }
    public void Begin()
    {
        Cancel();
        layout = GetComponent<OverworldV2Layout>();
        if (layout)
        {
            arrival = layout.ArrivalPosition; destination = layout.IslandDestination;
            var approachPoint = Vector3.Lerp(arrival, layout.CampPosition, .45f);
            // The raised approach need not match a straight height interpolation.
            // Keep its authored XZ while following the actual walking surface.
            if (world.NavigationGround(approachPoint, out var approachGround))
                approachPoint.y = approachGround.y - .1f;
            approach = new[] { approachPoint, layout.CampPosition };
            islandClue = null;
            foreach (var item in FindObjectsByType<BaselineInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item.gameObject.scene == gameObject.scene && item.action == BaselineInteraction.ActionKind.AcceptTask && item.index == 1)
                { islandClue = item; break; }
        }
        // The V2 destination is a solid inscription prop. Validate its landing,
        // not a standing capsule inside the interaction's visual collider.
        var safeDestination = layout ? layout.IslandRestPosition : destination;
        if (!guide || !world.NavigationGround(arrival, out var ground) || !world.NavigationGround(safeDestination, out var end))
        { Debug.LogWarning("Opening introduction needs a guide and safe arrival/vista ground.", this); return; }
        if (!layout) destination.y = end.y - .1f;
        movement.Relocate(new Vector3(arrival.x, ground.y-.1f, arrival.z), Quaternion.identity);
        look.ResetOrbit(0, 14);
        waypoint = 0; distanceWalked = angleTurned = nearGuideTime = swimSeconds = 0;
        spokeToGuide = tourSeen = reachedDepartureShore = reachedIslandShore = false;
        JumpPracticed = FallPracticed = FieldAidPracticed = StaminaRestPracticed = ClimbPracticed = false;
        ResetActionAttempt();
        Active = true; ShowLesson(Lesson.Look);
    }
    void ShowLesson(Lesson lesson)
    {
        CurrentLesson = lesson; CardOpen = true; flow.HoldTutorialCard();
        if (!preview) preview = gameObject.AddComponent<BaselineTutorialPreview>();
        preview.Show(world.player.GetComponentInChildren<Animator>(), lesson);
        if (marker) marker.enabled = false;
    }
    public void StartPractice()
    {
        if (!Active || !CardOpen) return;
        CardOpen = false; if (preview) preview.Hide();
        previousPosition = world.player.position; previousLook = look.transform.rotation;
        ResetActionAttempt();
        flow.ReleaseTutorialCard();
    }
    public void Cancel()
    {
        suspendedForTravel = false;
        bool wasCard = CardOpen; Active = CardOpen = false; nearGuideTime = 0;
        ResetActionAttempt();
        if (CameraTour) CameraTour.Abort();
        if (marker) marker.enabled = false; if (preview) preview.Hide();
        if (wasCard && flow) flow.ReleaseTutorialCard();
    }
    public void Skip()
    {
        if (!Active) return;
        Cancel();
        if (layout)
        {
            bool saved = flow.CompleteOpeningTutorial();
            world.ShowNotice(saved ? "Introduction skipped. Explore freely. The guide and practice sword remain available." :
                "Introduction skipped. Saving failed; autosave will retry.");
        }
        else world.ShowNotice("Introduction skipped. The guide and practice sword are still available.");
    }
    static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z), new Vector2(b.x,b.z));
    void AfterGuide()
    {
        if (layout) ShowLesson(PracticeReady ? Lesson.Jump : Lesson.Track);
        else CurrentLesson = Lesson.Vista;
    }
    void FinishIslandIntroduction()
    {
        Cancel();
        bool saved = flow.CompleteOpeningTutorial();
        world.ShowNotice(saved ? "Island discovery complete. Explore the shores and use the stone water-access steps." :
            "Island discovery complete. Saving failed; autosave will retry.");
    }
    public bool TryInterfaceBack()
    {
        if(!Visible)return false;
        if(TourPlaying){LastSkipInputFrame=Time.frameCount;CameraTour.Complete();return true;}
        if(CardOpen){LastSkipInputFrame=Time.frameCount;StartPractice();return true;}
        return false;
    }
    void Update()
    {
        var position = world.player.position; var rotation = look.transform.rotation;
        float moved = Vector3.ProjectOnPlane(position-previousPosition, Vector3.up).magnitude;
        float turned = Quaternion.Angle(rotation,previousLook);
        previousPosition = position; previousLook = rotation;
        if (marker) marker.enabled = Visible && !ModalOpen && CurrentLesson >= Lesson.Approach;
        if (!Visible) { nearGuideTime = 0; ResetActionAttempt(); return; }
        var keys = Keyboard.current;
        if (keys != null && keys.tabKey.wasPressedThisFrame) { LastSkipInputFrame = Time.frameCount; Skip(); return; }
        if (TourPlaying)
        {
            if (keys != null && keys.escapeKey.wasPressedThisFrame)
            { LastSkipInputFrame = Time.frameCount; CameraTour.Complete(); }
            return;
        }
        if (CardOpen)
        {
            if (keys != null && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame)) StartPractice();
            return;
        }
        if (movement.IsGameplayInputLocked || Time.timeScale <= 0) { nearGuideTime = 0; ResetActionAttempt(); return; }
        if (keys != null && keys.hKey.wasPressedThisFrame) rInputFrame = Time.frameCount;
        if (layout && PlanarDistance(position, layout.IntroShorePosition) < 4) reachedDepartureShore = true;
        if (layout && movement.SwimmingActive && PlanarDistance(position, layout.IslandRestPosition) < 28 &&
            PlanarDistance(position, layout.IntroShorePosition) < 32) swimSeconds += Time.deltaTime;
        if (layout && swimSeconds >= .35f && movement.IsStableGrounded && !movement.SwimmingActive &&
            PlanarDistance(position, layout.IslandRestPosition) <= 6 && position.y >= layout.IslandRestPosition.y - .4f)
            reachedIslandShore = true;
        if (spokeToGuide && GameSession.Instance && GameSession.Instance.HasSword &&
            (CurrentLesson == Lesson.Talk || (CurrentLesson == Lesson.Approach && tourSeen)))
        { AfterGuide(); if (CardOpen) return; }
        switch (CurrentLesson)
        {
            case Lesson.Look:
                if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > .1f && turned < 35) angleTurned += turned;
                if (angleTurned >= 25 && movement.CanStartInteraction) ShowLesson(Lesson.Move);
                break;
            case Lesson.Move:
                bool moving = keys != null && (keys.wKey.isPressed || keys.aKey.isPressed || keys.sKey.isPressed || keys.dKey.isPressed);
                if (moving && moved < 1.2f && movement.IsStableGrounded) distanceWalked += moved;
                if (distanceWalked >= 4) CurrentLesson = Lesson.Approach;
                break;
            case Lesson.Approach:
                bool ready = Vector3.Distance(position,guide.transform.position) < 4 && movement.CanStartInteraction &&
                    !movement.SwimmingActive && !movement.ClimbingActive && !movement.MantlingActive;
                nearGuideTime = ready ? nearGuideTime + Time.deltaTime : 0;
                if (nearGuideTime >= .4f)
                {
                    if (tourSeen) ShowLesson(Lesson.Talk);
                    else
                    {
                        if (!CameraTour) CameraTour = gameObject.AddComponent<BaselineOpeningCamera>();
                        tourSeen = CameraTour.Play(flow, () => {
                            if (!Active) return;
                            if (spokeToGuide && GameSession.Instance && GameSession.Instance.HasSword) AfterGuide();
                            else ShowLesson(Lesson.Talk);
                        }, guide.transform);
                        if (!tourSeen) ShowLesson(Lesson.Talk);
                    }
                    break;
                }
                if (waypoint < approach.Length && Vector3.Distance(position,CurrentTarget) < 2.3f) waypoint++;
                UpdateMarker(); break;
            case Lesson.Talk: UpdateMarker(); break;
            case Lesson.Track:
                UpdateMarker();
                // Only an explicit journal choice can set Tracked. An already completed
                // island task cannot be tracked again, so acknowledge prior discovery.
                if ((world.Tracked == 1 || world.completed[1]) && movement.CanStartInteraction) ShowLesson(Lesson.Swim);
                break;
            case Lesson.Swim:
                UpdateMarker();
                if (reachedIslandShore && movement.CanStartInteraction)
                {
                    if (PracticeReady) ShowLesson(Lesson.RecoverStamina);
                    else CurrentLesson = Lesson.ReachIsland;
                }
                break;
            case Lesson.Jump:
            case Lesson.Drop:
            case Lesson.Heal:
            case Lesson.RecoverStamina:
            case Lesson.Climb:
                UpdateMarker(); break;
            case Lesson.ReachIsland:
                UpdateMarker();
                if (reachedIslandShore && world.visited[1] && world.completed[1] && movement.IsStableGrounded &&
                    PlanarDistance(position, CurrentTarget) <= arrivalRadius + 1) FinishIslandIntroduction();
                break;
            case Lesson.Vista:
                UpdateMarker();
                if (movement.IsStableGrounded && Vector3.Distance(position,destination) <= arrivalRadius)
                { Cancel(); world.ShowNotice("The lake is yours to explore. Open J when you want to track a clue."); }
                break;
        }
    }
    // Events carry evidence only. In particular, never pause inside Damaged:
    // Vitals must finish its landing/death/field-aid work first.
    void OnPlayerDamaged(Health target, DamageInfo damage)
    {
        if (!Active || !PracticeReady || CardOpen || health.IsDead || damage.Source ||
            !dropArmed || !dropAirborne || !movement.IsStableGrounded || movement.SwimmingActive ||
            movement.ClimbingActive || movement.MantlingActive) return;
        if (PlanarDistance(world.player.position, layout.DropLandingPosition) <= 6 &&
            dropPeak - world.player.position.y > 4)
            fallDamageObserved = true;
    }
    void OnPlayerDied(Health target, DamageInfo damage) { ResetActionAttempt(); }
    void OnHealthChanged(Health target, int before, int after)
    {
        // TryHeal raises this before Vitals decrements charges. Compare charges
        // in our later LateUpdate, including aid used on the very landing frame.
        if (Active && after > before && !ModalOpen && !movement.IsGameplayInputLocked)
            healEventFrame = Time.frameCount;
    }
    void ResetActionAttempt()
    {
        jumpArmed = jumpRose = dropArmed = dropAirborne = fallDamageObserved = mantleSeen = false;
        climbSeconds = 0; healEventFrame = rInputFrame = -100;
        if (vitals) lastAidCharges = vitals.AidCharges;
        actionPositionKnown = false; actionWasGrounded = false;
    }
    void LateUpdate()
    {
        if (!Active || !PracticeReady) return;
        if (!Visible || ModalOpen || !vitals.SimulationActive || health.IsDead)
        { ResetActionAttempt(); return; }
        Vector3 position = world.player.position;
        // Death/recall/room returns cannot count as jumps, drops or pull-ups.
        // Ordinary travel is also cleared by OnDisable/OnEnable below.
        if (actionPositionKnown && Vector3.Distance(position, actionPreviousPosition) > Mathf.Max(2.5f, Time.deltaTime * 12))
        { ResetActionAttempt(); actionPreviousPosition = position; actionPositionKnown = true; return; }
        actionPreviousPosition = position; actionPositionKnown = true;
        bool grounded = movement.IsStableGrounded && !movement.SwimmingActive && !movement.ClimbingActive && !movement.MantlingActive;
        bool usedAid = vitals.LastFieldAidUseFrame == Time.frameCount && vitals.AidCharges < lastAidCharges;
        lastAidCharges = vitals.AidCharges;
        if (usedAid && (FallPracticed || fallDamageObserved)) FieldAidPracticed = true;

        switch (CurrentLesson)
        {
            case Lesson.Jump:
                bool inJumpArea = PlanarDistance(position, layout.JumpPracticePosition) <= 8;
                if (!inJumpArea || movement.SwimmingActive || movement.ClimbingActive || movement.MantlingActive)
                { jumpArmed = jumpRose = false; break; }
                if (!jumpArmed && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame &&
                    (grounded || actionWasGrounded))
                {
                    jumpArmed = true; jumpRose = false; jumpStartY = jumpPeak = position.y; jumpStartedAt = Time.time;
                }
                if (jumpArmed)
                {
                    jumpPeak = Mathf.Max(jumpPeak, position.y);
                    if (!grounded && jumpPeak > jumpStartY + .45f) jumpRose = true;
                    if (jumpRose && grounded && Time.time - jumpStartedAt > .2f)
                    { JumpPracticed = true; ShowLesson(Lesson.Drop); }
                    else if (Time.time - jumpStartedAt > 5) jumpArmed = jumpRose = false;
                }
                break;
            case Lesson.Drop:
                // Landing beside the ledge compresses the gameplay camera. Keep
                // walking live until the player reaches the open landing marker.
                // The real damage has already counted; never require another fall.
                if (FallPracticed)
                {
                    if (PlanarDistance(position, layout.DropLandingPosition) <= 1 && movement.CanStartInteraction)
                        ShowLesson(FieldAidPracticed ? Lesson.Track : Lesson.Heal);
                    break;
                }
                if (!dropArmed && grounded && PlanarDistance(position, layout.DropPlatformPosition) < 2.7f &&
                    Mathf.Abs(position.y - layout.DropPlatformPosition.y) < .65f)
                { dropArmed = true; dropPeak = position.y; }
                if (dropArmed)
                {
                    dropPeak = Mathf.Max(dropPeak, position.y);
                    if (!grounded && !movement.SwimmingActive && !movement.ClimbingActive && !movement.MantlingActive)
                        dropAirborne = true;
                    if (fallDamageObserved && grounded)
                    {
                        FallPracticed = true;
                        dropArmed = dropAirborne = fallDamageObserved = false;
                    }
                    else if (movement.SwimmingActive || movement.ClimbingActive || movement.MantlingActive ||
                        PlanarDistance(position, layout.DropPlatformPosition) > 13 ||
                        (grounded && position.y < layout.DropPlatformPosition.y - 1))
                    { dropArmed = dropAirborne = fallDamageObserved = false; }
                }
                break;
            case Lesson.Heal:
                if (FieldAidPracticed && grounded) ShowLesson(Lesson.Track);
                break;
            case Lesson.RecoverStamina:
                if (reachedIslandShore && grounded && PlanarDistance(position, layout.IslandRestPosition) <= 6 &&
                    vitals.Stamina >= Mathf.Min(80, vitals.MaximumStamina))
                { StaminaRestPracticed = true; ShowLesson(Lesson.Climb); }
                break;
            case Lesson.Climb:
                if (PlanarDistance(position, layout.ClimbTopPosition) > 7)
                { climbSeconds = 0; mantleSeen = false; break; }
                if (movement.ClimbingActive || movement.MantlingActive) climbSeconds += Time.deltaTime;
                if (movement.MantlingActive) mantleSeen = true;
                if (grounded && position.y < layout.ClimbTopPosition.y - .4f)
                { climbSeconds = 0; mantleSeen = false; }
                if (climbSeconds >= .15f && mantleSeen && grounded &&
                    PlanarDistance(position, layout.ClimbTopPosition) <= 2.1f && position.y >= layout.ClimbTopPosition.y - .25f)
                { ClimbPracticed = true; CurrentLesson = Lesson.ReachIsland; }
                break;
        }
        actionWasGrounded = grounded;
    }
    void UpdateMarker()
    {
        if (!marker)
        {
            var go = new GameObject("Opening destination (runtime)"); go.transform.SetParent(transform);
            marker = go.AddComponent<LineRenderer>(); marker.sharedMaterial = world.routeMaterial;
            marker.useWorldSpace = true; marker.loop = true; marker.positionCount = 40;
            marker.startWidth = marker.endWidth = .09f;
        }
        var target = CurrentTarget;
        if (world.NavigationGround(target, out var floor)) target.y = floor.y;
        for (int i=0;i<40;i++)
        {
            float angle = i*Mathf.PI*2/40;
            marker.SetPosition(i,target+new Vector3(Mathf.Cos(angle)*1.2f,.04f,Mathf.Sin(angle)*1.2f));
        }
        marker.enabled = true;
    }
    string Instruction => CurrentLesson == Lesson.Look ? "Move the mouse to look around." :
        CurrentLesson == Lesson.Move ? "Use WASD to walk a few steps." :
        CurrentLesson == Lesson.Approach ? "Follow the path to the traveler. Hold Shift to run if you wish." :
        CurrentLesson == Lesson.Talk ? "Face the traveler and press E to talk. E continues the conversation." :
        CurrentLesson == Lesson.Jump ? "Follow the marker to the low stone step. Use WASD and Space to jump, then land on dry ground." :
        CurrentLesson == Lesson.Drop ? (FallPracticed ?
            "The fall cost health. Keep walking away from the ledge to the open landing marker, then learn to use field aid." :
            dropArmed ? "Walk off the marked ledge toward the broad landing. The fall will cost health." :
            "Follow the stone ramp up to the marked ledge. Walk off toward the landing to learn about fall damage.") :
        CurrentLesson == Lesson.Heal ? (vitals && vitals.AidCharges == 0 ?
            "No field aid left. Return to the camp marker and press H to refill, then return to the practice ledge. Tab skips the introduction." :
            health && health.CurrentHealth == health.MaxHealth ?
            "You are healthy again. Walk off the practice ledge to get hurt, then press H on dry ground to use field aid." :
            "Press H on dry ground to use one field aid charge and recover health. Resting at camp refills your supplies.") :
        CurrentLesson == Lesson.RecoverStamina ? "Rest on the marked dry island shore until stamina reaches 80. Swimming and climbing both spend stamina." :
        CurrentLesson == Lesson.Climb ? "Face the marked low wall and hold W to climb and pull onto its top. Space lets go; watch your stamina." :
        CurrentLesson == Lesson.Track ? (!world.accepted[1] ? "Read the lakeside clue with E. Then open J and choose Track for The island inscription." :
            "Open J and choose Track for The island inscription. Close the journal when ready.") :
        CurrentLesson == Lesson.Swim ? "Use WASD to swim from the marked shore to the low island landing. Watch stamina; rest on dry ground." :
        CurrentLesson == Lesson.ReachIsland ? "Explore the island and press E at the inscription to complete the discovery." :
        "Walk through the opening and discover the lake.";
    string LessonTitle => CurrentLesson == Lesson.Look ? "LOOK" : CurrentLesson == Lesson.Move ? "MOVE" :
        CurrentLesson == Lesson.Jump ? "JUMP" : CurrentLesson == Lesson.Drop ? "A REAL FALL" :
        CurrentLesson == Lesson.Heal ? "FIELD AID" : CurrentLesson == Lesson.RecoverStamina ? "RECOVER STAMINA" :
        CurrentLesson == Lesson.Climb ? "CLIMB" : CurrentLesson == Lesson.Track ? "TRACK A CLUE" :
        CurrentLesson == Lesson.Swim ? "CROSS THE LAKE" : "TALK";
    string LessonKeys => CurrentLesson == Lesson.Look ? "<  MOUSE  >" : CurrentLesson == Lesson.Track ? "J  /  TRACK" :
        CurrentLesson == Lesson.Jump ? "WASD  +  SPACE" : CurrentLesson == Lesson.Heal ? "H  /  FIELD AID" :
        CurrentLesson == Lesson.RecoverStamina ? "REST  /  STAMINA" : CurrentLesson == Lesson.Climb ? "HOLD W" :
        CurrentLesson == Lesson.Swim || CurrentLesson == Lesson.Drop ? "WASD" : "E";
    void OnGUI()
    {
        WayfarerUITheme.Apply();
        if (!Visible || TourPlaying) return;
        // The existing interaction prompt occupies this same lower-screen area.
        if (!CardOpen && dialogue && dialogue.IsInteractionPromptVisible) return;
        var oldMatrix = GUI.matrix; var oldColor = GUI.color; int oldDepth = GUI.depth;
        float scale = Mathf.Min(Screen.width/960f, Screen.height/600f);
        float width = Screen.width/scale, height = Screen.height/scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1)); GUI.color = Color.white; GUI.depth = -30;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize=22, fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleCenter };
            body = new GUIStyle(GUI.skin.label) { fontSize=16, wordWrap=true, alignment=TextAnchor.MiddleCenter };
            small = new GUIStyle(body) { fontSize=12 };
        }
        if (CardOpen)
        {
            var box = new Rect((width-420)/2,(height-448)/2,420,448);
            WayfarerUITheme.BeginPanel("first-steps-card",box,true);
            GUI.Label(new Rect(box.x+20,box.y+12,380,34),"FIRST STEPS  /  "+LessonTitle,title);
            if (PreviewTexture) GUI.DrawTexture(new Rect(box.x+100,box.y+50,220,168),PreviewTexture,ScaleMode.ScaleToFit);
            if (CurrentLesson==Lesson.Move)
            {
                string[] keys={"W","A","S","D"};
                for(int i=0;i<4;i++)
                {
                    GUI.color=preview && preview.DemoDirection==i ? new Color(1,.83f,.4f) : new Color(.45f,.55f,.6f);
                    GUI.Box(new Rect(box.x+112+i*50,box.y+222,46,31),keys[i]);
                }
            }
            else
            {
                GUI.color = Color.Lerp(new Color(.50f,.75f,.8f),new Color(1,.83f,.4f),(.5f+.5f*Mathf.Sin(Time.unscaledTime*4)));
                GUI.Box(new Rect(box.x+135,box.y+222,150,31),LessonKeys);
            }
            GUI.color=Color.white;
            GUI.Label(new Rect(box.x+22,box.y+258,376,76),Instruction,body);
            if (GUI.Button(new Rect(box.x+34,box.y+340,352,36),"Start practice  [Enter]")) StartPractice();
            if (GUI.Button(new Rect(box.x+100,box.y+385,220,29),"Skip introduction  [Tab]")) Skip();
            GUI.Label(new Rect(box.x+20,box.y+420,380,18),"World paused while you watch",small);
            WayfarerUITheme.EndPanel();
        }
        else
        {
            var box = new Rect((width-580)/2,height-166,580,100);
            WayfarerUITheme.BeginPanel("first-steps-hint",box,true);
            GUI.Label(new Rect(box.x+12,box.y+5,556,71),Instruction,body);
            GUI.Label(new Rect(box.x+12,box.y+77,556,20),"Tab: skip introduction",small);
            WayfarerUITheme.EndPanel();
        }
        GUI.matrix=oldMatrix; GUI.color=oldColor; GUI.depth=oldDepth;
    }
    void OnEnable()
    {
        if (!suspendedForTravel) return;
        suspendedForTravel = false; nearGuideTime = 0;
        ResetActionAttempt();
        // Resume the same lesson, but do not count a transition as walking/looking.
        if (world && world.player) previousPosition = world.player.position;
        if (look) previousLook = look.transform.rotation;
    }
    void OnDisable()
    {
        var travel = SideWorldTravel.Instance;
        if (Active && !ModalOpen && travel && travel.IsHostSuspended(gameObject.scene))
        {
            suspendedForTravel = true; nearGuideTime = 0;
            ResetActionAttempt();
            if (marker) marker.enabled = false;
            if (preview) preview.Hide();
            return;
        }
        Cancel();
    }
    void OnDestroy()
    {
        Cancel(); // Teardown must release resources, never mark the opening complete.
        if (guide) guide.DialogueCompleted -= OnGuideCompleted;
        if (health) { health.Damaged -= OnPlayerDamaged; health.Died -= OnPlayerDied; health.HealthChanged -= OnHealthChanged; }
    }
}
