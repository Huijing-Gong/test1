#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KingOfGraphic.Hero;

// Opt-in private QA. Authored ladder + actual CharacterController movement.
// Relocation/setup and synthetic keys are disclosed; no animation is directly played.
public sealed class HeroBlockedExitQA : MonoBehaviour {
    const string Root="ToolReports/HeroIntegration20261016/BlockedExitQA";
    [Serializable] public class Check { public string name,detail; public bool pass; }
    [Serializable] public class Frame { public float realtime,delta,along; public int phase; public Vector3 position; public string action,stage; }
    [Serializable] public class Report { public string evidence="Controlled private QA: API relocation/admission + synthetic InputSystem, temporary obstacle, actual authored ladder and CharacterController. Not natural play."; public List<Check> checks=new List<Check>();public List<Frame> frames=new List<Frame>();public bool completed; }
    Report report=new Report(); HeroWorldAdapter hero; HeroTraversal traversal; PlayerMovement movement; PlayerVitals vitals;
    HeroTraversalSurface ladder; Keyboard keyboard,priorKeyboard; Mouse mouse,priorMouse;
    GameObject obstacle; Vector3 originalPosition; Quaternion originalRotation; float oldScale;
    CursorLockMode oldCursor;bool oldCursorVisible,restored,monitor; string stage="setup";
    readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    int Phase=>traversal?(int)typeof(HeroTraversal).GetField("phase",flags).GetValue(traversal):0;
    float Along=>traversal?(float)typeof(HeroTraversal).GetField("along",flags).GetValue(traversal):0;
    void Save(){Directory.CreateDirectory(Root);File.WriteAllText(Root+"/report.json",JsonUtility.ToJson(report,true));}
    void CheckIt(string name,bool pass,string detail){report.checks.Add(new Check{name=name,pass=pass,detail=detail});Save();}
    void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(960,540)});}
    void LateUpdate(){if(monitor&&hero)report.frames.Add(new Frame{realtime=Time.realtimeSinceStartup,delta=Time.deltaTime,phase=Phase,along=Along,position=hero.transform.position,action=hero.CurrentAction,stage=stage});}
    IEnumerator WaitUntilBounded(Func<bool> condition,float timeout){float end=Time.realtimeSinceStartup+timeout;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;}
    IEnumerator Start(){
        var flow=FindFirstObjectByType<BaselineFlow>();
        if(!flow||!flow.saveKeyPrefix.StartsWith("QA.NativePlay.",StringComparison.Ordinal)){CheckIt("private_scope",false,"Refused: requires existing isolated observer prefix BEFORE BaselineFlow.Start");yield break;}
        if(!flow.Started)flow.ContinueGame();yield return new WaitForSecondsRealtime(.5f);
        hero=FindFirstObjectByType<HeroWorldAdapter>();if(!hero){CheckIt("hero_available",false,"No hero");yield break;}
        traversal=hero.GetComponent<HeroTraversal>();movement=hero.GetComponent<PlayerMovement>();vitals=hero.GetComponent<PlayerVitals>();
        ladder=HeroTraversalSurface.Available.FirstOrDefault(x=>x&&x.kind==HeroTraversalKind.Ladder&&x.topExit);
        if(!ladder||!traversal){CheckIt("ladder_available",false,"No authored top-exit ladder");yield break;}
        originalPosition=hero.transform.position;originalRotation=hero.transform.rotation;oldScale=Time.timeScale;oldCursor=Cursor.lockState;oldCursorVisible=Cursor.visible;
        priorKeyboard=Keyboard.current;priorMouse=Mouse.current;keyboard=InputSystem.AddDevice<Keyboard>("BlockedExitSynthetic");mouse=InputSystem.AddDevice<Mouse>("BlockedExitSyntheticMouse");keyboard.MakeCurrent();mouse.MakeCurrent();
        try {
            Time.timeScale=1;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;hero.CancelForTransition();vitals.Refill();
            movement.Relocate(ladder.Top-ladder.transform.forward*.3f,ladder.transform.rotation);Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.65f);Keys();monitor=true;
            bool entered=traversal.TryEnter(ladder);CheckIt("top_admission",entered,"API setup admission from top; action="+traversal.ActionId+" position="+hero.transform.position);
            if(!entered)yield break;
            yield return WaitUntilBounded(()=>Phase==2,hero.Library.Duration("LADDER_ENTER_TOP")+2);
            CheckIt("entered_top_hold",Phase==2&&traversal.ActionId=="LADDER_HOLD"&&Along>.95f,"phase="+Phase+" along="+Along);if(Phase!=2)yield break;
            Vector3 holdPosition=hero.transform.position;int first=report.frames.Count;
            stage="blocked_exit";Keys(Key.W);yield return WaitUntilBounded(()=>Phase==3,.6f);Keys();
            CheckIt("exit_started",Phase==3,"phase="+Phase);if(Phase!=3)yield break;
            // Placed only AFTER exit admission: blocks the path and destination clearance.
            obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.name="QA temporary ladder exit blocker";
            obstacle.transform.rotation=ladder.transform.rotation;obstacle.transform.position=ladder.Top+ladder.transform.forward*.58f+Vector3.up*.95f;obstacle.transform.localScale=new Vector3(3,2,.22f);Physics.SyncTransforms();
            yield return WaitUntilBounded(()=>Phase==4||!traversal.Active,hero.Library.Duration("LADDER_EXIT_TOP")+2);
            CheckIt("blocked_exit_uses_return",Phase==4,"phase="+Phase+" actual="+hero.transform.position);if(Phase!=4)yield break;
            stage="return";yield return WaitUntilBounded(()=>Phase==2||!traversal.Active,6);
            CheckIt("returned_to_hold",traversal.Active&&Phase==2&&Vector3.Distance(hero.transform.position,holdPosition)<.2f,"position="+hero.transform.position+" originalHold="+holdPosition+" phase="+Phase+" along="+Along);
            var sample=report.frames.Skip(first).ToArray();float maximum=0;bool smooth=true;
            for(int i=1;i<sample.Length;i++){float distance=Vector3.Distance(sample[i].position,sample[i-1].position);maximum=Mathf.Max(maximum,distance);if((sample[i-1].phase==4||sample[i].phase==4)&&distance>Mathf.Max(.12f,sample[i].delta*5+.04f))smooth=false;}
            CheckIt("return_no_snap",smooth,"phase4 displacement bound 5m/s + .04m (min .12m); overallMaxStep="+maximum+" samples="+sample.Length);
            obstacle.SetActive(false);Physics.SyncTransforms();stage="normal_exit";
            if(Phase==2){Keys(Key.W);yield return WaitUntilBounded(()=>Phase==3,.6f);Keys();yield return WaitUntilBounded(()=>!traversal.Active,hero.Library.Duration("LADDER_EXIT_TOP")+3);}
            CheckIt("normal_exit_after_unblocked",!traversal.Active&&!movement.IsGameplayInputLocked,"phase="+Phase+" locked="+movement.IsGameplayInputLocked+" pos="+hero.transform.position);
            report.completed=true;Save();
        } finally {Restore();Save();}
    }
    void Restore(){if(restored)return;restored=true;monitor=false;if(obstacle){obstacle.SetActive(false);Destroy(obstacle);}if(hero){hero.CancelForTransition();movement.Relocate(originalPosition,originalRotation);}Time.timeScale=oldScale;Cursor.lockState=oldCursor;Cursor.visible=oldCursorVisible;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);priorKeyboard?.MakeCurrent();priorMouse?.MakeCurrent();}
    void OnDestroy(){if(hero)Restore();Save();}
}
#endif
