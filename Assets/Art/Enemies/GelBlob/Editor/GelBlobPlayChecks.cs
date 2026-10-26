using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Save-isolated real Play checks. All arena geometry and captures are disposable.
public static class GelBlobPlayChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(20500, 100, 20500);
    static Transform player;
    static CharacterController controller;
    static PlayerMovement movement;
    static string cameraCaptureDirectory;
    public static IEnumerator Tests(BaselineFlow flow, Transform target, Action<string> log, bool cameraOnly = false)
    {
        cameraCaptureDirectory = Path.Combine(Path.GetTempPath(), "anca-gel-camera-play", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        GelBlobBuilder.CheckAssets(); EnemyAudioPlayChecks.CheckAssets();
        var actualLayout = flow.GetComponent<OverworldV2Layout>();
        player = target; movement = player.GetComponent<PlayerMovement>(); controller = player.GetComponent<CharacterController>();
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        var authored = Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None);
        Need(authored.Length == 3, "Expected exactly three initial gels.");
        foreach (var ai in authored)
        {
            Need(GelBlobBuilder.AboveLake(actualLayout, ai.transform.position), "Authored gel is below the map's visible lake: " + ai.name);
            ai.enabled = false;
        }
        Collider floor = Box("QA gel supported floor", Origin + Vector3.down * .15f, new Vector3(30, .3f, 30));
        var data = new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var grounds = data.FindProperty("navigationSurfaces"); grounds.arraySize++;
        grounds.GetArrayElementAtIndex(grounds.arraySize - 1).objectReferenceValue = floor; data.ApplyModifiedPropertiesWithoutUndo();
        movement.enabled = false; movement.ClearImpact(); PlacePlayer(Origin + Vector3.forward * 3f);
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GelBlobBuilder.PrefabPath), Origin + Vector3.up * .025f, Quaternion.identity);
        var gel = actor.GetComponent<GelBlobAI>(); var hp = actor.GetComponent<Health>(); var visual = actor.GetComponent<GelBlobVisual>();
        Health playerHealth = player.GetComponent<Health>(); playerHealth.RestoreFullHealth();
        Physics.SyncTransforms(); yield return null;
        var trace = actor.transform.Find("Reusable wet contact trace").GetComponent<MeshRenderer>();
        Need(visual.BubbleCount == 3 && trace.sharedMaterial == actor.transform.Find("Single translucent gel hull").GetComponent<Renderer>().sharedMaterial &&
            trace.GetComponent<MeshFilter>().sharedMesh.vertexCount == 17 && !trace.GetComponent<Collider>() && !visual.SmearEnabled,
            "Contact trace/bubbles allocate an extra material, light, collider or excessive geometry.");
        Need(Mathf.Abs(gel.BodyScale - 1.2f) < .001f && Mathf.Abs(gel.LandingRadius - 1.2f) < .001f &&
            Mathf.Abs((float)typeof(GelBlobAI).GetField("windupSeconds", Private).GetValue(gel) - 1f) < .001f &&
            Mathf.Abs((float)typeof(GelBlobAI).GetField("landingLockSeconds", Private).GetValue(gel) - .6f) < .001f &&
            Mathf.Abs((float)typeof(GelBlobAI).GetField("recoverySeconds", Private).GetValue(gel) - 1.35f) < .001f &&
            Mathf.Abs(actor.GetComponent<BoxCollider>().size.x - 1.18f * gel.Shape.x * gel.BodyScale) < .01f &&
            Mathf.Abs(actor.transform.Find("Single translucent gel hull").localScale.x - gel.Shape.x * gel.BodyScale) < .01f,
            "Enlarged visual, physical body and marked radius disagree.");
        if (cameraOnly)
        {
            yield return FollowCameraChecks(gel, log);
            Object.Destroy(actor); Object.Destroy(floor.gameObject); yield return null;
            foreach (GelBlobAI placed in authored)
            { yield return AuthoredFollowCamera(placed,log); yield return AuthoredSwordImpacts(placed,log); }
            yield break;
        }
        Need(gel.SafeLanding(Origin + Vector3.right * 4), "Flat full footprint rejected.");
        Collider wall = Box("QA gel wall", Origin + new Vector3(0, 1.1f, 1.5f), new Vector3(5, 2.2f, .3f));
        Physics.SyncTransforms(); Need(!gel.ArcClear(Origin, Origin + Vector3.forward * 3), "Leap crosses blocking wall.");
        Need(!gel.SafeLanding(Origin + Vector3.right * 14.8f), "Centre-only ledge check accepted unsupported body.");
        Object.Destroy(wall.gameObject); yield return null;
        log("PASS full footprint and entire arc reject walls/ledge without enlarged damage or teleport.");
        Vector3 basinFeet = new Vector3(Origin.x + 40f, actualLayout.LakePosition.y - .4f, Origin.z);
        Collider basin = Box("QA nav-approved floor below visual lake", basinFeet + Vector3.down * .15f, new Vector3(6, .3f, 6));
        data.Update(); grounds = data.FindProperty("navigationSurfaces"); grounds.arraySize++;
        grounds.GetArrayElementAtIndex(grounds.arraySize - 1).objectReferenceValue = basin; data.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        Need(new EnemyGroundQuery(actor.transform).TryFootprint(basinFeet, Quaternion.identity, Vector2.one * .8f, 35f, .7f, .8f, out _, true),
            "Below-lake fixture does not reproduce a physically supported approved floor.");
        Need(!gel.SafeLanding(basinFeet), "Gel accepts approved basin floor under non-colliding visible lake.");
        Object.Destroy(basin.gameObject); yield return null;
        log("PASS visible lake height rejects nav-approved underwater basin, without changing global ground rules.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup, 3f);
        LineRenderer marked = actor.transform.Find("Locked landing warning").GetComponent<LineRenderer>();
        yield return null;
        for (int i = 0; i < marked.positionCount; i++)
        {
            Vector3 offset = marked.GetPosition(i) - gel.LandingPoint; offset.y = 0;
            Need(Mathf.Abs(offset.magnitude - gel.LandingRadius) < .015f, "Visible circle and enlarged damage radius disagree.");
        }
        int locks = gel.LockCount;
        Need(!visual.LockMarkerEnabled, "Unlocked aim already displays a lock marker.");
        yield return Wait(() => gel.LandingLocked && gel.Phase == GelBlobAI.State.Windup, 2f); yield return null;
        var reticle = actor.transform.Find("Reusable landing lock crosshair").GetComponent<LineRenderer>();
        float initialSpan = (reticle.GetPosition(3) - reticle.GetPosition(9)).magnitude;
        Need(gel.LockCount == locks + 1 && visual.LockMarkerEnabled && EnemyAudio.CountCue(EnemySoundCue.CoreLock) == 1,
            "Target lock lacks a single shared audio cue and visible crosshair.");
        PlayerCombat hitStopOwner = player.GetComponent<PlayerCombat>();
        float previousTimeScale = Time.timeScale;
        try
        {
            // A raw timeScale=0 means pause, not sword hit-stop. Use the existing
            // owner's production request, and sample after the first zero-dt frame.
            typeof(PlayerCombat).GetMethod("RequestHitStop", Private).Invoke(hitStopOwner, new object[] { .1f });
            yield return null;
            Vector3 frozenMarker = reticle.GetPosition(3);
            yield return new WaitForSecondsRealtime(.03f);
            Need(hitStopOwner.IsApplyingHitStop && visual.LockMarkerEnabled &&
                Vector3.Distance(reticle.GetPosition(3), frozenMarker) < .001f && gel.LockCount == locks + 1,
                "Hit-stop hides, moves or retriggers the locked marker rather than freezing it.");
        }
        finally { hitStopOwner.CancelForTransition(); Time.timeScale = previousTimeScale; }
        Vector3 lockedPoint = gel.LandingPoint;
        yield return Delay(.17f);
        Need(visual.LockMarkerEnabled && (reticle.GetPosition(3) - reticle.GetPosition(9)).magnitude < initialSpan - .08f &&
            Vector3.Distance(gel.LandingPoint, lockedPoint) < .001f && gel.LockCount == locks + 1,
            "Lock marker does not contract once, moves the target, or retriggers sound.");
        log("PASS target lock contracts one cached crosshair and plays one existing warning; outer damage disk does not shrink or retarget.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 2f);
        Vector3 locked = gel.LandingPoint;
        int landings = gel.LandingCount;
        yield return Wait(() => gel.LandingCount > landings, 2f); yield return null;
        Need(gel.DamageCount == 1 && playerHealth.CurrentHealth == playerHealth.MaxHealth - 10, "Stationary player missed or duplicate landing damage: " + gel.DamageCount + "/" + playerHealth.CurrentHealth);
        Need(gel.Phase == GelBlobAI.State.Recover, "Landing lacks counterattack recovery.");
        Vector3 resting = gel.transform.position;
        int hits = gel.DamageCount; yield return Delay(1.1f);
        Need(gel.Phase == GelBlobAI.State.Recover && gel.DamageCount == hits, "Recovery retriggers damage or exits too early.");
        Need(Vector3.Distance(resting, gel.transform.position) < .01f, "Gel moves during extended recovery.");
        Need(actor.transform.Find("Suspended mechanical core/Cyan iris").localScale.y < .10f,
            "Contact-constrained recovery has no readable sleepy iris.");
        log("PASS enlarged physical jump hits once for ten damage; stays still in a >1.1 second counterattack recovery.");

        yield return ThreatAndContactChecks(gel, flow, log);
        yield return SlopeAndBlockedSplashChecks(gel, flow, log);

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.Progress > .8f, 3f);
        Need(gel.Shape.y < .58f && gel.Shape.x > 1.3f, "Windup does not clearly squash with conserved volume.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 2f);
        float maximumStretch = 0f; Vector3 firstLanding = gel.LandingPoint;
        // Unoccupied target: inspect the full splat, not the safe narrowed contact pose.
        PlacePlayer(Origin + new Vector3(3, 0, 3));
        float shapeDeadline = Time.realtimeSinceStartup + 2f;
        while (gel.Phase == GelBlobAI.State.Flight && Time.realtimeSinceStartup < shapeDeadline)
        {
            Vector3 shape = gel.Shape; maximumStretch = Mathf.Max(maximumStretch, shape.y);
            Need(Mathf.Abs(shape.x * shape.y * shape.z - 1f) < .01f, "Leap animation changes gel volume.");
            controller.Move(Vector3.down * .04f); yield return null;
        }
        Need(maximumStretch > 1.2f && gel.Phase == GelBlobAI.State.Recover && gel.Shape.y < .68f && gel.LandingPoint == firstLanding,
            "Launch/apex/landing lack distinct elastic poses or attack changed target.");
        Need(visual.WarningEnabled, "Landing impact lacks an exact-radius contact flash.");
        // The coroutine resumes after AI.Update; render enable/property blocks
        // are authored by Visual.LateUpdate. Observe the next rendered frame.
        yield return null;
        Need(visual.SmearEnabled && visual.DropletCount <= 8, "Supported landing lacks its small bounded wet trace; details=" +
            EnemyPresentationBudget.Details(gel.transform.position) + " age=" + typeof(GelBlobVisual).GetField("smearAge",Private).GetValue(visual) +
            " cameraDistance=" + Vector3.Distance(Camera.main.transform.position,gel.transform.position).ToString("F2") + " feet=" + gel.transform.position.ToString("F3"));
        foreach (Vector3 vertex in trace.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 world = trace.transform.TransformPoint(vertex);
            Need(new EnemyGroundQuery(actor.transform).TrySample(world,.05f,.08f,35f,out RaycastHit under) &&
                Mathf.Abs(world.y - under.point.y - .015f) < .006f,
                "Wet trace floats or sinks through supported floor.");
        }
        yield return Delay(.27f);
        Need(gel.Shape.y > .95f && gel.Phase == GelBlobAI.State.Recover, "Landing never rebounds into a usable counterattack window.");
        log("PASS volume-preserving squash, launch stretch, apex roundness, contact flatten/rebound and stationary recovery are visible in real Play.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Recover && gel.Progress > .48f, 1f); yield return null;
        var lensBlock = new MaterialPropertyBlock();
        Need(!visual.SmearEnabled && visual.DropletCount == 0, "Wet trace or droplets outlive the short landing feedback.");
        actor.transform.Find("Suspended mechanical core/Cyan iris").GetComponent<Renderer>().GetPropertyBlock(lensBlock);
        Need(gel.Shape.y < .90f && gel.Shape.y > .83f && gel.RecoveryReadiness < .01f &&
            lensBlock.GetColor("_EmissionColor").maxColorComponent < .5f && !visual.LockMarkerEnabled &&
            actor.transform.Find("Suspended mechanical core/Cyan iris").localScale.y < .10f,
            "Mid-recovery looks ready or retains an attack marker.");
        CheckCoreInside(actor.transform.Find("Single translucent gel hull"), actor.transform.Find("Suspended mechanical core"));
        yield return Wait(() => gel.Phase == GelBlobAI.State.Recover && gel.Progress > .94f, 1f); yield return null;
        actor.transform.Find("Suspended mechanical core/Cyan iris").GetComponent<Renderer>().GetPropertyBlock(lensBlock);
        Need(gel.Shape.y > .96f && lensBlock.GetColor("_EmissionColor").maxColorComponent > 1.4f &&
            actor.transform.Find("Suspended mechanical core/Cyan iris").localScale.y > .18f,
            "Recovery does not clearly rearm just before the real window ends.");
        log("PASS fatigue pose and dim core persist during recovery, remain physically contained, and rearm only near its actual end.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 3f);
        locked = gel.LandingPoint; landings = gel.LandingCount;
        PlacePlayer(Origin + new Vector3(3, 0, 3));
        yield return Wait(() => gel.LandingCount > landings, 2f);
        Need(Vector3.Distance(locked, gel.LandingPoint) < .01f && playerHealth.CurrentHealth == playerHealth.MaxHealth, "Flight homed on lateral dodge or hidden radius hit.");
        log("PASS locked leap stays straight; walking sideways out of marked landing disk avoids damage.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight && gel.Progress > .1f, 3f);
        // Spawn AHEAD of the widest pose, never inside it on a slow editor frame.
        float blockerZ = actor.transform.position.z + 1.15f;
        wall = Box("QA dynamic gel blocker", new Vector3(Origin.x, Origin.y + 1.1f, blockerZ), new Vector3(5, 2.2f, .2f));
        Physics.SyncTransforms(); yield return Wait(() => gel.Phase == GelBlobAI.State.Recover, 3f);
        Need(playerHealth.CurrentHealth == playerHealth.MaxHealth && actor.GetComponent<BoxCollider>().bounds.max.z < wall.bounds.min.z && !visual.WarningEnabled,
            "Dynamic obstruction crossed or caused unmarked damage.");
        Object.Destroy(wall.gameObject); yield return null;
        log("PASS newly introduced wall aborts flight into harmless vertical landing, without repeated bouncing or wall penetration.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup, 3f);
        Vector3 contactOnBody = actor.GetComponent<BoxCollider>().ClosestPoint(player.position + Vector3.up * .6f);
        hp.TryApplyDamage(new DamageInfo(1, player.gameObject, contactOnBody, Vector3.forward));
        Need(gel.Phase == GelBlobAI.State.Stagger && !gel.WarningVisible, "Real ground hit does not interrupt preparation.");
        yield return null;
        var gelBlock = new MaterialPropertyBlock();
        actor.transform.Find("Single translucent gel hull").GetComponent<Renderer>().GetPropertyBlock(gelBlock);
        Need(gelBlock.GetFloat("_HitAge") < .12f && gelBlock.GetFloat("_Hit") > .15f &&
            ((Vector3)gelBlock.GetVector("_HitPoint")).sqrMagnitude > .04f,
            "Real hit does not submit a localized, short-lived gel surface wave.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 3f);
        int remaining = hp.CurrentHealth; locked = gel.LandingPoint;
        hp.TryApplyDamage(new DamageInfo(1, player.gameObject, actor.transform.position, Vector3.forward));
        Need(hp.CurrentHealth == remaining - 1 && gel.Phase == GelBlobAI.State.Flight && gel.LandingPoint == locked, "Airborne gel invulnerable or retargeted on hit.");
        flow.SetMenu(true); yield return null;
        Need(!visual.WarningEnabled && !visual.LockMarkerEnabled && !visual.SmearEnabled && !visual.HitFlashEnabled && visual.DropletCount == 0,
            "Menu retains landing warning/lock marker/wet trace/droplets.");
        Vector3 paused = actor.transform.position; yield return new WaitForSecondsRealtime(.1f);
        Need(Vector3.Distance(paused, actor.transform.position) < .001f, "Menu moved actor.");
        flow.SetMenu(false); yield return Delay(.8f);
        Need(playerHealth.CurrentHealth == playerHealth.MaxHealth, "Resumed cancelled airborne attack caused damage.");
        log("PASS real hit interrupts ground charge; airborne hit remains valid; menu cancels attack and pauses motion.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Recover && gel.Progress > .45f, 4f); yield return null;
        Renderer iris = actor.transform.Find("Suspended mechanical core/Cyan iris").GetComponent<Renderer>();
        iris.GetPropertyBlock(lensBlock); float tiredEnergy = lensBlock.GetColor("_EmissionColor").maxColorComponent;
        float tiredIris = iris.transform.localScale.y;
        hp.TryApplyDamage(new DamageInfo(1000, player.gameObject, actor.transform.position, Vector3.forward)); yield return null;
        iris.GetPropertyBlock(lensBlock);
        Need(hp.IsDead && lensBlock.GetColor("_EmissionColor").maxColorComponent <= tiredEnergy + .001f &&
            iris.transform.localScale.y <= tiredIris + .001f && !visual.LockMarkerEnabled,
            "Death from fatigue relights/reopens the core or leaves a lock marker.");
        log("PASS fatigued death only extinguishes the current core; it never relights/reopens or leaves a marker.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 3f);
        hp.TryApplyDamage(new DamageInfo(1000, player.gameObject, actor.transform.position, Vector3.forward));
        Need(gel.Phase == GelBlobAI.State.Dead && !actor.GetComponent<BoxCollider>().enabled && !visual.WarningEnabled && !visual.LockMarkerEnabled, "Death retains collision/attack.");
        yield return Delay(.8f);
        Need(playerHealth.CurrentHealth == playerHealth.MaxHealth && actor.transform.position.y <= Origin.y + .04f,
            "Death attacks or leaves core floating in midair.");
        Need(visual.DropletCount == 0 && Object.FindObjectsByType<StoneScoutVitalPickup>(FindObjectsSortMode.None).Length == 1, "Death droplets linger or duplicate/missing reward.");
        flow.NewGame(); if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false); GameSession.Instance.GrantSword(); yield return Delay(.6f);
        Need(!hp.IsDead && hp.CurrentHealth == hp.MaxHealth && actor.GetComponent<BoxCollider>().enabled && gel.Phase == GelBlobAI.State.Idle &&
            Object.FindObjectsByType<StoneScoutVitalPickup>(FindObjectsSortMode.None).Length == 0 && !visual.SmearEnabled && !visual.HitFlashEnabled && visual.DropletCount == 0,
            "New game fails to reset gel or clean drop/feedback.");
        actor.transform.Find("Single translucent gel hull").GetComponent<Renderer>().GetPropertyBlock(gelBlock);
        Need(gelBlock.GetFloat("_Hit") < .001f && Mathf.Abs(gelBlock.GetFloat("_Fade") - 1f) < .001f,
            "New game retains a hit/dissolve shader state.");
        log("PASS airborne death safely lands core, cancels damage, emits one reward; actual NewGame restores enemy and clears reward.");
        log("PASS three reused opaque bubbles, 16-triangle shared-material grounded trace and eight short droplets; true-hit wave, menu/new-game cleanup.");
        yield return FarDetails(gel, log);
        yield return ReactionWindowChecks(gel, log);
        PlacePlayer(Origin + Vector3.forward * 3); Reset(gel);
        Keyboard previous = Keyboard.current; var background = InputSystem.settings.backgroundBehavior;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("GelJumpQA"); keyboard.MakeCurrent(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        try
        {
            movement.Relocate(player.position, Quaternion.identity); movement.enabled = true;
            // The larger disk locks earlier for walking dodges. Time an actual
            // jump near launch, not at the start of that longer locked interval.
            yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.LandingLocked && gel.Progress >= .78f, 4f, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Space)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Wait(() => !controller.isGrounded, 1f, false);
            landings = gel.LandingCount;
            yield return Wait(() => gel.LandingCount > landings, 2f, false);
            Need(playerHealth.CurrentHealth == playerHealth.MaxHealth && !controller.isGrounded,
                "Real Space jump failed: hp=" + playerHealth.CurrentHealth + " grounded=" + controller.isGrounded + " phase=" + gel.Phase);
            log("PASS real Space takeoff avoids actual landing hit.");
        }
        finally { movement.enabled = false; InputSystem.RemoveDevice(keyboard); if (previous != null) previous.MakeCurrent(); InputSystem.settings.backgroundBehavior = background; }
        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup, 3f);
        var sword = player.GetComponent<PlayerCombat>(); movement.Relocate(player.position, Quaternion.identity); movement.enabled = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        yield return Wait(() => { Cursor.lockState = CursorLockMode.Locked; return sword.CanAttackNow; }, 4f, false);
        // Test the gate directly at a fresh charge; player faces AWAY, out of actual sword reach.
        player.rotation = Quaternion.identity; Set(gel, "age", 0f);
        Need(sword.TryAttack(), "Air sword attack unavailable."); yield return new WaitForFixedUpdate();
        Need(gel.Phase == GelBlobAI.State.Windup && hp.CurrentHealth == hp.MaxHealth && !visual.HitFlashEnabled && visual.DropletCount==0, "Air swing stopped/damaged gel or emitted a fake hit cue.");
        sword.CancelForTransition(); movement.SetGameplayInputLocked(false); movement.enabled = false;
        log("PASS actual air sword swing locks player movement but does not interrupt gel charge.");
        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 3f);
        // A takeoff in the hit's queued frame must evade; stale movement grace is insufficient.
        Set(gel, "pendingHit", true); Set(gel, "pendingPoint", player.position);
        controller.Move(Vector3.up * .15f);
        gel.GetType().GetMethod("LateUpdate", Private).Invoke(gel, null);
        Need(playerHealth.CurrentHealth == playerHealth.MaxHealth, "Same-frame airborne player hit by queued landing.");
        log("PASS queued landing resolves after player movement; same-frame takeoff avoids it.");
        yield return KeyboardCombat(gel, log);
        yield return FollowCameraChecks(gel, log);
        Object.Destroy(actor); Object.Destroy(floor.gameObject); yield return null;

        // Actual authored environment: check support, approach and rendering under the unchanged sun.
        foreach (var ai in authored)
        { Need(ai.SafeLanding(ai.transform.position), "Authored full-footprint spawn invalid: " + ai.name); }
        GelBlobAI sample = Array.Find(authored, g => g.name == "GelBlob_LakeShoreNorth");
        Vector3 spawn = sample.transform.position;
        var actualGround = new EnemyGroundQuery(sample.transform);
        var navigation = EnemyNavigationWorld.Find(sample.gameObject.scene);
        bool approachChecked = false;
        for (int i = 0; i < 12 && !approachChecked; i++)
        {
            Vector3 candidate = spawn + Quaternion.Euler(0, i * 30f, 0) * Vector3.forward * 6f;
            if (!actualGround.TrySample(candidate, 3f, 3f, 35f, out RaycastHit pathFloor)) continue;
            candidate.y = pathFloor.point.y;
            if (!navigation.Sample(EnemyNavigationWorld.Profile.StoneScout, candidate, .5f, out _)) continue;
            PlacePlayer(candidate); sample.ResetForJourney(); sample.enabled = true;
            float until = Time.realtimeSinceStartup + 2.5f;
            while (Vector3.Distance(sample.transform.position, spawn) < .3f && Time.realtimeSinceStartup < until)
            { controller.Move(Vector3.down * .04f); yield return null; }
            log("Map approach trial " + i + ": phase=" + sample.Phase + " moved=" + Vector3.Distance(sample.transform.position, spawn).ToString("F3") + " playerHP=" + playerHealth.CurrentHealth);
            approachChecked = Vector3.Distance(sample.transform.position, spawn) >= .3f;
            sample.enabled = false;
        }
        Need(approachChecked, "Actual map gel never approaches at six metres; playerHP=" + playerHealth.CurrentHealth);
        sample.ResetForJourney();
        log("PASS actual authored-map navigation approaches player from six metres with body support/obstacle checks.");
        Vector3 playerSpot = spawn + Vector3.forward * 3f;
        Need(actualGround.TrySample(playerSpot, 2f, 2f, 35f, out RaycastHit actualFloor), "No supported real-map player spot.");
        playerSpot.y = actualFloor.point.y; PlacePlayer(playerSpot); sample.enabled = true; playerHealth.RestoreFullHealth();
        yield return CaptureStates(sample, log);
        sample.enabled = false;
        foreach (GelBlobAI placed in authored)
        { yield return AuthoredCombat(placed, log); yield return AuthoredFollowCamera(placed, log);
            yield return AuthoredSwordImpacts(placed,log); yield return CaptureAuthoredIdle(placed); }
        log("PASS all three actual-map spawns supported; real-map state screenshots captured without changing lighting or saving scene.");
    }
    static void Reset(GelBlobAI gel) { gel.ResetForJourney(); movement.ClearImpact(); player.GetComponent<Health>().RestoreFullHealth(); Physics.SyncTransforms(); }
    static IEnumerator Wait(Func<bool> condition, float seconds, bool settle = true)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until)
        { if (settle) controller.Move(Vector3.down * .04f); yield return null; }
        if (!condition())
        {
            string details = "";
            foreach (GelBlobAI actor in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None))
                if (actor.enabled) details += " " + actor.name + " phase=" + actor.Phase + " t=" + actor.Progress.ToString("F2") +
                    " pos=" + actor.transform.position.ToString("F3") + " landing=" + actor.LandingPoint.ToString("F3");
            Need(false, "Timed out waiting;" + details);
        }
    }
    static IEnumerator Delay(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) { if (!movement.enabled) controller.Move(Vector3.down * .04f); yield return null; } }
    static void PlacePlayer(Vector3 point)
    { movement.Relocate(point, Quaternion.identity); Physics.SyncTransforms(); controller.Move(Vector3.down * .08f); }
    static Collider Box(string name, Vector3 point, Vector3 size)
    { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = point; go.transform.localScale = size; return go.GetComponent<Collider>(); }
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    static void Need(bool value, string message) { if (!value) throw new InvalidOperationException("Gel QA: " + message); }
    static IEnumerator FarDetails(GelBlobAI gel, Action<string> log)
    {
        Camera main = Camera.main; var orbit = main.GetComponent<ThirdPersonCamera>();
        Vector3 position = main.transform.position; Quaternion rotation = main.transform.rotation;
        bool orbitEnabled = orbit.enabled, aiEnabled = gel.enabled;
        gel.enabled = false; orbit.enabled = false;
        try
        {
            main.transform.position = gel.transform.position + Vector3.up * 45f;
            yield return null; yield return null;
            var block = new MaterialPropertyBlock();
            gel.transform.Find("Single translucent gel hull").GetComponent<Renderer>().GetPropertyBlock(block);
            int visible = 0;
            foreach (Renderer part in gel.transform.Find("Single translucent gel hull").GetComponentsInChildren<Renderer>())
                if (part.transform != gel.transform.Find("Single translucent gel hull") && part.enabled) visible++;
            Need(block.GetFloat("_Detail") == 0 && visible == 0 && !gel.GetComponent<GelBlobVisual>().SmearEnabled,
                "Far gel still renders bubble/flow/smear details.");
        }
        finally { main.transform.SetPositionAndRotation(position,rotation); orbit.enabled = orbitEnabled; gel.enabled = aiEnabled; }
        Reset(gel); yield return null; yield return null;
        log("PASS 24m detail budget removes secondary bubbles/flow/contact trace, without disabling authoritative AI or warning.");
    }
    static IEnumerator ReactionWindowChecks(GelBlobAI gel, Action<string> log)
    {
        Keyboard previous = Keyboard.current; var background = InputSystem.settings.backgroundBehavior;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("GelDelayedReactionQA"); keyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Camera main = Camera.main; ThirdPersonCamera orbit = main.GetComponent<ThirdPersonCamera>();
        bool orbitEnabled = orbit && orbit.enabled; Quaternion originalRotation = main.transform.rotation;
        float[] distances = { 1.8f, 3f, 6.5f }, delays = { 0f, .15f, .25f };
        int passes = 0, trial = 0;
        try
        {
            if (orbit) orbit.enabled = false;
            main.transform.rotation = Quaternion.Euler(10, 180, 0);
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Cursor.lockState = CursorLockMode.Locked;
            foreach (float distance in distances)
                foreach (float delay in delays)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    movement.enabled = false; Reset(gel); PlacePlayer(Origin + Vector3.forward * distance);
                    movement.enabled = true;
                    yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.LandingLocked, 4f, false);
                    Vector3 locked = gel.LandingPoint, before = player.position;
                    float lockTime = Time.time;
                    int landings = gel.LandingCount, contacts = gel.ContactCount;
                    while (Time.time < lockTime + delay) yield return null;
                    float actualDelay = Time.time - lockTime;
                    Key key = trial++ % 2 == 0 ? Key.D : Key.A;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                    yield return Wait(() => gel.LandingCount > landings, 3f, false);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                    Health hp = player.GetComponent<Health>();
                    bool avoided = hp.CurrentHealth == hp.MaxHealth;
                    if (avoided) passes++;
                    Need(Vector3.Distance(locked, gel.LandingPoint) < .01f || gel.ContactCount > contacts,
                        "A delayed dodge made a locked attack home in mid-flight.");
                    log("Reaction " + distance.ToString("F1") + "m, delay=" + actualDelay.ToString("F3") +
                        "s, " + key + ": hp=" + hp.CurrentHealth + " moved=" +
                        Mathf.Abs(player.position.x - before.x).ToString("F3") + "m contacts=" + (gel.ContactCount - contacts));
                }
            Need(passes == distances.Length * delays.Length,
                "Delayed ordinary walking dodges passed only " + passes + "/9 at 1.8/3/6.5 metres.");
            log("PASS all nine delayed A/D walking dodges (0/150/250ms reaction) avoid the locked leap; no sprint, teleport or enlarged hitbox.");
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            movement.enabled = false; movement.ClearImpact();
            InputSystem.RemoveDevice(keyboard); if (previous != null) previous.MakeCurrent();
            InputSystem.settings.backgroundBehavior = background; main.transform.rotation = originalRotation;
            if (orbit) orbit.enabled = orbitEnabled;
        }
        Reset(gel);
    }
    static IEnumerator KeyboardCombat(GelBlobAI gel, Action<string> log)
    {
        Keyboard previous = Keyboard.current; var background = InputSystem.settings.backgroundBehavior;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>("GelLateralCombatQA"); keyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Camera main = Camera.main; ThirdPersonCamera orbit = main.GetComponent<ThirdPersonCamera>();
        bool orbitEnabled = orbit && orbit.enabled; Quaternion originalRotation = main.transform.rotation;
        var sword = player.GetComponent<PlayerCombat>(); Health hp = gel.GetComponent<Health>(), playerHP = player.GetComponent<Health>();
        try
        {
            if (orbit) orbit.enabled = false;
            main.transform.rotation = Quaternion.Euler(10, 180, 0);
            Reset(gel); PlacePlayer(Origin + Vector3.forward * 3); movement.enabled = true;
            // Real D-key movement begins only AFTER the target lock, not a teleported dodge.
            yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.LandingLocked, 4f, false);
            Vector3 before = player.position; int landings = gel.LandingCount;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.D));
            yield return Wait(() => gel.LandingCount > landings, 2f, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Need(playerHP.CurrentHealth == playerHP.MaxHealth && Mathf.Abs(player.position.x - before.x) > .8f,
                "A real lateral walking dodge after lock could not evade the marked landing: hp=" + playerHP.CurrentHealth +
                " moved=" + (player.position - before).ToString("F3") + " landing=" + gel.LandingPoint.ToString("F3") +
                " player=" + player.position.ToString("F3") + " contacts=" + gel.ContactCount);
            log("PASS actual D-key lateral movement after lock dodges the natural jump; no teleport or player movement edits.");

            sword.CancelForTransition(); movement.enabled = false; Reset(gel); PlacePlayer(Origin + Vector3.forward * 3);
            int damageBeforeCounter = gel.DamageCount;
            yield return Wait(() => gel.Phase == GelBlobAI.State.Recover, 4f);
            // Real attack checks whether a short, ground-level blob can actually be hit by the existing sword.
            player.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(gel.transform.position - player.position, Vector3.up));
            movement.enabled = true; movement.ClearImpact(); Cursor.lockState = CursorLockMode.Locked;
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Need(sword.CanAttackNow && sword.TryAttack(), "Player cannot counterattack during landing recovery.");
            int beforeHP = hp.CurrentHealth;
            yield return Wait(() => hp.CurrentHealth < beforeHP, .85f, false);
            Need(gel.Phase == GelBlobAI.State.Recover && gel.DamageCount == damageBeforeCounter + 1,
                "A real sword counterattack fails or shortens gel recovery/causes another landing hit.");
            log("PASS existing player sword naturally hits the landed gel during its stationary recovery; hit feedback does not shorten that window.");
            int swings = 1;
            while (!hp.IsDead && swings < 6)
            {
                yield return Wait(() => gel.Phase == GelBlobAI.State.Recover && gel.Progress < .5f && sword.CanAttackNow, 4f, false);
                player.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(gel.transform.position - player.position, Vector3.up));
                int beforeFollowupHP = hp.CurrentHealth;
                Need(sword.TryAttack(), "Natural follow-up sword strike failed."); swings++;
                yield return Wait(() => hp.CurrentHealth < beforeFollowupHP, .9f, false);
            }
            Need(hp.IsDead && gel.Phase == GelBlobAI.State.Dead && !VisualOf(gel).WarningEnabled,
                "Repeated natural recovery counterattacks cannot finish the fight or leave an attack marker.");
            yield return Delay(.6f);
            Need(VisualOf(gel).DropletCount == 0 && !VisualOf(gel).HitFlashEnabled, "A naturally defeated gel leaves transient hit feedback running.");
            log("PASS natural sword-only fight finishes in " + swings + " recovery counterattacks, with death cancellation and bounded debris.");
            Reset(gel); // Clear that encounter's drop before subsequent authored-map checks.
        }
        finally
        {
            sword.CancelForTransition(); movement.enabled = false; movement.ClearImpact();
            InputSystem.RemoveDevice(keyboard); if (previous != null) previous.MakeCurrent();
            InputSystem.settings.backgroundBehavior = background; main.transform.rotation = originalRotation;
            if (orbit) orbit.enabled = orbitEnabled;
        }
    }
    static GelBlobVisual VisualOf(GelBlobAI gel) => gel.GetComponent<GelBlobVisual>();
    // Unlike beauty captures, these keep the REAL follow camera, collision,
    // grounding, knockback and shake active throughout an autonomous attack.
    static IEnumerator FollowCameraChecks(GelBlobAI gel, Action<string> log)
    {
        Camera main = Camera.main; ThirdPersonCamera orbit = main.GetComponent<ThirdPersonCamera>();
        Need(orbit, "Missing actual follow camera.");
        bool enabled = orbit.enabled, locked = orbit.IsLookInputLocked;
        float yaw = (float)typeof(ThirdPersonCamera).GetField("yaw", Private).GetValue(orbit);
        float pitch = (float)typeof(ThirdPersonCamera).GetField("pitch", Private).GetValue(orbit);
        Keyboard previous = Keyboard.current; Keyboard keyboard = InputSystem.AddDevice<Keyboard>("GelCameraQA"); keyboard.MakeCurrent();
        var background = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var rt = new RenderTexture(960, 540, 24); rt.Create();
        string directory = cameraCaptureDirectory; Directory.CreateDirectory(directory);
        bool stable = true;
        try
        {
            orbit.enabled = true; orbit.SetCinematicControl(false); orbit.SetLookInputLocked(true);
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            for (int trial = 0; trial < 3; trial++)
            {
                gel.enabled = false; movement.enabled = false; Reset(gel); PlacePlayer(Origin + Vector3.forward * 3f);
                orbit.ResetOrbit(trial == 0 ? 180 : trial == 1 ? 0 : 90, 12);
                yield return Delay(.6f); // Drain older tests' shake before measuring.
                gel.enabled = true; movement.enabled = true;
                string name = trial == 0 ? "front" : trial == 1 ? "rear" : "side";
                float minDistance = 100, maxTurn = 0, maxGroundPitch = 0;
                Quaternion previousRotation = main.transform.rotation;
                int damageBefore = gel.DamageCount;
                int shots = 0; bool contactShot = false, zoomShot = false;
                float until = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < until && !(gel.Phase == GelBlobAI.State.Recover && gel.Progress > .5f))
                {
                    yield return null;
                    minDistance = Mathf.Min(minDistance, (float)typeof(ThirdPersonCamera).GetField("currentCameraDistance", Private).GetValue(orbit));
                    maxGroundPitch = Mathf.Max(maxGroundPitch, Mathf.Abs((float)typeof(ThirdPersonCamera).GetField("groundPitchOffset", Private).GetValue(orbit)));
                    maxTurn = Mathf.Max(maxTurn, Quaternion.Angle(previousRotation, main.transform.rotation)); previousRotation = main.transform.rotation;
                    if (!zoomShot && minDistance < 4f) { Capture(main, rt, directory, name + "-zoom-spike"); zoomShot = true; }
                    bool nextShot = (shots == 0 && gel.Phase == GelBlobAI.State.Windup && gel.Progress > .65f) ||
                        (shots == 1 && gel.Phase == GelBlobAI.State.Flight) ||
                        (shots == 2 && gel.Phase == GelBlobAI.State.Flight && gel.Progress > .45f) ||
                        (shots == 3 && gel.Phase == GelBlobAI.State.Recover);
                    if (nextShot) { Capture(main, rt, directory, name + "-" + shots); shots++; }
                    if (!contactShot && gel.DamageCount > damageBefore) { Capture(main, rt, directory, name + "-contact"); contactShot = true; }
                }
                Need(gel.DamageCount == damageBefore + 1 && player.GetComponent<Health>().CurrentHealth == player.GetComponent<Health>().MaxHealth - 10,
                    "Camera trial lost physical damage: " + name);
                log("Follow camera " + name + ": minBoom=" + minDistance.ToString("F3") + "m maxFrameTurn=" + maxTurn.ToString("F2") +
                    "deg groundPitch=" + maxGroundPitch.ToString("F2") + "deg hits=" + (gel.DamageCount - damageBefore));
                stable &= minDistance > 5.8f && maxTurn < 2f && maxGroundPitch < .1f;
            }
            gel.enabled = false; movement.enabled = false; movement.ClearImpact(); PlacePlayer(Origin + Vector3.forward * 3f);
            orbit.ResetOrbit(0, 12); yield return Delay(.4f);
            Collider wall = Box("QA follow camera solid wall", player.position + new Vector3(0, 4, -3), new Vector3(4, 8, .2f));
            try
            {
                Physics.SyncTransforms(); yield return Delay(.45f);
                float boom = (float)typeof(ThirdPersonCamera).GetField("currentCameraDistance", Private).GetValue(orbit);
                Need(boom < 3f && !wall.bounds.Contains(main.transform.position), "Camera no longer respects real walls.");
                log("PASS real wall still shortens camera safely: " + boom.ToString("F3") + "m.");
            }
            finally { Object.Destroy(wall.gameObject); }
            log("Actual follow-camera sequence captures: " + directory);
            Need(stable, "Gel causes follow-camera zoom/pitch spikes on clear ground; see front/rear/side measurements.");
            log("PASS front/rear/side physical impacts retain normal boom and stable pitch with existing collision-constrained player recoil.");
        }
        finally
        {
            gel.enabled = true; movement.enabled = false; Reset(gel);
            InputSystem.RemoveDevice(keyboard); if (previous != null) previous.MakeCurrent(); InputSystem.settings.backgroundBehavior = background;
            orbit.ResetOrbit(yaw, pitch); orbit.SetLookInputLocked(locked); orbit.enabled = enabled;
            rt.Release(); Object.Destroy(rt);
        }
    }
    static IEnumerator AuthoredFollowCamera(GelBlobAI gel, Action<string> log)
    {
        Camera main = Camera.main; ThirdPersonCamera orbit = main.GetComponent<ThirdPersonCamera>();
        bool enabled = orbit.enabled, locked = orbit.IsLookInputLocked;
        float yaw = (float)typeof(ThirdPersonCamera).GetField("yaw", Private).GetValue(orbit);
        float pitch = (float)typeof(ThirdPersonCamera).GetField("pitch", Private).GetValue(orbit);
        var rt = new RenderTexture(1280, 720, 24); rt.Create();
        string directory = cameraCaptureDirectory; Directory.CreateDirectory(directory);
        var terrain = new EnemyGroundQuery(gel.transform);
        try
        {
            gel.enabled = false; gel.ResetForJourney();
            Vector3 feet = gel.transform.position + Vector3.forward * 3;
            Need(terrain.TrySample(feet, 2, 2, 35, out RaycastHit floor), "No real-map camera test floor: " + gel.name);
            PlacePlayer(floor.point); player.GetComponent<Health>().RestoreFullHealth(); movement.ClearImpact(); movement.enabled = true;
            orbit.enabled = true; orbit.SetCinematicControl(false); orbit.SetLookInputLocked(true); orbit.ResetOrbit(0, 20);
            yield return Delay(.4f); int damageBefore = gel.DamageCount; gel.enabled = true;
            yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.Progress > .5f, 4f, false);
            Capture(main, rt, directory, gel.name + "-charge");
            yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 2f, false);
            Capture(main, rt, directory, gel.name + "-leap");
            yield return Wait(() => gel.Phase == GelBlobAI.State.Recover, 2f, false); yield return null;
            Capture(main, rt, directory, gel.name + "-contact");
            Need(gel.DamageCount == damageBefore + 1, "Actual-map follow-camera impact lost damage: " + gel.name);
            log("PASS real follow camera captures autonomous leap/impact in unchanged environment: " + gel.name);
        }
        finally
        {
            gel.enabled = false; movement.enabled = false; movement.ClearImpact(); gel.ResetForJourney();
            orbit.ResetOrbit(yaw, pitch); orbit.SetLookInputLocked(locked); orbit.enabled = enabled;
            rt.Release(); Object.Destroy(rt);
        }
    }
    // Genuine PlayerCombat sword damage under original map lighting and the real
    // 6m follow camera. Separate beauty captures must not substitute for this.
    static IEnumerator AuthoredSwordImpacts(GelBlobAI gel, Action<string> log)
    {
        Camera main=Camera.main; ThirdPersonCamera orbit=main.GetComponent<ThirdPersonCamera>();
        bool cameraEnabled=orbit.enabled, locked=orbit.IsLookInputLocked;
        float yaw=(float)typeof(ThirdPersonCamera).GetField("yaw",Private).GetValue(orbit);
        float pitch=(float)typeof(ThirdPersonCamera).GetField("pitch",Private).GetValue(orbit);
        var sword=player.GetComponent<PlayerCombat>(); var hp=gel.GetComponent<Health>(); var visual=VisualOf(gel);
        var rt=new RenderTexture(1280,720,24); rt.Create();
        string directory=Path.Combine(cameraCaptureDirectory,"sword-hits"); Directory.CreateDirectory(directory);
        var terrain=new EnemyGroundQuery(gel.transform);
        try
        {
            gel.enabled=false; Reset(gel);
            Need(terrain.TrySample(gel.transform.position+Vector3.forward*3,2,2,35,out RaycastHit support),
                "Sword feedback needs a supported real-map player position.");
            PlacePlayer(support.point); movement.enabled=true;
            orbit.enabled=true; orbit.SetCinematicControl(false); orbit.SetLookInputLocked(true); orbit.ResetOrbit(0,20);
            yield return Delay(.4f); gel.enabled=true;
            yield return Wait(()=>gel.Phase==GelBlobAI.State.Recover,4f,false);
            Vector3 towards=Vector3.ProjectOnPlane(gel.transform.position-player.position,Vector3.up).normalized;
            player.rotation=Quaternion.LookRotation(towards);
            orbit.ResetOrbit(Mathf.Atan2(towards.x,towards.z)*Mathf.Rad2Deg+25,20);
            movement.ClearImpact(); Cursor.lockState=CursorLockMode.Locked;
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            yield return Wait(()=>sword.CanAttackNow,1f,false);
            Capture(main,rt,directory,gel.name+"-before");
            int before=hp.CurrentHealth;
            Need(sword.TryAttack(),"Actual sword feedback strike unavailable.");
            yield return Wait(()=>hp.CurrentHealth<before,.9f,false);
            yield return null; // Visual.LateUpdate must publish the actual-hit cue.
            Need(visual.HitFlashEnabled && visual.DropletCount==8,"Actual sword hit lacks flash/directional wet splatter.");
            Need(sword.IsApplyingHitStop,"Actual hit cue wasn't observed during the existing sword hit-stop.");
            Capture(main,rt,directory,gel.name+"-impact");
            Color32[] shown=ReadCapture(main,rt);
            // Same-frame counterfactual isolates VFX contrast; don't compare a
            // moving player/camera against a several-frames-old idle screenshot.
            var shell=gel.transform.Find("Single translucent gel hull").GetComponent<Renderer>();
            var block=new MaterialPropertyBlock(); shell.GetPropertyBlock(block); float strength=block.GetFloat("_Hit");
            var flash=gel.transform.Find("Reusable wet sword impact").GetComponent<Renderer>();
            var drops=new System.Collections.Generic.List<GameObject>();
            foreach(Transform t in gel.GetComponentsInChildren<Transform>())
                if(t.name.StartsWith("Reusable gel droplet") && t.gameObject.activeSelf) drops.Add(t.gameObject);
            Color32[] hidden, flashOnly;
            try
            {
                flash.enabled=false; block.SetFloat("_Hit",0); shell.SetPropertyBlock(block);
                foreach(var d in drops) d.SetActive(false);
                Capture(main,rt,directory,gel.name+"-without-cue"); hidden=ReadCapture(main,rt);
                flash.enabled=true;
                Capture(main,rt,directory,gel.name+"-flash-only"); flashOnly=ReadCapture(main,rt);
                // Keep proper depth testing. A readable flash must not be made
                // visible by rendering through walls or over the player's body.
                Collider screen=Box("QA opaque flash occluder",Vector3.Lerp(main.transform.position,flash.transform.position,.5f),new Vector3(1.3f,1.3f,.08f));
                try
                {
                    screen.transform.rotation=Quaternion.LookRotation(flash.transform.position-main.transform.position);
                    var screenRenderer=screen.GetComponent<Renderer>();
                    screenRenderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Enemies/DomainPrototypes/Generated/Graphite.mat");
                    screenRenderer.shadowCastingMode=ShadowCastingMode.Off; Physics.SyncTransforms();
                    Capture(main,rt,directory,gel.name+"-wall-blocked"); Color32[] blocked=ReadCapture(main,rt);
                    flash.enabled=false; Color32[] without=ReadCapture(main,rt); int leaking=0;
                    for(int i=0;i<blocked.Length;i++)
                        if(Mathf.Abs(blocked[i].r-without[i].r)+Mathf.Abs(blocked[i].g-without[i].g)+Mathf.Abs(blocked[i].b-without[i].b)>90) leaking++;
                    Need(leaking<=8,"Hit flash renders through an opaque world blocker: "+leaking+" pixels.");
                }
                finally { screen.gameObject.SetActive(false); Object.Destroy(screen.gameObject); }
            }
            finally
            { block.SetFloat("_Hit",strength); shell.SetPropertyBlock(block); flash.enabled=true; foreach(var d in drops) d.SetActive(true); }
            int changed=0, flashPixels=0;
            for(int i=0;i<shown.Length;i++)
            {
                if(Mathf.Abs(shown[i].r-hidden[i].r)+Mathf.Abs(shown[i].g-hidden[i].g)+Mathf.Abs(shown[i].b-hidden[i].b)>90) changed++;
                if(Mathf.Abs(flashOnly[i].r-hidden[i].r)+Mathf.Abs(flashOnly[i].g-hidden[i].g)+Mathf.Abs(flashOnly[i].b-hidden[i].b)>90) flashPixels++;
            }
            Need(changed>=60,"Normal follow-camera hit cue is still too small/low contrast: "+changed+" pixels at "+gel.name);
            Need(flashPixels>=60,"Immediate flash is occluded, back-face culled or too small: "+flashPixels+" pixels at "+gel.name);
            log("Real sword cue "+gel.name+": high-contrast changed pixels="+changed+", flash-only="+flashPixels+", flash=36 triangles, droplets="+visual.DropletCount);
            yield return Wait(()=>!sword.IsApplyingHitStop,.5f,false);
            yield return Delay(.09f); Capture(main,rt,directory,gel.name+"-ripple");
            yield return Delay(.36f);
            Need(!visual.HitFlashEnabled && visual.DropletCount==0,"Sword hit feedback outlives its short budget.");
            log("PASS real-map sword impact remains readable with normal follow camera; genuine hit-stop, opaque-wall occlusion and short cleanup: "+gel.name);
        }
        finally
        {
            sword.CancelForTransition(); gel.enabled=false; movement.enabled=false; movement.ClearImpact(); gel.ResetForJourney();
            orbit.ResetOrbit(yaw,pitch); orbit.SetLookInputLocked(locked); orbit.enabled=cameraEnabled;
            rt.Release(); Object.Destroy(rt);
        }
    }
    static Color32[] ReadCapture(Camera camera,RenderTexture target)
    {
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
        RenderTexture previous=RenderTexture.active; RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try { image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); return image.GetPixels32(); }
        finally { RenderTexture.active=previous; Object.Destroy(image); }
    }
    static IEnumerator ThreatAndContactChecks(GelBlobAI gel, BaselineFlow flow, Action<string> log)
    {
        Health hp = player.GetComponent<Health>(); PlayerVitals feedback = player.GetComponent<PlayerVitals>();
        // Seven metres should begin a real attack without walking to sword range.
        Reset(gel); PlacePlayer(Origin + Vector3.forward * 6.5f);
        Vector3 initial = gel.transform.position;
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup, 1f);
        Need(Vector3.Distance(initial, gel.transform.position) < .05f, "Gel must approach to melee distance before starting a ranged leap.");
        Need(Vector3.Distance(gel.LandingPoint, player.position) < .2f, "Warning aims short of stationary target.");
        int landedBefore = gel.LandingCount;
        yield return Wait(() => gel.LandingCount > landedBefore && gel.Phase == GelBlobAI.State.Recover, 3f);
        Need(hp.CurrentHealth == hp.MaxHealth - 10, "Six-metre stationary player cannot be struck.");
        log("PASS 6.5m attack begins without a melee approach and hits its genuine target, not a 1.12m-short aim point.");

        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3f);
        yield return Wait(() => gel.Phase == GelBlobAI.State.Windup && gel.Progress > .12f && !gel.LandingLocked, 2f);
        Vector3 previous = gel.LandingPoint;
        float startAge = (float)typeof(GelBlobAI).GetField("age", Private).GetValue(gel);
        PlacePlayer(player.position + Vector3.right * .45f);
        yield return Delay(.065f);
        float moved = (gel.LandingPoint - previous).x;
        float elapsed = (float)typeof(GelBlobAI).GetField("age", Private).GetValue(gel) - startAge;
        Need(moved > .02f && moved <= elapsed * 6f + .025f && !gel.LandingLocked,
            "Warning ignores bounded slow tracking or fails to start following.");
        yield return Delay(.18f);
        Need((gel.LandingPoint - previous).x > .35f && !gel.LandingLocked, "Slower tracking never catches a settled target before lock.");
        yield return Wait(() => gel.Phase == GelBlobAI.State.Flight, 1f);
        int before = gel.DamageCount, contactBefore = gel.ContactCount;
        // Tiny shuffle is not a dodge out of the marked attack, and walking into
        // the leap must not silently cancel damage as if the player were a wall.
        PlacePlayer(player.position + Vector3.right * .15f);
        movement.enabled = true;
        yield return Wait(() => gel.DamageCount > before, 2f, false);
        Need(hp.CurrentHealth == hp.MaxHealth - 10 && gel.ContactCount > contactBefore &&
            (float)typeof(PlayerVitals).GetField("damageFlash", Private).GetValue(feedback) > 0f && movement.ImpactActive,
            "Body contact/tiny shuffle loses damage, player flash, or the existing impact response.");
        int count = gel.DamageCount;
        float until = Time.realtimeSinceStartup + .6f;
        while (Time.realtimeSinceStartup < until)
        {
            Need(!Physics.ComputePenetration(gel.GetComponent<BoxCollider>(), gel.transform.position, gel.transform.rotation,
                controller, controller.transform.position, controller.transform.rotation, out _, out float depth) || depth < .035f,
                "Contact descent/shape expansion penetrates the player: " + depth);
            yield return null;
        }
        Need(gel.DamageCount == count, "A single contact and its landing damage the player twice.");
        movement.enabled = false; movement.ClearImpact();
        log("PASS warning eases toward .45m target change with bounded speed and settles before lock; .15m shuffle still physically hits once with feedback and no penetration.");
    }
    static IEnumerator SlopeAndBlockedSplashChecks(GelBlobAI gel, BaselineFlow flow, Action<string> log)
    {
        // Isolated queued-impact tests: actual collision/controller grounding,
        // not a fake player flag. Natural jumping is covered separately.
        Vector3 centre = Origin + Vector3.right * 40f;
        Collider slope = Box("QA continuous gel splash slope", centre + Vector3.down * .15f, new Vector3(10, .3f, 10));
        slope.transform.rotation = Quaternion.Euler(0, 0, 30);
        var data = new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var surfaces = data.FindProperty("navigationSurfaces"); surfaces.arraySize++;
        surfaces.GetArrayElementAtIndex(surfaces.arraySize - 1).objectReferenceValue = slope; data.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        var terrain = new EnemyGroundQuery(gel.transform);
        Need(terrain.TrySample(centre, 3, 3, 35, out RaycastHit originFloor), "Slope fixture has no impact floor.");
        Vector3 target = centre + Vector3.right * 1.18f;
        Need(terrain.TrySample(target, 3, 3, 35, out RaycastHit playerFloor), "Slope fixture has no player floor.");
        Need(Mathf.Abs(playerFloor.point.y - originFloor.point.y) > .65f, "Fixture fails to reproduce old absolute-Y rejection.");
        Reset(gel); PlacePlayer(playerFloor.point); controller.Move(Vector3.down * .1f);
        Need(controller.isGrounded, "Slope player must be genuinely grounded.");
        Set(gel, "pendingHit", true); Set(gel, "pendingPoint", originFloor.point + Vector3.up * .025f);
        gel.GetType().GetMethod("LateUpdate", Private).Invoke(gel, null);
        Need(player.GetComponent<Health>().CurrentHealth == player.GetComponent<Health>().MaxHealth - 10,
            "Grounded capsule inside splash on a continuous slope receives no damage.");
        Object.Destroy(slope.gameObject); yield return null;

        Reset(gel); PlacePlayer(Origin + Vector3.forward * .85f);
        Collider screen = Box("QA splash damage screen", Origin + new Vector3(0, .9f, .42f), new Vector3(3, 1.8f, .12f));
        Physics.SyncTransforms(); Set(gel, "pendingHit", true); Set(gel, "pendingPoint", Origin + Vector3.up * .025f);
        gel.GetType().GetMethod("LateUpdate", Private).Invoke(gel, null);
        Need(player.GetComponent<Health>().CurrentHealth == player.GetComponent<Health>().MaxHealth, "Splash damages a player through a wall.");
        Object.Destroy(screen.gameObject); yield return null;
        Reset(gel); PlacePlayer(Origin + Vector3.forward * 3f);
        log("PASS queued splash damages a genuinely grounded player on a continuous 30-degree slope despite >.65m elevation difference, and solid walls block damage.");
    }
    static IEnumerator AuthoredCombat(GelBlobAI gel, Action<string> log)
    {
        var terrain = new EnemyGroundQuery(gel.transform);
        var nav = EnemyNavigationWorld.Find(gel.gameObject.scene);
        bool connected = false; int attempts = 0;
        gel.ResetForJourney(); Vector3 spawn = gel.transform.position;
        for (int i = 0; i < 12 && !connected && attempts < 4; i++)
        {
            Vector3 candidate = spawn + Quaternion.Euler(0, i * 30, 0) * Vector3.forward * 3;
            if (!terrain.TrySample(candidate, 1, 1, 35, out RaycastHit support) || Mathf.Abs(support.point.y - spawn.y) > .3f ||
                !nav.Sample(EnemyNavigationWorld.Profile.StoneScout, support.point, .5f, out _)) continue;
            candidate.y = support.point.y;
            PlacePlayer(candidate); gel.ResetForJourney(); gel.enabled = true; attempts++;
            int before = gel.DamageCount; float until = Time.realtimeSinceStartup + 4.5f;
            while (gel.DamageCount == before && Time.realtimeSinceStartup < until)
            { controller.Move(Vector3.down * .04f); yield return null; }
            connected = gel.DamageCount == before + 1;
            gel.enabled = false;
        }
        Need(connected, "Actual shoreline/forest encounter could not naturally jump and hit a stationary grounded player: " + gel.name);
        log("PASS actual-map autonomous jump/ground damage at " + gel.name + "; safe candidate attempts=" + attempts);
        gel.ResetForJourney();
    }
    static IEnumerator CaptureStates(GelBlobAI sample, Action<string> log)
    {
        string directory = Path.Combine(Path.GetTempPath(), "anca-gel-play"); Directory.CreateDirectory(directory);
        var go = new GameObject("QA gel real-map camera"); Camera camera = go.AddComponent<Camera>();
        Camera original = Camera.main; camera.CopyFrom(original); camera.enabled = false;
        var settings = camera.GetUniversalAdditionalCameraData(); var originalSettings = original.GetUniversalAdditionalCameraData();
        settings.renderPostProcessing = originalSettings.renderPostProcessing; settings.volumeLayerMask = originalSettings.volumeLayerMask; settings.renderShadows = true;
        var rt = new RenderTexture(1280, 720, 24); rt.Create();
        try
        {
            Vector3 focus = sample.transform.position + Vector3.up * .35f;
            camera.transform.position = focus + new Vector3(2.5f, 1.2f, 3.8f); camera.transform.LookAt(focus);
            sample.enabled = false; yield return null; Capture(camera, rt, directory, "idle-sun");
            camera.transform.position = focus + new Vector3(-3f, .9f, -3.6f); camera.transform.LookAt(focus);
            Capture(camera, rt, directory, "idle-backlit");
            camera.transform.position = focus + new Vector3(2.5f, 1.2f, 3.8f); camera.transform.LookAt(focus);
            sample.enabled = true;
            yield return Wait(() => sample.Phase == GelBlobAI.State.Windup && !sample.LandingLocked, 4f); yield return null;
            Capture(camera, rt, directory, "tracking");
            yield return Wait(() => sample.Phase == GelBlobAI.State.Windup && sample.LandingLocked, 2f); yield return null;
            Capture(camera, rt, directory, "lock-start");
            yield return Delay(.16f); Capture(camera, rt, directory, "lock-held");
            yield return Wait(() => sample.Phase == GelBlobAI.State.Windup && sample.Progress > .7f, 4f); yield return null;
            Capture(camera, rt, directory, "windup");
            Transform hull = sample.transform.Find("Single translucent gel hull"), core = sample.transform.Find("Suspended mechanical core");
            CheckCoreInside(hull, core);
            foreach (Transform child in hull)
                if (child.name.Contains("bubble")) CheckCoreInside(hull,child);
            CaptureOverdraw(sample, camera, rt, directory);
            yield return Wait(() => sample.Phase == GelBlobAI.State.Flight && sample.Progress > .4f, 2f); yield return null;
            CheckCoreInside(hull, core);
            Capture(camera, rt, directory, "flight");
            // Presentation-only dodge staging. Genuine input dodges/contact are
            // tested separately; here keep the landing empty to see its full splat.
            bool supportedDodge = false;
            var ground = new EnemyGroundQuery(sample.transform);
            for (int side = -1; side <= 1 && !supportedDodge; side += 2)
                if (ground.TrySample(player.position + Vector3.right * (side * 3f), 2f, 2f, 35f, out RaycastHit safe))
                { PlacePlayer(safe.point); supportedDodge = true; }
            Need(supportedDodge, "No safe presentation-only dodge position on real map.");
            yield return Wait(() => sample.Phase == GelBlobAI.State.Recover, 2f); yield return null;
            Vector3 landingFocus = sample.transform.position + Vector3.up * .35f;
            camera.transform.position = ChooseAuthoredView(sample, landingFocus, new Vector3(2.5f, 1.2f, 3.8f));
            camera.transform.LookAt(landingFocus);
            Vector3 framed = camera.WorldToViewportPoint(landingFocus);
            Need(framed.z > camera.nearClipPlane && framed.z < 6f && framed.x > .15f && framed.x < .85f &&
                framed.y > .15f && framed.y < .85f, "Recovery portrait must frame the gel, not distant scenery.");
            CheckCoreInside(hull, core);
            Capture(camera, rt, directory, "landing");
            CaptureOverdraw(sample,camera,rt,directory,"landing-overdraw");
            Vector3 visibleContact = sample.GetComponent<BoxCollider>().ClosestPoint(camera.transform.position);
            sample.GetComponent<Health>().TryApplyDamage(new DamageInfo(1,player.gameObject,visibleContact,sample.transform.position-camera.transform.position)); yield return null;
            Capture(camera, rt, directory, "hit");
            yield return Delay(.065f); Capture(camera,rt,directory,"hit-wave");
            yield return Wait(() => sample.Phase == GelBlobAI.State.Recover && sample.Progress > .5f, 2f); yield return null;
            CheckCoreInside(hull, core); Capture(camera, rt, directory, "recovery-tired");
            yield return Wait(() => sample.Phase == GelBlobAI.State.Recover && sample.Progress > .94f, 1f); yield return null;
            Capture(camera, rt, directory, "recovery-ready");
            sample.GetComponent<Health>().TryApplyDamage(new DamageInfo(1000, player.gameObject, sample.transform.position, Vector3.forward)); yield return Delay(.14f);
            Capture(camera,rt,directory,"death-breakup"); yield return Delay(.16f);
            Capture(camera, rt, directory, "death");
            log("Real-map screenshots: " + directory);
        }
        finally { rt.Release(); Object.Destroy(rt); Object.Destroy(go); }
    }
    static void CheckCoreInside(Transform hull, Transform core)
    {
        // Test the tapered silhouette, not just its enclosing bounding box.
        float[] heights = { 0, .035f, .15f, .33f, .54f, .73f, .86f, .90f };
        float[] radii = { .40f, .51f, .53f, .50f, .42f, .31f, .18f, 0 };
        var properties = new MaterialPropertyBlock(); hull.GetComponent<Renderer>().GetPropertyBlock(properties);
        Vector4 shear = properties.GetVector("_Wobble");
        foreach (MeshFilter part in core.GetComponentsInChildren<MeshFilter>())
            foreach (Vector3 vertex in part.sharedMesh.vertices)
            {
                Vector3 p = hull.InverseTransformPoint(part.transform.TransformPoint(vertex));
                Need(p.y >= -.005f && p.y <= .905f, "Mechanical core exits gel vertically: " + part.name);
                int row = 1; while (row < heights.Length - 1 && heights[row] < p.y) row++;
                float radius = Mathf.Lerp(radii[row - 1], radii[row], Mathf.InverseLerp(heights[row - 1], heights[row], p.y));
                float anchor = Mathf.Clamp01(p.y * 3f);
                float distance = new Vector2(p.x - shear.x * anchor, p.z - shear.z * anchor).magnitude;
                Need(distance <= radius * .935f + .012f, "Mechanical core exits tapered gel silhouette: " + part.name);
            }
    }
    static IEnumerator CaptureAuthoredIdle(GelBlobAI sample)
    {
        string directory = Path.Combine(Path.GetTempPath(), "anca-gel-play");
        var go = new GameObject("QA authored gel lighting camera"); var camera = go.AddComponent<Camera>();
        Camera original = Camera.main; camera.CopyFrom(original); camera.enabled = false;
        var settings = camera.GetUniversalAdditionalCameraData(); var sourceSettings = original.GetUniversalAdditionalCameraData();
        settings.renderPostProcessing = sourceSettings.renderPostProcessing; settings.volumeLayerMask = sourceSettings.volumeLayerMask; settings.renderShadows = true;
        var rt = new RenderTexture(1280, 720, 24); rt.Create();
        try
        {
            yield return null;
            Vector3 focus = sample.transform.position + Vector3.up * .35f;
            camera.transform.position = ChooseAuthoredView(sample, focus, new Vector3(2.5f, 1.2f, 3.8f)); camera.transform.LookAt(focus);
            Capture(camera, rt, directory, sample.name + "-front");
            camera.transform.position = ChooseAuthoredView(sample, focus, new Vector3(-3f, .9f, -3.6f)); camera.transform.LookAt(focus);
            Capture(camera, rt, directory, sample.name + "-back");
        }
        finally { rt.Release(); Object.Destroy(rt); Object.Destroy(go); }
    }
    static Vector3 ChooseAuthoredView(GelBlobAI sample, Vector3 focus, Vector3 preferred)
    {
        var terrain = new EnemyGroundQuery(sample.transform);
        Vector3 best = focus; float bestScore = float.NegativeInfinity;
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < 12; i++)
        {
            Vector3 offset = Quaternion.Euler(0, i * 30, 0) * preferred;
            Vector3 position = focus + offset; bool blocked = false;
            foreach (RaycastHit hit in Physics.SphereCastAll(focus, .15f, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(sample.transform) && !hit.collider.GetComponentInParent<Health>())
                { blocked = true; break; }
            if (terrain.TrySample(position, 30, 60, 89, out RaycastHit floor) && position.y < floor.point.y + .25f) blocked = true;
            if (blocked) continue;
            float score = Vector3.Dot(offset.normalized, preferred.normalized);
            // Some visible foliage has no collision; do not put the capture camera inside it.
            foreach (MeshRenderer renderer in renderers)
                if (renderer.enabled && renderer.sharedMaterial && renderer.sharedMaterial.renderQueue < 2500 &&
                    !renderer.GetComponentInParent<Health>() && renderer.bounds.size.magnitude < 35f && renderer.bounds.Contains(position))
                { score -= 10; break; }
            if (score > bestScore) { bestScore = score; best = position; }
        }
        Need(bestScore > -5, "No unoccluded actual-map capture view for " + sample.name);
        return best;
    }
    static void Capture(Camera camera, RenderTexture target, string directory, string filename)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(directory, filename + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; Object.Destroy(image);
    }
    static void CaptureOverdraw(GelBlobAI actor, Camera camera, RenderTexture rt, string directory, string filename = "transparent-overdraw")
    {
        var rendering = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
        var mode = rendering.overdrawMode; int max = rendering.maxOverdrawCount, mask = camera.cullingMask;
        var clear = camera.clearFlags; Color background = camera.backgroundColor;
        var layers = new System.Collections.Generic.Dictionary<GameObject, int>();
        try
        {
            foreach (Transform part in actor.GetComponentsInChildren<Transform>(true))
            { layers[part.gameObject] = part.gameObject.layer; part.gameObject.layer = 30; }
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            rendering.overdrawMode = DebugOverdrawMode.Transparent; rendering.maxOverdrawCount = 8;
            Capture(camera, rt, directory, filename);
            foreach (Renderer part in actor.GetComponentsInChildren<Renderer>())
                Need(!ShaderUtil.ShaderHasError(part.sharedMaterial.shader), "Overdraw shader variant failed compilation.");
        }
        finally
        {
            foreach (var entry in layers) if (entry.Key) entry.Key.layer = entry.Value;
            camera.cullingMask = mask; camera.clearFlags = clear; camera.backgroundColor = background;
            rendering.overdrawMode = mode; rendering.maxOverdrawCount = max;
        }
    }
}
