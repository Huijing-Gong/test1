using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Native Play checks with controlled encounter starts, not a human playthrough.
[InitializeOnLoad]
public static class DemonKingTrialChecks
{
    const string Key="QA.DemonKingTrial.";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static BaselineFlow flow;static BaselineWorld world;static StoneScoutRoadsideAI ai;static DemonKingCombatVisual visual;
    static Health heroHealth,enemyHealth;static PlayerMovement movement;static Transform hero;static GameObject actor;
    static int contacts;static float contactAt;static Vector3 home;static Quaternion facing;
    static string Report=>DemonKingTrialInstaller.Output+"/validation.txt";
    static DemonKingTrialChecks(){EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Watch;}
    static void Log(string text){Directory.CreateDirectory(DemonKingTrialInstaller.Output);File.AppendAllText(Report,text+"\n");Debug.Log("DEMON_QA "+text);}
    static void Need(bool pass,string text){if(!pass)throw new InvalidOperationException(text);Log("PASS "+text);}
    static string State=>typeof(StoneScoutRoadsideAI).GetField("state",Flags).GetValue(ai).ToString();
    public static void RunAutomation()
    {
        if(!Application.dataPath.StartsWith("/private/tmp/demonking-trial-",StringComparison.Ordinal))throw new InvalidOperationException("Disposable clone only.");
        EditorSceneManager.OpenScene(DemonKingTrialInstaller.ScenePath);Directory.CreateDirectory(DemonKingTrialInstaller.Output);
        File.WriteAllText(Report,"Native Unity Play checks. NewGame/Skip are QA setup in a disposable slot; controlled player placements test authored AI and blade contacts. Not human input testing.\n");
        var f=Object.FindFirstObjectByType<BaselineFlow>();SessionState.SetString(Key+"real",f.saveKeyPrefix);
        foreach(string suffix in new[]{".primary",".backup"})SessionState.SetString(Key+suffix,Slot(f.saveKeyPrefix+suffix));
        SessionState.SetString(Key+"scene",Hash(DemonKingTrialInstaller.ScenePath));
        SessionState.SetString(Key+"prefix",Key+Guid.NewGuid().ToString("N"));f.saveKeyPrefix=SessionState.GetString(Key+"prefix","");
        SessionState.SetBool(Key+"active",true);SessionState.SetFloat(Key+"start",(float)EditorApplication.timeSinceStartup);
        Application.runInBackground=true;EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key+"active",false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {flow=Object.FindFirstObjectByType<BaselineFlow>();flow.saveKeyPrefix=SessionState.GetString(Key+"prefix","");flow.StartCoroutine(Guard(Tests()));}
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            bool passed=SessionState.GetBool(Key+"pass",false);string original=SessionState.GetString(Key+"real","");
            foreach(string suffix in new[]{".primary",".backup"})if(Slot(original+suffix)!=SessionState.GetString(Key+suffix,"")){passed=false;Log("FAIL real player slot changed");}
            if(Hash(DemonKingTrialInstaller.ScenePath)!=SessionState.GetString(Key+"scene","")){passed=false;Log("FAIL scene changed in Play");}
            BaselineSaveStore.Clear(SessionState.GetString(Key+"prefix",""));SessionState.SetBool(Key+"active",false);Log(passed?"FINAL PASS; original slots and scene unchanged during Play.":"FINAL FAIL");EditorApplication.Exit(passed?0:1);
        }
    }
    static IEnumerator Tests()
    {
        yield return null;flow.NewGame();if(flow.Tutorial)flow.Tutorial.Skip();flow.SetMenu(false);GameSession.Instance.GrantSword();
        world=flow.GetComponent<BaselineWorld>();hero=world.player;movement=hero.GetComponent<PlayerMovement>();heroHealth=hero.GetComponent<Health>();
        actor=Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None).Single(a=>a.name==DemonKingTrialInstaller.ActorName).gameObject;
        ai=actor.GetComponent<StoneScoutRoadsideAI>();visual=actor.GetComponentInChildren<DemonKingCombatVisual>();enemyHealth=actor.GetComponent<Health>();home=actor.transform.position;facing=actor.transform.rotation;
        heroHealth.Damaged+=OnHeroHit;
        yield return new WaitForSeconds(.35f);
        Need(hero.GetComponent<PlayerVitals>().EnemyCombatActive,"Real main-world enemy combat is active");
        Need(actor.GetComponentInChildren<StoneScoutVisualMotion>(true)==visual,"All legacy adapters resolve the new DemonKing visual");
        Need(enemyHealth.MaxHealth==60 && actor.GetComponent<StoneScoutLootDrop>() && actor.GetComponent<StoneScoutCombatTarget>(),"Original Health, drops and encounter reset retained");
        Need(Object.FindObjectsByType<DemonKingCombatVisual>(FindObjectsSortMode.None).Length==1,"Exactly one authored replacement; five original Stone Scouts retained");
        var follow=Camera.main.GetComponent<ThirdPersonCamera>();if(follow)follow.enabled=false;
        Place(home+actor.transform.forward*4);yield return new WaitForSeconds(.25f);movement.enabled=false;
        yield return Wait(()=>State=="Chase"||State=="Windup",10,"Original alert and chase acquire player");
        bool warning=false,wake=false,capturedWind=false,capturedStrike=false;
        float until=Time.realtimeSinceStartup+14;
        while(Time.realtimeSinceStartup<until && contacts==0)
        {
            if(visual.WarningVisible){warning=true;if(!capturedWind && visual.AttackElapsed>.32f){Capture("01-Windup.png");capturedWind=true;}}
            if(visual.TrailVisible){wake=true;if(!capturedStrike){Capture("02-BladeWake.png");capturedStrike=true;}}
            yield return null;
        }
        Need(contacts==1,"Actual autonomous AI landed one blade hit; state="+State+" attackTime="+visual.AttackElapsed);
        Need(contactAt>=visual.contactStart-.035f && contactAt<=visual.contactEnd+.035f,"Damage coincides with actual animated cutting window at "+contactAt);
        Need(warning,"Weapon visibly charged before contact");
        yield return new WaitForSeconds(.12f);wake|=visual.TrailVisible;Need(wake,"Short actual blade wake became visible");
        Capture("03-AfterContact.png");int hp=heroHealth.CurrentHealth;yield return new WaitForSeconds(.45f);Need(contacts==1 && heroHealth.CurrentHealth==hp && State=="Recover","Single hit and stationary counterattack recovery, no multi-hit");

        Reset();Place(SafePoint(4));yield return Wait(()=>State=="Windup" && visual.AttackElapsed>.2f,10,"Point-blank contact fixture begins from a real telegraph");
        int nearCount=contacts;Place(actor.transform.position+actor.transform.forward*1.02f);
        yield return Wait(()=>State=="Recover",3,"Point-blank swing finishes");Need(contacts==nearCount+1,"Actual cutting edge still hits at close capsule distance");

        Reset();Place(SafePoint(4));yield return Wait(()=>State=="Windup" && visual.AttackElapsed>.2f,10,"Cover fixture starts during a real telegraph");
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="QA temporary masonry blocker";blocker.transform.position=(actor.transform.position+hero.position)*.5f+Vector3.up;blocker.transform.localScale=new Vector3(.5f,2.1f,2);
        Physics.SyncTransforms();int blockedCount=contacts;yield return Wait(()=>State=="Recover",3,"Blocked swing finishes");Need(contacts==blockedCount,"Solid foreground cover blocks damage");Object.Destroy(blocker);yield return null;

        Reset();Place(SafePoint(4));yield return Wait(()=>State=="Windup" && visual.AttackElapsed>.38f,10,"Second telegraph begins");
        int count=contacts;hp=heroHealth.CurrentHealth;Place(SafePoint(6));
        yield return Wait(()=>State=="Recover",3,"Whiff enters recovery after controlled evasive displacement");
        Need(contacts==count && heroHealth.CurrentHealth==hp,"Moving outside the actual cutting edge avoids damage");
        Vector3 at=actor.transform.position;yield return new WaitForSeconds(.5f);Need((actor.transform.position-at).sqrMagnitude<.01f,"Whiff leaves an actual stationary punish window");

        Reset();Place(SafePoint(4));yield return Wait(()=>State=="Windup" && visual.AttackElapsed>.15f,10,"Interruptible windup starts");
        count=contacts;enemyHealth.TryApplyDamage(new DamageInfo(1,hero.gameObject,visual.TorsoWorldPosition,hero.forward));
        Need(State=="Stagger" && !visual.WarningVisible,"Real damage cancels windup and its warning");yield return new WaitForSeconds(.09f);Capture("04-HitReaction.png");
        yield return new WaitForSeconds(.22f);Need(contacts==count,"Interrupted swing cannot deal delayed damage");
        Place(flow.GetComponent<OverworldV2Layout>().CampPosition);
        yield return Wait(()=>State=="Idle" && Vector3.Distance(actor.GetComponent<Rigidbody>().position,home)<.2f,20,"Existing navigation returns the heavier visual to its authored home");

        // Test the actual support AI against the new skinned recipient in the same world.
        Reset();ai.enabled=false;Place(SafePoint(4));
        var healer=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Enemies/RepairWisp/Generated/RepairWisp.prefab"),SafePoint(2.7f)+Vector3.up*.015f,Quaternion.identity);
        var groupGo=new GameObject("QA temporary repair group");var group=groupGo.AddComponent<RepairWispGroup>();group.Configure(new[]{enemyHealth});
        var repair=healer.GetComponent<RepairWispAI>();repair.ConfigureGroup(group);enemyHealth.TryApplyDamage(new DamageInfo(20,hero.gameObject,visual.TorsoWorldPosition,Vector3.right));group.Alert();
        yield return Wait(()=>repair.BeamVisible,16,"Existing healer targets DemonKing Health");yield return new WaitForSeconds(.25f);
        var highlight=actor.GetComponent<RepairSupportHighlight>();Need(highlight && highlight.Visible && highlight.OverlayCount>0,"Healing shader binds to the skinned body");Capture("05-HealingRecipient.png");
        yield return Wait(()=>repair.SuccessfulRepairs>0,8,"Existing support completes a real heal");Need(enemyHealth.CurrentHealth>40,"Heal actually restored Health");
        Object.Destroy(healer);Object.Destroy(groupGo);yield return null;
        enemyHealth.TryApplyDamage(new DamageInfo(9999,hero.gameObject,visual.TorsoWorldPosition,Vector3.forward));
        Need(visual.IsDead && !actor.GetComponent<CapsuleCollider>().enabled && !visual.TrailVisible,"Death cancels attack effects and body collision");
        yield return new WaitForSeconds(1.55f);Capture("06-Death.png");
        Need(visual.pose.GetCurrentAnimatorStateInfo(0).IsName("Death") && !visual.pose.GetCurrentAnimatorStateInfo(0).loop,"Death animation holds instead of looping");
        Need(actor.GetComponent<StoneScoutLootDrop>().GetType().GetField("activeDrop",Flags).GetValue(actor.GetComponent<StoneScoutLootDrop>())!=null,"Original one-time healing shard drop exists");
        actor.GetComponent<StoneScoutCombatTarget>().ResetForJourney();ai.enabled=true;
        Need(!visual.IsDead && enemyHealth.CurrentHealth==60 && actor.GetComponent<CapsuleCollider>().enabled && !visual.TrailVisible,"Journey reset restores DemonKing and clears effects/drop");
        Place(flow.GetComponent<OverworldV2Layout>().CampPosition);movement.enabled=true;
        Need(Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None).Length==6,"Six authored Scouts remain after support fixture cleanup");
        Log("COMPLETE authored chase/contact/whiff/recovery/stagger, skinned healing, death/drop and reset. Controlled encounter placements; manual feel and all terrain routes are not claimed.");
    }
    static void OnHeroHit(Health h,DamageInfo d){if(actor && d.Source==actor){contacts++;contactAt=visual.AttackElapsed;Log("CONTACT "+contacts+" visualTime="+contactAt+" point="+d.HitPoint);}}
    static void Reset(){actor.GetComponent<StoneScoutCombatTarget>().ResetForJourney();ai.enabled=true;heroHealth.RestoreFullHealth();Physics.SyncTransforms();}
    static Vector3 Ground(Vector3 p){if(!world.NavigationGround(p,out var ground))throw new InvalidOperationException("No ground at "+p);return ground;}
    static Vector3 SafePoint(float distance)
    {
        for(int i=0;i<16;i++)
        {
            Vector3 p=home+Quaternion.Euler(0,i*22.5f,0)*facing*Vector3.forward*distance;
            if(world.NavigationGround(p,out var g) && Mathf.Abs(g.y-home.y)<.7f)
            {Log("FIXTURE controlled safe point "+g+" distance="+distance);return g;}
        }
        throw new InvalidOperationException("No safe controlled encounter point at radius "+distance);
    }
    static void Place(Vector3 p){movement.Relocate(Ground(p)-Vector3.up*.1f,Quaternion.LookRotation(home-p));heroHealth.RestoreFullHealth();Physics.SyncTransforms();}
    static IEnumerator Wait(Func<bool> test,float seconds,string text){float until=Time.realtimeSinceStartup+seconds;while(!test() && Time.realtimeSinceStartup<until)yield return null;Need(test(),text+" state="+State);}
    static IEnumerator Guard(IEnumerator sequence)
    {
        var stack=new Stack<IEnumerator>();stack.Push(sequence);bool success=true;
        while(stack.Count>0){object item=null;bool next=false;try{next=stack.Peek().MoveNext();if(next)item=stack.Peek().Current;}catch(Exception e){Log("FAIL "+e);success=false;break;}if(!next){stack.Pop();continue;}if(item is IEnumerator inner){stack.Push(inner);continue;}yield return item;}
        if(heroHealth)heroHealth.Damaged-=OnHeroHit;SessionState.SetBool(Key+"pass",success);EditorApplication.ExitPlaymode();
    }
    static void Watch(){if(SessionState.GetBool(Key+"active",false) && EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"start",0)>220){Log("FAIL overall timeout");SessionState.SetBool(Key+"pass",false);if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();else EditorApplication.Exit(1);}}
    static string Slot(string k)=>PlayerPrefs.HasKey(k)?PlayerPrefs.GetString(k,""):"<missing>";
    static string Hash(string p){using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(File.ReadAllBytes(p)));}
    static void Capture(string name)
    {
        Vector3 focus=actor.transform.position+actor.transform.forward*.8f+Vector3.up*1.15f;var c=Camera.main;c.transform.position=focus+actor.transform.right*6.5f+Vector3.up*2.4f-actor.transform.forward*5;c.transform.LookAt(focus);c.fieldOfView=40;
        var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var old=RenderTexture.active;var target=c.targetTexture;
        try{c.targetTexture=rt;c.aspect=16f/9;c.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(DemonKingTrialInstaller.Output+"/"+name,tex.EncodeToPNG());}
        finally{RenderTexture.active=old;c.targetTexture=target;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
    }
}
