using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using SamProduction.BossArena;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class Folded220ContactQA
{
    const string Main="Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string QA=Main;
    const string S="QA.Folded220Contact.";
    const string Report="ToolReports/WorldStyle20261009/ContactBudgetPass01/RuntimeQA";
    static BaselineFlow flow; static BaselineWorld world; static PlayerMovement player; static PlayerVitals vitals; static ThirdPersonCamera view; static OverworldV2Layout layout;
    static Keyboard keyboard,oldKeyboard; static Mouse mouse,oldMouse; static bool oldKeyboardEnabled,oldMouseEnabled;
    static InputSettings.BackgroundBehavior oldBehavior; static bool oldBackground;
    static KeyboardState desired;
    static Stack<IEnumerator> routines=new Stack<IEnumerator>();
    static int lastFrame;static double resume,deadline;
    static List<float> frameMs=new List<float>();
    public static bool Running{get;private set;}
    public static string Status=>SessionState.GetString(S+"status","Idle");
    static Folded220ContactQA(){EditorApplication.playModeStateChanged+=Changed;AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(Running)Stop("INCOMPLETE assembly reload");};}
    static string Hash(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s??"")));}
    static string Slot(string p)=>Hash(PlayerPrefs.HasKey(p)?PlayerPrefs.GetString(p):"<missing>");
    static void RealCheck(){string p=SessionState.GetString(S+"real","");if(Slot(p+".primary")!=SessionState.GetString(S+"primary","")||Slot(p+".backup")!=SessionState.GetString(S+"backup",""))throw new Exception("Real save changed");}
    static void Log(string s){File.AppendAllText(Report+"/result.txt",s+"\n");SessionState.SetString(S+"status",s);}
    static void Check(bool yes,string s){Log((yes?"PASS ":"FAIL ")+s);if(!yes)throw new Exception(s);}
    static float Planar(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
    static float Heading(Vector3 a,Vector3 b)=>Mathf.Atan2(b.x-a.x,b.z-a.z)*Mathf.Rad2Deg;
    public static void Prepare(bool toolUndoRegistration=false)
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||scene.path!=Main||(scene.isDirty&&!toolUndoRegistration)||SessionState.GetBool(S+"prepared",false)||SceneManager.sceneCount!=1)throw new Exception("Exclusive clean integrated main required");
        Directory.CreateDirectory(Report);
        var f=Object.FindFirstObjectByType<BaselineFlow>();string p="QA.Folded220Contact."+Guid.NewGuid().ToString("N");
        SessionState.SetString(S+"real",f.saveKeyPrefix);SessionState.SetString(S+"primary",Slot(f.saveKeyPrefix+".primary"));SessionState.SetString(S+"backup",Slot(f.saveKeyPrefix+".backup"));
        SessionState.SetString(S+"main",Hash(File.ReadAllText(Main)));SessionState.SetString(S+"prefix",p);
        SessionState.SetString(S+"override",EditorSceneManager.playModeStartScene?AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene):"");
        SessionState.SetBool(S+"background",Application.runInBackground);SessionState.SetFloat(S+"time",Time.timeScale);SessionState.SetInt(S+"cursor",(int)Cursor.lockState);SessionState.SetBool(S+"cursorVisible",Cursor.visible);
        File.WriteAllText(Report+"/result.txt","METHOD: Unity runtime clone of integrated edit scene; isolated QA save prefix override in unsaved edit memory only; no duplicate scene file (hard Assets budget). Real primary/backup and source scene SHA guarded. Controlled InputSystem, actual physics, API setup; no natural playthrough claim.\n");
        string real=f.saveKeyPrefix;
        foreach(string suffix in new[]{".primary",".backup"})if(PlayerPrefs.HasKey(real+suffix))PlayerPrefs.SetString(p+suffix,PlayerPrefs.GetString(real+suffix));PlayerPrefs.Save();
        f.saveKeyPrefix=p;EditorUtility.SetDirty(f);EditorSceneManager.MarkSceneDirty(scene);
        RealCheck();SessionState.SetBool(S+"prepared",true);EditorSceneManager.playModeStartScene=null;SessionState.SetString(S+"status","Prepared memory-only isolated save");
    }
    static void Changed(PlayModeStateChange change)
    {
        if(change==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(S+"prepared",false))EditorApplication.delayCall+=Begin;
        if(change==PlayModeStateChange.ExitingPlayMode&&Running)Stop("INCOMPLETE externally stopped");
        if(change==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(S+"prepared",false))Restore();
    }
    static void Begin()
    {
        try{
        if(SceneManager.GetActiveScene().path!=QA||SceneManager.sceneCount!=1)throw new Exception("Wrong QA scene");
        flow=Object.FindFirstObjectByType<BaselineFlow>();world=flow.GetComponent<BaselineWorld>();layout=flow.GetComponent<OverworldV2Layout>();player=world.player.GetComponent<PlayerMovement>();vitals=player.GetComponent<PlayerVitals>();view=Camera.main.GetComponent<ThirdPersonCamera>();
        Check(flow.saveKeyPrefix==SessionState.GetString(S+"prefix","")&&!flow.Started,"Save isolation and title state");
        RealCheck();oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldKeyboardEnabled=oldKeyboard!=null&&oldKeyboard.enabled;oldMouseEnabled=oldMouse!=null&&oldMouse.enabled;oldBehavior=InputSystem.settings.backgroundBehavior;oldBackground=Application.runInBackground;
        if(oldKeyboardEnabled)InputSystem.DisableDevice(oldKeyboard);if(oldMouseEnabled)InputSystem.DisableDevice(oldMouse);
        keyboard=InputSystem.AddDevice<Keyboard>("Folded220 QA Keyboard");mouse=InputSystem.AddDevice<Mouse>("Folded220 QA Mouse");keyboard.MakeCurrent();mouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;Application.runInBackground=true;
        InputSystem.onBeforeUpdate+=Input;Running=true;routines.Clear();routines.Push(Run());lastFrame=-1;resume=0;deadline=EditorApplication.timeSinceStartup+450;EditorApplication.update+=Tick;Log("Running");
        }catch(Exception e){Log("FAIL begin "+e);Stop("FAIL begin");EditorApplication.isPlaying=false;}
    }
    static void Input(){if(Running&&keyboard!=null&&InputState.currentUpdateType==InputUpdateType.Dynamic){InputSystem.QueueStateEvent(keyboard,desired);InputSystem.QueueStateEvent(mouse,new MouseState());}}
    static void Keys(params Key[] keys){desired=new KeyboardState(keys);}
    static void Capture(string name){ScreenCapture.CaptureScreenshot(Report+"/"+name+"-game-view.png");Log("CAPTURE "+name+" player="+player.transform.position+" camera="+view.transform.position);}
    static IEnumerator Run()
    {
        yield return .8f;
        if(PlayerPrefs.HasKey(flow.saveKeyPrefix+".primary")){Check(flow.ContinueGame(),"Copied real-slot Continue API accepted");yield return 1f;RealCheck();}
        flow.NewGame();yield return .5f;flow.Tutorial.Skip();yield return .5f;GameSession.Instance.RestoreProgress(true,true,false);
        double until=EditorApplication.timeSinceStartup+22;while(view.CinematicControlled&&EditorApplication.timeSinceStartup<until)yield return null;
        Check(flow.Started&&!flow.Tutorial.Active&&!view.CinematicControlled,"API fixture released");
        var floor=GameObject.Find("Own unified main-world floor collider").GetComponent<MeshCollider>();
        var surface=GameObject.Find("Own terrain surface matching walking floor").GetComponent<MeshFilter>();
        Check(surface.sharedMesh==floor.sharedMesh,"Visible terrain uses identical physical surface geometry");
        var scouts=Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None);
        var domains=Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None);
        Check(scouts.Length==6&&domains.Length==5,"All eleven legacy enemies present");
        foreach(var ai in scouts.Cast<MonoBehaviour>().Concat(domains.Cast<MonoBehaviour>())) {
            var so=new SerializedObject(ai);
            Check(so.FindProperty(ai is StoneScoutRoadsideAI?"terrain":"ground").objectReferenceValue==floor,ai.name+" keeps new-world ground binding");
            Check(floor.Raycast(new Ray(new Vector3(ai.transform.position.x,80,ai.transform.position.z),Vector3.down),out var hit,110),ai.name+" physical floor found");
            Check(Mathf.Abs(ai.transform.position.y-hit.point.y-.015f)<.04f,ai.name+" height preserved after Awake/NewGame reset");
        }
        var initial=scouts.ToDictionary(x=>x,x=>x.transform.position);
        float maxGap=0;int samples=0;
        for(int i=0;i<32;i++){foreach(var scout in scouts){var p=scout.transform.position;if(floor.Raycast(new Ray(new Vector3(p.x,80,p.z),Vector3.down),out var h,110)){maxGap=Mathf.Max(maxGap,Mathf.Abs(p.y-h.point.y-.015f));samples++;}}yield return .25f;}
        Check(maxGap<.055f,"Scout real fixed-step patrol stays on floor; samples="+samples+" maximum error="+maxGap);
        Log("PATROL moved="+scouts.Count(x=>Planar(x.transform.position,initial[x])>.1f)+"/6; distant player, no combat acceptance claim");
        var names=new[]{"Survey cylinder","Eastern relay","Island inscription","Eastern notice","WAVE rune","LEAF rune","SUN rune","Weathered inscription","Western hidden chest","Island hidden chest","Sealed eastern chest","IntroNPC"};
        int selected=0;
        foreach(string n in names){
            Keys();var t=GameObject.Find(n).transform;var npc=t.GetComponent<NPCDialogue>();var p=t.position+Vector3.back*1.3f;
            Check(floor.Raycast(new Ray(new Vector3(p.x,80,p.z),Vector3.down),out var h,110),n+" approach floor found");
            p.y=h.point.y+.12f;float heading=Heading(p,t.position);player.Relocate(p,Quaternion.Euler(0,heading,0));view.ResetOrbit(heading,16);Physics.SyncTransforms();yield return .6f;
            Check(player.IsStableGrounded,n+" player actually supported");
            var interaction=player.GetComponent<PlayerInteraction>();
            Log(n+" diagnostic npcEnabled="+npc.enabled+" interactable="+npc.IsInteractable+" selected="+(interaction.SelectedNPC?interaction.SelectedNPC.name:"<none>")+" player="+player.transform.position+" forward="+player.transform.forward);
            Check(interaction.SelectedNPC==npc,n+" native trigger and facing select expected NPC");
            selected++;
            if(n=="Survey cylinder"||n=="Western hidden chest"||n=="Eastern relay"){Keys(Key.E);yield return .12f;Keys();yield return .4f;Check(interaction.IsDialogueActive,n+" controlled E opens original dialogue");Capture(n.Replace(" ","-"));Keys(Key.Escape);yield return .12f;Keys();yield return .6f;Check(!interaction.IsDialogueActive,n+" controlled Escape cancels without completing reward");}
        }
        Log("Selected native interactions="+selected+"/12");
        player.Relocate(layout.CampPosition,Quaternion.identity);yield return .6f;
        foreach(var ai in domains)ai.ResetForJourney();
        yield return .3f;
        foreach(var ai in domains){var p=ai.transform.position;Check(floor.Raycast(new Ray(new Vector3(p.x,80,p.z),Vector3.down),out var h,110)&&Mathf.Abs(p.y-h.point.y-.015f)<.04f,ai.name+" encounter reset retains grounded home");}
        var frameSamples=new List<float>();for(int i=0;i<240;i++){if(Time.unscaledDeltaTime>0)frameSamples.Add(Time.unscaledDeltaTime*1000);yield return null;}
        frameSamples.Sort();Log("EDITOR frame diagnostic only: samples="+frameSamples.Count+" median_ms="+frameSamples[frameSamples.Count/2]+" p95_ms="+frameSamples[(int)(frameSamples.Count*.95f)]+"; foreground/standalone performance not reaccepted");
        RealCheck();Stop("PASS Contact grounding / native interactions; API fixture and controlled InputSystem, not natural playthrough");EditorApplication.isPlaying=false;
    }
    static IEnumerator Segment(string name,Vector3 start,Vector3 target,bool sprint,bool stairs)
    {
        Keys();player.Relocate(start,Quaternion.Euler(0,Heading(start,target),0));view.ResetOrbit(Heading(start,target),14);Physics.SyncTransforms();vitals.Refill();yield return .8f;
        var path=world.WalkingRoute(player.transform.position,target);Check(path!=null&&path.Length>0,name+" route exists");target=path[path.Length-1];int next=0;double until=EditorApplication.timeSinceStartup+100;
        float lastPlayerY=player.transform.position.y,lastCameraY=view.transform.position.y,maxCameraReverse=0,maxPlayerJump=0,airTime=0;int samples=0;bool captured=false;
        while(Planar(player.transform.position,target)>.65f&&EditorApplication.timeSinceStartup<until){
            while(next<path.Length-1&&Planar(player.transform.position,path[next])<.72f)next++;
            view.ResetOrbit(Heading(player.transform.position,path[next]),14);Keys(sprint?new[]{Key.W,Key.LeftShift}:new[]{Key.W});
            if(stairs){float dy=player.transform.position.y-lastPlayerY;float dc=view.transform.position.y-lastCameraY;if(dy>=-.001f)maxCameraReverse=Mathf.Max(maxCameraReverse,-dc);maxPlayerJump=Mathf.Max(maxPlayerJump,Mathf.Abs(dy));if(!player.IsStableGrounded)airTime+=Time.deltaTime;samples++;lastPlayerY=player.transform.position.y;lastCameraY=view.transform.position.y;}
            if(Time.unscaledDeltaTime>0)frameMs.Add(Time.unscaledDeltaTime*1000f);
            if(!captured&&next>path.Length/2){Capture(name+"-middle");captured=true;}
            yield return null;
        }
        Keys();yield return .7f;Log("MEASURE "+name+" player="+player.transform.position+" target="+target+" remaining="+Planar(player.transform.position,target)+" grounded="+player.IsStableGrounded+" cameraReverseY="+maxCameraReverse+" maxPlayerYstep="+maxPlayerJump+" airborneSeconds="+airTime+" samples="+samples);
        Check(Planar(player.transform.position,target)<1&&player.IsStableGrounded,name+" actual controller reached landing");
        Capture(name+"-landing");yield return .4f;
    }
    static void Tick()
    {
        if(!Running)return;try{
            if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("QA bounded deadline");
            if(EditorApplication.timeSinceStartup<resume||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            if(routines.Count==0)return;
            var top=routines.Peek();if(!top.MoveNext()){if(Running&&routines.Count>0&&ReferenceEquals(routines.Peek(),top))routines.Pop();return;}
            if(top.Current is IEnumerator nested)routines.Push(nested);else if(top.Current is float seconds)resume=EditorApplication.timeSinceStartup+seconds;
        }catch(Exception e){Log("FAIL "+e);Stop("FAIL "+e.Message);EditorApplication.isPlaying=false;}
    }
    static void Stop(string status)
    {
        InputSystem.onBeforeUpdate-=Input;EditorApplication.update-=Tick;Running=false;routines.Clear();
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}if(mouse!=null){InputSystem.RemoveDevice(mouse);mouse=null;}
        if(oldKeyboard!=null){if(oldKeyboardEnabled)InputSystem.EnableDevice(oldKeyboard);oldKeyboard.MakeCurrent();}
        if(oldMouse!=null){if(oldMouseEnabled)InputSystem.EnableDevice(oldMouse);oldMouse.MakeCurrent();}
        InputSystem.settings.backgroundBehavior=oldBehavior;Application.runInBackground=oldBackground;Log(status);
    }
    public static void Restore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit play first");if(!SessionState.GetBool(S+"prepared",false))return;
        RealCheck();if(Hash(File.ReadAllText(Main))!=SessionState.GetString(S+"main",""))throw new Exception("Candidate scene changed");
        string previous=SessionState.GetString(S+"override","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previous)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Time.timeScale=SessionState.GetFloat(S+"time",1);Application.runInBackground=SessionState.GetBool(S+"background",false);Cursor.lockState=(CursorLockMode)SessionState.GetInt(S+"cursor",0);Cursor.visible=SessionState.GetBool(S+"cursorVisible",true);
        BaselineSaveStore.Clear(SessionState.GetString(S+"prefix",""));SessionState.SetBool(S+"prepared",false);EditorSceneManager.OpenScene(Main,OpenSceneMode.Single);
        Log(Status+"; RESTORED real slots/candidate SHA unchanged; background/input/cursor/time/start scene restored; unsaved QA override discarded by reloading exact source; no duplicate scene file retained");
    }
}