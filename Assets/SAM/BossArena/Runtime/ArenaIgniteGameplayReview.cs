using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace SamProduction.BossArena
{
    public sealed class ArenaIgniteGameplayReview : MonoBehaviour
    {
        [Serializable] private sealed class Frame
        {public double wall,simulated,raw;public int stage;public float sourceTime,health;public bool active,invulnerable,cameraOwned,focused;public Vector3 boss,player,leftFoot,rightFoot,camera;}
        [Serializable] private sealed class Stage
        {public string name;public bool passed;public ArenaEventRecord[] events;}
        [Serializable] private sealed class Report
        {public string scope="NORMAL_CLOCK_SCRIPTED_IGNITION_NOT_HALF_HP_AI_NATIVE_INPUT_OR_PERFORMANCE",build,error;public bool complete;public double simulated,maxInterval,unsimulatedBoundary;public Frame[] frames;public Stage[] stages;}
        private static readonly string[] Names={"CINEMATIC + CONTROL RETURN","FRONT SOURCE VIEW","SIDE SOURCE VIEW"};
        private ArenaSession arena;private ArenaIgniteMotor motor;private Camera reviewCamera;
        private readonly List<Frame> frames=new List<Frame>();private readonly List<Stage> stages=new List<Stage>();
        private string output,error;private int stage;private double started,previous,simulated,stageTime,maxInterval,boundary;
        private bool ready,finishing,saved,began,dodged;private GUIStyle style;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--sam-ignite-review")<0)return;
            if(args.Any(a=>a!="--sam-ignite-review"&&(a.EndsWith("-contracts",StringComparison.Ordinal)||a.EndsWith("-review",StringComparison.Ordinal)||a=="--sam-benchmark")))
            {Debug.LogError("Multiple simulation owners");Application.Quit(2);return;}
            new GameObject("Ignite normal clock review").AddComponent<ArenaIgniteGameplayReview>();
        }
        private static string Arg(string key,string fallback)
        {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        private IEnumerator Start()
        {
            yield return null;
            try
            {
                output=Path.GetFullPath(Arg("--sam-output",Path.Combine(Application.persistentDataPath,"ignite_review")));
                Require(!File.Exists(Path.Combine(output,"realtime_ignite.json")),"Preserve prior recording");Directory.CreateDirectory(output);
                arena=FindFirstObjectByType<ArenaSession>();motor=arena?.boss.IgniteMotor;Require(motor!=null,"Imported ignition absent");
                arena.automaticTick=false;arena.view.automaticTick=false;arena.boss.decisionEnabled=false;arena.boss.useDashSampleAI=false;
                arena.Restart(false);reviewCamera=arena.view.GetComponent<Camera>();
                Application.runInBackground=true;Time.captureFramerate=0;Time.timeScale=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
                Screen.SetResolution(1920,1080,FullScreenMode.Windowed);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
            catch(Exception e){Fail(e);yield break;}
            yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();
            try{started=previous=Time.realtimeSinceStartupAsDouble;BeginStage(0);ready=true;}catch(Exception e){Fail(e);}
        }
        private void BeginStage(int index)
        {
            stage=index;stageTime=0;began=dodged=false;
            arena.bossStart=new Vector3(0,.025f,0);arena.playerStart=new Vector3(stage==0?0:3,.05f,6);arena.Restart(true);arena.boss.ResetBoss(arena.bossStart,Quaternion.identity);
            arena.player.ResetPlayer(arena.playerStart,Quaternion.Euler(0,180,0));arena.view.ResetCamera();arena.view.TickLook(Vector2.zero,true);arena.view.TickCamera(0,true);Physics.SyncTransforms();
        }
        private void Update()
        {
            if(!ready||finishing||saved)return;
            try
            {
                Require(Time.captureFramerate==0&&Mathf.Abs(Time.timeScale-1)<.000001f,"Clock changed");
                double now=Time.realtimeSinceStartupAsDouble,raw=now-previous;previous=now;Require(raw>=0&&raw<=5,"Unreviewable display interval");maxInterval=Math.Max(maxInterval,raw);double remaining=raw;
                while(remaining>1e-7&&!finishing)
                {
                    if(!began&&stageTime>=.4){Require(arena.boss.TrySkill(ArenaAttack.Ignite),"Actual brain rejected ignition");began=true;}
                    if(began&&!motor.Active&&stageTime>=7)
                    {
                        bool okay=motor.Completed&&!arena.view.CinematicActive&&!arena.boss.fighter.Invulnerable&&motor.BeginCount==1;
                        stages.Add(new Stage{name=Names[stage],passed=okay,events=arena.Events.ToArray()});Require(okay,"Ignition fixture failed");
                        if(stage==2){finishing=true;boundary=remaining;StartCoroutine(Finish());break;}BeginStage(stage+1);continue;
                    }
                    Require(stageTime<9,"Ignition stalled");ArenaCommand command=default;
                    if(stage==0&&began)
                    {
                        command.move=Vector2.right;
                        if(motor.Active){command.attack=true;command.dodge=true;command.look=new Vector2(20,0);}
                        else if(!dodged&&stageTime>=5.4){command.dodge=true;dodged=true;}
                    }
                    double step=Math.Min(1d/120,remaining);arena.Simulate(command,(float)step);arena.view.TickCamera((float)step);simulated+=step;stageTime+=step;remaining-=step;
                }
                if(stage>0)
                {
                    Vector3 target=new Vector3(0,1.65f,.15f);reviewCamera.fieldOfView=42;
                    reviewCamera.transform.position=target+(stage==1?new Vector3(0,.5f,7.5f):new Vector3(7.5f,.5f,0));reviewCamera.transform.LookAt(target);
                }
                frames.Add(new Frame{wall=now-started,simulated=simulated,raw=raw,stage=stage,sourceTime=motor.SourceTime,health=arena.boss.fighter.Health,
                    active=motor.Active,invulnerable=arena.boss.fighter.Invulnerable,cameraOwned=arena.view.CinematicActive,focused=Application.isFocused,
                    boss=arena.boss.transform.position,player=arena.player.transform.position,leftFoot=arena.boss.Socket("socket_foot.L").position,rightFoot=arena.boss.FootPosition,camera=reviewCamera.transform.position});
            }
            catch(Exception e){Fail(e);}
        }
        private IEnumerator Finish(){yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();Save(true);Application.Quit();}
        private void Save(bool complete)
        {
            if(saved||output==null)return;saved=true;File.WriteAllText(Path.Combine(output,"realtime_ignite.json"),JsonUtility.ToJson(new Report{build=Application.version,error=error,complete=complete,
                simulated=simulated,maxInterval=maxInterval,unsimulatedBoundary=boundary,frames=frames.ToArray(),stages=stages.ToArray()},true));
        }
        private void Fail(Exception e){error=e.ToString();Debug.LogException(e);ready=false;finishing=true;Save(false);Application.Quit(2);}
        private void OnApplicationQuit(){if(!saved)Save(false);}
        private void OnGUI()
        {
            if(arena==null)return;if(style==null)style=new GUIStyle(GUI.skin.label){fontSize=18,normal={textColor=Color.white}};
            GUI.Label(new Rect(24,230,1400,90),"IGNITION 18 / "+Names[stage]+"\nScripted fixture + ordinary wall clock / full source, owned camera, actual control return\nHalf-health AI, native input and performance are separate",style);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
