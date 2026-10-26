using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run only on the separately prepared validation scene and its disposable save slot.
[InitializeOnLoad]
public static class OverworldV2TraversalChecks
{
    const string Report="ToolReports/MapV2Migration/";
    const string Media="Docs/MapV2/Migration/";
    static IEnumerator routine;
    static double resume,deadline;
    static int lastFrame;
    static bool walking;
    static Keyboard keyboard, oldKeyboard;
    static Mouse mouse,oldMouse;
    static bool oldKeyboardEnabled,oldMouseEnabled,oldBackground;
    static InputSettings.BackgroundBehavior oldBehavior;
    static BaselineWorld world;
    static BaselineFlow flow;
    static OverworldV2Layout layout;
    static PlayerMovement player;
    static PlayerVitals vitals;
    static ThirdPersonCamera orbit;
    static readonly List<string> log=new List<string>();
    static readonly List<string> samples=new List<string>();
    static string activeCase;
    static float swimSeconds,climbSeconds,minStamina;
    public static bool Running=>routine!=null;
    public static string Status=>Running?activeCase+" "+player.transform.position:SessionState.GetString("V2.Traversal.Result","not run");
    static OverworldV2TraversalChecks()
    {
        EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode)Stop(false);};
        AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(Running){Record("INCOMPLETE assembly reload");Stop(true);}};
    }
    public static void Begin()
    {
        if(!EditorApplication.isPlaying || !SceneManager.GetActiveScene().path.EndsWith("OverworldV2Validation.unity") || Running)throw new InvalidOperationException("Prepared V2 validation Play scene required.");
        flow=Object.FindFirstObjectByType<BaselineFlow>();world=flow.GetComponent<BaselineWorld>();layout=flow.GetComponent<OverworldV2Layout>();
        if(flow.saveKeyPrefix==BaselineSaveStore.DefaultKeyPrefix || flow.saveKeyPrefix=="LakeDomain.Baseline.Save" || !layout)throw new InvalidOperationException("Disposable QA prefix and V2 layout required.");
        player=world.player.GetComponent<PlayerMovement>();vitals=player.GetComponent<PlayerVitals>();orbit=Camera.main.GetComponent<ThirdPersonCamera>();
        Directory.CreateDirectory(Report);Directory.CreateDirectory(Media);log.Clear();samples.Clear();samples.Add("case,time,x,y,z,swimming,grounded,stamina,cameraDistance");
        oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldKeyboardEnabled=oldKeyboard!=null&&oldKeyboard.enabled;oldMouseEnabled=oldMouse!=null&&oldMouse.enabled;
        oldBehavior=InputSystem.settings.backgroundBehavior;oldBackground=Application.runInBackground;
        keyboard=InputSystem.AddDevice<Keyboard>("V2TraversalQAKeyboard");mouse=InputSystem.AddDevice<Mouse>("V2TraversalQAMouse");
        if(oldKeyboardEnabled)InputSystem.DisableDevice(oldKeyboard);if(oldMouseEnabled)InputSystem.DisableDevice(oldMouse);
        keyboard.MakeCurrent();mouse.MakeCurrent();InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;Application.runInBackground=true;
        Record("METHOD controlled starts + actual PlayerMovement, WaterVolume, Vitals; injected W (straight headings set once). Interaction, death/return checks use public APIs. Not a human WASD playthrough. Save prefix="+flow.saveKeyPrefix);
        routine=Run();resume=0;deadline=EditorApplication.timeSinceStartup+240;lastFrame=-1;EditorApplication.update+=Tick;
    }
    static void Record(string text){log.Add(text);File.WriteAllLines(Report+"traversal-checks.txt",log);}
    static void Check(bool ok,string label)=>Record((ok?"PASS ":"FAIL ")+label);
    static void Capture(string name)
    {
        ScreenCapture.CaptureScreenshot(Media+name+"-game-view.png");
        Record("CAPTURE "+name+" Game View; player="+player.transform.position+" camera="+Camera.main.transform.position+" euler="+Camera.main.transform.eulerAngles);
    }
    static float Distance(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
    static float Heading(Vector3 a,Vector3 b)=>Mathf.Atan2(b.x-a.x,b.z-a.z)*Mathf.Rad2Deg;
    static void Place(Vector3 p,float yaw)
    {walking=false;InputSystem.QueueStateEvent(keyboard,new KeyboardState());player.Relocate(p,Quaternion.Euler(0,yaw,0));orbit.ResetOrbit(yaw,14);Physics.SyncTransforms();}
    static IEnumerator Swim(string label,Vector3 destination)
    {
        activeCase=label;swimSeconds=climbSeconds=0;minStamina=vitals.Stamina;
        orbit.ResetOrbit(Heading(player.transform.position,destination),14);yield return .5f;
        walking=true;double stop=EditorApplication.timeSinceStartup+28;bool captured=false;
        while(Distance(player.transform.position,destination)>.55f && EditorApplication.timeSinceStartup<stop)
        {
            if(!captured&&swimSeconds>3){Capture(label+"-middle");captured=true;}
            yield return .05f;
        }
        walking=false;yield return .8f;
        Check(Distance(player.transform.position,destination)<1 && swimSeconds>.5f && player.IsStableGrounded&&!player.SwimmingActive,
            label+" reached="+(Distance(player.transform.position,destination)<1)+" swimmingSeconds="+swimSeconds+" climbSeconds="+climbSeconds+" minimumStamina="+minStamina+" landed="+player.IsStableGrounded+" pos="+player.transform.position);
        Capture(label+"-landing");yield return .3f;
    }
    static IEnumerator Run()
    {
        activeCase="fresh start";flow.NewGame();yield return .6f;flow.Tutorial.Skip();yield return .6f;
        Capture("arrival");yield return .3f;
        Place(layout.CampPosition+new Vector3(0,0,-5),0);yield return .6f;Capture("hub");yield return .3f;
        flow.NewGame();yield return .4f;flow.Tutorial.StartPractice();
        // Set up only this lesson via API; the movement, landing and finish predicates remain real.
        typeof(BaselineCampTutorial).GetProperty("CurrentLesson").SetValue(flow.Tutorial,BaselineCampTutorial.Lesson.Track);
        foreach(var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if(item.action==BaselineInteraction.ActionKind.AcceptTask&&item.index==1)world.Interact(item);
        world.Track(1);yield return .3f;flow.Tutorial.StartPractice();yield return .3f;
        Record("METHOD swim lesson set up by API (not complete Look/Move/Talk/J inputs); actual swim/landing conditions and tutorial finish run normally.");
        Place(layout.IntroShorePosition,Heading(layout.IntroShorePosition,layout.IslandRestPosition));vitals.Refill();yield return .7f;
        Capture("tutorial-shore");yield return .25f;
        var outward=Swim("outward-swim",layout.IslandRestPosition);while(outward.MoveNext())yield return outward.Current;
        Check(flow.Tutorial.CurrentLesson==BaselineCampTutorial.Lesson.ReachIsland&&!layout.BridgeOpen,"real swim and stable landing advance lesson; bridge still closed before inscription");
        if(world.NavigationGround(world.objectives[1].position+new Vector3(2,0,0),out var beside))Place(beside,-90);
        yield return .6f;
        foreach(var item in Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
            if(item.action==BaselineInteraction.ActionKind.Objective&&item.index==1)world.Interact(item);
        yield return .5f;
        Check(!flow.Tutorial.Active&&layout.BridgeOpen&&flow.Progress.openingCompleted,"island objective API after real swim finishes tutorial and opens north bridge");
        Place(layout.IslandRestPosition,180);yield return .4f;
        yield return 5f;
        var backward=Swim("return-swim",layout.IntroShorePosition);while(backward.MoveNext())yield return backward.Current;
        activeCase="dry ground exclusion";Place(layout.IntroShorePosition+new Vector3(0,.2f,-1),0);yield return .8f;
        Check(!player.SwimmingActive&&!player.IsInWater,"dry shore inside coarse WaterVolume excluded; depth="+player.WaterDepth);
        activeCase="camp supply";Place(layout.CampPosition,0);yield return .7f;vitals.ApplyEnvironmentDamage(30);yield return .1f;
        Check(vitals.IsAtCamp&&vitals.TryUseRecovery()&&player.GetComponent<Health>().CurrentHealth==player.GetComponent<Health>().MaxHealth,"R recovery API recognizes new C2 camp and replenishes");
        activeCase="deep water exhaustion";Place(new Vector3(-38,-.68f,1),0);vitals.Refill();yield return .8f;
        Check(player.SwimmingActive,"controlled deep-water position activates swimming");
        // Shorten only this test's private resource state, not any serialized value or game setting.
        typeof(PlayerVitals).GetProperty("Stamina").SetValue(vitals,.05f);yield return .8f;
        Check(Distance(player.transform.position,layout.CampPosition)<1&&!player.GetComponent<Health>().IsDead,"zero-stamina water death respawns at new camp");
        Place(world.objectives[2].position+new Vector3(5,.2f,-5),-45);yield return .7f;Capture("forest-court");yield return .3f;
        Place(new Vector3(54.6f,38.08f,61),90);yield return .8f;Capture("tower-arrival");yield return .3f;
        GameSession.Instance.RestoreProgress(true,true,false);var returnAt=player.transform.position;
        Check(flow.EnterEnding(),"original optional ending-room entry API");yield return .8f;
        Check(flow.endingRoom.IsInside,"ending room instantiated");flow.LeaveEnding();yield return .7f;
        Check(!flow.endingRoom.IsInside&&Vector3.Distance(player.transform.position,returnAt)<.5f&&player.IsStableGrounded,"ending return retained safe tower entry pose");
        activeCase="side A round trip";var entrance=Object.FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None);SideWorldEntrance sideA=null,sideB=null;
        foreach(var e in entrance){if(e.isSideWorldB)sideB=e;else sideA=e;}
        flow.NewGame();yield return .4f;flow.Tutorial.StartPractice();yield return .2f;
        Place(sideA.transform.position+new Vector3(3,.1f,0),-90);yield return .6f;
        world.accepted[0]=true;world.Track(0);world.opened[1]=true;string state=JsonUtility.ToJson(flow.Progress);
        var aPosition=player.transform.position;var travel=SideWorldTravel.Instance;travel.Enter(false);yield return .5f;
        double until=EditorApplication.timeSinceStartup+25;while(travel.Busy&&EditorApplication.timeSinceStartup<until)yield return .1f;
        Check(travel.IsAway&&!travel.IsWebPreview&&!travel.Busy,"side A actual scene loaded via existing API (no completion claim)");
        if(travel.IsAway&&!travel.Busy){travel.ReturnToMain();yield return .5f;until=EditorApplication.timeSinceStartup+20;while(travel.Busy&&EditorApplication.timeSinceStartup<until)yield return .1f;}
        yield return .6f;
        Check(!travel.IsAway&&world.gameObject.activeInHierarchy&&world.Tracked==0&&world.opened[1]&&Distance(player.transform.position,aPosition)<.6f,"side A return preserves tracking/chest/session and pose");
        Check(flow.Tutorial.Active&&!flow.Tutorial.ModalOpen&&!layout.BridgeOpen,"side A return resumes unfinished non-modal tutorial without opening bridge");
        Record("NOT COVERED HERE: native B return/retry/rewards are checked by SideGameBIntegrationChecks; browser play remains a separate check.");
        Capture("side-A-return");yield return .3f;
        Record("COMPLETE bounded traversal checks; inspect FAIL lines. No human WASD, no full A combat or native B completion playthrough.");
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||routine==null)return;
        if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
        InputSystem.QueueStateEvent(keyboard,walking?new KeyboardState(Key.W):new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState());
        if(player.gameObject.activeInHierarchy)
        {
            if(player.SwimmingActive)swimSeconds+=Time.deltaTime;if(player.ClimbingActive||player.MantlingActive)climbSeconds+=Time.deltaTime;
            minStamina=Mathf.Min(minStamina,vitals.Stamina);var p=player.transform.position;
            if(Time.frameCount%6==0)samples.Add(activeCase+","+Time.time+","+p.x+","+p.y+","+p.z+","+player.SwimmingActive+","+player.IsStableGrounded+","+vitals.Stamina+","+Vector3.Distance(Camera.main?Camera.main.transform.position:p,p+Vector3.up*1.05f));
        }
        if(EditorApplication.timeSinceStartup>deadline){Record("FAIL bounded deadline");Stop(true);return;}
        if(EditorApplication.timeSinceStartup<resume)return;
        try{if(!routine.MoveNext()){Stop(true);return;}resume=EditorApplication.timeSinceStartup+(routine.Current is float seconds?seconds:.03);}
        catch(Exception e){Record("FAIL exception "+e);Stop(true);}
    }
    public static void Stop(bool exitPlay=true)
    {
        if(!Running)return;
        routine=null;walking=false;EditorApplication.update-=Tick;File.WriteAllLines(Report+"traversal-samples.csv",samples);
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        if(oldKeyboard!=null&&oldKeyboard.added){if(oldKeyboardEnabled)InputSystem.EnableDevice(oldKeyboard);oldKeyboard.MakeCurrent();}
        if(oldMouse!=null&&oldMouse.added){if(oldMouseEnabled)InputSystem.EnableDevice(oldMouse);oldMouse.MakeCurrent();}
        InputSystem.settings.backgroundBehavior=oldBehavior;Application.runInBackground=oldBackground;
        Record("RESTORED Unity input devices/background behavior before Play exit; QA prefix remains disposable through OnDisable.");
        SessionState.SetString("V2.Traversal.Result","Finished; failures="+log.FindAll(s=>s.StartsWith("FAIL")).Count);
        if(exitPlay)EditorApplication.isPlaying=false;
    }
}
