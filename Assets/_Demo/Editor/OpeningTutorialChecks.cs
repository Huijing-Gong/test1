using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class OpeningTutorialChecks
{
    const string Prefix="QA.OpeningCamera.20260923";
    const string Report="ToolReports/OpeningCamera/";
    static IEnumerator routine;
    static double next;
    static BaselineFlow flow;
    static BaselineWorld world;
    static BaselineCampTutorial tutorial;
    static PlayerMovement player;
    static ThirdPersonCamera look;
    static DialogueUI dialogue;
    static Keyboard keyboard, previousKeyboard;
    static Mouse mouse, previousMouse;
    static InputSettings.BackgroundBehavior background;
    static bool runInBackground;
    static bool injectLook;
    static Vector3? drive;
    static double driveDeadline;
    static float minimumCameraDistance;
    static bool recordTour, sawTour;
    static float nextCapture;
    static int captureIndex;
    static string frames;
    static readonly List<string> captureTimes=new List<string>();
    static readonly List<string> log=new List<string>();
    public static bool Running => routine!=null;
    static OpeningTutorialChecks(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;}
    public static void Begin()
    {
        if(!EditorApplication.isPlaying||Running)throw new InvalidOperationException("Enter Play first.");
        flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();
        if(flow.Started)throw new InvalidOperationException("Requires title screen to protect user progress.");
        world=flow.GetComponent<BaselineWorld>();tutorial=flow.Tutorial;
        player=world.player.GetComponent<PlayerMovement>();look=Camera.main.GetComponent<ThirdPersonCamera>();
        dialogue=UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
        flow.saveKeyPrefix=Prefix;flow.NewGame();
        Directory.CreateDirectory(Report);log.Clear();
        previousKeyboard=Keyboard.current;previousMouse=Mouse.current;
        background=InputSystem.settings.backgroundBehavior;runInBackground=Application.runInBackground;
        Application.runInBackground=true;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard=InputSystem.AddDevice<Keyboard>("OpeningTutorialQA");mouse=InputSystem.AddDevice<Mouse>("OpeningTutorialMouseQA");
        keyboard.MakeCurrent();mouse.MakeCurrent();minimumCameraDistance=100;
        recordTour=true;sawTour=false;nextCapture=0;captureIndex=0;captureTimes.Clear();
        frames=Report+"capture-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(frames);
        File.WriteAllText(Report+"latest-capture.txt",frames);
        routine=Run();next=0;EditorApplication.update+=Tick;
    }
    static void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    static void Check(bool ok,string message)
    {
        log.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllLines(Report+"runtime-checks.txt",log);
    }
    static void Capture(string name){ScreenCapture.CaptureScreenshot(Report+name+".png");}
    static void Drive(Vector3 target)
    {
        drive=target;driveDeadline=EditorApplication.timeSinceStartup+25;Keys(Key.W);
    }
    static IEnumerator Run()
    {
        yield return .8f;
        Check(tutorial.Active&&tutorial.CardOpen&&Time.timeScale==0&&player.IsGameplayInputLocked,"new journey spawns beside barrier and pauses lesson");
        Capture("game-look");yield return .4f;
        var preview=GameObject.Find("Tutorial avatar (visual only)").GetComponent<Animator>();
        float animation=preview.GetCurrentAnimatorStateInfo(0).normalizedTime;
        var position=player.transform.position;float stamina=player.GetComponent<PlayerVitals>().Stamina;
        Keys(Key.W);yield return .8f;Keys();
        Check(Vector3.Distance(position,player.transform.position)<.01f&&Mathf.Approximately(stamina,player.GetComponent<PlayerVitals>().Stamina),"paused lesson ignores movement and preserves stamina");
        Check(preview.GetCurrentAnimatorStateInfo(0).normalizedTime>animation,"preview animator advances while world time is zero");
        flow.SetMenu(true);Check(!flow.MenuOpen&&!flow.ShowMap(),"lesson prevents overlapping pause/map modals");
        Keys(Key.Enter);yield return .2f;Keys();yield return .3f;
        Check(!tutorial.CardOpen&&Time.timeScale==1&&!player.IsGameplayInputLocked,"Enter dismisses lesson and restores input/time");
        injectLook=true;yield return 1.5f;injectLook=false;
        Check(tutorial.CardOpen&&tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Move,"injected mouse orbit completes actual look practice");
        Capture("game-move-card");yield return .4f;
        // Movement preview and highlight should both change at unscaled time.
        animation=preview.GetCurrentAnimatorStateInfo(0).normalizedTime;yield return .6f;
        Check(preview.GetCurrentAnimatorStateInfo(0).normalizedTime>animation,"walking demonstration animates while paused");
        Capture("game-move-card-next-frame");yield return .3f;
        Keys(Key.Enter);yield return .15f;Keys();yield return .3f;
        look.ResetOrbit(0,14);Capture("game-arrival-practice");yield return .4f;
        Drive(new Vector3(-22,0,-66));while(drive.HasValue)yield return .05f;
        Check(tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Approach,"actual WASD walking completes four-metre practice");
        Drive(new Vector3(-12,0,-62));while(drive.HasValue)yield return .05f;
        Drive(new Vector3(-4,0,-63));while(drive.HasValue)yield return .05f;
        Check(tutorial.TourPlaying,"continuous W approach automatically triggers first scenic sequence");
        var tour=tutorial.CameraTour;
        var at=player.transform.position;float resource=player.GetComponent<PlayerVitals>().Stamina;
        var originalPose=tour.EntryPose;float fov=Camera.main.fieldOfView;
        int labels=0;foreach(var t in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))if(t.GetComponent<Renderer>().enabled)labels++;
        Check(labels==0&&Time.timeScale==0&&look.CinematicControlled,"tour owns camera/time and hides development world labels");
        flow.SetMenu(true);Check(!flow.MenuOpen&&!flow.ShowMap(),"tour excludes overlapping pause/map UI");
        Keys(Key.W,Key.Space,Key.J,Key.R);yield return .5f;Keys();
        Check(Vector3.Distance(at,player.transform.position)<.01f&&Mathf.Approximately(resource,player.GetComponent<PlayerVitals>().Stamina)&&!world.JournalOpen,
            "camera flight freezes player/resources and blocks jump/journal/recovery input");
        Check(!GameSession.Instance.HasSword&&!world.accepted[0],"scenic trigger does not grant sword or accept clue");
        double end=EditorApplication.timeSinceStartup+18;
        while(tutorial.TourPlaying&&EditorApplication.timeSinceStartup<end)yield return .05f;
        Check(!tutorial.TourPlaying&&tutorial.CardOpen&&tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Talk,"normal finish transfers directly to E lesson without unlocking play");
        Check(!look.CinematicControlled&&Time.timeScale==0&&Vector3.Distance(Camera.main.transform.position,originalPose.position)<.02f&&Quaternion.Angle(Camera.main.transform.rotation,originalPose.rotation)<.1f&&Camera.main.fieldOfView==fov,"normal finish restores exact entry camera pose/FOV and retains card lock");
        Capture("game-talk-card");yield return .4f;
        Keys(Key.Enter);yield return .15f;Keys();yield return .3f;
        Drive(new Vector3(-4,0,-63));while(drive.HasValue)yield return .05f;
        look.ResetOrbit(0,14);player.transform.rotation=Quaternion.identity;
        Capture("game-guide-practice");yield return .4f;
        Keys(Key.E);yield return .2f;Keys();yield return .2f;
        Check(dialogue.IsDialogueActive,"actual E opens existing guide dialogue");
        Keys(Key.E);yield return .2f;Keys();yield return .2f;
        Keys(Key.E);yield return .2f;Keys();yield return .5f;
        Check(GameSession.Instance.HasSword&&world.accepted[0]&&tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Vista,"real dialogue grants sword, records existing clue, and advances tutorial");
        Check(world.Tracked==-1&&!world.completed[0]&&!world.opened[0]&&!world.opened[1]&&!world.opened[2],"tutorial does not auto-track, complete tasks or open chests");
        Drive(new Vector3(0,0,-58));while(drive.HasValue)yield return .05f;
        Drive(new Vector3(0,0,-43));while(drive.HasValue)yield return .05f;
        Check(!tutorial.Active&&Time.timeScale==1,"continuous injected walking to lake completes introduction");
        Capture("game-vista");yield return .4f;
        Check(minimumCameraDistance>3,"route camera maintained clearance (sampled minimum "+minimumCameraDistance.ToString("F2")+" m)");
        flow.SaveNow();Check(flow.ContinueGame(),"isolated save reload");
        yield return .5f;Check(!tutorial.Active&&GameSession.Instance.HasSword&&world.accepted[0],"Continue retains progress and never replays opening");
        for(int i=0;i<3;i++){var route=world.WalkingRoute(player.transform.position,world.objectives[i].position);Check(route.Length>0,"complete walking route to original target "+i+" has "+route.Length+" samples");}
        flow.NewGame();yield return .3f;Keys(Key.Tab);yield return .15f;Keys();yield return .4f;
        Check(!tutorial.Active&&!player.IsGameplayInputLocked&&Time.timeScale==1&&!GameSession.Instance.HasSword&&!world.accepted[0],"Tab skips paused card without granting sword or clue");
        flow.NewGame();yield return .2f;tutorial.StartPractice();yield return .4f;
        flow.SetMenu(true);tutorial.Skip();yield return .3f;
        Check(flow.MenuOpen&&Time.timeScale==0&&player.IsGameplayInputLocked,"skip from pause menu retains menu lock");
        flow.SetMenu(false);yield return .4f;
        Check(Time.timeScale==1&&!player.IsGameplayInputLocked,"closing pause after skip restores play");
        flow.NewGame();yield return .2f;tutorial.StartPractice();yield return .4f;
        flow.RespawnPlayerAtCamp();yield return .4f;
        Check(!player.IsGameplayInputLocked&&Time.timeScale==1&&Vector3.Distance(player.transform.position,BaselineSaveData.DefaultSafePosition)<1,"controlled respawn keeps original safe camp");
        tutorial.Skip();
        // Direct API starts below isolate cancellation/ownership from the already-tested real trigger.
        foreach(float cancelAt in new[]{.1f,3f,7f,10.5f,12.7f})
        {
            var cancelTour= tutorial.CameraTour;
            var pose=new Pose(Camera.main.transform.position,Camera.main.transform.rotation);
            float originalFov=Camera.main.fieldOfView;
            Check(cancelTour.Play(flow,null),"API fixture starts cancellation at "+cancelAt);
            yield return cancelAt;
            cancelTour.Complete();yield return .08f;
            Check(!cancelTour.IsPlaying&&!look.CinematicControlled&&Camera.main.fieldOfView==originalFov&&Quaternion.Angle(Camera.main.transform.rotation,pose.rotation)<.2f,
                "phase cancellation restores camera at "+cancelAt);
            yield return .15f;Check(Time.timeScale==1&&!player.IsGameplayInputLocked,"phase cancellation releases time/input at "+cancelAt);
        }
        flow.NewGame();yield return .3f;flow.ContinueGame();yield return .4f;
        Check(!tutorial.Active&&Time.timeScale==1&&!player.IsGameplayInputLocked,"Continue while card open releases tutorial pause");
        var activeTour=tutorial.CameraTour;
        activeTour.Play(flow,null);yield return .2f;
        flow.ContinueGame();yield return .4f;
        Check(!activeTour.IsPlaying&&!look.CinematicControlled&&Time.timeScale==1&&!player.IsGameplayInputLocked,"Continue during flight aborts and restores normal game");
        activeTour.Play(flow,null);yield return .2f;activeTour.enabled=false;yield return .4f;
        Check(!activeTour.IsPlaying&&!look.CinematicControlled&&Time.timeScale==1&&!player.IsGameplayInputLocked,"component disable abort restores camera/time/input");activeTour.enabled=true;
        var edges=EdgeCases();while(edges.MoveNext())yield return edges.Current;
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name="Opening QA temporary entry obstruction";
        obstacle.transform.position=Vector3.Lerp(Camera.main.transform.position,new Vector3(-3,3.6f,-67),.5f);
        obstacle.transform.localScale=Vector3.one*2;Physics.SyncTransforms();
        activeTour.Play(flow,null);yield return .1f;
        Check(activeTour.IsPlaying&&activeTour.UsesEntryFade,"controlled solid obstruction chooses fade entry instead of camera flight through wall");
        float black;activeTour.Evaluate(.55f,activeTour.EntryPose,true,out black);
        Check(black>.99f,"blocked-entry cut occurs under full black");
        activeTour.Complete();UnityEngine.Object.Destroy(obstacle);yield return .3f;
        Check(Time.timeScale==1&&!player.IsGameplayInputLocked,"blocked-entry skip releases input/time");
    }
    static IEnumerator EdgeCases()
    {
        var offsets=new[]{new Vector3(0,0,-2),new Vector3(-3,0,0),new Vector3(0,0,-2),new Vector3(0,0,3)};
        for(int i=0;i<offsets.Length;i++)
        {
            flow.NewGame();yield return .25f;tutorial.StartPractice();yield return .25f;
            injectLook=true;yield return 1.5f;injectLook=false;tutorial.StartPractice();yield return .3f;
            look.ResetOrbit(0,14);Drive(new Vector3(-22,0,-66));while(drive.HasValue)yield return .05f;
            Check(tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Approach,"edge fixture reaches approach through actual input "+i);
            var near=new Vector3(-4,1.05f,-61)+offsets[i];
            if(i==0)
            {
                player.Relocate(near+Vector3.up*4,Quaternion.identity);look.ResetOrbit(0,14);Physics.SyncTransforms();yield return .2f;
                Check(!tutorial.TourPlaying,"airborne controlled arrival does not trigger camera before landing");
                yield return 1.4f;
            }
            else
            {
                player.Relocate(near,Quaternion.identity);look.ResetOrbit(i==3?180:90,14);Physics.SyncTransforms();
                if(i==1)
                {
                    flow.SetMenu(true);yield return .6f;Check(!tutorial.TourPlaying,"NPC proximity while pause menu is open does not trigger");
                    flow.SetMenu(false);yield return .7f;
                }
                else if(i==2)
                {
                    look.ResetOrbit(0,14);player.transform.rotation=Quaternion.identity;
                    yield return .1f;Keys(Key.E);yield return .1f;Keys();yield return .15f;
                    Check(dialogue.IsDialogueActive&&!tutorial.TourPlaying,"early E dialogue defers scenic takeover");
                    Keys(Key.E);yield return .1f;Keys();yield return .15f;
                    Keys(Key.E);yield return .1f;Keys();yield return .7f;
                }
                else yield return .7f;
            }
            Check(tutorial.TourPlaying,"grounded approach triggers from case "+i);
            if(i==3)Check(tutorial.CameraTour.UsesEntryFade == !BaselineOpeningCamera.ClearSegment(tutorial.CameraTour.EntryPose.position,new Vector3(-3,3.6f,-67)),"opposite-side entry selects fade according to actual camera clearance");
            if(i==1)
            {
                Keys(Key.Tab);yield return .15f;Keys();yield return .3f;
                Check(!tutorial.Active&&!tutorial.TourPlaying&&!player.IsGameplayInputLocked&&Time.timeScale==1&&!GameSession.Instance.HasSword,"injected Tab skips entire introduction from flight");
            }
            else
            {
                Keys(Key.Escape);yield return .15f;Keys();yield return .25f;
                if(i==2)Check(!tutorial.TourPlaying&&!tutorial.CardOpen&&tutorial.CurrentLesson==BaselineCampTutorial.Lesson.Vista&&GameSession.Instance.HasSword&&Time.timeScale==1,"early dialogue reward survives tour; Esc resumes vista without duplicate E lesson [lesson="+tutorial.CurrentLesson+", sword="+GameSession.Instance.HasSword+", menu="+flow.MenuOpen+", time="+Time.timeScale+"]");
                else
                {
                    Check(!tutorial.TourPlaying&&tutorial.CardOpen&&!flow.MenuOpen&&Time.timeScale==0,"injected Esc skips only view and retains E lesson lock "+i);
                    tutorial.StartPractice();yield return .3f;
                    player.Relocate(new Vector3(0,1.05f,-67),Quaternion.identity);yield return .3f;
                    player.Relocate(near,Quaternion.identity);yield return .6f;
                    Check(!tutorial.TourPlaying,"returning to guide does not replay completed/skipped view "+i);
                }
            }
            var anim=player.GetComponentInChildren<Animator>();
            Check(anim.updateMode==AnimatorUpdateMode.Normal,"actor animation clock restored after tour case "+i);
            tutorial.Skip();yield return .2f;
        }
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||!player){Stop();return;}
        EditorApplication.QueuePlayerLoopUpdate();
        if(recordTour && tutorial.TourPlaying)
        {
            sawTour=true;
            if(tutorial.CameraTour.Elapsed>=nextCapture)
            {
                string file="frame-"+(captureIndex++).ToString("D4")+".png";
                ScreenCapture.CaptureScreenshot(frames+"/"+file);
                captureTimes.Add(file+","+tutorial.CameraTour.Elapsed.ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllLines(frames+"/times.csv",captureTimes);
                nextCapture=tutorial.CameraTour.Elapsed+.12f;
            }
        }
        else if(sawTour)recordTour=false;
        if(injectLook&&!tutorial.ModalOpen)
        {
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
            InputSystem.QueueStateEvent(mouse,new MouseState{delta=new Vector2(12,0)});
        }
        if(drive.HasValue)
        {
            var delta=drive.Value-player.transform.position;delta.y=0;
            if(delta.magnitude<.65f||tutorial.ModalOpen||EditorApplication.timeSinceStartup>driveDeadline)
            {
                if(EditorApplication.timeSinceStartup>driveDeadline)Check(false,"walking timed out at "+player.transform.position+" toward "+drive.Value);
                drive=null;Keys();
            }
            else
            {
                minimumCameraDistance=Mathf.Min(minimumCameraDistance,Vector3.Distance(Camera.main.transform.position,player.transform.position+Vector3.up*1.6f));
                look.ResetOrbit(Quaternion.LookRotation(delta).eulerAngles.y,14);
            }
        }
        if(EditorApplication.timeSinceStartup<next)return;
        try{if(!routine.MoveNext()){Stop();return;}next=EditorApplication.timeSinceStartup+(routine.Current is float delay?delay:.02);}
        catch(Exception e){Check(false,e.ToString());Stop();}
    }
    public static void Stop()
    {
        EditorApplication.update-=Tick;routine=null;drive=null;injectLook=false;
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;InputSystem.settings.backgroundBehavior=background;Application.runInBackground=runInBackground;}
        if(mouse!=null){InputSystem.RemoveDevice(mouse);mouse=null;}
        if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();
        if(previousMouse!=null&&previousMouse.added)previousMouse.MakeCurrent();
        if(tutorial)tutorial.Cancel();
    }
    public static void ClearTestSlot()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
        BaselineSaveStore.Clear(Prefix);
    }
}

