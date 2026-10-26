using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KingOfGraphic.Hero;
[InitializeOnLoad]
public static class ResponsiveStairRecording
{
    const string Flag="ResponsiveStair.Recording";
    static string output="LocalDocs/StairReview";
    static BaselineFlow flow;static HeroWorldAdapter hero;static PlayerMovement movement;static ThirdPersonCamera follow;
    static Keyboard keyboard,original;static GameObject stage;static int section,waypoint,frame,lastFrame=-1,phase;
    static int oldFps,oldVsync,oldCapture;static float age;static double deadline;static Vector3[] path,mapPath;static StringBuilder report;static StreamWriter manifest;
    static RenderTexture texture;static readonly HashSet<string> actions=new HashSet<string>();static int climbingFrames,airFrames,adaptedFrames;
    static ResponsiveStairRecording(){EditorApplication.update+=Watch;}
    static void Keys(bool move){InputSystem.QueueStateEvent(keyboard,move?new KeyboardState(Key.W,Key.LeftAlt):new KeyboardState());}
    static void Watch(){
        if(File.Exists("Temp/stair-recording.txt")&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating){
            string request=File.ReadAllText("Temp/stair-recording.txt").Trim();
            output=request=="camera-before"?"LocalDocs/CameraReview/Before":request=="camera-after"?"LocalDocs/CameraReview/After":"LocalDocs/StairReview";
            SessionState.SetString("ResponsiveStair.Output",output);
            File.Delete("Temp/stair-recording.txt");SessionState.SetBool(Flag,true);SessionState.SetBool("HeroQA.Enabled",true);phase=section=frame=0;lastFrame=-1;flow=null;hero=null;report=new StringBuilder();deadline=EditorApplication.timeSinceStartup+240;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Demo/Scenes/OverworldBaseline.unity");EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isPaused)return;
        if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
        try{
            if(deadline==0){deadline=EditorApplication.timeSinceStartup+240;report=new StringBuilder();}
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Recording timeout");
            if(!flow)flow=UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();if(!flow)return;
            if(!flow.saveKeyPrefix.StartsWith("QA.NativePlay."))throw new Exception("Private save scope missing");
            if(!flow.Started){if(flow.TitleScreenVisible&&!flow.TitleTransitionInProgress)flow.RequestTitleEntry(false);return;}
            if(!hero)hero=UnityEngine.Object.FindFirstObjectByType<HeroWorldAdapter>();if(!hero||flow.BlockWorldInput)return;
            if(phase==0){
                output=SessionState.GetString("ResponsiveStair.Output","LocalDocs/StairReview");
                movement=hero.GetComponent<PlayerMovement>();follow=Camera.main.GetComponent<ThirdPersonCamera>();original=Keyboard.current;keyboard=InputSystem.AddDevice<Keyboard>("Temporary stairs verification keyboard");keyboard.MakeCurrent();
                oldFps=Application.targetFrameRate;oldVsync=QualitySettings.vSyncCount;oldCapture=Time.captureFramerate;QualitySettings.vSyncCount=0;Application.targetFrameRate=30;Time.captureFramerate=30;
                texture=new RenderTexture(960,540,24);Directory.CreateDirectory(output+"/Frames");manifest=new StreamWriter(output+"/frames.csv");manifest.WriteLine("frame,section,action,x,y,z,grounded,stepSupport,cameraX,cameraY,cameraZ,pitch,yaw,cameraDistance");
                var map=UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).FirstOrDefault(c=>c.name=="Own R4 six short physical stairs75");
                if(!map||!map.sharedMesh||map.sharedMesh.vertexCount!=228)throw new Exception("Original six-step map mesh missing");
                var v=map.sharedMesh.vertices;var points=new List<Vector3>();
                for(int k=0;k<6;k++){Bounds b=new Bounds(map.transform.TransformPoint(v[k*38]),Vector3.zero);for(int j=1;j<38;j++)b.Encapsulate(map.transform.TransformPoint(v[k*38+j]));points.Add(new Vector3(b.center.x,b.max.y+.06f,b.center.z));}
                Physics.SyncTransforms();
                mapPath=points.OrderBy(p=>p.y).ToArray();
                for(int k=0;k<mapPath.Length;k++){
                    var hits=Physics.RaycastAll(mapPath[k]+Vector3.up,Vector3.down,3,~0,QueryTriggerInteraction.Ignore);
                    var support=hits.Where(h=>h.collider.name=="R10-collision"&&h.normal.y>.707f).OrderBy(h=>h.distance).ToArray();
                    if(support.Length==0)throw new Exception("Missing current R10 route support at "+mapPath[k]);
                    mapPath[k]=new Vector3(mapPath[k].x,support[0].point.y+.06f,mapPath[k].z);
                }
                report.AppendLine("Map source: "+map.name+"; top centers: "+string.Join(" / ",mapPath.Select(p=>p.ToString("F3"))));
                BeginSection();return;
            }
            Cursor.lockState=CursorLockMode.Locked;follow.SetLookInputLocked(true);age+=Time.deltaTime;
            if(phase==1){Keys(false);if(age>.6f){phase=2;age=0;Keys(true);}return;}
            if(phase==2){
                var delta=path[waypoint]-hero.transform.position;delta.y=0;
                if(delta.magnitude<.2f&&Mathf.Abs(path[waypoint].y-hero.transform.position.y)<.18f){waypoint++;if(waypoint>=path.Length){Keys(false);phase=3;age=0;}}
                if(age>20)throw new Exception("Stair route blocked in section "+section+" at "+hero.transform.position+" target "+path[Mathf.Min(waypoint,path.Length-1)]);
                climbingFrames+=movement.ClimbingActive||movement.MantlingActive?1:0;airFrames+=movement.IsStableGrounded?0:1;adaptedFrames+=movement.StairSupportActive?1:0;
                if(!string.IsNullOrEmpty(hero.CurrentAction))actions.Add(hero.CurrentAction);
            }
            Capture();
            if(phase==3&&age>.5f){
                if(climbingFrames>0)throw new Exception("Stair risers incorrectly entered wall climbing in section "+section);
                if(adaptedFrames==0)throw new Exception("Stair adaptation never detected geometry in section "+section);
                report.AppendLine("PASS section "+section+": arrived; wallClimbFrames="+climbingFrames+" airFrames="+airFrames+" adaptedFrames="+adaptedFrames+" actions="+string.Join(",",actions));
                section++;if(section>=6){Finish(true);return;}BeginSection();
            }
        }catch(Exception e){if(report==null)report=new StringBuilder();report.AppendLine(e.ToString());Finish(false);}
    }
    static void Face(Vector3 direction){direction.y=0;if(direction.sqrMagnitude<.0001f)return;float yaw=Quaternion.LookRotation(direction).eulerAngles.y;follow.ResetOrbit(yaw,22);}
    static void BeginSection(){
        Keys(false);actions.Clear();climbingFrames=airFrames=adaptedFrames=0;waypoint=1;age=0;phase=1;
        if(stage){UnityEngine.Object.Destroy(stage);stage=null;}
        if(section<4){
            Vector3 origin=new Vector3(10000+section*20,0,10000);float rise=section<2?.08f:.30f;int count=section<2?12:8;float tread=.6f;
            stage=new GameObject("Temporary stair recording stage");
            void Box(Vector3 center,Vector3 size){var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.SetParent(stage.transform);o.transform.position=origin+center;o.transform.localScale=size;}
            Box(new Vector3(0,-.15f,0),new Vector3(5,.3f,25));
            for(int i=0;i<count;i++){float h=(i+1)*rise;Box(new Vector3(0,h*.5f,1+(i+.5f)*tread),new Vector3(3,h,tread));}
            float z=1+count*tread;Box(new Vector3(0,count*rise*.5f,z+1),new Vector3(3,count*rise,2));
            Vector3 bottom=origin+new Vector3(0,.06f,0),top=origin+new Vector3(0,count*rise+.06f,z+.7f);
            path=section%2==0?new[]{bottom,top}:new[]{top,bottom};
        }else path=section==4?new[]{mapPath[0],mapPath[mapPath.Length-1]}:new[]{mapPath[mapPath.Length-1],mapPath[0]};
        Physics.SyncTransforms();Vector3 direction=path[1]-path[0];direction.y=0;movement.Relocate(path[0],Quaternion.LookRotation(direction));hero.ResetLocomotionAfterRelocation();Face(direction);Cursor.lockState=CursorLockMode.Locked;
    }
    static void Capture(){
        var cam=Camera.main;var old=cam.targetTexture;var active=RenderTexture.active;var shot=new Texture2D(960,540,TextureFormat.RGB24,false);
        try{cam.targetTexture=texture;cam.Render();RenderTexture.active=texture;shot.ReadPixels(new Rect(0,0,960,540),0,0);shot.Apply();File.WriteAllBytes(output+"/Frames/"+frame.ToString("D5")+".png",shot.EncodeToPNG());var p=hero.transform.position;var c=cam.transform.position;var r=cam.transform.eulerAngles;manifest.WriteLine(frame+","+section+","+hero.CurrentAction+","+p.x+","+p.y+","+p.z+","+movement.IsStableGrounded+","+movement.StairSupportActive+","+c.x+","+c.y+","+c.z+","+Mathf.DeltaAngle(0,r.x)+","+r.y+","+Vector3.Distance(c,p+Vector3.up*1.6f));frame++;}
        finally{cam.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(shot);}
    }

    static void Finish(bool success){
        SessionState.SetBool(Flag,false);if(manifest!=null){manifest.Dispose();manifest=null;}
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}if(original!=null)original.MakeCurrent();
        if(follow)follow.SetLookInputLocked(false);if(stage)UnityEngine.Object.Destroy(stage);if(texture)UnityEngine.Object.Destroy(texture);
        Application.targetFrameRate=oldFps;QualitySettings.vSyncCount=oldVsync;Time.captureFramerate=oldCapture;
        report.Insert(0,success?"PASS complete stairs keyboard/physics/presentation recording\n":"FAIL stairs recording\n");Directory.CreateDirectory(output);File.WriteAllText(output+"/validation.txt",report.ToString());Debug.Log(report.ToString());
        File.WriteAllText("Temp/open-initial-title.txt","restore title");
    }
}
