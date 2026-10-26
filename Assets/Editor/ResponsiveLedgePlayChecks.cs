using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveLedgePlayChecks
{
    const string Flag="ResponsiveLedge.PlayCheck";
    static HeroWorldAdapter hero;static PlayerMovement movement;static BaselineFlow flow;static GameObject stage,cameraObject;static Camera camera;static RenderTexture texture;
    static readonly System.Collections.Generic.HashSet<string> seen=new System.Collections.Generic.HashSet<string>();
    static int phase,frame;static float age,lastCapture;static double deadline;static Vector3 origin;static StringBuilder report;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Call(string name,params object[] args)=>typeof(PlayerMovement).GetMethod(name,Private).Invoke(movement,args);
    static ResponsiveLedgePlayChecks(){EditorApplication.update+=Watch;}
    static void Watch(){
        if(File.Exists("Temp/ledge-playchecks.txt")&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode){
            File.Delete("Temp/ledge-playchecks.txt");SessionState.SetBool(Flag,true);SessionState.SetBool("HeroQA.Enabled",true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Demo/Scenes/OverworldBaseline.unity");seen.Clear();phase=frame=0;age=lastCapture=0;hero=null;flow=null;report=new StringBuilder();deadline=EditorApplication.timeSinceStartup+70;EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isPaused)return;
        if(deadline==0){deadline=EditorApplication.timeSinceStartup+70;report=new StringBuilder();}
        try{
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out at phase "+phase);
            if(!flow)flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();if(!flow)return;
            if(!flow.saveKeyPrefix.StartsWith("QA.NativePlay."))throw new Exception("Private verification save scope missing");
            if(!flow.Started){if(flow.TitleScreenVisible&&!flow.TitleTransitionInProgress)flow.RequestTitleEntry(false);return;}
            if(!hero)hero=UnityEngine.Object.FindFirstObjectByType<HeroWorldAdapter>();if(!hero||flow.BlockWorldInput)return;
            Cursor.lockState=CursorLockMode.Locked;
            if(phase==0){
                movement=hero.GetComponent<PlayerMovement>();movement.enabled=false;var cc=hero.GetComponent<CharacterController>();cc.enabled=false;hero.transform.SetPositionAndRotation(new Vector3(10000,0,10000),Quaternion.identity);cc.enabled=true;hero.ResetLocomotionAfterRelocation();origin=hero.transform.position;
                stage=new GameObject("Temporary ledge verification stage");
                GameObject Box(string name,Vector3 position,Vector3 size){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(stage.transform);o.transform.position=origin+position;o.transform.localScale=size;return o;}
                Box("Floor",new Vector3(0,-.15f,0),new Vector3(8,.3f,8));var wall=Box("Wall with standing platform",new Vector3(0,1.25f,1.5f),new Vector3(3,2.5f,2));
                Physics.SyncTransforms();Call("EnterClimbing",Vector3.back,origin+new Vector3(0,1,.5f),wall.GetComponent<Collider>());
                cameraObject=new GameObject("Verification camera");camera=cameraObject.AddComponent<Camera>();camera.transform.position=origin+new Vector3(4.3f,3.8f,-4.5f);camera.transform.LookAt(origin+new Vector3(0,1.8f,.6f));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.12f,.18f);camera.fieldOfView=44;texture=new RenderTexture(640,640,24);camera.targetTexture=texture;
                Directory.CreateDirectory("LocalDocs/ClimbReview/Runtime");phase=1;age=0;return;
            }
            if(!string.IsNullOrEmpty(hero.CurrentAction))seen.Add(hero.CurrentAction);
            float dt=Time.deltaTime;age+=dt;
            if(phase==1){Call("UpdateClimbing",Vector2.up);if(movement.LedgeHangingActive){report.AppendLine("PASS ordinary climb reaches a valid ledge and enters grab");phase=2;age=0;}}
            else if(phase==2){Call("UpdateClimbing",Vector2.up);if(age>1.7f){if(!movement.LedgeHangingActive||movement.MantlingActive)throw new Exception("Held up skipped hang");report.AppendLine("PASS held W remains hanging after grab completes");Call("UpdateClimbing",Vector2.zero);phase=3;age=0;}}
            else if(phase==3){Call("UpdateClimbing",Vector2.up);if(!movement.MantlingActive)throw new Exception("Fresh W did not start mantle");report.AppendLine("PASS fresh W begins top transfer");phase=4;age=0;}
            else if(phase==4){Call("UpdateMantling");if(!movement.MantlingActive){
                var p=hero.transform.position-origin;if(movement.ClimbingActive||p.y<2.45f||p.z<.8f)throw new Exception("Top transfer failed or ended below platform: "+p);
                report.AppendLine("PASS collision-resolved top transfer finishes on platform at "+p.ToString("F3"));phase=5;age=0;
            }}
            else if(phase==5){if(age>.25f){foreach(var id in new[]{"WALL_LEDGE_GRAB","WALL_LEDGE_HANG","WALL_LEDGE_TOP"})if(!seen.Contains(id))throw new Exception("Animation was not presented: "+id);report.AppendLine("PASS all three imported animations were presented: "+string.Join(", ",seen));Finish(true);return;}}
            if(Time.time-lastCapture>.075f){lastCapture=Time.time;camera.Render();var old=RenderTexture.active;RenderTexture.active=texture;var shot=new Texture2D(640,640,TextureFormat.RGB24,false);shot.ReadPixels(new Rect(0,0,640,640),0,0);shot.Apply();File.WriteAllBytes("LocalDocs/ClimbReview/Runtime/"+(frame++).ToString("D3")+".png",shot.EncodeToPNG());UnityEngine.Object.DestroyImmediate(shot);RenderTexture.active=old;}
        }catch(Exception e){if(report==null)report=new StringBuilder();report.AppendLine(e.ToString());Finish(false);}
    }
    static void Finish(bool success){
        SessionState.SetBool(Flag,false);report.Insert(0,success?"PASS runtime ledge sequence\n":"FAIL runtime ledge sequence\n");Directory.CreateDirectory("LocalDocs/ClimbReview");File.WriteAllText("LocalDocs/ClimbReview/ledge-play-validation.txt",report.ToString());
        if(movement)movement.enabled=true;if(stage)UnityEngine.Object.Destroy(stage);if(cameraObject)UnityEngine.Object.Destroy(cameraObject);if(texture)UnityEngine.Object.Destroy(texture);
        if(success)Debug.Log("LEDGE_RUNTIME_CHECK_PASS "+report);else Debug.LogError("LEDGE_RUNTIME_CHECK_FAIL "+report);
        File.WriteAllText("Temp/open-initial-title.txt","return");
    }
}
