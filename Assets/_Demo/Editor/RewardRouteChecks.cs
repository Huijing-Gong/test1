using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Bounded, explicit QA only. Authored main scene and real save slots are never edited.
[InitializeOnLoad]
public static class RewardRouteChecks
{
    const string Main="Assets/_Demo/Scenes/OverworldBaseline.unity", QA="Assets/_Demo/Scenes/RewardRoutesQA.unity";
    const string State="QA.RewardRoutes20261001.", Report="ToolReports/RewardRoutes20261001/";
    static BaselineFlow flow; static BaselineWorld world; static PlayerMovement player; static PlayerVitals vitals;
    static BaselineLoadout loadout; static OverworldV2Layout layout; static ThirdPersonCamera camera;
    static Keyboard keyboard, originalKeyboard; static Mouse mouse, originalMouse;
    static bool keyboardEnabled,mouseEnabled,background; static InputSettings.BackgroundBehavior behavior;
    static KeyboardState desired; static MouseState desiredMouse; static Stack<IEnumerator> routines=new Stack<IEnumerator>();
    static double resume,deadline; static int lastFrame=-1; static string mode,phase;
    static float minimum,swimTime,climbTime,sprintTime,walkTime,spent; static Vector3 segmentStart;
    public static bool Running { get; private set; }
    public static string Status=>Running?mode+" / "+phase:SessionState.GetString(State+"status","Idle");
    static RewardRouteChecks()
    {
        EditorApplication.playModeStateChanged+=Changed;
        AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(Running)Stop("INCOMPLETE assembly reload");};
    }
    static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");}
    static string Slot(string key)=>Hash(PlayerPrefs.HasKey(key)?PlayerPrefs.GetString(key):"<missing>");
    static void RealCheck()
    {
        var prefix=SessionState.GetString(State+"real","");
        if(Slot(prefix+".primary")!=SessionState.GetString(State+"primary","") || Slot(prefix+".backup")!=SessionState.GetString(State+"backup",""))
            throw new InvalidOperationException("Real save slot changed.");
    }
    public static void Prepare(string requestedMode)
    {
        if(requestedMode!="baseline"&&requestedMode!="prototype"&&requestedMode!="manual"&&requestedMode!="chest-save")throw new ArgumentException("Unknown mode");
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||scene.path!=Main||scene.isDirty||SceneManager.sceneCount!=1||SessionState.GetBool(State+"prepared",false))
            throw new InvalidOperationException("Requires exclusive clean main scene in Edit mode.");
        if(File.Exists(QA))throw new InvalidOperationException("Existing QA copy; do not overwrite it.");
        var original=Object.FindFirstObjectByType<BaselineFlow>();
        var prefix="QA.RewardRoutes."+Guid.NewGuid().ToString("N");
        SessionState.SetString(State+"real",original.saveKeyPrefix);
        SessionState.SetString(State+"primary",Slot(original.saveKeyPrefix+".primary"));SessionState.SetString(State+"backup",Slot(original.saveKeyPrefix+".backup"));
        SessionState.SetString(State+"main",Hash(File.ReadAllText(Main)));
        SessionState.SetString(State+"prefix",prefix);SessionState.SetString(State+"mode",requestedMode);
        SessionState.SetString(State+"override",EditorSceneManager.playModeStartScene?AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene):"");
        SessionState.SetFloat(State+"time",Time.timeScale);SessionState.SetBool(State+"background",Application.runInBackground);
        SessionState.SetInt(State+"cursor",(int)Cursor.lockState);SessionState.SetBool(State+"cursorVisible",Cursor.visible);
        Directory.CreateDirectory(Report);Directory.CreateDirectory(Report+"Media");
        File.WriteAllText(Report+requestedMode+".txt","METHOD: isolated saved scene copy, QA prefix, explicit API fixtures and controlled InputSystem keyboard/mouse. No human continuous playthrough claim.\n");
        if(!EditorSceneManager.SaveScene(scene,QA,true))throw new InvalidOperationException("Save QA copy failed");
        Scene copy=default;
        try{
            copy=EditorSceneManager.OpenScene(QA,OpenSceneMode.Additive);
            foreach(var root in copy.GetRootGameObjects())foreach(var f in root.GetComponentsInChildren<BaselineFlow>(true)){f.saveKeyPrefix=prefix;EditorUtility.SetDirty(f);}
            EditorSceneManager.MarkSceneDirty(copy);if(!EditorSceneManager.SaveScene(copy))throw new InvalidOperationException("Save QA prefix failed");
        }finally{SceneManager.SetActiveScene(scene);if(copy.IsValid()&&copy.isLoaded)EditorSceneManager.CloseScene(copy,true);}
        RealCheck();SessionState.SetBool(State+"prepared",true);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(QA);
        SessionState.SetString(State+"status","Prepared "+requestedMode);
    }
    static void Changed(PlayModeStateChange change)
    {
        if(change==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(State+"prepared",false))EditorApplication.delayCall+=Begin;
        if(change==PlayModeStateChange.ExitingPlayMode&&Running)Stop("Stopped");
        if(change==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(State+"prepared",false))Restore();
    }
    static void Begin()
    {
        if(SceneManager.GetActiveScene().path!=QA||SceneManager.sceneCount!=1)throw new InvalidOperationException("Wrong QA scene");
        flow=Object.FindFirstObjectByType<BaselineFlow>();world=flow.GetComponent<BaselineWorld>();player=world.player.GetComponent<PlayerMovement>();
        vitals=player.GetComponent<PlayerVitals>();loadout=player.GetComponent<BaselineLoadout>();layout=flow.GetComponent<OverworldV2Layout>();camera=Camera.main.GetComponent<ThirdPersonCamera>();
        if(flow.Started||flow.saveKeyPrefix!=SessionState.GetString(State+"prefix","")||flow.saveKeyPrefix==SessionState.GetString(State+"real",""))throw new InvalidOperationException("Wrong save isolation");
        RealCheck();mode=SessionState.GetString(State+"mode","");phase="initializing";
        if(mode=="manual"){SessionState.SetString(State+"status","Manual QA title; original devices retained");return;}
        originalKeyboard=Keyboard.current;originalMouse=Mouse.current;keyboardEnabled=originalKeyboard!=null&&originalKeyboard.enabled;mouseEnabled=originalMouse!=null&&originalMouse.enabled;
        behavior=InputSystem.settings.backgroundBehavior;background=Application.runInBackground;
        if(keyboardEnabled)InputSystem.DisableDevice(originalKeyboard);if(mouseEnabled)InputSystem.DisableDevice(originalMouse);
        keyboard=InputSystem.AddDevice<Keyboard>("RewardRouteQAKeyboard");mouse=InputSystem.AddDevice<Mouse>("RewardRouteQAMouse");keyboard.MakeCurrent();mouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;Application.runInBackground=true;desired=new KeyboardState();desiredMouse=new MouseState();
        InputSystem.onBeforeUpdate+=Input;Running=true;routines.Clear();routines.Push(mode=="chest-save"?ChestSaveRegression():Run());resume=0;lastFrame=-1;deadline=EditorApplication.timeSinceStartup+300;
        EditorApplication.update+=Tick;
    }
    static void Input(){if(Running&&keyboard!=null&&InputState.currentUpdateType==InputUpdateType.Dynamic){InputSystem.QueueStateEvent(keyboard,desired);InputSystem.QueueStateEvent(mouse,desiredMouse);}}
    static void Keys(params Key[] keys){desired=new KeyboardState(keys);}
    static void Log(string message){File.AppendAllText(Report+mode+".txt",message+"\n");}
    static void Check(bool ok,string message){Log((ok?"PASS ":"FAIL ")+message);if(!ok)throw new InvalidOperationException(message);}
    static float Planar(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
    static float Heading(Vector3 a,Vector3 b)=>Mathf.Atan2(b.x-a.x,b.z-a.z)*Mathf.Rad2Deg;
    static void Place(Vector3 at,Vector3 target){Keys();player.Relocate(at,Quaternion.Euler(0,Heading(at,target),0));camera.ResetOrbit(Heading(at,target),14);Physics.SyncTransforms();}
    static void Capture(string name){ScreenCapture.CaptureScreenshot(Report+"Media/"+mode+"-"+name+"-game-view.png");Log("CAPTURE Game View "+name+" player="+player.transform.position);}
    static IEnumerator Press(Key key){Keys(key);yield return .12f;Keys();yield return .25f;}
    static IEnumerator Segment(string name,Vector3 start,Vector3 target,bool sprint,bool walkingGraph)
    {
        phase=name;Place(start,target);vitals.Refill();yield return .6f;
        minimum=vitals.Stamina;swimTime=climbTime=sprintTime=walkTime=spent=0;float previousStamina=vitals.Stamina;segmentStart=player.transform.position;
        var path=walkingGraph?world.WalkingRoute(player.transform.position,target):new[]{target};Check(path!=null&&path.Length>0,name+" route exists");
        // WalkingRoute deliberately ends beside its destination. Measure that actual approach.
        if(walkingGraph){target=path[path.Length-1];Log("APPROACH endpoint="+target+" campDistance="+Planar(target,layout.CampPosition));}
        int next=0;double until=EditorApplication.timeSinceStartup+60;bool captured=false;
        while(Planar(player.transform.position,target)>.65f&&EditorApplication.timeSinceStartup<until)
        {
            while(next<path.Length-1&&Planar(player.transform.position,path[next])<.7f)next++;
            if(walkingGraph)camera.ResetOrbit(Heading(player.transform.position,path[next]),14);Keys(sprint?new[]{Key.W,Key.LeftShift}:new[]{Key.W});
            minimum=Mathf.Min(minimum,vitals.Stamina);
            spent+=Mathf.Max(0,previousStamina-vitals.Stamina);previousStamina=vitals.Stamina;
            if(player.SwimmingActive)swimTime+=Time.deltaTime;if(player.ClimbingActive||player.MantlingActive)climbTime+=Time.deltaTime;if(player.SprintingActive)sprintTime+=Time.deltaTime;
            if(sprint&&!player.SprintingActive&&player.IsStableGrounded)walkTime+=Time.deltaTime;
            if(!captured&&(swimTime>3||sprintTime>5)){Capture(name+"-middle");captured=true;}
            yield return null;
        }
        Keys();yield return .7f;
        Log("MEASURE "+name+" minStamina="+minimum+" swimSeconds="+swimTime+" climbSeconds="+climbTime+" sprintSeconds="+sprintTime+" exhaustedWalkSeconds="+walkTime+" staminaSpent="+spent+" displacement="+Planar(segmentStart,player.transform.position));
        Check(Planar(player.transform.position,target)<1&&player.IsStableGrounded,name+" reached landing pos="+player.transform.position);
        if(walkingGraph)Check(vitals.IsAtCamp,name+" arrived inside existing camp rest range");
        Capture(name+"-landing");yield return .3f;
    }
    static IEnumerator Run()
    {
        yield return .7f;flow.NewGame();yield return .4f;flow.Tutorial.Skip();yield return .5f;
        Check(flow.Started&&!flow.Tutorial.Active,"new-game/skip API releases inputs");
        Check(loadout.SprintDrainMultiplier==1&&!flow.ToggleEquipment(1),"unowned reward provides no effect and cannot toggle");
        GameSession.Instance.RestoreProgress(true,true,false);
        // Tutorial skip and opening camera control are separate. Let the authored
        // opening finish before a controlled traversal comparison starts.
        double openingUntil=EditorApplication.timeSinceStartup+18;
        while(camera.CinematicControlled&&EditorApplication.timeSinceStartup<openingUntil)yield return null;
        Check(!camera.CinematicControlled,"opening finished before route comparison");
        var chest=Array.Find(Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None),x=>x.action==BaselineInteraction.ActionKind.Chest&&x.index==1);
        world.Interact(chest);yield return .3f;Check(world.opened[1]&&loadout.IsEquipped(1),"ridge reward API fixture owned/equipped");
        Capture("ridge-reward");yield return .2f;
        if(flow.rewards.IsOpen)flow.rewards.Dismiss();yield return .3f;
        world.Interact(chest);yield return .2f;Check(world.opened[1]&&loadout.SprintDrainMultiplier==.7f,"repeat chest receipt is idempotent; effect does not stack");
        if(mode=="baseline"){Check(flow.ToggleEquipment(1)&&loadout.SprintDrainMultiplier==1,"comparison disables only Ridge charm");}
        Vector3 ridge;Check(world.NavigationGround(new Vector3(-63.34f,4.77f,-43.21f),out ridge),"west return road has ground");
        yield return Segment("ridge-to-camp",ridge,layout.CampPosition,true,true);
        if(mode=="prototype")yield return Regression();
        RealCheck();Log("PASS route measurement complete; no natural full playthrough claim");
        if(mode=="baseline")
        {
            // Exactly two comparison cases in one isolated Play session; no additional feature work.
            mode="prototype";SessionState.SetString(State+"mode",mode);
            File.WriteAllText(Report+mode+".txt","METHOD: same isolated scene/save and actual InputSystem controller, charm equipped comparison plus regressions. API fixtures labelled; not a human playthrough.\n");
            yield return Run();yield break;
        }
        SessionState.SetString(State+"status","Completed "+mode);Stop("Completed");EditorApplication.isPlaying=false;
    }
    // Opt-in narrow regression; no route, trial, combat or build rerun.
    static IEnumerator ChestSaveRegression()
    {
        phase="chest save refusal/retry";
        Check(flow.saveKeyPrefix.StartsWith("QA.RewardRoutes.",StringComparison.Ordinal)&&
            flow.saveKeyPrefix==SessionState.GetString(State+"prefix","")&&
            flow.saveKeyPrefix!=SessionState.GetString(State+"real",""),"disposable QA save prefix required");
        Log("METHOD: NewGame/Skip/sword/approach are API fixtures. Refusal uses original DialogueUI completion APIs synchronously; retry/close/repeated interaction use E/Enter input. Not natural discovery or disk/quota fault injection.");
        yield return .7f;flow.NewGame();yield return .4f;flow.Tutorial.Skip();yield return .5f;
        GameSession.Instance.RestoreProgress(true,true,false);
        double openingUntil=EditorApplication.timeSinceStartup+18;
        while(camera.CinematicControlled&&EditorApplication.timeSinceStartup<openingUntil)yield return null;
        Check(!camera.CinematicControlled,"authored opening finished before chest/input checks");
        var chest=Array.Find(Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None),
            x=>x.action==BaselineInteraction.ActionKind.Chest&&x.index==1);
        Check(chest&&flow.rewards,"Ridge chest and existing reward presenter available");
        Check(world.NavigationGround(chest.transform.position+Vector3.forward*1.3f,out var ground),"existing chest approach has ground");
        Place(ground,chest.transform.position);yield return .8f;
        var interaction=player.GetComponent<PlayerInteraction>();var dialogue=Object.FindFirstObjectByType<DialogueUI>();
        var npc=chest.GetComponent<NPCDialogue>();var visual=chest.GetComponent<BaselineChestVisual>();
        Check(interaction&&dialogue&&visual&&visual.dissolve&&interaction.SelectedNPC==npc,"original scanner selects intact Ridge chest");
        Check(!world.opened[1]&&!flow.Progress.opened[1]&&!loadout.OwnsEquipment(1)&&flow.SaveNow(),"unclaimed chest starts with valid QA save");
        string primaryKey=flow.saveKeyPrefix+".primary",backupKey=flow.saveKeyPrefix+".backup";
        string primary=PlayerPrefs.GetString(primaryKey),backup=PlayerPrefs.GetString(backupKey);
        string progress=JsonUtility.ToJson(flow.Progress);int damage=loadout.DamageBonus,events=0;
        float multiplier=loadout.SprintDrainMultiplier,dissolve=visual.dissolve.Progress;
        Quaternion lid=chest.lid?chest.lid.localRotation:Quaternion.identity;
        CursorLockMode cursor=Cursor.lockState;bool cursorVisible=Cursor.visible;
        Action onChanged=()=>events++;world.StateChanged+=onChanged;
        try
        {
            // No yield while QA envelopes are changed: restoration also runs on assertion failure.
            const string rejected="{\"formatVersion\":999}";
            try
            {
                PlayerPrefs.SetString(primaryKey,rejected);
                for(int attempt=0;attempt<2;attempt++)
                {
                    Check(dialogue.BeginDialogue(npc,player,camera),"refused chest can reopen original dialogue");
                    for(int line=0;line<npc.LineCount;line++)dialogue.AdvanceDialogue();
                    Check(!world.opened[1]&&!flow.Progress.opened[1]&&!loadout.OwnsEquipment(1)&&
                        loadout.DamageBonus==damage&&loadout.SprintDrainMultiplier==multiplier&&
                        JsonUtility.ToJson(flow.Progress)==progress&&events==0,"save refusal rolls back both receipts and emits no progress/equipment gain");
                    Check(!flow.rewards.IsOpen&&!dialogue.IsDialogueActive&&!dialogue.IsRewardPresentationActive&&
                        !player.IsGameplayInputLocked&&!camera.IsLookInputLocked&&Cursor.lockState==cursor&&Cursor.visible==cursorVisible,
                        "refusal leaves original dialogue closed and input/cursor restored");
                    Check(npc.IsInteractable&&!visual.dissolve.IsDissolving&&visual.dissolve.Progress==dissolve&&
                        (!chest.lid||Quaternion.Angle(chest.lid.localRotation,lid)<.01f),"refusal preserves chest visuals and retry interaction");
                    var notice=typeof(BaselineWorld).GetField("notice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    Check(flow.Status.Contains("Cannot replace an incompatible save")&&(string)notice.GetValue(world)==flow.Status,
                        "refusal retains save error notice instead of reward success notice");
                    Check(PlayerPrefs.GetString(primaryKey)==rejected&&PlayerPrefs.GetString(backupKey)==backup,"refusal preserves rejected QA primary and valid backup");
                }
            }
            finally{PlayerPrefs.SetString(primaryKey,primary);PlayerPrefs.SetString(backupKey,backup);PlayerPrefs.Save();}
            yield return .4f;
            Check(interaction.SelectedNPC==npc,"refused chest remains selectable for actual E retry");
            yield return Press(Key.E);Check(dialogue.ActiveNPC==npc&&dialogue.IsDialogueActive,"actual E reopens refused chest");
            for(int line=0;line<npc.LineCount;line++)yield return Press(Key.E);
            Check(world.opened[1]&&flow.Progress.opened[1]&&loadout.IsEquipped(1)&&events==1&&
                loadout.DamageBonus==damage&&loadout.SprintDrainMultiplier==.7f&&flow.rewards.IsOpen,
                "successful E retry commits one Ridge receipt before reward presentation");
            Check(player.IsGameplayInputLocked&&camera.IsLookInputLocked&&dialogue.IsRewardPresentationActive&&
                !visual.dissolve.IsDissolving,"saved reward holds inputs and delays dissolve until dismissal");
            Check(BaselineSaveStore.TryLoad(flow.saveKeyPrefix,out var saved,out var message)&&saved.opened[1]&&
                saved.unequippedMask==flow.Progress.unequippedMask,"successful receipt is readable from QA save: "+message);
            yield return Press(Key.Enter);
            Check(!flow.rewards.IsOpen&&!dialogue.IsRewardPresentationActive&&!player.IsGameplayInputLocked&&
                !camera.IsLookInputLocked&&Cursor.lockState==cursor&&Cursor.visible==cursorVisible&&
                (visual.dissolve.IsDissolving||visual.dissolve.IsDissolved)&&!npc.IsInteractable,
                "actual Enter closes once, restores input/cursor and finalizes chest visual");
            yield return Press(Key.E);yield return Press(Key.E);
            Check(events==1&&!flow.rewards.IsOpen&&!dialogue.IsDialogueActive&&world.opened[1]&&
                loadout.DamageBonus==damage&&loadout.SprintDrainMultiplier==.7f,"repeat E interaction grants and displays no second reward");
            Check(flow.ContinueGame(),"continue reloads successful QA receipt");yield return .6f;
            Check(world.opened[1]&&flow.Progress.opened[1]&&loadout.IsEquipped(1)&&loadout.DamageBonus==damage&&
                loadout.SprintDrainMultiplier==.7f&&!flow.rewards.IsOpen&&!player.IsGameplayInputLocked&&
                !camera.IsLookInputLocked&&visual.dissolve.IsDissolved&&!npc.IsInteractable,"Continue preserves receipt/effect/closed chest without replaying reward");
            RealCheck();Log("PENDING actual OS disk failure and browser quota exhaustion; unsupported-format QA refusal only.");
        }
        finally{world.StateChanged-=onChanged;}
        Stop("Completed chest-save");EditorApplication.isPlaying=false;
    }

    static IEnumerator Regression()
    {
        phase="equipment/save/API regression";flow.SetMenu(true);
        Check(flow.ToggleEquipment(1)&&!loadout.IsEquipped(1)&&loadout.SprintDrainMultiplier==1,"unequip removes efficiency immediately");
        Check(flow.SaveNow()&&flow.ContinueGame(),"save/continue APIs accept existing equipment mask");yield return .6f;
        Check(world.opened[1]&&!loadout.IsEquipped(1)&&loadout.SprintDrainMultiplier==1,"continue preserves receipt and unequipped choice");
        flow.SetMenu(true);Check(flow.ToggleEquipment(1)&&loadout.SprintDrainMultiplier==.7f,"re-equip restores efficiency");
        string primary=PlayerPrefs.GetString(flow.saveKeyPrefix+".primary"),backup=PlayerPrefs.GetString(flow.saveKeyPrefix+".backup");int mask=flow.Progress.unequippedMask;
        try{PlayerPrefs.SetString(flow.saveKeyPrefix+".primary","{\"formatVersion\":999}");Check(!flow.ToggleEquipment(1)&&flow.Progress.unequippedMask==mask&&loadout.IsEquipped(1),"actual SaveStore refusal rolls back equip choice");}
        finally{PlayerPrefs.SetString(flow.saveKeyPrefix+".primary",primary);PlayerPrefs.SetString(flow.saveKeyPrefix+".backup",backup);PlayerPrefs.Save();}
        flow.SetMenu(false);yield return .4f;
        yield return Press(Key.J);Check(world.JournalOpen,"actual J input opens journal with effect descriptions");Capture("journal");yield return .2f;
        yield return Press(Key.J);Check(!world.JournalOpen,"actual J closes journal");
        yield return Press(Key.Escape);Check(flow.MenuOpen,"actual Esc opens equipment menu");Capture("equipment-menu");yield return .3f;
        yield return Press(Key.Escape);Check(!flow.MenuOpen,"actual Esc resumes play");
        // Existing version/schema fixtures, not a new migration or synthetic gameplay victory.
        BaselineSaveStoreChecks.Run();Check(true,"existing save roundtrip/corruption/v1-v4 migration suite passed");
        world.opened[0]=world.opened[2]=true;flow.Progress.trialACompleted=flow.Progress.trialBCompleted=true;
        Check(flow.ClaimTrialReward(0),"trial A completion fixture claims existing reward");yield return .2f;if(flow.rewards.IsOpen)flow.rewards.Dismiss();yield return .3f;
        Check(!flow.ClaimTrialReward(0),"trial A reward remains idempotent");
        Check(flow.ClaimTrialReward(1),"trial B completion fixture claims existing reward");yield return .2f;if(flow.rewards.IsOpen)flow.rewards.Dismiss();yield return .3f;
        Check(!flow.ClaimTrialReward(1)&&loadout.DamageBonus==18,"all five retain identities; other four damage contributions total 18");
        flow.SetMenu(true);for(int i=0;i<5;i++)Check(flow.ToggleEquipment(i),"equipment toggle "+i);flow.SetMenu(false);yield return .3f;
        Check(loadout.DamageBonus==0&&loadout.SprintDrainMultiplier==1,"all unequipped returns practice-sword/resource baseline");
        string owned=JsonUtility.ToJson(flow.Progress);var hp=player.GetComponent<Health>();vitals.ApplyEnvironmentDamage(500);yield return .8f;
        Check(!hp.IsDead&&hp.CurrentHealth==hp.MaxHealth&&Planar(player.transform.position,layout.CampPosition)<1&&flow.Progress.unequippedMask==31&&world.opened[1],"death recovery preserves receipts/mask and refills camp resources");
        // Native worlds use their own controllers. Enter/return APIs are fixtures, not A/B completion input tests.
        var travel=SideWorldTravel.Instance;
        travel.Enter(false);yield return 5f;Check(travel.IsAway&&!player.gameObject.activeInHierarchy,"native A enter API suspends main-world vitals");travel.ReturnToMain();yield return 3f;
        Check(!travel.IsAway&&loadout.SprintDrainMultiplier==1&&world.opened[1]&&flow.Progress.unequippedMask==31,"A return preserves reward state");
        travel.Enter(true);yield return 5f;Check(travel.IsAway&&!player.gameObject.activeInHierarchy,"native B enter API suspends main-world vitals");travel.ReturnToMain();yield return 3f;
        Check(!travel.IsAway&&flow.Progress.unequippedMask==31,"B return preserves reward state");
        // Optional ending still accepts the initial sword. Only attacks use real input/animation/hit windows.
        Check(flow.EnterEnding(),"initial sword with all rewards unequipped can enter passive final figure room");yield return 1f;
        var target=GameObject.Find("Passive final figure");Check(target,"passive figure exists");
        Vector3 start=player.transform.position;camera.ResetOrbit(Heading(start,target.transform.position),14);Keys(Key.W);
        double until=EditorApplication.timeSinceStartup+10;
        while(Planar(player.transform.position,target.transform.position)>1.1f&&EditorApplication.timeSinceStartup<until)yield return null;
        Keys();yield return .5f;var combat=player.GetComponent<PlayerCombat>();int before=combat.SuccessfulHitCount;
        Log("COMBAT approach="+player.transform.position+" forward="+player.transform.forward+" target="+target.transform.position+" grounded="+player.IsStableGrounded+" canAttack="+combat.CanAttackNow+" cursor="+Cursor.lockState+" mouse="+Mouse.current?.name+" health="+target.GetComponent<Health>().CurrentHealth);
        for(int i=0;i<12&&!flow.VictoryOpen;i++)
        {
            // Queue state in Dynamic input update, matching the keyboard driver.
            desiredMouse=new MouseState().WithButton(MouseButton.Left);yield return .12f;
            desiredMouse=new MouseState();yield return 1.7f;
            Log("COMBAT click="+i+" hits="+combat.SuccessfulHitCount+" health="+target.GetComponent<Health>().CurrentHealth+" canAttack="+combat.CanAttackNow+" cursor="+Cursor.lockState);
        }
        Check(combat.SuccessfulHitCount>before&&flow.VictoryOpen&&flow.Progress.bossDefeated,"injected left-click actual sword hit windows defeat passive figure with initial sword; not a Boss playthrough");
        Capture("initial-sword-victory");yield return .3f;
    }
    public static void QueueBuild()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||SceneManager.GetActiveScene().isDirty||SessionState.GetBool(State+"prepared",false))throw new InvalidOperationException("Build requires clean restored Edit mode");
        if(PlayerSettings.WebGL.compressionFormat!=WebGLCompressionFormat.Gzip)throw new InvalidOperationException("Preserve existing Gzip setting");
        const string output="Builds/RewardRoutes20261001";if(Directory.Exists(output))throw new InvalidOperationException("Build output exists; do not overwrite it");
        File.WriteAllText(Report+"build-status.txt","Queued");
        EditorApplication.delayCall+=()=>{
            File.WriteAllText(Report+"build-status.txt","Building");
            try{
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Main,SideWorldTravel.BattlePath,SideWorldTravel.SideGameBPath},target=BuildTarget.WebGL,locationPathName=output,options=BuildOptions.DetailedBuildReport});
                File.WriteAllText(Report+"build-summary.txt",report.summary.result+"\nErrors="+report.summary.totalErrors+" Warnings="+report.summary.totalWarnings+"\nBytes="+report.summary.totalSize+" Duration="+report.summary.totalTime+"\nOutput="+output);
                File.WriteAllText(Report+"build-status.txt",report.summary.result.ToString());
            }catch(Exception e){File.WriteAllText(Report+"build-status.txt","Failed "+e);}
        };
    }
    static void Tick()
    {
        if(!Running)return;
        try{
            if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("QA deadline/play exit");
            if(EditorApplication.timeSinceStartup<resume||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            if(routines.Count==0){Stop("Completed");return;}
            var top=routines.Peek();if(!top.MoveNext()){if(Running&&routines.Count>0&&ReferenceEquals(routines.Peek(),top))routines.Pop();return;}
            if(top.Current is IEnumerator nested)routines.Push(nested);else if(top.Current is float seconds)resume=EditorApplication.timeSinceStartup+seconds;
        }catch(Exception e){Log("FAIL "+e);Stop("FAIL "+e.Message);EditorApplication.isPlaying=false;}
    }
    static void Stop(string status)
    {
        InputSystem.onBeforeUpdate-=Input;EditorApplication.update-=Tick;Running=false;routines.Clear();
        if(keyboard!=null){InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.RemoveDevice(keyboard);keyboard=null;}if(mouse!=null){InputSystem.RemoveDevice(mouse);mouse=null;}
        if(originalKeyboard!=null){if(keyboardEnabled)InputSystem.EnableDevice(originalKeyboard);originalKeyboard.MakeCurrent();}
        if(originalMouse!=null){if(mouseEnabled)InputSystem.EnableDevice(originalMouse);originalMouse.MakeCurrent();}
        InputSystem.settings.backgroundBehavior=behavior;Application.runInBackground=background;SessionState.SetString(State+"status",status);
    }
    public static void Restore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first");
        if(!SessionState.GetBool(State+"prepared",false))return;
        RealCheck();if(Hash(File.ReadAllText(Main))!=SessionState.GetString(State+"main",""))throw new InvalidOperationException("Main scene changed");
        string previous=SessionState.GetString(State+"override","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previous)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Time.timeScale=SessionState.GetFloat(State+"time",1);Application.runInBackground=SessionState.GetBool(State+"background",false);
        Cursor.lockState=(CursorLockMode)SessionState.GetInt(State+"cursor",0);Cursor.visible=SessionState.GetBool(State+"cursorVisible",true);
        BaselineSaveStore.Clear(SessionState.GetString(State+"prefix",""));
        AssetDatabase.DeleteAsset(QA);SessionState.SetBool(State+"prepared",false);
        File.AppendAllText(Report+SessionState.GetString(State+"mode","")+".txt","RESTORED real primary/backup SHA256 unchanged; main scene unchanged; QA copy/slot cleared; original start/time/cursor/background restored.\n");
    }
}
