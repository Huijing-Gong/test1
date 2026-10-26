// Test-build observer. Does not synthesize input, teleport during play, or drive combat.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using SamProduction.BossArena;

public sealed class NativePlayObserver : MonoBehaviour
{
    static bool BrowserPlayer => Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor;
    static string UrlValue(string key, string fallback) {
        var url=Application.absoluteURL; int begin=url.IndexOf('?'); if(begin<0)return fallback;
        var query=url.Substring(begin+1).Split('#')[0];
        foreach(var field in query.Split('&')) {var pair=field.Split(new[]{'='},2);if(Uri.UnescapeDataString(pair[0])==key)return pair.Length>1?Uri.UnescapeDataString(pair[1].Replace("+"," ")):"";}
        return fallback;
    }
    static void FailClosed(Scene scene,LoadSceneMode mode) {
        foreach(var root in scene.GetRootGameObjects())foreach(var owner in root.GetComponentsInChildren<BaselineFlow>(true))owner.enabled=false;
    }
    bool diskEnabled=true;
    double perfNext, frameTotal; int frameCount; float frameMaximum;
    readonly System.Collections.Generic.List<float> frameSamples=new System.Collections.Generic.List<float>(1200);
    string prefix, output, mode; BaselineFlow flow; BaselineWorld world; ArenaSession observedArena;
    string[] formal = new string[2]; bool[] exists = new bool[2];
    float next; int shots; bool burst; string lastState="";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        bool requested=!Application.isEditor&&Debug.isDebugBuild&&(BrowserPlayer?UrlValue("heroqa","0")=="1":Environment.GetCommandLineArgs().Contains("--native-play-qa"));
#if UNITY_EDITOR
if(Application.isEditor)requested=UnityEditor.SessionState.GetBool("HeroQA.Enabled",false);
#endif
if(!requested)return;
        var go=new GameObject("Native input observation and private save scope");
        DontDestroyOnLoad(go); var observer=go.AddComponent<NativePlayObserver>();try{observer.Prepare();}catch(Exception exception){SceneManager.sceneLoaded+=FailClosed;Time.timeScale=0;Debug.LogException(exception);
#if UNITY_EDITOR
if(Application.isEditor)UnityEditor.EditorApplication.isPlaying=false;
#endif
if(!BrowserPlayer)Application.Quit(2);}
    }
    string Arg(string key,string fallback) {
        if(BrowserPlayer){if(key=="--native-prefix")return UrlValue("prefix",fallback);if(key=="--native-seed")return UrlValue("seed",fallback);return fallback;}
#if UNITY_EDITOR
if(Application.isEditor){if(key=="--native-prefix")return "QA.NativePlay.Hero20261016";if(key=="--native-output")return Path.GetFullPath("ToolReports/HeroIntegration20261016/EditorQA");if(key=="--native-seed")return "camp";}
#endif
var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
    void Prepare()
    {
        prefix=Arg("--native-prefix","QA.NativePlay."+Guid.NewGuid().ToString("N"));
        if(!prefix.StartsWith("QA.NativePlay.",StringComparison.Ordinal)||prefix.Length<=14||prefix.Length>128||prefix.Any(c=>!(char.IsLetterOrDigit(c)||c=='.'||c=='_'||c=='-')))throw new Exception("Private QA.NativePlay prefix required; startup blocked");
        output=Path.GetFullPath(Arg("--native-output",Path.Combine(Application.persistentDataPath,"NativePlayQA")));
        try{Directory.CreateDirectory(output);}catch(Exception ex){diskEnabled=false;Debug.LogWarning("HERO_QA_DISK_UNAVAILABLE "+ex.Message);}mode=Arg("--native-seed","fresh");if(mode!="fresh"&&mode!="camp"&&mode!="gate")throw new Exception("Unknown QA seed; startup blocked");perfNext=Time.realtimeSinceStartupAsDouble+10;
        for(int i=0;i<2;i++){var k=BaselineSaveStore.DefaultKeyPrefix+(i==0?".primary":".backup");exists[i]=PlayerPrefs.HasKey(k);formal[i]=PlayerPrefs.GetString(k,"");}
        Write("BOOT build="+Application.buildGUID+" mode="+mode+" prefix="+prefix+" size="+Screen.width+"x"+Screen.height+" realKeyboard="+(Keyboard.current?.enabled)+" realMouse="+(Mouse.current?.enabled));
        SceneManager.sceneLoaded+=Loaded;Application.logMessageReceived+=Log;
        if(!BrowserPlayer&&Environment.GetCommandLineArgs().Contains("--native-clean")){BaselineSaveStore.Clear(prefix);Write("CLEAN private slots; formalExact="+FormalExact());Application.Quit();}
    }
    void Loaded(Scene scene,LoadSceneMode load)
    {
        Write("SCENE "+scene.path);
        var f=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BaselineFlow>(true)).FirstOrDefault();
        if(!f)return;flow=f;world=f.GetComponent<BaselineWorld>();flow.saveKeyPrefix=prefix;
        if(mode!="fresh"&&!BaselineSaveStore.HasAnyData(prefix))
        {
            var l=f.GetComponent<OverworldV2Layout>();var pos=mode=="gate"&&f.endingEntrance?f.endingEntrance.position+Vector3.back*2:l.CampPosition;
            var data=new BaselineSaveData{mapVersion=l.AuthoredMapVersion,introCompleted=true,openingCompleted=true,hasSword=true,northBridgeOpen=true,safePosition=pos,safeYaw=0,accepted=new[]{true,true,true},tracked=0};
            if(!BaselineSaveStore.Save(prefix,data,out var message))throw new Exception(message);
            Write("SETUP seeded private "+mode+" at "+pos+"; route before this point is not natural-play evidence");
        }
    }
    bool FormalExact(){for(int i=0;i<2;i++){var k=BaselineSaveStore.DefaultKeyPrefix+(i==0?".primary":".backup");if(PlayerPrefs.HasKey(k)!=exists[i]||PlayerPrefs.GetString(k,"")!=formal[i])return false;}return true;}
    void SafeFile(string name,string value,bool append=true){if(!diskEnabled)return;try{if(append)File.AppendAllText(Path.Combine(output,name),value);else File.WriteAllText(Path.Combine(output,name),value);}catch(Exception ex){diskEnabled=false;Debug.LogWarning("HERO_QA_DISK_UNAVAILABLE "+ex.Message);}}
    void Write(string text){var line=DateTime.UtcNow.ToString("O")+" t="+Time.realtimeSinceStartup.ToString("F3")+" "+text;SafeFile("events.txt",line+"\n");if(BrowserPlayer)Debug.Log("HERO_QA "+line);}
    void Log(string m,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)SafeFile("errors.txt",type+" "+m+"\n"+trace+"\n");}
    void LateUpdate()
    {
        MeasurePerformance();var k=Keyboard.current;var m=Mouse.current;
        if(k!=null)foreach(var key in new[]{Key.W,Key.A,Key.S,Key.D,Key.Space,Key.LeftShift,Key.LeftCtrl,Key.RightCtrl,Key.C,Key.E,Key.J,Key.M,Key.Escape,Key.R,Key.Q,Key.H,Key.B,Key.Enter,Key.F1})if(k[key].wasPressedThisFrame)Write((k.native?"NATIVE_KEY ":"SYNTHETIC_KEY ")+key);
        if(m!=null&&m.leftButton.wasPressedThisFrame)Write((m.native?"NATIVE_MOUSE ":"SYNTHETIC_MOUSE ")+"LMB");
        if(m!=null&&m.rightButton.wasPressedThisFrame)Write((m.native?"NATIVE_MOUSE ":"SYNTHETIC_MOUSE ")+"RMB");
        if(k!=null&&k.f8Key.wasPressedThisFrame)Shot("manual");
        if(k!=null&&k.f9Key.wasPressedThisFrame&&!burst)StartCoroutine(Burst());
        if(Time.realtimeSinceStartup>=next){next=Time.realtimeSinceStartup+.25f;Snapshot();}
    }
    void Snapshot()
    {
        var arena=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();observedArena=arena;
        var eq=world&&world.player?world.player.GetComponent<PlayerEquipment>():null;
        var combat=world&&world.player?world.player.GetComponent<PlayerCombat>():null;
        var state="focus="+Application.isFocused+" scale="+Time.timeScale+" formalExact="+FormalExact()+" screen="+Screen.width+"x"+Screen.height;
        if(flow)state+=" title="+flow.TitleScreenVisible+" started="+flow.Started+" menu="+flow.MenuOpen+" map="+flow.MapOpen+" journal="+(world&&world.JournalOpen)+" pos="+(world&&world.player?world.player.position.ToString("F2"):"none")+" drawn="+(eq&&eq.SwordDrawn)+" attacking="+(combat&&combat.IsAttacking);
        var hero=world&&world.player?world.player.GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>():null;if(hero)state+=" heroAction="+hero.CurrentAction;
        if(arena)state+=" ARENA="+arena.State+" p="+arena.player.transform.position.ToString("F2")+" hp="+arena.player.fighter.Health+" bossHP="+arena.boss.fighter.Health+" action="+arena.player.ActionName+" heroAction="+(arena.player.Hero?arena.player.Hero.CurrentAction:"none")+" drawn="+arena.player.SwordDrawn+" lock="+arena.view.LockedOn;
        if(state!=lastState){Write("STATE "+state);lastState=state;}
        SafeFile("current-state.txt",state,false);
    }
    void Shot(string label){if(BrowserPlayer){StartCoroutine(BrowserShot(label));return;}string file=Path.Combine(output,(++shots).ToString("D3")+"-"+label+".png");ScreenCapture.CaptureScreenshot(file);Write("SCREENSHOT "+Path.GetFileName(file));}
    IEnumerator BrowserShot(string label){yield return new WaitForEndOfFrame();Texture2D texture=null;try{texture=ScreenCapture.CaptureScreenshotAsTexture();if(!texture)throw new Exception("No screenshot texture");if(diskEnabled){File.WriteAllBytes(Path.Combine(output,(++shots).ToString("D3")+"-"+label+".png"),texture.EncodeToPNG());Write("SCREENSHOT persistentDataPath virtual file; no browser download claimed");}else Write("SCREENSHOT_SKIPPED disk unavailable");}catch(Exception ex){Write("SCREENSHOT_UNAVAILABLE "+ex.Message);}finally{if(texture)Destroy(texture);}}
    void MeasurePerformance(){
        // Focused, active 1920x1080 game frames only; unscaled frame intervals include hit stop.
        bool eligible=Screen.width==1920&&Screen.height==1080&&Application.isFocused&&((observedArena&&observedArena.Active)||(flow&&flow.Started&&!flow.WorldHudHidden));
        if(eligible){float ms=Time.unscaledDeltaTime*1000;if(ms>0){frameTotal+=ms;frameCount++;frameMaximum=Mathf.Max(frameMaximum,ms);frameSamples.Add(ms);}}
        if(Time.realtimeSinceStartupAsDouble<perfNext)return;perfNext=Time.realtimeSinceStartupAsDouble+10;
        if(frameCount>0){frameSamples.Sort();float p95=frameSamples[Mathf.Clamp(Mathf.CeilToInt(frameCount*.95f)-1,0,frameCount-1)];Write("PERF_1080P controlled_or_natural_as_recorded frameCount="+frameCount+" meanMs="+(frameTotal/frameCount).ToString("F3")+" p95Ms="+p95.ToString("F3")+" maxMs="+frameMaximum.ToString("F3")+" profilerAllocatedBytes="+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()+" profilerReservedBytes="+UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong()+" monoUsedBytes="+UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()+" gpuMs=unavailable profilerCountersAreNotOSWorkingSet=true");}
        frameSamples.Clear();frameTotal=0;frameCount=0;frameMaximum=0;
    }
    IEnumerator Burst(){burst=true;for(int i=0;i<16;i++){Shot("burst");yield return new WaitForSecondsRealtime(.2f);}burst=false;}
    void OnApplicationQuit(){Write("QUIT formalExact="+FormalExact()+" private progress retained for Continue test");SceneManager.sceneLoaded-=Loaded;Application.logMessageReceived-=Log;}
}
