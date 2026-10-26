#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Explicit Editor-only controlled traversal on the existing stair mesh.
// No hardware-input, natural-play, or performance claim.
[DefaultExecutionOrder(-10000)]
public class StairTraversalProbeQA : MonoBehaviour
{
    public string output;
    ThirdPersonCamera look;
    CharacterController cc;
    Transform actor;
    List<Vector3> path = new List<Vector3>();
    readonly List<string> samples = new List<string>();
    int waypoint = 1, frames;
    float vy = -2;
    int oldFps, oldVsync, oldCapture;
    bool done;
    void Awake()
    {
        var flow = FindFirstObjectByType<BaselineFlow>();
        flow.saveKeyPrefix = "QA.StairsSam20261007.Unstarted";
        flow.enabled = false; // BEFORE original owner's Start; no save owner starts.
        var movement = FindFirstObjectByType<PlayerMovement>();
        movement.enabled = false;
        actor = movement.transform; cc = actor.GetComponent<CharacterController>();
        var vitals = actor.GetComponent<PlayerVitals>(); if(vitals) vitals.enabled = false;
        look = Camera.main.GetComponent<ThirdPersonCamera>();
        look.SetLookInputLocked(true);
        var stair = FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).First(x=>x.name.StartsWith("E6_ExternalSteps"));
        var v = stair.sharedMesh.vertices;
        // Original authored stair boxes have eight vertices each, upper four last.
        for(int i=0; i<32; i++) {
            Vector3 center=Vector3.zero;
            for(int j=4;j<8;j++) center += stair.transform.TransformPoint(v[i*8+j]);
            path.Add(center*.25f);
        }
        cc.enabled=false;
        actor.position=path[0]+Vector3.up*(.06f-cc.center.y+cc.height*.5f);
        cc.enabled=true; Physics.SyncTransforms();
        float heading=Quaternion.LookRotation(path[1]-path[0]).eulerAngles.y;
        look.ResetOrbit(heading,24);
        oldFps=Application.targetFrameRate; oldVsync=QualitySettings.vSyncCount; oldCapture=Time.captureFramerate;
        Application.targetFrameRate=60; QualitySettings.vSyncCount=0; Time.captureFramerate=60;
        samples.Add("frame,waypoint,actorX,actorY,actorZ,cameraX,cameraY,cameraZ,pitch,cameraDistance,grounded");
        Directory.CreateDirectory(output);
        
    }
    void Update()
    {
        if(done) return;
        if(frames>0) Observe();
        if(done) return;
        look.SetLookInputLocked(true);
        if(waypoint < path.Count) {
            var d=path[waypoint]-actor.position; d.y=0;
            if(d.magnitude<.12f) { waypoint++; if(waypoint<path.Count) { d=path[waypoint]-actor.position; d.y=0; } }
            if(waypoint<path.Count) {
                vy=cc.isGrounded ? -2 : vy-20*Time.deltaTime;
                var delta=d.normalized*Mathf.Min(3*Time.deltaTime,d.magnitude); delta.y=vy*Time.deltaTime;
                cc.Move(delta);
                if(d.sqrMagnitude>0) actor.rotation=Quaternion.LookRotation(d);
            }
        } else cc.Move(Vector3.down*2*Time.deltaTime);
        frames++;
    }
    void Observe()
    {
            Vector3 p=actor.position, c=look.transform.position;
            float pitch=Mathf.DeltaAngle(0,look.transform.eulerAngles.x);
            samples.Add(string.Join(",",new [] {frames.ToString(),waypoint.ToString(),F(p.x),F(p.y),F(p.z),F(c.x),F(c.y),F(c.z),F(pitch),F(Vector3.Distance(c,p+Vector3.up*1.6f)),cc.isGrounded?"1":"0"}));
            if(frames==1||frames==120||frames==240) Capture(Path.Combine(output,"stairs-"+frames+".png"));
            if((waypoint>=path.Count && frames>300)||frames>=600) {
                done=true;
                Capture(Path.Combine(output,"stairs-final.png"));
                File.WriteAllLines(Path.Combine(output,"traversal.csv"),samples);
                File.WriteAllText(Path.Combine(output,"result.json"),
                    "{\"scope\":\"EDITOR_CONTROLLED_CHARACTERCONTROLLER_ON_ORIGINAL_STAIR_MESH_NOT_NATIVE_INPUT\",\"completed\":"+((waypoint>=path.Count)?"true":"false")+",\"frames\":"+frames+",\"waypoint\":"+waypoint+",\"rise\":"+F(actor.position.y-path[0].y)+"}");
                Restore();
                EditorApplication.delayCall += ()=>EditorApplication.isPlaying=false;
            }
    }
    static string F(float x)=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    void Restore() { Application.targetFrameRate=oldFps; QualitySettings.vSyncCount=oldVsync; Time.captureFramerate=oldCapture; }
    void OnDestroy() { Restore(); }
    static void Capture(string file) {
        var camera=Camera.main; var previous=camera.targetTexture; var active=RenderTexture.active;
        var rt=new RenderTexture(1280,720,24); var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply(); File.WriteAllBytes(file,texture.EncodeToPNG()); }
        finally { camera.targetTexture=previous; RenderTexture.active=active; Destroy(rt); Destroy(texture); }
    }
    public static void Prepare(string label) {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before preparing QA");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Demo/Scenes/OverworldBaseline.unity" || scene.isDirty) throw new InvalidOperationException("Clean authored main required");
        const string temp="Assets/Editor/StairsSamProbeQA.unity";
        // This exact temporary scene was created by this verifier; reuse it between runs.
        EditorSceneManager.SaveScene(scene,temp,true);
        var qa=EditorSceneManager.OpenScene(temp,OpenSceneMode.Single);
        var go=new GameObject("Explicit stairs QA only");
        var harness=go.AddComponent<StairTraversalProbeQA>();
        harness.output=Path.GetFullPath("ToolReports/StairsSam20261007/"+label);
        var f=FindFirstObjectByType<BaselineFlow>();
        f.saveKeyPrefix="QA.StairsSam20261007.Unstarted"; f.enabled=false;
        EditorSceneManager.SaveScene(qa);
        EditorApplication.delayCall += ()=> { var game=EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")); game.Focus(); EditorApplication.isPlaying=true; };
    }
}

#endif
