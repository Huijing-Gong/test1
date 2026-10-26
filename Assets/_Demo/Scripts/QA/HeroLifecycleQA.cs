#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;
using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;using KingOfGraphic.Hero;
public sealed class HeroLifecycleQA:MonoBehaviour {
 [Serializable]public class Check{public string name,detail;public bool pass;}
 [Serializable]public class Frame{public float time;public string phase,action;public Vector3 position;public bool climb,swim;}
 [Serializable]public class Report{public string evidence="Synthetic InputSystem; physics fixtures and API damage/setup explicitly used; not natural keyboard play.";public List<Check> checks=new List<Check>();public List<string> observed=new List<string>();public List<Frame> frames=new List<Frame>();public bool completed;}
 Report report=new Report();const string Root="ToolReports/HeroIntegration20261016/LifecycleQA";string phase="setup";
 HeroWorldAdapter hero;PlayerMovement move;PlayerVitals vitals;Health hp;HeroTraversal traversal;Keyboard keyboard,priorK;Mouse mouse,priorM;GameObject fixtures,source;bool restored;
 void Save(){Directory.CreateDirectory(Root);File.WriteAllText(Root+"/report.json",JsonUtility.ToJson(report,true));}
 void CheckIt(string name,bool pass,string detail){report.checks.Add(new Check{name=name,pass=pass,detail=detail});Save();}
 void LateUpdate(){if(!hero)return;string id=hero.CurrentAction;if(id!=null&&!report.observed.Contains(id)){report.observed.Add(id);Save();}report.frames.Add(new Frame{time=Time.realtimeSinceStartup,phase=phase,action=id,position=hero.transform.position,climb=move.ClimbingActive,swim=move.SwimmingActive});}
 bool Saw(string id)=>report.frames.Any(x=>x.phase==phase&&x.action==id);
 void Set(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(960,540)});}
 IEnumerator Hold(float seconds,params Key[] keys){Set(keys);yield return new WaitForSecondsRealtime(seconds);Set();yield return null;}
 void Place(Vector3 p,float yaw=0){hero.CancelForTransition();vitals.Refill();move.Relocate(p,Quaternion.Euler(0,yaw,0));FindFirstObjectByType<ThirdPersonCamera>()?.ResetOrbit(yaw,18);Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
 GameObject Box(string name,Vector3 p,Vector3 scale){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(fixtures.transform);g.transform.position=p;g.transform.localScale=scale;return g;}
 void Damage(int amount,Vector3 direction){source.transform.position=hero.transform.position-direction*2;hp.TryApplyDamage(new DamageInfo(amount,source,hero.transform.position+Vector3.up,direction));}
 IEnumerator Start(){
 var flow=FindFirstObjectByType<BaselineFlow>();if(!flow||flow.saveKeyPrefix!="QA.NativePlay.Hero20261016")yield break;if(!flow.Started)flow.ContinueGame();yield return new WaitForSecondsRealtime(.5f);
 hero=FindFirstObjectByType<HeroWorldAdapter>();move=hero.GetComponent<PlayerMovement>();vitals=hero.GetComponent<PlayerVitals>();hp=hero.GetComponent<Health>();traversal=hero.GetComponent<HeroTraversal>();
 priorK=Keyboard.current;priorM=Mouse.current;keyboard=InputSystem.AddDevice<Keyboard>("HeroLifecycleSynthetic");mouse=InputSystem.AddDevice<Mouse>("HeroLifecycleSyntheticMouse");keyboard.MakeCurrent();mouse.MakeCurrent();
 fixtures=new GameObject("QA temporary physical fixtures - not authored map");source=new GameObject("QA damage source");source.transform.SetParent(fixtures.transform);
 Box("QA ground",new Vector3(400,9.5f,400),new Vector3(24,1,24));var wall=Box("QA climb wall",new Vector3(400,12,404),new Vector3(5,4,1));
 Place(new Vector3(400,10.05f,402.7f));yield return new WaitForSecondsRealtime(.7f);phase="wall_up";var p=hero.transform.position;yield return Hold(3,Key.W);CheckIt(phase,move.ClimbingActive&&hero.transform.position.y>p.y+.7f,"start="+p+" end="+hero.transform.position);
 phase="wall_down";p=hero.transform.position;yield return Hold(.65f,Key.S);CheckIt(phase,Saw("CLIMB_DOWN")&&hero.transform.position.y<p.y-.15f,"delta="+(hero.transform.position-p));
 phase="wall_left";p=hero.transform.position;yield return Hold(.65f,Key.A);CheckIt(phase,Saw("CLIMB_SIDE_L")&&hero.transform.position.x<p.x-.15f,"delta="+(hero.transform.position-p));
 phase="wall_right";p=hero.transform.position;yield return Hold(.65f,Key.D);CheckIt(phase,Saw("CLIMB_SIDE_R")&&hero.transform.position.x>p.x+.15f,"delta="+(hero.transform.position-p));
 phase="wall_top";yield return Hold(6,Key.W);yield return new WaitForSecondsRealtime(4.2f);CheckIt(phase,!move.ClimbingActive&&!move.MantlingActive&&hero.transform.position.y>13.8f,"pos="+hero.transform.position+" pose="+hero.CurrentAction);
 Place(new Vector3(400,10.05f,402.7f));yield return Hold(2,Key.W);phase="wall_detach";yield return Hold(.08f,Key.Space);yield return new WaitForSecondsRealtime(.7f);CheckIt(phase,!move.ClimbingActive&&Saw("CLIMB_DETACH"),"jump release");
 wall.SetActive(false);Place(new Vector3(400,10.05f,396));yield return new WaitForSecondsRealtime(.7f);phase="sprint_jump";Set(Key.W,Key.LeftShift);yield return new WaitForSecondsRealtime(1.2f);yield return Hold(.08f,Key.W,Key.LeftShift,Key.Space);yield return new WaitForSecondsRealtime(2);CheckIt(phase,Saw("LOC_SPRINT")&&Saw("AIR_RUN_JUMP"),"authored movement state IDs");
 Place(new Vector3(400,13.6f,400));phase="heavy_landing";yield return new WaitForSecondsRealtime(2.6f);CheckIt(phase,Saw("AIR_LAND_HEAVY"),"3.6m fall onto controlled solid floor, below fall damage threshold");
 foreach(var d in new[]{Vector3.back,Vector3.forward,Vector3.right,Vector3.left}){Place(new Vector3(400,10.05f,400));yield return new WaitForSecondsRealtime(1.1f);phase="directional_hurt";Damage(1,d);yield return new WaitForSecondsRealtime(.9f);}
 CheckIt("four_hurt_directions",new[]{"HIT_LIGHT_F","HIT_LIGHT_B","HIT_LIGHT_L","HIT_LIGHT_R"}.All(id=>report.observed.Contains(id)),string.Join(",",report.observed.Where(x=>x.StartsWith("HIT_LIGHT"))));
 phase="knockdown";Damage(31,Vector3.back);yield return new WaitForSecondsRealtime(4);CheckIt(phase,Saw("HIT_KNOCKDOWN")&&Saw("HIT_GETUP"),"real Health damage entry then recovery");
 Place(new Vector3(400,10.05f,400));yield return new WaitForSecondsRealtime(1.1f);Damage(10,Vector3.back);yield return new WaitForSecondsRealtime(1);phase="field_aid";int charges=vitals.AidCharges,health=hp.CurrentHealth;yield return Hold(.08f,Key.H);yield return new WaitForSecondsRealtime(2);CheckIt(phase,Saw("INTERACT_USE_ITEM")&&vitals.AidCharges==charges-1&&hp.CurrentHealth>health,"charges="+charges+"->"+vitals.AidCharges+" hp="+health+"->"+hp.CurrentHealth);
 Damage(10,Vector3.back);yield return new WaitForSecondsRealtime(1);phase="field_aid_cancel";charges=vitals.AidCharges;yield return Hold(.08f,Key.H);Damage(1,Vector3.back);yield return new WaitForSecondsRealtime(2);CheckIt(phase,vitals.AidCharges==charges,"hurt before event keeps charge="+charges);
 phase="pickup";var shard=StoneScoutVitalPickup.Spawn(hero.transform.position+Vector3.up*.9f,null,5,10);int before=hp.CurrentHealth;yield return new WaitForSecondsRealtime(2);CheckIt(phase,Saw("INTERACT_PICKUP")&&hp.CurrentHealth==Mathf.Min(hp.MaxHealth,before+5)&&!shard,"single existing heal transaction");
 foreach(var kind in new[]{HeroTraversalKind.Ladder,HeroTraversalKind.Rope,HeroTraversalKind.Ledge}){
 var surface=HeroTraversalSurface.Available.First(x=>x.kind==kind);phase=kind+"_top_down";Place(surface.Top+surface.transform.forward*.4f,surface.transform.eulerAngles.y);yield return new WaitForSecondsRealtime(1.1f);yield return Hold(.08f,Key.E);yield return new WaitForSecondsRealtime(1.5f);
 CheckIt(phase+"_enter",traversal.Active,"root="+hero.transform.position+" action="+traversal.ActionId);
 if(!traversal.Active)continue;if(kind==HeroTraversalKind.Ledge){CheckIt("ledge_top_hangs_at_bottom",Vector3.Distance(hero.transform.position,surface.Bottom)<.2f,"root="+hero.transform.position+" bottom="+surface.Bottom);yield return Hold(.08f,Key.Space);yield return new WaitForSecondsRealtime(1);CheckIt("ledge_drop",!traversal.Active,"release");}
 else{yield return Hold(7,Key.S);yield return new WaitForSecondsRealtime(2);CheckIt(phase+"_exit",!traversal.Active,"root="+hero.transform.position+" action="+traversal.ActionId);}}
 phase="lake_swim";Place(new Vector3(-83.54f,2.4f,20.57f),0);yield return new WaitForSecondsRealtime(1.5f);p=hero.transform.position;yield return Hold(1.4f,Key.W);float normal=Vector3.ProjectOnPlane(hero.transform.position-p,Vector3.up).magnitude;p=hero.transform.position;yield return Hold(1.4f,Key.W,Key.LeftShift);float fast=Vector3.ProjectOnPlane(hero.transform.position-p,Vector3.up).magnitude;yield return Hold(.4f,Key.A);yield return Hold(.4f,Key.D);yield return new WaitForSecondsRealtime(1.1f);CheckIt("lake_swim_speed",move.SwimmingActive&&fast>normal*1.15f,"normal="+normal+" fast="+fast+" pos="+hero.transform.position);
 phase="finish";Place(new Vector3(2,11.5f,-60));report.completed=true;Save();Restore();}
 void Restore(){if(restored)return;restored=true;Time.timeScale=1;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);priorK?.MakeCurrent();priorM?.MakeCurrent();if(fixtures)Destroy(fixtures);}
 void OnDestroy(){Save();Restore();}
}
#endif
