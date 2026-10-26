#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KingOfGraphic.Hero;
public sealed class HeroControlledQA:MonoBehaviour
{
    [Serializable] public class CheckResult{public string name,detail;public bool pass;}
    [Serializable] public class Report{public string evidence="Editor controlled InputSystem test device plus QA setup relocation; not hardware play";public List<CheckResult> checks=new List<CheckResult>();public List<string> observed=new List<string>();public bool completed;}
    Report report=new Report();Keyboard keyboard,priorKeyboard;Mouse mouse,priorMouse;
    HeroWorldAdapter hero;PlayerMovement movement;PlayerVitals vitals;HeroTraversal traversal;BaselineFlow flow;
    string root="ToolReports/HeroIntegration20261016/ControlledQA";bool finalized;
    void Save(){Directory.CreateDirectory(root);File.WriteAllText(root+"/report.json",JsonUtility.ToJson(report,true));}
    void Check(string name,bool pass,string detail){report.checks.Add(new CheckResult{name=name,pass=pass,detail=detail});Save();}
    void LateUpdate(){if(!hero)return;foreach(var id in new[]{hero.CurrentAction,hero.pose.CurrentOverlay})if(!string.IsNullOrEmpty(id)&&!report.observed.Contains(id)){report.observed.Add(id);Save();}}
    void Set(Key[] keys,bool left=false){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(960,540)}.WithButton(MouseButton.Left,left));}
    IEnumerator Hold(float seconds,Key[] keys=null,bool left=false){Set(keys??Array.Empty<Key>(),left);yield return new WaitForSecondsRealtime(seconds);Set(Array.Empty<Key>());yield return null;}
    void Place(Vector3 position,Quaternion rotation){hero.CancelForTransition();movement.Relocate(position,rotation);vitals.ResetTraversal();Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
    IEnumerator Start()
    {
        flow=FindFirstObjectByType<BaselineFlow>();if(!flow||flow.saveKeyPrefix!="QA.NativePlay.Hero20261016"){Check("private_save",false,"Missing private prefix");yield break;}
        if(!flow.Started)flow.ContinueGame();yield return new WaitForSecondsRealtime(.5f);
        hero=FindFirstObjectByType<HeroWorldAdapter>();movement=hero.GetComponent<PlayerMovement>();vitals=hero.GetComponent<PlayerVitals>();traversal=hero.GetComponent<HeroTraversal>();
        priorKeyboard=Keyboard.current;priorMouse=Mouse.current;keyboard=InputSystem.AddDevice<Keyboard>("HeroQAKeyboard");mouse=InputSystem.AddDevice<Mouse>("HeroQAMouse");keyboard.MakeCurrent();mouse.MakeCurrent();
        var origin=hero.transform.position;var yaw=hero.transform.rotation;
        yield return Hold(.08f,null,true);yield return new WaitForSecondsRealtime(.8f);
        Check("first_sword",report.observed.Contains("ATK_A1"),"LMB edge must produce A1 without draw wait");
        yield return Hold(1.9f,null,true);yield return new WaitForSecondsRealtime(1);
        Check("charged_release",report.observed.Contains("ATK_CHARGE_START")&&report.observed.Contains("ATK_CHARGE_HOLD")&&report.observed.Contains("ATK_CHARGE_RELEASE"),string.Join(",",report.observed));
        yield return Hold(.47f,null,true);yield return new WaitForSecondsRealtime(1.3f);
        Check("early_charge_heavy",report.observed.Contains("ATK_HEAVY"),"Release before full hold");
        yield return Hold(.8f,new[]{Key.Q});yield return new WaitForSecondsRealtime(.4f);
        yield return Hold(.55f,new[]{Key.LeftCtrl});yield return new WaitForSecondsRealtime(.4f);
        yield return Hold(.05f,new[]{Key.C});yield return Hold(.5f,new[]{Key.W});yield return Hold(.05f,new[]{Key.C});
        Check("crouch_and_dodge",report.observed.Contains("LOC_CROUCH_ENTER")&&report.observed.Any(x=>x.StartsWith("DODGE_")),"Controlled C/Ctrl");
        Place(origin,yaw);yield return new WaitForSecondsRealtime(.2f);
        yield return Hold(.08f,new[]{Key.Space});yield return new WaitForSecondsRealtime(1.4f);
        Check("jump",report.observed.Contains("AIR_TAKEOFF")&&report.observed.Contains("AIR_FALL"),"Existing jump controller");
        foreach(var surface in HeroTraversalSurface.Available.ToArray())
        {
            vitals.Refill();Place(surface.Bottom-surface.transform.forward*.35f,surface.transform.rotation);yield return new WaitForSecondsRealtime(.3f);
            float stamina=vitals.Stamina;yield return Hold(.08f,new[]{Key.E});yield return new WaitForSecondsRealtime(surface.kind==HeroTraversalKind.Mantle?4.2f:surface.kind==HeroTraversalKind.Seat?1.9f:surface.kind==HeroTraversalKind.Scramble?2.1f:surface.kind==HeroTraversalKind.Vault?1.2f:.9f);
            bool entered=traversal.Active;bool obstacle=surface.kind==HeroTraversalKind.LowStep||surface.kind==HeroTraversalKind.Vault||surface.kind==HeroTraversalKind.Mantle||surface.kind==HeroTraversalKind.Scramble;
            if(obstacle){Check(surface.name+" obstacle",!traversal.Active&&Vector3.Distance(hero.transform.position,surface.Top)<.4f,"position="+hero.transform.position+" top="+surface.Top);continue;}
            Check(surface.name+" entry",entered,"action="+traversal.ActionId+" position="+hero.transform.position);
            if(!entered)continue;
            if(surface.kind==HeroTraversalKind.Seat){yield return Hold(.1f,new[]{Key.E});yield return new WaitForSecondsRealtime(1.6f);Check(surface.name+" exit",!traversal.Active,"position="+hero.transform.position);continue;}
            if(surface.kind==HeroTraversalKind.Ledge){yield return Hold(.5f,new[]{Key.D});yield return Hold(.5f,new[]{Key.A});}
            yield return Hold(4.2f,new[]{Key.W});if(surface.topExit)yield return new WaitForSecondsRealtime(surface.kind==HeroTraversalKind.Ledge?2:4.1f);
            Check(surface.name+" stamina",vitals.Stamina<stamina-2,"before="+stamina+" after="+vitals.Stamina+" contactError="+traversal.MaximumHandContactError);
            if(surface.topExit)Check(surface.name+" top_exit",!traversal.Active,"position="+hero.transform.position+" action="+traversal.ActionId);
            else{yield return Hold(4.2f,new[]{Key.S});yield return new WaitForSecondsRealtime(1.5f);Check(surface.name+" descend",!traversal.Active,"position="+hero.transform.position);}
            if(traversal.Active){yield return Hold(.1f,new[]{Key.Space});yield return new WaitForSecondsRealtime(.8f);}
        }
        Place(origin,yaw);report.completed=true;Save();Restore();
    }
    void Restore(){if(finalized)return;finalized=true;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);priorKeyboard?.MakeCurrent();priorMouse?.MakeCurrent();}
    void OnDestroy(){Save();Restore();}
}
#endif
