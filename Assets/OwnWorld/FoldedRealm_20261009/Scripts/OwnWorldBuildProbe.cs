using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

public sealed class OwnWorldBuildProbe : MonoBehaviour
{
    string dir,prefix,real,primary,backup;
    BaselineFlow flow;BaselineWorld world;ThirdPersonCamera look;PlayerMovement player;
    float started;bool finished;
    [Serializable] public sealed class Region { public string name;public int samples,cpuSamples,gpuSamples;public float medianMs,p95Ms,maxMs,cpuMedianMs,gpuMedianMs;public long allocatedBytes; }
    [Serializable] public sealed class Receipt { public string method,utc,unity,cpu,gpu,graphicsApi,quality;public int width,height;public bool realSlotsUnchanged,qaSlotCleared;public Region[] regions; }
    readonly List<Region> regions=new List<Region>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartOptIn()
    {
#if !UNITY_EDITOR
        var args=Environment.GetCommandLineArgs();if(!args.Contains("-own-world-profile"))return;
        var f=FindFirstObjectByType<BaselineFlow>();if(!f)return;
        var g=new GameObject("Own world opt-in standalone probe");DontDestroyOnLoad(g);g.AddComponent<OwnWorldBuildProbe>();
#endif
    }
    IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();int n=Array.IndexOf(args,"-own-world-profile-dir");
        dir=n>=0&&n+1<args.Length?args[n+1]:Application.persistentDataPath+"/OwnWorldProfile";
        Directory.CreateDirectory(dir);started=Time.realtimeSinceStartup;Application.runInBackground=true;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        flow=FindFirstObjectByType<BaselineFlow>();world=flow.GetComponent<BaselineWorld>();player=world.player.GetComponent<PlayerMovement>();look=Camera.main.GetComponent<ThirdPersonCamera>();
        real=flow.saveKeyPrefix;primary=PlayerPrefs.HasKey(real+".primary")?PlayerPrefs.GetString(real+".primary"):null;backup=PlayerPrefs.HasKey(real+".backup")?PlayerPrefs.GetString(real+".backup"):null;
        prefix="QA.OwnWindowsProfile."+Guid.NewGuid().ToString("N");flow.saveKeyPrefix=prefix;
        Log("METHOD: opt-in Windows standalone; isolated save; API fixture and fixed-camera rendered frame sampling; VSync off; main world uncapped; Sam preserves its production 60 FPS cap; actual GPU; not natural play or full performance acceptance.");
        Log("Waiting for game window focus before GPU sample");float focusDeadline=Time.realtimeSinceStartup+65;while(!Application.isFocused&&Time.realtimeSinceStartup<focusDeadline)yield return null;if(!Application.isFocused){Log("FAIL no focused visible window; no GPU acceptance");finished=true;Application.Quit(4);yield break;}yield return null;flow.NewGame();yield return new WaitForSecondsRealtime(.5f);flow.Tutorial.Skip();GameSession.Instance.RestoreProgress(true,true,false);
        float until=Time.realtimeSinceStartup+28;while(look.CinematicControlled&&Time.realtimeSinceStartup<until)yield return null;
        flow.SetMenu(false);yield return new WaitForSecondsRealtime(1f);
        player.SetGameplayInputLocked(true);look.SetLookInputLocked(true);look.enabled=false;
        Vector3[] poses={new Vector3(0,2.7f,-97),new Vector3(-73.7f,5,-11.6f),new Vector3(-47,17,93),new Vector3(72.38f,34.2f,78.94f)};
        Vector3[] eyes={new Vector3(1.29f,5,-102.24f),new Vector3(-73.76f,9,-11.65f),new Vector3(-47,21,93),new Vector3(70,36.5f,64)};
        Vector3[] targets={new Vector3(0,12,-50.47f),new Vector3(-44,12,48),new Vector3(-20,12,0),new Vector3(72,38,80)};
        string[] names={"arrival","lake","rear-bank","highest-entrance"};
        for(int i=0;i<names.Length;i++)
        {
            player.Relocate(poses[i],Quaternion.identity);Physics.SyncTransforms();Camera.main.transform.position=eyes[i];Camera.main.transform.LookAt(targets[i]);Camera.main.fieldOfView=60;
            yield return Sample(names[i],Camera.main);
        }
        player.SetGameplayInputLocked(false);look.enabled=true;look.SetLookInputLocked(false);player.Relocate(flow.endingEntrance.position,Quaternion.identity);Physics.SyncTransforms();yield return new WaitForSecondsRealtime(.6f);
        var bridge=flow.SamBridge;
        if(bridge&&bridge.TryEnter())
        {
            until=Time.realtimeSinceStartup+40;while(bridge.Busy&&Time.realtimeSinceStartup<until)yield return null;
            if(bridge.IsAway&&!bridge.Busy){Log("Sam bridge standalone loaded; initial sword damage="+bridge.Session.player.swordDamage);yield return Sample("sam-arena",Camera.main);bridge.TryReturn();until=Time.realtimeSinceStartup+30;while(bridge.Busy&&Time.realtimeSinceStartup<until)yield return null;Log("Sam standalone return="+!bridge.IsAway);}
            else Log("FAIL Sam standalone load timeout");
        }else Log("FAIL Sam standalone entry");
        bool same=(PlayerPrefs.HasKey(real+".primary")?PlayerPrefs.GetString(real+".primary"):null)==primary&&(PlayerPrefs.HasKey(real+".backup")?PlayerPrefs.GetString(real+".backup"):null)==backup;
        Destroy(flow.gameObject);yield return null;BaselineSaveStore.Clear(prefix);PlayerPrefs.Save();bool cleared=!PlayerPrefs.HasKey(prefix+".primary")&&!PlayerPrefs.HasKey(prefix+".backup");
        var receipt=new Receipt{method="Windows standalone controlled static views, 45 warmup and 240 measured rendered frames/view. Default project quality; VSync off; main-world views uncapped, Sam uses its production 60 FPS cap. CPU/GPU only when FrameTiming returns positive. Unity tracked allocation differs from OS working set. No natural playthrough claim.",utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),quality=QualitySettings.names[QualitySettings.GetQualityLevel()],width=Screen.width,height=Screen.height,realSlotsUnchanged=same,qaSlotCleared=cleared,regions=regions.ToArray()};
        File.WriteAllText(dir+"/receipt.json",JsonUtility.ToJson(receipt,true));Log("DONE realSlotsUnchanged="+same+" qaSlotCleared="+cleared);finished=true;Application.Quit(same&&cleared?0:2);
    }
    IEnumerator Sample(string name,Camera camera)
    {
        for(int i=0;i<45;i++){FrameTimingManager.CaptureFrameTimings();yield return null;}
        var frame=new List<float>();var cpu=new List<float>();var gpu=new List<float>();var timing=new FrameTiming[1];var csv=new StringBuilder("frame,elapsedMs,cpuMs,gpuMs\n");
        for(int i=0;i<240;i++)
        {
            FrameTimingManager.CaptureFrameTimings();yield return null;float delta=Time.unscaledDeltaTime*1000;frame.Add(delta);double c=0,g=0;
            if(FrameTimingManager.GetLatestTimings(1,timing)>0){c=timing[0].cpuFrameTime;g=timing[0].gpuFrameTime;if(c>0)cpu.Add((float)c);if(g>0)gpu.Add((float)g);}
            csv.AppendLine(i+","+delta.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+c.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+g.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        }
        frame.Sort();cpu.Sort();gpu.Sort();var r=new Region{name=name,samples=frame.Count,cpuSamples=cpu.Count,gpuSamples=gpu.Count,medianMs=Q(frame,.5f),p95Ms=Q(frame,.95f),maxMs=frame.Max(),cpuMedianMs=Q(cpu,.5f),gpuMedianMs=Q(gpu,.5f),allocatedBytes=Profiler.GetTotalAllocatedMemoryLong()};regions.Add(r);File.WriteAllText(dir+"/"+name+".csv",csv.ToString());Log(name+" medianMs="+r.medianMs+" p95Ms="+r.p95Ms+" allocated="+r.allocatedBytes);ScreenCapture.CaptureScreenshot(dir+"/"+name+".png");yield return new WaitForEndOfFrame();yield return null;
    }
    float Q(List<float> values,float q){return values.Count==0?0:values[Mathf.Clamp(Mathf.RoundToInt((values.Count-1)*q),0,values.Count-1)];}
    void Log(string text){Debug.Log("[OwnWindowsProbe] "+text);File.AppendAllText(dir+"/run.txt",text+"\n");}
    void Update(){if(!finished&&started>0&&Time.realtimeSinceStartup-started>220){Log("FAIL bounded timeout; QA prefix retained, real prefix protected");finished=true;Application.Quit(3);}}
}
