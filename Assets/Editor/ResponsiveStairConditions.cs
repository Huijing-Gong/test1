using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveStairConditions
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ResponsiveStairConditions(){EditorApplication.update+=()=>{if(!File.Exists("Temp/stair-conditions.txt")||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Temp/stair-conditions.txt");Run();};}
    static void Run(){var report=new StringBuilder();GameObject actor=null,floor=null,step=null,ceiling=null;
        void Check(bool ok,string text){report.AppendLine((ok?"PASS ":"FAIL ")+text);if(!ok)throw new Exception(text);}
        try{
            actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WorldHero/RedHeroAuthoredVisual.prefab"));actor.transform.position=new Vector3(10000,.05f,10000);var cc=actor.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.3f;cc.center=new Vector3(0,.9f,0);cc.stepOffset=.3f;
            var movement=actor.AddComponent<PlayerMovement>();movement.enabled=false;var hero=actor.AddComponent<HeroWorldAdapter>();hero.enabled=false;hero.pose=actor.GetComponentInChildren<HeroPoseDriver>();
            void Set(string n,object v)=>typeof(PlayerMovement).GetField(n,Flags).SetValue(movement,v);
            Set("characterController",cc);Set("hero",hero);Set("stableGrounded",true);
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(10000,-.1f,10000);floor.transform.localScale=new Vector3(5,.2f,5);
            bool Probe(){Set("stairSupportTimer",0f);Physics.SyncTransforms();typeof(PlayerMovement).GetMethod("UpdateWalkableStepProbe",Flags).Invoke(movement,new object[]{Vector3.forward});return (bool)typeof(PlayerMovement).GetField("walkableStepAhead",Flags).GetValue(movement);}
            Check(!Probe()&&!movement.StairSupportActive,"Flat ground with capsule skin clearance is not classified as stairs");
            step=GameObject.CreatePrimitive(PrimitiveType.Cube);void Height(float h){step.transform.position=new Vector3(10000,h*.5f,10000.8f);step.transform.localScale=new Vector3(3,h,1);}
            Height(.08f);Check(Probe(),"8cm low riser remains a walking step");Height(.3f);Check(Probe(),"30cm high riser remains a walking step");Height(.40f);Check(Probe(),"40cm supported riser is within the normal step limit");Height(.7f);Check(!Probe(),"70cm obstacle is not treated as a walkable stair");
            Height(.3f);ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.transform.position=new Vector3(10000,1.6f,10000.7f);ceiling.transform.localScale=new Vector3(3,.3f,1);Check(!Probe(),"Low ceiling rejects auto-step classification");
        }catch(Exception e){report.AppendLine(e.ToString());Debug.LogException(e);}finally{if(ceiling)UnityEngine.Object.DestroyImmediate(ceiling);if(step)UnityEngine.Object.DestroyImmediate(step);if(floor)UnityEngine.Object.DestroyImmediate(floor);if(actor)UnityEngine.Object.DestroyImmediate(actor);Directory.CreateDirectory("LocalDocs/StairReview");File.WriteAllText("LocalDocs/StairReview/conditions.txt",report.ToString());}
    }
}
