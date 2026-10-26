using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Scoped scene and native Play checks. Runtime test moves only the QA player, never enemies.
[InitializeOnLoad]
public static class EnemyDistributionChecks
{
    const string Key="QA.RegionalP0.";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static string Report=>EnemyDistributionInstaller.Output+"/validation.txt";
    [Serializable] class Pose {public string name;public Vector3 point;}
    [Serializable] class Snapshot {public Pose[] poses;}
    static EnemyDistributionChecks() {EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Watch;}
    static void Log(string s){Directory.CreateDirectory(EnemyDistributionInstaller.Output);File.AppendAllText(Report,s+"\n");Debug.Log("REGIONAL_QA "+s);}
    static void Require(bool pass,string reason){if(!pass)throw new InvalidOperationException(reason);Log("PASS "+reason);}
    static Health[] Actors()=>Object.FindObjectsByType<Health>(FindObjectsSortMode.None).Where(h=>EnemyDistributionInstaller.Role(h)!="").ToArray();
    public static void RunAutomation()
    {
        EditorSceneManager.OpenScene(EnemyDistributionInstaller.ScenePath);
        File.WriteAllText(Report,"P0 regional enemy layout: native Unity static + scripted Play checks. Not a full human playtest.\n");
        EnemyDistributionInstaller.ValidateAuthoring();
        EnemyFoundationPlacementChecks.Run();
        StoneScoutVariantChecks.Run();
        DomainPrototypeChecks.Run();
        RepairWispChecks.Run();
        // The obsolete grid command must neither move an actor nor rewrite the scene.
        var hash=System.Security.Cryptography.SHA256.Create();
        var before=Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(EnemyDistributionInstaller.ScenePath)));
        StoneScoutVariantPlacement.Place();
        Require(before==Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(EnemyDistributionInstaller.ScenePath))),"legacy grid command preserves authored scene bytes");
        Capture("Actual_TopDown.png",new Vector3(0,180,0),Vector3.zero,118,true);
        EditorSceneManager.OpenScene(EnemyDistributionInstaller.ScenePath);
        var poses=Actors().Select(h=>new Pose{name=h.name,point=h.transform.position}).ToArray();
        SessionState.SetString(Key+"poses",JsonUtility.ToJson(new Snapshot{poses=poses}));
        var flow=Object.FindFirstObjectByType<BaselineFlow>();
        SessionState.SetString(Key+"originalPrefix",flow.saveKeyPrefix);
        foreach(var suffix in new[]{".primary",".backup"}) {
            string k=flow.saveKeyPrefix+suffix;
            SessionState.SetBool(Key+suffix+"exists",PlayerPrefs.HasKey(k));
            SessionState.SetString(Key+suffix+"value",PlayerPrefs.GetString(k,""));
        }
        SessionState.SetString(Key+"prefix",Key+Guid.NewGuid().ToString("N"));
        SessionState.SetBool(Key+"active",true);SessionState.SetBool(Key+"done",false);
        SessionState.SetString(Key+"deadline",DateTime.UtcNow.AddSeconds(240).Ticks.ToString());
        Application.runInBackground=true;EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key+"active",false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode) {
            var flow=Object.FindFirstObjectByType<BaselineFlow>();flow.saveKeyPrefix=SessionState.GetString(Key+"prefix","");
            flow.StartCoroutine(Guard(Tests(flow)));
        }
        if(mode==PlayModeStateChange.EnteredEditMode) {
            bool passed=SessionState.GetBool(Key+"passed",false);
            string prefix=SessionState.GetString(Key+"originalPrefix","");
            foreach(var suffix in new[]{".primary",".backup"})
                if(PlayerPrefs.HasKey(prefix+suffix)!=SessionState.GetBool(Key+suffix+"exists",false) || PlayerPrefs.GetString(prefix+suffix,"")!=SessionState.GetString(Key+suffix+"value",""))
                {passed=false;Log("FAIL original save changed");}
            BaselineSaveStore.Clear(SessionState.GetString(Key+"prefix",""));
            SessionState.SetBool(Key+"active",false);
            Log(passed?"FINAL PASS; original save slot unchanged; no scene/prefab saved during Play.":"FINAL FAIL");
            EditorApplication.Exit(passed?0:1);
        }
    }
    static IEnumerator Tests(BaselineFlow flow)
    {
        yield return null;flow.NewGame();if(flow.Tutorial)flow.Tutorial.Skip();flow.SetMenu(false);
        GameSession.Instance.GrantSword();
        var player=flow.GetComponent<BaselineWorld>().player;var movement=player.GetComponent<PlayerMovement>();
        var hp=player.GetComponent<Health>();var vitals=player.GetComponent<PlayerVitals>();
        float wait=Time.realtimeSinceStartup+12;
        while(!vitals.EnemyCombatActive && Time.realtimeSinceStartup<wait)yield return null;
        Require(vitals.EnemyCombatActive,"real overworld combat enabled; started="+flow.Started+", blocked="+flow.BlockWorldInput+", menu="+flow.MenuOpen+", timescale="+Time.timeScale+", externalLock="+movement.HasExternalInputLock);
        var actors=Actors().ToDictionary(h=>h.name);Require(actors.Count==17,"Play contains exactly seventeen authored enemies");
        var poses=JsonUtility.FromJson<Snapshot>(SessionState.GetString(Key+"poses","")).poses;
        foreach(var pose in poses) {
            var actor=actors[pose.name];
            Component ai=(Component)actor.GetComponent<StoneScoutRoadsideAI>() ?? (Component)actor.GetComponent<DomainPrototypeEnemy>() ??
                (Component)actor.GetComponent<RailBeetleAI>() ?? (Component)actor.GetComponent<RepairWispAI>() ?? actor.GetComponent<GelBlobAI>();
            Vector3 home=(Vector3)ai.GetType().GetField("home",Private).GetValue(ai);
            Vector3 delta=home-pose.point;
            Require(new Vector2(delta.x,delta.z).magnitude<.02f && Mathf.Abs(delta.y)<.15f,
                pose.name+" caches the new authored home (ground-fit delta "+delta.ToString("F3")+")");
        }
        movement.enabled=false;movement.ClearImpact();
        var layout=flow.GetComponent<OverworldV2Layout>();
        foreach(var name in new[]{"StoneScout_Sand_01","LeafWisp_EastForest","RuinPrism_WestRuins","GelBlob_DampForest","RailBeetle_PathGuard"}) {
            var actor=actors[name];var original=poses.First(p=>p.name==name).point;
            float distance=actor.GetComponent<RailBeetleAI>()?5:actor.GetComponent<StoneScoutRoadsideAI>()?3:actor.GetComponent<GelBlobAI>()?4:2.8f;
            Vector3 target=default;bool found=false;
            for(int i=0;i<16;i++) {
                var wish=original+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*distance;
                if(EnemyDistributionInstaller.Floor(layout,wish,out var floor) && Mathf.Abs(floor.y-original.y)<.7f) {
                    var query=new EnemyGroundQuery(player);
                    if(query.TryFootprint(floor+Vector3.up*.015f,Quaternion.identity,Vector2.one*.4f,35,.5f,.6f,out _,true))
                    {target=floor+Vector3.up*.02f;found=true;break;}
                }
            }
            Require(found,name+" has a grounded player approach");
            MovePlayer(player,target);hp.RestoreFullHealth();bool seen=false;float deadline=Time.realtimeSinceStartup+12;
            var states=new HashSet<string>();int maxAttackers=0;
            while(Time.realtimeSinceStartup<deadline) {
                hp.RestoreFullHealth();movement.ClearImpact();
                string state=State(actor);states.Add(state);
                if(actor.GetComponent<StoneScoutRoadsideAI>()) {
                    seen|=state=="Windup"||state=="HitWindow";
                    int simultaneous=actors.Values.Where(h=>h.GetComponent<StoneScoutRoadsideAI>()).Count(h=>State(h)=="Windup"||State(h)=="HitWindow");maxAttackers=Math.Max(maxAttackers,simultaneous);
                } else if(actor.TryGetComponent(out DomainPrototypeEnemy domain))seen|=domain.EnemyKind==DomainPrototypeEnemy.Kind.LeafWisp?domain.RangedShots>0:domain.PulseActive;
                else if(actor.TryGetComponent(out GelBlobAI gel))seen|=gel.LandingCount>0;
                else if(actor.TryGetComponent(out RailBeetleAI rail))seen|=rail.CurrentState==RailBeetleAI.BehaviourState.Charge;
                if(seen)break;yield return null;
            }
            Log(name+" observed states: "+string.Join("/",states));
            Require(seen,name+" autonomously completes attack initiation on the real terrain");
            if(actor.GetComponent<StoneScoutRoadsideAI>())Require(maxAttackers<=1,"paired Scouts preserve their existing attack coordination");
            Capture("Play_"+name+".png",actor.transform.position+new Vector3(9,7,-12),actor.transform.position+Vector3.up,0,false);
            MovePlayer(player,layout.CampPosition+Vector3.up*.05f);hp.RestoreFullHealth();yield return new WaitForSeconds(.35f);
        }
        foreach(var name in new[]{"RepairWisp_HiddenChest","RepairWisp_Tower"}) {
            var ai=actors[name].GetComponent<RepairWispAI>();
            var group=new SerializedObject(ai).FindProperty("group").objectReferenceValue as RepairWispGroup;
            var guard=group.Guards[0];guard.TryApplyDamage(new DamageInfo(30,player.gameObject,guard.transform.position,Vector3.zero));
            MovePlayer(player,ai.transform.position+Vector3.back*4);hp.RestoreFullHealth();
            float deadline=Time.realtimeSinceStartup+14;
            while(ai.SuccessfulRepairs==0 && Time.realtimeSinceStartup<deadline){hp.RestoreFullHealth();movement.ClearImpact();yield return null;}
            Require(ai.SuccessfulRepairs>0 && ai.RemainingRepairs<3,name+" heals its explicit local guard in real Play");
        }
        MovePlayer(player,layout.CampPosition);flow.NewGame();if(flow.Tutorial)flow.Tutorial.Skip();flow.SetMenu(false);
        // Interpolated render transforms may still show the previous patrol frame.
        // Check the authoritative physics pose immediately after the real journey reset.
        foreach(var pose in poses) {
            var body=actors[pose.name].GetComponent<Rigidbody>();
            var actual=body?body.position:actors[pose.name].transform.position;
            var delta=actual-pose.point;
            Require(new Vector2(delta.x,delta.z).magnitude<.02f && Mathf.Abs(delta.y)<.15f,
                pose.name+" journey reset restores new physics home (delta "+delta.ToString("F3")+")");
        }
        yield return null;
    }
    static string State(Health actor) {
        Component ai=(Component)actor.GetComponent<StoneScoutRoadsideAI>() ?? (Component)actor.GetComponent<DomainPrototypeEnemy>() ?? actor.GetComponent<RailBeetleAI>();
        if(ai)return ai.GetType().GetField("state",Private).GetValue(ai).ToString();
        return actor.GetComponent<GelBlobAI>()?.Phase.ToString()??"";
    }
    static void MovePlayer(Transform player,Vector3 point) {
        var cc=player.GetComponent<CharacterController>();bool enabled=cc.enabled;cc.enabled=false;player.position=point;cc.enabled=enabled;Physics.SyncTransforms();
    }
    static void Capture(string name,Vector3 position,Vector3 focus,float size,bool ortho) {
        var go=new GameObject("QA native render");var cam=go.AddComponent<Camera>();
        cam.transform.position=position;cam.transform.LookAt(focus,ortho?Vector3.forward:Vector3.up);
        cam.orthographic=ortho;cam.orthographicSize=size;cam.fieldOfView=55;cam.nearClipPlane=.2f;cam.farClipPlane=500;
        int width=ortho?1536:1280,height=ortho?1536:720;cam.aspect=(float)width/height;
        var rt=new RenderTexture(width,height,24);var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try {cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
            File.WriteAllBytes(EnemyDistributionInstaller.Output+"/"+name,tex.EncodeToPNG());}
        finally {RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
    }
    static IEnumerator Guard(IEnumerator tests) {
        var stack=new Stack<IEnumerator>();stack.Push(tests);
        while(stack.Count>0) {
            object current;
            try {if(!stack.Peek().MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}current=stack.Peek().Current;}
            catch(Exception e){Finish(false,e.ToString());yield break;}
            if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
        }
        Finish(true,"all scripted Play checks completed");
    }
    static void Finish(bool passed,string reason) {
        if(SessionState.GetBool(Key+"done",false))return;
        SessionState.SetBool(Key+"done",true);SessionState.SetBool(Key+"passed",passed);Log((passed?"PASS ":"FAIL ")+reason);
        Time.timeScale=1;EditorApplication.delayCall+=()=>EditorApplication.ExitPlaymode();
    }
    static void Watch() {
        if(SessionState.GetBool(Key+"active",false)&&!SessionState.GetBool(Key+"done",false)&&
            long.TryParse(SessionState.GetString(Key+"deadline",""),out long d)&&DateTime.UtcNow.Ticks>d)Finish(false,"240-second watchdog");
    }
}
