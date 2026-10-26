using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveLedgeChecks
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ResponsiveLedgeChecks(){EditorApplication.update+=()=>{if(!File.Exists("Temp/ledge-checks.txt")||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;File.Delete("Temp/ledge-checks.txt");Run();};}
    static void Set(object o,string name,object v)=>o.GetType().GetField(name,Flags).SetValue(o,v);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static void Run(){
        var report=new StringBuilder();GameObject actor=null,wall=null,ceiling=null;var oldCursor=Cursor.lockState;
        void Check(bool ok,string text){report.AppendLine((ok?"PASS ":"FAIL ")+text);if(!ok)throw new Exception(text);}
        try{
            actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));actor.name="Temporary ledge verification";
            actor.transform.position=new Vector3(10000,.5f,10000.05f);actor.transform.rotation=Quaternion.identity;
            var pose=actor.GetComponentInChildren<HeroPoseDriver>();var cc=actor.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.3f;cc.center=new Vector3(0,.9f,0);cc.skinWidth=.03f;
            var movement=actor.AddComponent<PlayerMovement>();movement.enabled=false;var hero=actor.AddComponent<HeroWorldAdapter>();hero.pose=pose;hero.enabled=false;
            Set(movement,"characterController",cc);Set(movement,"animator",pose.animator);Set(movement,"hero",hero);Set(movement,"climbSurfaceNormal",Vector3.back);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary supported wall top";wall.transform.position=new Vector3(10000,1,10001.5f);wall.transform.localScale=new Vector3(3,2,2);var collider=wall.GetComponent<Collider>();Physics.SyncTransforms();
            bool Top(){var args=new object[]{Vector3.zero,null};return (bool)Call(movement,"TryFindClimbTop",args);}
            Check(Top(),"Broad wall with clear standing space admits a top transfer");
            var facility=actor.AddComponent<HeroTraversal>();facility.enabled=false;Set(facility,"capsule",cc);
            var target=wall.AddComponent<HeroTraversalSurface>();target.kind=HeroTraversalKind.Ledge;
            Check((bool)Call(facility,"CanApproachLedge",target),"Facing the ledge permits a front-side interaction");
            actor.transform.rotation=Quaternion.Euler(0,180,0);Check(!(bool)Call(facility,"CanApproachLedge",target),"Facing away rejects predefined ledge entry");actor.transform.rotation=Quaternion.identity;
            Check((bool)Call(facility,"HasLedgeTopSupport",new Vector3(10000,2.05f,10001)),"Predefined ledge exit has a supported landing footprint");
            wall.transform.localScale=new Vector3(.2f,2,2);Physics.SyncTransforms();Check(!Top(),"Narrow column top rejects unsafe landing footprint");Check(!(bool)Call(facility,"HasLedgeTopSupport",new Vector3(10000,2.05f,10001)),"Predefined narrow pole top also rejects an unsafe landing");wall.transform.localScale=new Vector3(3,2,2);Physics.SyncTransforms();
            ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.transform.position=new Vector3(10000,3,10001);ceiling.transform.localScale=new Vector3(3,.3f,2);Physics.SyncTransforms();Check(!Top(),"Blocked headroom rejects mantle");UnityEngine.Object.DestroyImmediate(ceiling);ceiling=null;
            wall.SetActive(false);Physics.SyncTransforms();Check(!Top(),"Missing support rejects mantle");wall.SetActive(true);Physics.SyncTransforms();
            Set(movement,"currentClimbCollider",collider);Set(movement,"climbingActive",true);Call(movement,"EnterLedgeHang",new Vector3(10000,2.05f,10001));
            Set(movement,"ledgeHangElapsed",1f);actor.transform.position=new Vector3(10000,.32f,10000.05f);Physics.SyncTransforms();
            Call(movement,"UpdateLedgeHang",Vector2.up);Check(movement.LedgeHangingActive&&!movement.MantlingActive,"Held W during grab does not immediately skip hanging");
            Call(movement,"UpdateLedgeHang",Vector2.zero);Call(movement,"UpdateLedgeHang",Vector2.up);Check(movement.MantlingActive&&!movement.LedgeHangingActive,"Release W then press W starts revalidated top animation");
            Set(movement,"mantlingActive",false);Set(movement,"climbingActive",true);Call(movement,"EnterLedgeHang",new Vector3(10000,2.05f,10001));Set(movement,"ledgeHangElapsed",1f);Call(movement,"UpdateLedgeHang",Vector2.down);Check(movement.ClimbingActive&&!movement.LedgeHangingActive,"S returns from hang to ordinary climbing");
            Call(movement,"EnterLedgeHang",new Vector3(10000,2.05f,10001));collider.enabled=false;Call(movement,"UpdateLedgeHang",Vector2.zero);Check(!movement.LedgeHangingActive&&!movement.ClimbingActive,"Removing the supporting wall releases the grip");collider.enabled=true;
            Set(movement,"climbingActive",true);Call(movement,"EnterLedgeHang",new Vector3(10000,2.05f,10001));Call(movement,"ExitClimbing",false,true);Check(!movement.LedgeHangingActive&&!movement.ClimbingActive,"Detach clears hanging state and prevents immediate re-grab");
            foreach(var id in new[]{"WALL_LEDGE_GRAB","WALL_LEDGE_HANG","WALL_LEDGE_TOP"}){var d=pose.library.Find(id);Check(d!=null&&d.clip&&d.authoredMotion&&d.authoredClimbRoot&&d.authoredClimbRootClip==d.clip,id+" has paired imported source and calibrated capsule compensation");}
            foreach(var pair in new[]{new[]{"POLE_CLIMB","CLIMB_UP"},new[]{"POLE_HOLD","CLIMB_IDLE"},new[]{"POLE_DOWN","CLIMB_DOWN"}})Check(pose.library.Find(pair[0]).clip==pose.library.Find(pair[1]).clip,pair[0]+" shares ordinary climbing motion");
        }catch(Exception e){report.AppendLine("ERROR "+e);Debug.LogException(e);}
        finally{if(ceiling)UnityEngine.Object.DestroyImmediate(ceiling);if(wall)UnityEngine.Object.DestroyImmediate(wall);if(actor)UnityEngine.Object.DestroyImmediate(actor);Cursor.lockState=oldCursor;Directory.CreateDirectory("LocalDocs/ClimbReview");File.WriteAllText("LocalDocs/ClimbReview/ledge-validation.txt",report.ToString());}
    }
}
