using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Opt-in labelled fixtures; ordinary elapsed wall time and real combat resolver.
    public sealed class ArenaDiveGameplayReview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        { public double wall,simulated,raw;public int stage;public float sourceTime,radius,health;public bool active,cancelled,invulnerable,focused;public Vector3 boss,player,leftFoot,rightFoot,centre,camera; }
        [Serializable] private sealed class Stage
        { public string name;public bool passed;public int hits,dodges;public ArenaEventRecord[] events; }
        [Serializable] private sealed class Report
        { public string scope="NORMAL_WALL_CLOCK_SCRIPTED_GAMEPLAY_NOT_NATIVE_INPUT_OR_PERFORMANCE",build,view,error;public bool complete;public double simulated,maxInterval,unsimulatedBoundary;public Frame[] frames;public Stage[] stages; }
        private static readonly string[] Names={"STATIONARY / ONE WAVE HIT","LATE DODGE / ACTUAL AVOID","RUN OUT AFTER LOCK","AIR CANCEL / FULL LANDING"};
        private ArenaSession arena;private ArenaDiveMotor motor;private Camera camera;
        private readonly List<Frame> frames=new List<Frame>();private readonly List<Stage> stages=new List<Stage>();
        private string output,view,error;private int stage;private double started,previous,simulated,stageTime,maxInterval,boundary;
        private bool ready,finishing,saved,began,commandIssued;private GUIStyle style;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-dive-review")<0)return;
            foreach(string flag in new[]{"--sam-benchmark","--sam-dive-contracts","--sam-dash-review","--sam-dash-contracts"})
                if(Array.IndexOf(args,flag)>=0){Debug.LogError("Multiple simulation owners");Application.Quit(2);return;}
            new GameObject("Dive normal clock review").AddComponent<ArenaDiveGameplayReview>();
        }
        private static string Arg(string key,string fallback)
        {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        private IEnumerator Start()
        {
            yield return null;
            try
            {
                output=Path.GetFullPath(Arg("--sam-output",Path.Combine(Application.persistentDataPath,"dive_review")));
                if(File.Exists(Path.Combine(output,"realtime_dive.json")))throw new IOException("Preserve prior review");Directory.CreateDirectory(output);
                arena=FindFirstObjectByType<ArenaSession>();motor=arena?.boss.DiveMotor;Require(motor!=null,"Dive source absent");
                arena.automaticTick=false;arena.view.automaticTick=false;arena.boss.decisionEnabled=false;arena.boss.useDashSampleAI=false;arena.player.view=null;
                arena.Restart(false);view=Arg("--sam-view","side");camera=arena.view.GetComponent<Camera>();
                Application.runInBackground=true;Time.captureFramerate=0;Time.timeScale=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
                Screen.SetResolution(1920,1080,FullScreenMode.Windowed);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;PositionCamera();
            }
            catch(Exception e){Fail(e);yield break;}
            yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();
            try{started=previous=Time.realtimeSinceStartupAsDouble;BeginStage(0);ready=true;}catch(Exception e){Fail(e);}
        }
        private void BeginStage(int index)
        {
            stage=index;stageTime=0;began=commandIssued=false;
            arena.bossStart=new Vector3(0,.025f,0);arena.playerStart=new Vector3(0,.05f,15);arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.identity);
            arena.player.ResetPlayer(new Vector3(0,.05f,6),Quaternion.identity);Physics.SyncTransforms();
        }
        private void Update()
        {
            if(!ready||finishing||saved)return;
            try
            {
                Require(Time.captureFramerate==0&&Mathf.Abs(Time.timeScale-1)<.000001f,"Clock changed");
                double now=Time.realtimeSinceStartupAsDouble,raw=now-previous;previous=now;Require(raw>=0&&raw<=5,"Unreviewable display interval");maxInterval=Math.Max(maxInterval,raw);
                double remaining=raw;
                while(remaining>.0000001&&!finishing)
                {
                    if(!began&&stageTime>=.25){Require(arena.boss.TrySkill(ArenaAttack.DiveKick),"Actual brain rejected source");began=true;}
                    if(began&&!motor.Active&&stageTime>=7)
                    {
                        int hits=arena.player.fighter.HitCount,dodges=arena.player.fighter.DodgeCount;
                        bool okay=stage==0?hits==1:stage==1?hits==0&&dodges==1:hits==0;
                        stages.Add(new Stage{name=Names[stage],passed=okay,events=arena.Events.ToArray(),hits=hits,dodges=dodges});Require(okay,"Actual fixture result failed: "+Names[stage]);
                        if(stage==3){finishing=true;boundary=remaining;StartCoroutine(Finish());break;}BeginStage(stage+1);continue;
                    }
                    Require(stageTime<10,"Source recovery stalled");ArenaCommand command=default;
                    if(stage==1&&motor.Active&&motor.SourceTime>=3.4f&&!commandIssued){command=new ArenaCommand{dodge=true,move=Vector2.right};commandIssued=true;}
                    if(stage==3&&motor.Active&&motor.SourceTime>=2.9f&&!commandIssued){arena.boss.CancelAttack();commandIssued=true;}
                    if(stage==2&&motor.Locked)command=new ArenaCommand{move=Vector2.right,run=true};
                    double step=Math.Min(1d/120,remaining);arena.Simulate(command,(float)step);simulated+=step;stageTime+=step;remaining-=step;
                }
                PositionCamera();frames.Add(new Frame{wall=now-started,simulated=simulated,raw=raw,stage=stage,sourceTime=motor.SourceTime,radius=motor.WaveRadius,
                    health=arena.player.fighter.Health,active=motor.Active,cancelled=motor.Cancelled,invulnerable=arena.player.fighter.Invulnerable,focused=Application.isFocused,
                    boss=arena.boss.transform.position,player=arena.player.transform.position,leftFoot=arena.boss.Socket("socket_foot.L").position,rightFoot=arena.boss.FootPosition,centre=motor.Centre,camera=camera.transform.position});
            }
            catch(Exception e){Fail(e);}
        }
        private void PositionCamera()
        {
            if(camera==null)return;
            if(view=="close_front"||view=="close_side")
            {
                var focus=arena.boss.transform.position+Vector3.up*1.8f;camera.fieldOfView=48;
                camera.transform.position=focus+(view=="close_front"?new Vector3(0,1,9):new Vector3(9,1,0));camera.transform.LookAt(focus);return;
            }
            Vector3 target=new Vector3(0,2.5f,4);
            camera.fieldOfView=50;camera.transform.position=target+(view=="front"?new Vector3(0,6,20):new Vector3(17,7,3));camera.transform.LookAt(target);
        }
        private IEnumerator Finish(){yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();Save(true);Application.Quit();}
        private void Save(bool complete)
        {
            if(saved||output==null)return;saved=true;
            File.WriteAllText(Path.Combine(output,"realtime_dive.json"),JsonUtility.ToJson(new Report{build=Application.version,view=view,error=error,complete=complete,
                simulated=simulated,maxInterval=maxInterval,unsimulatedBoundary=boundary,frames=frames.ToArray(),stages=stages.ToArray()},true));
        }
        private void Fail(Exception e){error=e.ToString();Debug.LogException(e);ready=false;finishing=true;Save(false);Application.Quit(2);}
        private void OnApplicationQuit(){if(!saved)Save(false);}
        private void OnGUI()
        {
            if(arena==null)return;if(style==null)style=new GUIStyle(GUI.skin.label){fontSize=18,normal={textColor=Color.white}};
            GUI.Label(new Rect(24,230,1400,90),"DIVE 18 / "+Names[stage]+"\nScripted fixture + ordinary wall clock / "+view+" / source motion, fire wave and actual damage\nNative input and performance verification are separate",style);
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
