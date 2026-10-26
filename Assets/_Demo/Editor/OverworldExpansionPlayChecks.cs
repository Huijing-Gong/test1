using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

// Controlled Editor acceptance; deliberately not described as human WASD play.
[InitializeOnLoad]
public static class OverworldExpansionPlayChecks
{
    static BaselineWorld world;
    static CharacterController controller;
    static PlayerMovement movement;
    static readonly List<string> Log=new List<string>();
    static Vector3[] path;
    static int leg,index,phase;
    static double previous,started,lastProgress;
    static Vector3 lastPosition;
    static bool sawSwim,sawWade,sawDryAfter;
    static int transitions;
    static bool wasSwimming;
    public static bool Running {get;private set;}
    static OverworldExpansionPlayChecks(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;}
    public static void Begin()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Play first");
        world=UnityEngine.Object.FindFirstObjectByType<BaselineWorld>();controller=world.player.GetComponent<CharacterController>();movement=world.player.GetComponent<PlayerMovement>();
        Log.Clear();leg=0;phase=0;transitions=0;sawSwim=sawWade=sawDryAfter=wasSwimming=false;previous=started=lastProgress=EditorApplication.timeSinceStartup;lastPosition=world.player.position;
        Place(new Vector3(0,1.06f,-67));View("spawn",new Vector3(0,5,-74),new Vector3(0,3,-30));
        StartLeg();Running=true;EditorApplication.update+=Tick;
    }
    static void StartLeg()
    {
        world.RefreshNavigation();path=world.WalkingRoute(world.player.position,world.objectives[leg].position);index=0;
        Log.Add("Walking leg "+leg+" samples="+path.Length+" status="+world.NavigationStatus);
        lastProgress=EditorApplication.timeSinceStartup;lastPosition=world.player.position;
    }
    static void Place(Vector3 p){controller.enabled=false;world.player.position=p;controller.enabled=true;Physics.SyncTransforms();}
    public static void View(string name,Vector3 pos,Vector3 target)
    {
        var cam=Camera.main;var oldPos=cam.transform.position;var oldRot=cam.transform.rotation;
        try{cam.transform.position=pos;cam.transform.LookAt(target);OverworldExpansionChecks.Shot(name);}
        finally{cam.transform.SetPositionAndRotation(oldPos,oldRot);}
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||!world){Stop();return;}
        double now=EditorApplication.timeSinceStartup;float dt=Mathf.Clamp((float)(now-previous),0,.04f);previous=now;
        try
        {
            if(phase==0)
            {
                if(path.Length==0){Log.Add("FAIL no walking route");NextPhase();return;}
                while(index<path.Length&&Vector2.Distance(new Vector2(world.player.position.x,world.player.position.z),new Vector2(path[index].x,path[index].z))<.3f)index++;
                if(index==path.Length)
                {
                    Log.Add("PASS continuous controlled CharacterController leg "+leg+" arrived="+world.player.position+" swimming="+movement.SwimmingActive);
                    string name=leg==0?"west-reveal":leg==1?"island-reveal":"east-reveal";
                    var p=world.player.position;View(name,p+new Vector3(-5,3,-6),world.objectives[leg].position+Vector3.up*1.4f);
                    leg++;if(leg<3){StartLeg();return;}NextPhase();return;
                }
                var delta=path[index]-world.player.position;delta.y=0;controller.Move(Vector3.ClampMagnitude(delta,5*dt));
                if(movement.SwimmingActive){Log.Add("FAIL walking leg entered swimming");NextPhase();return;}
                if(Vector3.Distance(lastPosition,world.player.position)>.8f){lastPosition=world.player.position;lastProgress=now;}
                if(now-lastProgress>5||now-started>160){Log.Add("FAIL walking stalled leg="+leg+" index="+index+" position="+world.player.position);NextPhase();}
            }
            else
            {
                if(now-started<1)return;
                bool swim=movement.SwimmingActive;
                if(swim!=wasSwimming){transitions++;Log.Add("Water transition at "+world.player.position+" swim="+swim);wasSwimming=swim;}
                sawSwim|=swim;sawWade|=movement.IsWading;sawDryAfter|=sawSwim&&!movement.IsInWater;
                controller.Move(Vector3.right*5*dt);
                if(world.player.position.x>=38||now-started>40){Log.Add("Continuous water crossing end="+world.player.position+" swim="+sawSwim+" wade="+sawWade+" dryAfter="+sawDryAfter+" transitions="+transitions);Stop();}
            }
        }
        catch(Exception e){Log.Add("FAIL "+e);Stop();}
    }
    static void NextPhase(){Stop();} // Shoreline loops are a separate controlled check; see acceptance report.
    public static void Stop()
    {
        EditorApplication.update-=Tick;Running=false;
        if(Log.Count>0)File.WriteAllLines("ToolReports/MapExpansion/continuous-checks.txt",Log);
    }
    public static void Regression()
    {
        var w=UnityEngine.Object.FindFirstObjectByType<BaselineWorld>();var items=UnityEngine.Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None);var logs=new List<string>();
        Action<bool,string> check=(ok,s)=>{logs.Add((ok?"PASS ":"FAIL ")+s);};
        Func<BaselineInteraction.ActionKind,int,BaselineInteraction> item=(kind,i)=>items.First(a=>a.action==kind&&a.index==i);
        var ui=UnityEngine.Object.FindFirstObjectByType<DialogueUI>();
        Action<BaselineInteraction> interact=a=>a.GetComponent<NPCDialogue>().NotifyDialogueCompleted(ui);
        check(w.Tracked==-1,"No automatic tracking");
        interact(item(BaselineInteraction.ActionKind.Chest,0));interact(item(BaselineInteraction.ActionKind.Chest,1));check(w.opened[0]&&w.opened[1]&&!w.accepted.Any(x=>x),"Both hidden chests independent of tasks");
        interact(item(BaselineInteraction.ActionKind.Chest,2));check(!w.opened[2],"Puzzle chest locked");
        foreach(int i in new[]{2,1})interact(item(BaselineInteraction.ActionKind.Rune,i));check(w.RuneProgress==0,"Wrong sequence resets");
        foreach(int i in new[]{2,0,1})interact(item(BaselineInteraction.ActionKind.Rune,i));interact(item(BaselineInteraction.ActionKind.Chest,2));check(w.opened[2]&&w.PuzzleSolved,"SUN WAVE LEAF unlocks chest");
        for(int i=0;i<3;i++){interact(item(BaselineInteraction.ActionKind.AcceptTask,i));check(w.Tracked==-1,"Task "+i+" acceptance does not track");w.Track(i);check(w.Tracked==i,"Manual track "+i);w.Track(-1);check(w.accepted[i],"Untrack retains task "+i);interact(item(BaselineInteraction.ActionKind.Objective,i));check(w.completed[i],"Task complete "+i);}
        foreach(var c in items.Where(a=>a.action==BaselineInteraction.ActionKind.Chest)){var q=c.lid.localRotation;interact(c);check(q==c.lid.localRotation,"Repeated chest does not reopen "+c.index);}
        check(w.WalkingRoute(new Vector3(-12,-1.2f,-18),w.objectives[0].position).Length==0,"Deep water has no fake walking route");
        check(w.WalkingRoute(new Vector3(2,-1.2f,-18),w.objectives[0].position).Length==0,"Swimming below bridge cannot join deck route");
        // Leave one active tracked task for the transition-state preservation check.
        w.completed[2]=false;w.Track(2);
        File.WriteAllLines("ToolReports/MapExpansion/interaction-checks.txt",logs);
    }
}
