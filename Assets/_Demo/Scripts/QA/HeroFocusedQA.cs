#if UNITY_EDITOR
using System;using System.IO;using System.Collections;using System.Collections.Generic;using System.Linq;
using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;using KingOfGraphic.Hero;
public sealed class HeroFocusedQA:MonoBehaviour
{
 [Serializable]public class Frame{public float time,y,clock,delta;public long sequence;public bool pressed,held,release,hitstop;public string cursor;public string action,overlay,phase;public bool grounded,locked;}
 [Serializable]public class Check{public string name,detail;public bool pass;}
 [Serializable]public class Report{public string evidence="Synthetic InputSystem device with real controller physics; QA setup/API damage explicitly noted";public List<Frame> frames=new List<Frame>();public List<Check> checks=new List<Check>();public bool completed;}
 public bool combatOnly;Report report=new Report();Keyboard keyboard,priorK;Mouse mouse,priorM;HeroWorldAdapter hero;PlayerMovement movement;PlayerVitals vitals;HeroTraversal traversal;string phase="setup";float started;bool restored;GameObject target;
 const string Root="ToolReports/HeroIntegration20261016/FocusedQA";
 void Save(){Directory.CreateDirectory(Root);File.WriteAllText(Root+"/report.json",JsonUtility.ToJson(report,true));}
 void CheckIt(string name,bool pass,string detail){report.checks.Add(new Check{name=name,pass=pass,detail=detail});Save();}
 void LateUpdate(){if(!hero)return;report.frames.Add(new Frame{clock=hero.combat.Time,sequence=hero.combat.Sequence,delta=Time.deltaTime,pressed=Mouse.current.leftButton.wasPressedThisFrame,held=Mouse.current.leftButton.isPressed,release=Mouse.current.leftButton.wasReleasedThisFrame,hitstop=hero.GetComponent<PlayerCombat>().IsApplyingHitStop,cursor=Cursor.lockState.ToString(),time=Time.realtimeSinceStartup-started,y=hero.transform.position.y,action=hero.CurrentAction,overlay=hero.pose.CurrentOverlay,phase=phase,grounded=movement.IsStableGrounded,locked=movement.IsGameplayInputLocked});}
 void Set(bool left=false,params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(960,540)}.WithButton(MouseButton.Left,left));}
 IEnumerator Hold(float seconds,bool left=false,params Key[] keys){Set(left,keys);yield return new WaitForSecondsRealtime(seconds);Set();yield return null;}
 void Place(Vector3 p,Quaternion q){hero.CancelForTransition();movement.Relocate(p,q);vitals.Refill();FindFirstObjectByType<ThirdPersonCamera>()?.ResetOrbit(q.eulerAngles.y,18);Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
 bool Saw(string id,string inPhase)=>report.frames.Any(x=>x.phase==inPhase&&(x.action==id||x.overlay==id));
 IEnumerator Start(){
 var flow=FindFirstObjectByType<BaselineFlow>();if(!flow||flow.saveKeyPrefix!="QA.NativePlay.Hero20261016")yield break;if(!flow.Started)flow.ContinueGame();yield return new WaitForSecondsRealtime(.4f);
 hero=FindFirstObjectByType<HeroWorldAdapter>();movement=hero.GetComponent<PlayerMovement>();vitals=hero.GetComponent<PlayerVitals>();traversal=hero.GetComponent<HeroTraversal>();started=Time.realtimeSinceStartup;
 priorK=Keyboard.current;priorM=Mouse.current;keyboard=InputSystem.AddDevice<Keyboard>("HeroFocusedSynthetic");mouse=InputSystem.AddDevice<Mouse>("HeroFocusedSyntheticMouse");keyboard.MakeCurrent();mouse.MakeCurrent();
 Place(new Vector3(2,11.5f,-60),Quaternion.identity);yield return new WaitForSecondsRealtime(.5f);
 phase="jump";yield return Hold(.08f,false,Key.Space);yield return new WaitForSecondsRealtime(1.8f);var jump=report.frames.Where(x=>x.phase==phase).ToArray();CheckIt("jump_rise_fall",Saw("AIR_RISE",phase)&&Saw("AIR_FALL",phase),"minY="+jump.Min(x=>x.y)+" maxY="+jump.Max(x=>x.y)+" IDs="+string.Join(",",jump.Select(x=>x.action).Distinct()));
 phase="combo";target=GameObject.CreatePrimitive(PrimitiveType.Capsule);target.name="QA stationary enemy for actual blade contact";target.transform.position=hero.transform.position+Vector3.forward*.85f+Vector3.up*.9f;target.transform.localScale=new Vector3(.65f,.9f,.65f);var health=target.AddComponent<Health>();health.SetMaximumHealth(1000);int hits=0;health.Damaged+=(h,d)=>hits++;
 yield return Hold(.04f,true);yield return new WaitForSecondsRealtime(.25f);yield return Hold(.04f,true);yield return new WaitForSecondsRealtime(.46f);yield return Hold(.04f,true);yield return new WaitForSecondsRealtime(1.2f);
 CheckIt("three_combo",Saw("ATK_A1",phase)&&Saw("ATK_A2",phase)&&Saw("ATK_A3",phase),"actual health="+health.CurrentHealth+" accepted contacts="+hits);CheckIt("blade_damage_once_each",hits>0&&hits<=3,"hits="+hits+" damage="+(1000-health.CurrentHealth));target.SetActive(false);
 phase="charge_cancel";yield return Hold(1f,true);yield return Hold(.08f,false,Key.LeftCtrl);yield return new WaitForSecondsRealtime(.6f);CheckIt("charge_cancel",Saw("ATK_CHARGE_CANCEL",phase)&&Saw("DODGE_B",phase),"upper-body cancel with immediate dodge");
 phase="pause";yield return Hold(.04f,true);Set();float time=hero.combat.Time;Time.timeScale=0;yield return new WaitForSecondsRealtime(.4f);CheckIt("pause_freeze",Mathf.Abs(hero.combat.Time-time)<.001f,"clock="+hero.combat.Time);Time.timeScale=1;yield return new WaitForSecondsRealtime(1);
 if(combatOnly){report.completed=true;Save();Restore();yield break;}
 foreach(var kind in new[]{HeroTraversalKind.Ledge,HeroTraversalKind.Rope,HeroTraversalKind.Ladder}){
 var s=HeroTraversalSurface.Available.First(x=>x.kind==kind);phase=kind.ToString();Place(s.Bottom-s.transform.forward*.35f,s.transform.rotation);yield return new WaitForSecondsRealtime(.7f);yield return Hold(.08f,false,Key.E);yield return new WaitForSecondsRealtime(.8f);CheckIt(phase+"_enter",traversal.Active,"pos="+hero.transform.position+" water="+movement.SwimmingActive+" locks="+movement.IsGameplayInputLocked);
 if(!traversal.Active)continue;
 ScreenCapture.CaptureScreenshot(Root+"/"+phase+"-contact.png");yield return Hold(kind==HeroTraversalKind.Ladder?6f:4.5f,false,Key.W);yield return new WaitForSecondsRealtime(4.3f);CheckIt(phase+"_exit",!traversal.Active,"pos="+hero.transform.position+" action="+traversal.ActionId+" handError="+traversal.MaximumHandContactError);if(traversal.Active)traversal.Cancel();}
 phase="finish";Place(new Vector3(2,11.5f,-60),Quaternion.identity);report.completed=true;Save();Restore();}
 void Restore(){if(restored)return;restored=true;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);priorK?.MakeCurrent();priorM?.MakeCurrent();if(target)Destroy(target);}
 void OnDestroy(){Save();Restore();}
}
#endif
