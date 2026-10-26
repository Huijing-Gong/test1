// QA build only. Empty bootstrap scene first; Boss scene second. No Baseline references.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using SamProduction.BossArena;

public sealed class WindowsPromoProbe : MonoBehaviour
{
    const string Formal="LakeDomain.Baseline.Save";
    const string BossScene="Assets/SAM/BossArena/Scenes/SAM_BossArena.unity";
    [Serializable] public struct Frame { public int round,frame; public bool warmup,focused; public double wall,deltaMs,cpuMs,gpuMs; public float skillTime; public int attack; public bool attacking; }
    [Serializable] public class Round { public int index,frames,meshes,materials,gc0,gc1,gc2; public bool warmup; public long allocatedBytes,managedBytes; public double duration,meanMs,p95Ms,maxMs; }
    [Serializable] public class Report { public string scope="Standalone controlled forced Meteor; not natural play or whole-game performance",prefix,platform,cpu,gpu,api,error; public int width,height; public bool complete,formalExact,locked; public List<Round> rounds=new List<Round>(); public List<Frame> frames=new List<Frame>(65536); public List<string> captures=new List<string>(); public List<string> limits=new List<string>(); }
    static WindowsPromoProbe instance;
    Report report=new Report(); ArenaSession arena; string output; string[] raw=new string[2]; bool[] exists=new bool[2];
    readonly string[] formalKeys={Formal+".primary",Formal+".backup"};
    readonly FrameTiming[] timing=new FrameTiming[1]; ulong lastTiming; bool finished,recording; double started,lastWall,roundAt; int round,recordIndex; float recordAt; List<double> roundTimes=new List<double>(8192); Coroutine work;
    static bool EnabledFlag()=>Environment.GetCommandLineArgs().Contains("--promo-arena-qa");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Boot()
    {
        if(!EnabledFlag()||instance)return;
        var go=new GameObject("OPTIONAL Windows Promo QA");DontDestroyOnLoad(go);instance=go.AddComponent<WindowsPromoProbe>();instance.Prepare();
    }
    void Prepare()
    {
        report.prefix="QA.PromoArena."+Guid.NewGuid().ToString("N");
        for(int i=0;i<2;i++){string key=Formal+(i==0?".primary":".backup");exists[i]=PlayerPrefs.HasKey(key);raw[i]=PlayerPrefs.GetString(key,"");}
        var args=Environment.GetCommandLineArgs();int n=Array.IndexOf(args,"--promo-output");output=n>=0&&n+1<args.Length?Path.GetFullPath(args[n+1]):Path.Combine(Application.persistentDataPath,"PromoArenaQA",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);Application.runInBackground=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Time.timeScale=1;Time.captureFramerate=0;
        report.platform=Application.platform.ToString();report.cpu=SystemInfo.processorType;report.gpu=SystemInfo.graphicsDeviceName;report.api=SystemInfo.graphicsDeviceType.ToString();
        report.limits.Add("Private prefix is a run identity only: no SaveStore/Flow instantiated, no persistence API called, no private save seeded.");
        report.limits.Add("Runtime Mesh/Material totals include loaded assets; counts are not proof of leaks. Compare post-warmup rounds and validate any trend.");
        report.limits.Add("FrameTiming CPU/GPU may be unavailable or delayed; nonpositive or repeated timestamps excluded. delta is wall frame time, not GPU time.");
        report.limits.Add("Capture stage includes readback/JPEG/disk overhead and is excluded from performance rounds. Forced repeat/reset is not natural combat.");
        started=Time.realtimeSinceStartupAsDouble;work=StartCoroutine(Guard(Run()));
    }
    bool Exact(){for(int i=0;i<2;i++){string k=formalKeys[i];if(PlayerPrefs.HasKey(k)!=exists[i]||PlayerPrefs.GetString(k,"")!=raw[i])return false;}return true;}
    IEnumerator Guard(IEnumerator routine)
    {
        while(!finished){object next=null;bool advanced;try{advanced=routine.MoveNext();if(advanced)next=routine.Current;}catch(Exception e){Fail(e.ToString());yield break;}if(!advanced)yield break;if(next is IEnumerator nested)yield return Guard(nested);else yield return next;}
    }
    IEnumerator Run()
    {
        yield return null;
        // A build with a Main/Flow bootstrap is invalid; this check is diagnostic, not a substitute for an empty first scene.
        foreach(var b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(b&&b.GetType().Name=="BaselineFlow"){Fail("Invalid QA build: BaselineFlow found in bootstrap");yield break;}
        var load=SceneManager.LoadSceneAsync(BossScene,LoadSceneMode.Single);if(load==null){Fail("Boss scene missing from QA build");yield break;}yield return load;yield return null;
        arena=FindFirstObjectByType<ArenaSession>();if(!arena||!arena.boss||!arena.boss.v13Motor||!arena.player||!arena.view){Fail("Authored V13 ArenaSession required");yield break;}
        arena.automaticTick=false;arena.view.automaticTick=false;arena.boss.decisionEnabled=false;
        // Separate startup screenshot; settle before the performance segment.
        yield return new WaitForSecondsRealtime(1);yield return Capture("stage-entry.jpg");yield return new WaitForSecondsRealtime(1);
        for(round=0;round<13;round++){
            if(!BeginRound()){Fail("ForceAttack Meteor rejected round "+round);yield break;}
            while(arena.boss.Attacking||Time.realtimeSinceStartupAsDouble-roundAt<7){yield return null;if(finished)yield break;if(Time.realtimeSinceStartupAsDouble-roundAt>20){Fail("Meteor round timeout");yield break;}}
            // Allow Destroy queues to settle; these frames remain included, no screenshot/readback.
            yield return null;yield return null;FinishRound();
        }
        recording=true;round=13;if(!BeginRound()){Fail("Recording Meteor rejected");yield break;}
        recordAt=Time.realtimeSinceStartup;
        while(Time.realtimeSinceStartupAsDouble-roundAt<7){yield return new WaitForEndOfFrame();if(finished)yield break;if(Time.realtimeSinceStartup>=recordAt){recordAt=Time.realtimeSinceStartup+1f/30f;WriteCapture("frames/frame-"+(recordIndex++).ToString("D05")+".jpg");}}
        recording=false;yield return Capture("stage-after-meteor.jpg");Finish(true);
    }
    bool BeginRound()
    {
        if(!Exact())throw new Exception("Formal slots drift before round; never overwritten");
        arena.Restart(true);arena.boss.decisionEnabled=false;arena.player.ResetPlayer(new Vector3(8,.05f,-9),Quaternion.LookRotation(Vector3.ProjectOnPlane(arena.boss.transform.position-new Vector3(8,.05f,-9),Vector3.up)));
        arena.view.ResetCamera();arena.view.RecenterView();arena.view.TickCamera(1,true);if(!arena.view.LockedOn)arena.view.TickLook(Vector2.zero,true);
        report.locked=arena.view.LockedOn;if(!report.locked)return false;roundTimes.Clear();roundAt=lastWall=Time.realtimeSinceStartupAsDouble;
        return arena.boss.ForceAttack(ArenaAttack.Meteor,arena);
    }
    void Update()
    {
        if(finished)return;
        try{
            if(Time.realtimeSinceStartupAsDouble-started>360)throw new TimeoutException("360 second QA bound");
            if(arena==null)return;
            double now=Time.realtimeSinceStartupAsDouble,dt=Math.Max(0,now-lastWall);lastWall=now;
            if(roundAt<=0)return;
            arena.SimulateElapsed(default,dt);arena.view.TickCamera((float)dt);
            if(!recording&&round<13){
                var f=new Frame{round=round,warmup=round<3,frame=Time.frameCount,wall=now,deltaMs=Time.unscaledDeltaTime*1000d,focused=Application.isFocused,skillTime=arena.boss.SkillTime,attack=(int)arena.boss.CurrentAttack,attacking=arena.boss.Attacking,cpuMs=-1,gpuMs=-1};
                uint count=FrameTimingManager.GetLatestTimings(1,timing);if(count>0&&timing[0].frameStartTimestamp>lastTiming){lastTiming=timing[0].frameStartTimestamp;if(timing[0].cpuFrameTime>0)f.cpuMs=timing[0].cpuFrameTime;if(timing[0].gpuFrameTime>0)f.gpuMs=timing[0].gpuFrameTime;}
                FrameTimingManager.CaptureFrameTimings();report.frames.Add(f);roundTimes.Add(f.deltaMs);
            }
        }catch(Exception e){Fail(e.ToString());}
    }
    void FinishRound()
    {
        if(!Exact())throw new Exception("Formal slots drift after round; never overwritten");
        var times=roundTimes.OrderBy(v=>v).ToArray();report.rounds.Add(new Round{index=round,warmup=round<3,frames=times.Length,duration=Time.realtimeSinceStartupAsDouble-roundAt,meanMs=times.Length>0?times.Average():-1,p95Ms=times.Length>0?times[Math.Min(times.Length-1,(int)Math.Ceiling(times.Length*.95)-1)]:-1,maxMs=times.Length>0?times[times.Length-1]:-1,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,allocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),managedBytes=GC.GetTotalMemory(false),gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2)});
    }
    IEnumerator Capture(string file){yield return new WaitForEndOfFrame();WriteCapture(file);}
    void WriteCapture(string file)
    {
        double captureWall=Time.realtimeSinceStartupAsDouble;Texture2D tex=null;try{tex=ScreenCapture.CaptureScreenshotAsTexture();string path=Path.Combine(output,file);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,tex.EncodeToJPG(80));report.captures.Add(file+" wall="+captureWall.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));}finally{if(tex)Destroy(tex);}
    }
    void Fail(string error){report.error=error;Finish(false);}
    void Finish(bool complete)
    {
        if(finished)return;finished=true;report.complete=complete;report.formalExact=Exact();report.width=Screen.width;report.height=Screen.height;
        if(arena){arena.automaticTick=false;arena.boss.decisionEnabled=false;}
        File.WriteAllText(Path.Combine(output,"promo-arena-report.json"),JsonUtility.ToJson(report,true));Debug.Log("PROMO_ARENA_QA_COMPLETE "+output+" formalExact="+report.formalExact);
        if(Environment.GetCommandLineArgs().Contains("--promo-exit"))Application.Quit(complete&&report.formalExact?0:2);
        else if(complete&&Environment.GetCommandLineArgs().Contains("--promo-manual"))
        {
            arena.Restart(false);arena.automaticTick=true;arena.view.automaticTick=true;arena.boss.decisionEnabled=true;
            Debug.Log("PROMO_MANUAL_READY: original keyboard/mouse and practice controls restored; QA report excludes subsequent manual play.");
        }
    }
    void OnApplicationQuit(){if(!finished){report.error="Quit before completed";Finish(false);}else{report.formalExact=Exact();File.WriteAllText(Path.Combine(output,"promo-arena-report.json"),JsonUtility.ToJson(report,true));}}
}
