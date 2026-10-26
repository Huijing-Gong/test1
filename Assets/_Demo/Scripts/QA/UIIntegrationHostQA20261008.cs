#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using SamProduction.BossArena;

// Opt-in host API + real PhysX regression. No hardware, natural play, or perf claim.
[DefaultExecutionOrder(-9000)]
public sealed class UIIntegrationHostQA20261008 : MonoBehaviour
{
    [Serializable] sealed class Receipt
    {
        public string scope = "HOST_API_AND_REAL_CAPSULE_FIXTURES_NOT_NATIVE_INPUT_OR_NATURAL_PLAY";
        public string unity, version, prefix, error; public bool passed, qaStoreCleared;
        public string[] assertions, hostEvents; public int assertionsPassed;
    }
    const string Main = "Assets/Editor/UIIntegrationProbe20261008.unity";
    const float Step = 1f / 120;
    readonly List<string> assertions = new List<string>();
    readonly List<string> hostEvents = new List<string>();
    string prefix, output; BaselineFlow flow; BaselineSamBridge bridge; ArenaSession arena;
    Receipt receipt;
    double deadline;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--takeover-integration-checks") < 0) return;
        var go = new GameObject("Opt-in fresh takeover integration checks");
        DontDestroyOnLoad(go); go.AddComponent<UIIntegrationHostQA20261008>();
    }
    void Awake() { DontDestroyOnLoad(gameObject); }
    IEnumerator Start()
    {
        receipt = new Receipt { unity = Application.unityVersion, version = Application.version };
        try
        {
            output = Path.GetFullPath("ToolReports/UIIntegration20261008/Host02/integration-checks.json");
            if (File.Exists(output)) throw new IOException("Preserve previous result");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            prefix = "QA.Takeover.IntegrationA04." + Guid.NewGuid().ToString("N") + ".Save";
            receipt.prefix = prefix;
            // The seed and every later write use the unique QA prefix only.
            var seed = new BaselineSaveData { introCompleted = true, hasSword = true, openingCompleted = true };
            Require(BaselineSaveStore.Save(prefix, seed, out var message), "isolated initial-sword fixture saved: " + message);
            SceneManager.sceneLoaded += PrepareMain;
        }
        catch (Exception error) { Fail(error); yield break; }
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Main,new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        IEnumerator tests = Cases();
        while (true)
        {
            bool next;
            try { next = tests.MoveNext(); }
            catch (Exception error) { Fail(error); yield break; }
            if (!next) break;
            yield return tests.Current;
        }
        Finish(null);
    }
    void PrepareMain(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != Main) return;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<BaselineFlow>(true))
            { candidate.saveKeyPrefix = prefix; flow = candidate; }
    }
    IEnumerator Cases()
    {
        Require(flow && flow.saveKeyPrefix == prefix && flow.useSamEnding, "saved authored main copy bound to unique runtime QA store");
        Require(flow.ContinueGame(), "API Continue restored isolated initial-sword fixture");
        yield return null; yield return null;
        Require(flow.Started && !flow.BlockWorldInput && GameSession.Instance.HasSword, "initial sword enters unlocked main world");
        var uiChecks=UICases(); while(uiChecks.MoveNext()) yield return uiChecks.Current;
        var cameraChecks=CameraCases(); while(cameraChecks.MoveNext()) yield return cameraChecks.Current;
        bridge = flow.SamBridge; bridge.Observed += hostEvents.Add;
        Require(flow.EnterEnding(), "original host EnterEnding accepted initial sword");
        deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (bridge.Busy) { Timeout(); yield return null; }
        arena = bridge.Session;
        Require(arena && bridge.IsAway && arena.State == ArenaRoundState.Ready && arena.HostRetry != null, "exact additive Sam session bound Ready with host retry");
        Require(bridge.DamageSnapshot == 18 && arena.player.swordDamage == 18, "initial sword damage retained at 18");
        yield return null;yield return null;
        var samUI=arena.GetComponent<BaselineSamUIPresentation>();
        Require(samUI && samUI.Ready && samUI.IntroActive,"teammate Boss/player health art and Guardian dialogue bind to the real Sam session");
        deadline=Time.realtimeSinceStartupAsDouble+8;
        while(!GameObject.Find("Guardian line")) { Timeout();yield return null; }
        Require(GameObject.Find("Guardian line").GetComponent<UnityEngine.UI.Text>().isActiveAndEnabled,"Sam opening dialogue displays its live text before capture");
        yield return Frame("12-sam-intro.png");
        Capture("sam-ready.png");
        Control(); Require(bridge.TryRetry(), "host retry starts fight"); Control();
        Queue(); yield return null;yield return null;
        Require(!samUI.IntroActive && samUI.Ready,"starting existing Sam combat dismisses dialogue and retains live teammate HUD");
        yield return Frame("13-sam-combat-hud.png");Capture("sam-counter.png"); long attempt = arena.AttemptId;
        Require(bridge.TryRetry(), "host retry accepts queued counter round"); Control();
        Tick(default, .8f);
        Require(arena.AttemptId > attempt && !arena.player.CounterQueued && !arena.player.PerfectCounterReady &&
            !arena.defenseFeedback.CounterBladeVisible && Count("player_attack_started") == 0, "retry clears queue/reward/highlight without phantom sword");
        Queue(); Require(bridge.TryReturn(), "host return accepts pending counter");
        deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (bridge.IsAway || bridge.Busy) { Timeout(); yield return null; }
        Require(bridge.LastResourcesPreserved && !SceneManager.GetSceneByPath(BaselineSamBridge.ScenePath).isLoaded &&
            !flow.Progress.bossDefeated && !flow.VictoryOpen, "pending counter return preserves resources and unloads only Sam without victory");
        Require(BaselineSaveStore.TryLoad(prefix, out var returned, out _) && returned.hasSword && !returned.bossDefeated &&
            !returned.trialACompleted && !returned.trialBCompleted, "return QA save preserves sword and side-world progress");
        Require(flow.EnterEnding(), "second host visit accepted");
        deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (bridge.Busy) { Timeout(); yield return null; }
        arena = bridge.Session;
        Require(!arena.player.CounterQueued && !arena.player.PerfectCounterReady && !arena.defenseFeedback.CounterBladeVisible,
            "new visit has no retained counter state");
        Control(); Require(bridge.TryRetry(), "second visit host retry starts fight"); Control();
        Queue(); Contact(arena.player, arena.boss.fighter, arena.player.fighter.maximumHealth + 1);
        Require(arena.State == ArenaRoundState.Defeat && !arena.player.CounterQueued && !flow.Progress.bossDefeated,
            "real lethal capsule clears counter and host defeat grants no ending");
        Require(bridge.TryRetry(), "consumed host defeat permits retry"); Control();
        Contact(arena.boss, arena.player.fighter, arena.boss.fighter.maximumHealth + 1);
        Require(arena.State == ArenaRoundState.Victory && flow.Progress.bossDefeated && GameSession.Instance.BossDefeated &&
            !flow.EndingOutcomes.PendingSave, "controlled real-capsule victory consumed by original durable owner");
        Require(BaselineSaveStore.TryLoad(prefix, out var won, out _) && won.bossDefeated && won.hasSword &&
            !won.trialACompleted && !won.trialBCompleted, "QA victory save keeps initial sword and side-world flags");
        Require(bridge.TryReturn(), "consumed host victory permits return");
        deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (bridge.IsAway || bridge.Busy) { Timeout(); yield return null; }
        Require(bridge.LastResourcesPreserved && flow.VictoryOpen && !SceneManager.GetSceneByPath(BaselineSamBridge.ScenePath).isLoaded,
            "victory presentation opens after Sam unload and resource restore");
        // Visible controlled result is held briefly for screenshots/recorder.
        yield return new WaitForSecondsRealtime(2);
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Main,new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        Require(flow.ContinueGame(), "API Continue after main-scene reload accepts QA victory");
        yield return null; yield return null;
        Require(flow.Progress.bossDefeated && GameSession.Instance.BossDefeated && GameSession.Instance.HasSword,
            "API reload restores both durable ending mirrors and initial sword");
        yield return new WaitForSecondsRealtime(2);
    }
    void Control() { arena.automaticTick = false; arena.view.automaticTick = false; arena.boss.decisionEnabled = false; }
    void Queue()
    {
        Tick(new ArenaCommand { dodge = true }, .0833333f);
        Contact(arena.player, arena.boss.fighter, 10);
        Require(arena.player.fighter.PerfectDodgeCount == 1, "real PhysX contact grants current perfect dodge");
        Tick(default, .43f - arena.player.DodgeState.Age);
        Tick(new ArenaCommand { attack = true }, Step);
        Require(arena.player.CounterQueued && arena.player.Dodging && arena.player.AttackTime < 0, "A04 queued counter waits for original dodge completion");
    }
    void Contact(Component target, ArenaFighter source, float damage)
    {
        Vector3 point = target.transform.position;
        arena.StrikeCapsule(source, arena.NewStrike(), "takeover_host_capsule_fixture",
            point + Vector3.up * .4f, point + Vector3.up * 1.25f, .25f, damage, 1);
    }
    void Tick(ArenaCommand command, float elapsed)
    {
        while (elapsed > 1e-7f) { float part = Mathf.Min(elapsed, Step); arena.Simulate(command, part); elapsed -= part; command = command.WithoutEdges(); }
    }
    int Count(string kind) => arena.Events.Count(e => e.kind == kind);
    void Timeout() { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Host transition exceeded 30 seconds"); }
    void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); assertions.Add(message); }
    void Fail(Exception error) { Debug.LogException(error); Finish(error.ToString()); }
    void Finish(string error)
    {
        SceneManager.sceneLoaded -= PrepareMain;
        if (bridge) bridge.Observed -= hostEvents.Add;
        StartCoroutine(FinishAfterHostUnload(error));
    }
    IEnumerator FinishAfterHostUnload(string error)
    {
        // Let the original host finish its teardown save before clearing this QA prefix.
        if (SceneManager.GetSceneByPath(Main).isLoaded)
        {
            yield return SceneManager.LoadSceneAsync(BaselineSamBridge.ScenePath, LoadSceneMode.Single);
            yield return null; yield return null;
        }
        try { if (prefix != null) { BaselineSaveStore.Clear(prefix); receipt.qaStoreCleared = !BaselineSaveStore.HasAnyData(prefix); } }
        catch (Exception cleanup) { error = (error ?? "") + "\nQA cleanup: " + cleanup; }
        receipt.error = error; receipt.assertions = assertions.ToArray(); receipt.assertionsPassed = assertions.Count;
        receipt.hostEvents = hostEvents.ToArray(); receipt.passed = error == null && receipt.qaStoreCleared;
        if (output != null && !File.Exists(output))
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(receipt, true));
        Debug.Log("TAKEOVER_INTEGRATION " + (receipt.passed ? "PASS" : "FAIL") + " assertions=" + assertions.Count);
        EditorApplication.delayCall += ()=>EditorApplication.ExitPlaymode();
    }

    IEnumerator UICases()
    {
        var world=flow.GetComponent<BaselineWorld>();
        var movement=world.player.GetComponent<PlayerMovement>();
        var look=Camera.main.GetComponent<ThirdPersonCamera>();
        var dialogue=FindFirstObjectByType<DialogueUI>();
        var rio=dialogue.GetComponent<RioDialoguePresentation>();
        Require(rio && flow.GetComponent<BaselineMinimap>() && flow.GetComponent<BaselineQuickGearBar>(),"illustrated dialogue, minimap and quick gear initialized in isolated authored host copy");
        flow.NewGame(); if(flow.Tutorial) flow.Tutorial.Skip();
        yield return null;yield return null;
        var guide=GameObject.Find("IntroNPC").GetComponent<NPCDialogue>();
        Require(rio.Supports(guide) && dialogue.BeginDialogue(guide,movement,look),"original guide begins the teammate animated dialogue through real DialogueUI API");
        yield return new WaitForSecondsRealtime(1.3f); yield return Frame("01-guide-dialogue.png");
        Require(flow.WorldHudHidden && !movement.CanStartInteraction,"dialogue owns input and suppresses exploration HUD");
        for(int i=0;i<12 && dialogue.IsDialogueActive;i++) {dialogue.AdvanceDialogue();yield return new WaitForSecondsRealtime(.35f);}
        Require(!dialogue.IsDialogueActive && flow.rewards.IsOpen && GameSession.Instance.HasSword,"guide completion grants sword and opens teammate reward card exactly once");
        Require(BaselineSaveStore.TryLoad(prefix,out var swordSave,out _) && swordSave.hasSword && swordSave.introCompleted,"actual guide reward is durable in independent QA slot before dismissal");
        yield return Frame("02-practice-sword-reward.png"); flow.rewards.Dismiss();
        yield return new WaitForSecondsRealtime(.7f);yield return null;
        Require(!flow.rewards.IsOpen && !flow.BlockWorldInput,"reward close returns gameplay input");
        var sides=FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None);
        Require(sides.Length==2 && sides.All(s=>s.GetComponent<NPCDialogue>().Repeatable),"both A/B guide interactions remain repeatable for return and re-entry");
        var side=sides[0].GetComponent<NPCDialogue>();
        Require(rio.Supports(side) && dialogue.BeginDialogue(side,movement,look),"side guide binds its teammate illustrated dialogue");
        yield return new WaitForSecondsRealtime(1f);yield return Frame("03-side-guide.png");dialogue.CancelDialogueAnimated();
        yield return new WaitForSecondsRealtime(1.3f);
        Require(!dialogue.IsDialogueActive && !SideWorldTravel.Instance.IsAway,"animated dialogue cancellation stays in main world and restores input");
        yield return Frame("04-main-hud.png");
        bool fog=RenderSettings.fog;
        var renderers=world.player.GetComponentsInChildren<Renderer>();var flags=renderers.Select(x=>x.forceRenderingOff).ToArray();
        Require(flow.ShowMap(),"current-map atlas opens through Flow API");
        yield return new WaitForSecondsRealtime(.7f);System.Type.GetType("SceneAtlasChecks, Assembly-CSharp-Editor").GetMethod("Validate").Invoke(null,null);
        Require(RenderSettings.fog==fog && renderers.Select(x=>x.forceRenderingOff).SequenceEqual(flags),"atlas capture restores main fog and every player renderer");
        Require(Vector3.Distance(flow.GetComponent<BaselineMap>().TowerLandmarkPosition,flow.endingEntrance.position)<.001f,"tower marker follows retained new-map ending entrance");
        yield return Frame("05-region-map.png");flow.SetMenu(false);yield return new WaitForSecondsRealtime(1.1f);
        flow.SetMenu(true);yield return new WaitForSecondsRealtime(1f);yield return Frame("06-pause-journal.png");
        SetField(flow,"menuPage",1);yield return null;yield return Frame("07-equipment-journal.png");
        SetField(flow,"menuPage",2);yield return null;yield return Frame("08-controls-journal.png");
        flow.SetMenu(false);yield return new WaitForSecondsRealtime(.4f);yield return null;
        Require(!flow.MenuOpen && !flow.BlockWorldInput && Time.timeScale>0,"illustrated pause closing animation releases owned locks");
        Require(flow.TogglePracticeSword(),"quick equipment API can unequip owned practice sword");
        Require(!GameSession.Instance.SwordEquipped && !movement.GetComponent<PlayerEquipment>().CanAttack && BaselineSaveStore.TryLoad(prefix,out var unequipped,out _) && (unequipped.unequippedMask&32)!=0,"sword visibility, attack permission and saved unequip bit agree");
        Require(flow.TogglePracticeSword() && GameSession.Instance.SwordEquipped,"practice sword can be re-equipped without re-granting ownership");
        var quick=flow.GetComponent<BaselineQuickGearBar>();
        typeof(BaselineQuickGearBar).GetMethod("SetOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(quick,new object[]{true,true});
        yield return new WaitForSecondsRealtime(.4f);yield return Frame("09-quick-equipment.png");quick.CloseForTransition();
        flow.Progress.trialACompleted=true;flow.SetMenu(true);yield return new WaitForSecondsRealtime(1f);
        Require(flow.ClaimTrialReward(0),"completed A reward commits through existing owner then opens integrated presentation");
        Require(flow.rewards.IsOpen && !flow.ClaimTrialReward(0) && BaselineSaveStore.TryLoad(prefix,out var trial,out _) && trial.trialRewardClaimed[0],"reward cannot be duplicated and remains durable while card is open");
        yield return new WaitForSecondsRealtime(.5f);yield return Frame("10-trial-reward.png");flow.rewards.Dismiss();yield return new WaitForSecondsRealtime(.7f);
        var vitals=movement.GetComponent<PlayerVitals>();var health=movement.GetComponent<Health>();
        Require(vitals.ApplyEnvironmentDamage(health.MaxHealth),"real overworld Health damage triggers teammate death UI");
        yield return new WaitForSecondsRealtime(.7f);
        Require(flow.DeathOpen && health.IsDead && Time.timeScale==0,"death UI holds world until a recovery choice");
        yield return Frame("11-death-choice.png");movement.GetComponent<MainWorldDeathUI>().Retry();yield return null;yield return null;
        Require(!flow.DeathOpen && !health.IsDead && flow.Progress.trialRewardClaimed[0] && Time.timeScale>0,"Retry recovery preserves the collected reward and returns control");
        Require(vitals.ApplyEnvironmentDamage(health.MaxHealth),"second real death exercises safe-point choice");
        yield return null;movement.GetComponent<MainWorldDeathUI>().RestoreAtSafePoint();yield return null;yield return null;
        Require(!flow.DeathOpen && !health.IsDead && flow.Progress.trialRewardClaimed[0],"safe-point recovery preserves collected progress");
        // Restore initial-sword-only fixture before the existing Sam combat regression.
        Require(BaselineSaveStore.Save(prefix,new BaselineSaveData {introCompleted=true,hasSword=true,openingCompleted=true},out _) && flow.ContinueGame(),"reset QA-only fixture to initial sword before Sam regression");
        yield return null;yield return null;
    }
    static void SetField(object target,string field,object value)
    { target.GetType().GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(target,value); }
    IEnumerator Frame(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(output),name));
        yield return null;yield return null;
    }

    IEnumerator CameraCases()
    {
        var movement = flow.GetComponent<BaselineWorld>().player.GetComponent<PlayerMovement>();
        var look = Camera.main.GetComponent<ThirdPersonCamera>();
        var controller = movement.GetComponent<CharacterController>();
        var vitals = movement.GetComponent<PlayerVitals>();
        bool oldMovement = movement.enabled, oldVitals = vitals.enabled;
        float height=(float)typeof(ThirdPersonCamera).GetField("targetHeight",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(look);
        Vector3 oldPosition = movement.transform.position;
        Quaternion oldRotation = movement.transform.rotation;
        int capture = Time.captureFramerate;
        GameObject wall = null;
        movement.enabled = false; vitals.enabled = false;
        look.SetLookInputLocked(true);
        try {
            look.ResetOrbit(0,24);
            Vector3 pivot = movement.transform.position + Vector3.up*height;
            var rotation = Quaternion.Euler(24,0,0);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Owned camera collision fixture";
            wall.transform.position = pivot - rotation*Vector3.forward*3;
            wall.transform.localScale = new Vector3(8,8,.3f);
            Physics.SyncTransforms();
            yield return null; yield return null;
            float radius = (float)typeof(ThirdPersonCamera).GetMethod("GetNearClipPlaneRadius",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(look,null);
            Require(Vector3.Distance(look.transform.position,pivot)<3 &&
                !Physics.CheckSphere(look.transform.position,Mathf.Max(.25f,radius),1<<wall.layer,QueryTriggerInteraction.Ignore),
                "real wall pulls camera and complete near-clip sphere in front of obstruction");
            Destroy(wall); wall=null; yield return null;
            yield return new WaitForSecondsRealtime(1f);
            var diagnostic = new System.Text.StringBuilder(); diagnostic.AppendLine("deltaTime="+Time.deltaTime+" scale="+Time.timeScale+" currentDistance="+typeof(ThirdPersonCamera).GetField("currentCameraDistance",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(look)); diagnostic.AppendLine("position="+movement.transform.position+" pivot="+pivot+" camera="+look.transform.position+" distance="+Vector3.Distance(look.transform.position,pivot)); foreach(var hit in Physics.SphereCastAll(pivot,.9f,(look.transform.position-pivot).normalized,7,~0,QueryTriggerInteraction.Ignore)) diagnostic.AppendLine(hit.collider.name+" layer="+hit.collider.gameObject.layer+" d="+hit.distance+" at="+hit.point); File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"camera-diagnostic.txt"),diagnostic.ToString()); Capture("camera-return.png"); Require(Vector3.Distance(look.transform.position,pivot)>5.8f,"camera eases back out after real wall removal");
            typeof(ThirdPersonCamera).GetField("yaw",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(look,90f);
            typeof(ThirdPersonCamera).GetField("pitch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(look,40f);
            yield return null;
            var framed=Camera.main.WorldToViewportPoint(pivot);
            Require(framed.z>0 && Mathf.Abs(framed.x-.5f)<.05f && Mathf.Abs(framed.y-.5f)<.05f,"rapid orbit command keeps the actor centered while position turns");
            controller.enabled=false; movement.transform.position=oldPosition+Vector3.right*12; controller.enabled=true;
            look.ResetOrbit(120,35);
            Require(Quaternion.Angle(look.transform.rotation,Quaternion.Euler(35,120,0))<.001f &&
                Mathf.Abs(Vector3.Distance(look.transform.position,movement.transform.position+Vector3.up*height)-6)<.001f,
                "teleport ResetOrbit immediately restores heading, pitch and distance");
            look.SetCinematicControl(true);
            controller.enabled=false; movement.transform.position+=Vector3.up*.22f; controller.enabled=true;
            look.SetCinematicControl(false);
            float anchor=(float)typeof(ThirdPersonCamera).GetField("followHeight",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(look);
            Require(Mathf.Abs(anchor-movement.transform.position.y-height)<.001f,"cinematic handback resets height history");
            controller.enabled=false; movement.transform.position=oldPosition; controller.enabled=true; Physics.SyncTransforms();
            look.ResetOrbit(0,24); Time.captureFramerate=60;
            float velocity=8.944272f, highest=oldPosition.y; bool pitchStable=true;
            for(int i=0;i<120;i++) {
                if(i>0 && controller.isGrounded && velocity<0) velocity=-2;
                else velocity-=20*Time.deltaTime;
                controller.Move(Vector3.up*velocity*Time.deltaTime);
                highest=Mathf.Max(highest,movement.transform.position.y);
                yield return null;
                pitchStable &= Mathf.Abs(Mathf.DeltaAngle(24,look.transform.eulerAngles.x))<.01f;
            }
            Require(pitchStable, "entire real jump retains mouse-defined orbit");
            Require(highest-oldPosition.y>1.5f && Mathf.Abs(movement.transform.position.y-oldPosition.y)<.15f,
                "real CharacterController jump rises and lands while follow remains stable");
        }
        finally {
            if(wall) Destroy(wall);
            Time.captureFramerate=capture;
            controller.enabled=false; movement.transform.SetPositionAndRotation(oldPosition,oldRotation); controller.enabled=true;
            movement.enabled=oldMovement; vitals.enabled=oldVitals; look.SetLookInputLocked(false); look.ResetOrbit(oldRotation.eulerAngles.y,24);
        }
    }
    void Capture(string name)
    {
        var cam = Camera.main;
        if (!cam) throw new InvalidOperationException("No visible arena camera");
        var previous = cam.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(1280,720,24); var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try { cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name),image.EncodeToPNG()); }
        finally { cam.targetTexture=previous; RenderTexture.active=active; Destroy(rt); Destroy(image); }
    }
}

#endif
