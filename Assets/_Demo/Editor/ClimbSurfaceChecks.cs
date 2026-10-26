using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Temporary geometry exercises real probes, locomotion and mantle; never saved into the map.
[InitializeOnLoad]
public static class ClimbSurfaceChecks
{
    const string Prefix = "QA.ClimbSurface.20260923";
    static IEnumerator routine;
    static double next;
    static PlayerMovement player;
    static BaselineFlow flow;
    static Keyboard keyboard, previous;
    static InputSettings.BackgroundBehavior background;
    static ThirdPersonCamera cameraControl;
    static bool cameraEnabled;
    static Quaternion cameraRotation;
    static GameObject root, wall, blocker;
    static bool sawClimb, sawMantle, wasMantling, finishedMantle, stopAfterMantle;
    static readonly List<string> log = new List<string>();
    public static bool Running => routine != null;
    static ClimbSurfaceChecks() { AssemblyReloadEvents.beforeAssemblyReload += Stop; }
    public static void Begin()
    {
        if (!EditorApplication.isPlaying || Running) throw new InvalidOperationException("Enter Play first.");
        flow = UnityEngine.Object.FindFirstObjectByType<BaselineFlow>();
        if(flow.Started) throw new InvalidOperationException("Use the title screen to protect real progress.");
        player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        flow.saveKeyPrefix = Prefix; flow.NewGame(); if(flow.Tutorial) flow.Tutorial.Cancel();
        log.Clear(); Directory.CreateDirectory("ToolReports/ClimbPolicy");
        previous = Keyboard.current; background = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("ClimbSurfaceQA"); keyboard.MakeCurrent();
        cameraControl = Camera.main.GetComponent<ThirdPersonCamera>(); cameraEnabled = cameraControl.enabled;
        cameraRotation = Camera.main.transform.rotation; cameraControl.enabled = false; Camera.main.transform.rotation = Quaternion.identity;
        root = new GameObject("QA climbing fixtures");
        wall = Shape("QA ordinary wall", new Vector3(0,4,-56.7f),new Vector3(6,6,.5f));
        routine=Run(); next=0; EditorApplication.update+=Tick;
    }
    static GameObject Shape(string name, Vector3 position, Vector3 scale)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(root.transform);
        go.transform.position=position; go.transform.localScale=scale; return go;
    }
    static void Place()
    {
        Keys(); player.Relocate(new Vector3(0,1.05f,-58),Quaternion.identity);
        player.GetComponent<PlayerVitals>().Refill(); Physics.SyncTransforms(); sawClimb=sawMantle=wasMantling=finishedMantle=false;
    }
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); }
    static void Check(bool ok,string name) { log.Add((ok?"PASS ":"FAIL ")+name); File.WriteAllLines("ToolReports/ClimbPolicy/runtime-checks.txt",log); }
    static IEnumerator Run()
    {
        yield return .6f; Place(); Keys(Key.W); yield return 1f;
        Check(sawClimb && player.ClimbingActive,"unmarked ordinary wall remains climbable");
        var deny=root.AddComponent<NonClimbableSurface>(); Place(); Keys(Key.W); yield return 1f;
        Check(!sawClimb&&!sawMantle,"parent exclusion blocks attachment and mantle");
        UnityEngine.Object.Destroy(deny); yield return .1f;
        blocker=Shape("QA forbidden foreground",new Vector3(0,4,-57.27f),new Vector3(6,6,.12f)); blocker.AddComponent<NonClimbableSurface>();
        Place(); Keys(Key.W); yield return 1f;
        Check(!sawClimb&&!sawMantle,"forbidden foreground occludes climbable wall behind it");
        UnityEngine.Object.Destroy(blocker); yield return .1f;
        wall.transform.position=new Vector3(0,2,-56.1f);wall.transform.localScale=new Vector3(6,2,2);
        Place(); stopAfterMantle=true; Keys(Key.W); yield return 6f; stopAfterMantle=false;
        Check(sawClimb&&sawMantle&&finishedMantle&&player.transform.position.y>2.8f&&!player.MantlingActive,"ordinary solid ledge still supports completed mantle; position="+player.transform.position);
        blocker=Shape("QA forbidden landing cap",new Vector3(0,3.07f,-56.1f),new Vector3(6,.12f,2));blocker.AddComponent<NonClimbableSurface>();
        Place(); Keys(Key.W); yield return 4f;
        Check(sawClimb&&!sawMantle&&player.transform.position.y<2.8f,"forbidden top cannot be bypassed by selecting supporting platform underneath");
        Keys(); root.SetActive(false);
        Collider tree=null;
        foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            if(c.name=="TreeTrunk_REPLACE"&&Vector3.Distance(c.transform.position,new Vector3(-60,2.45f,-27))<.5f)tree=c;
        if(tree)
        {
            Place(); player.Relocate(new Vector3(tree.bounds.center.x,tree.bounds.min.y+.05f,tree.bounds.min.z-.8f),Quaternion.identity);
            Physics.SyncTransforms(); Keys(Key.W); yield return .8f;
            Check(sawClimb,"actual western tree trunk remains climbable");
        }
        else Check(false,"test tree not found");
        Place(); var at=new Vector3(0,1,-83.1f);
        foreach(var h in Physics.RaycastAll(new Vector3(at.x,30,at.z),Vector3.down,60,~0,QueryTriggerInteraction.Ignore))
            if(h.collider.name=="LakeTerrain_REPLACE_OR_SCULPT")at.y=h.point.y+.05f;
        player.Relocate(at,Quaternion.Euler(0,180,0));Camera.main.transform.rotation=Quaternion.Euler(0,180,0);
        Physics.SyncTransforms(); Keys(Key.W); yield return 1f;
        Check(!sawClimb&&!sawMantle,"actual invisible southern boundary cannot be climbed"); Keys();
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||!player){Stop();return;}
        sawClimb|=player.ClimbingActive;sawMantle|=player.MantlingActive;
        if(wasMantling&&!player.MantlingActive&&player.transform.position.y>2.8f){finishedMantle=true;if(stopAfterMantle)Keys();}
        wasMantling=player.MantlingActive;
        if(EditorApplication.timeSinceStartup<next)return;
        try { if(!routine.MoveNext()){Stop();return;}next=EditorApplication.timeSinceStartup+(routine.Current is float delay?delay:.02); }
        catch(Exception exception){Check(false,exception.ToString());Stop();}
    }
    public static void Stop()
    {
        EditorApplication.update-=Tick;routine=null;
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;InputSystem.settings.backgroundBehavior=background;}
        if(previous!=null&&previous.added)previous.MakeCurrent();
        if(cameraControl){cameraControl.enabled=cameraEnabled;if(Camera.main)Camera.main.transform.rotation=cameraRotation;}
        if(root)UnityEngine.Object.DestroyImmediate(root);
        if(player&&EditorApplication.isPlaying){player.Relocate(BaselineSaveData.DefaultSafePosition,Quaternion.identity);player.GetComponent<PlayerVitals>().Refill();}
    }
    public static void ClearTestSlot()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
        BaselineSaveStore.Clear(Prefix);
    }
}
