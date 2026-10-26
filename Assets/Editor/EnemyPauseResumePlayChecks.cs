using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Native menu handlers and autonomous enemy attacks, in a disposable save/flat fixture.
public static class EnemyPauseResumePlayChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Vector3 Origin=new Vector3(23000,100,23000);
    public const string Output="LocalDocs/Enemies/PauseResume-20261021";
    public static IEnumerator Tests(BaselineFlow flow,Transform hero,Action<string> log)
    {
        Directory.CreateDirectory(Output);
        foreach(var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None))ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None))ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None))ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None))ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None))ai.enabled=false;
        foreach(var nav in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None))nav.enabled=false;
        var movement=hero.GetComponent<PlayerMovement>();movement.enabled=false;
        var hp=hero.GetComponent<Health>();var vitals=hero.GetComponent<PlayerVitals>();
        var world=flow.GetComponent<BaselineWorld>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA pause supported ground";floor.transform.position=Origin-Vector3.up*.15f;floor.transform.localScale=new Vector3(70,.3f,70);
        var layout=new SerializedObject(flow.GetComponent<OverworldV2Layout>());var grounds=layout.FindProperty("navigationSurfaces");grounds.arraySize++;
        grounds.GetArrayElementAtIndex(grounds.arraySize-1).objectReferenceValue=floor.GetComponent<Collider>();layout.ApplyModifiedPropertiesWithoutUndo();
        string[] paths={"Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab","Assets/Art/Enemies/DemonKingTrial/DemonKingScoutTrial.prefab",
            "Assets/Art/Enemies/GelBlob/Generated/GelBlob.prefab","Assets/Art/Enemies/RailBeetle/RailBeetle.prefab",
            "Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab","Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab"};
        foreach(string path in paths)
        {
            for(int menu=0;menu<3;menu++)
            {
                Place(hero,Origin+Vector3.forward*30);
                var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),Origin+Vector3.up*.025f,Quaternion.identity);
                var aw=actor.GetComponent<EnemyAwareness>();
                Place(hero,Origin+Vector3.forward*(actor.GetComponent<RailBeetleAI>()?5f:actor.GetComponent<StoneScoutRoadsideAI>()?2.5f:3.5f));
                yield return Wait(()=>Phase(actor)=="Windup"||Phase(actor)=="Warning",14,"Autonomous windup "+path);
                string before=Phase(actor);float timer=Timer(actor),evidence=aw.Evidence;int hpBefore=hp.CurrentHealth;
                int confirmations=aw.ConfirmationCount;
                string kind=menu==0?"J journal":menu==1?"M map":"Esc pause";
                Open(flow,world,menu);
                yield return new WaitForSecondsRealtime(.28f);
                Need(!vitals.EnemyCombatActive,"Real menu blocks combat "+kind);
                Need(Phase(actor)==before,"Menu must freeze, not cancel/return: "+kind+" "+path+" was="+before+" now="+Phase(actor));
                Need(Mathf.Abs(Timer(actor)-timer)<.025f,"Attack timer cannot advance/reset in menu: "+path);
                Need(aw.Engaged && aw.ConfirmationCount==confirmations && Mathf.Abs(aw.Evidence-evidence)<.001f,"Pause retains existing target, no reacquisition: "+path);
                Need(hp.CurrentHealth==hpBefore,"No damage while paused: "+path);
                Close(flow,world,menu);
                yield return Wait(()=>vitals.EnemyCombatActive,5,"Menu close restores gameplay "+kind);
                yield return Wait(()=>Phase(actor)!=before,5,"Interrupted frame continues after close "+path);
                Need(aw.Engaged && aw.ConfirmationCount==confirmations,"Resume preserves aggro without replaying '?'/'!': "+path);
                Place(hero,Origin+Vector3.forward*30);
                Object.Destroy(actor);yield return null;
                log("PASS pause/resume "+kind+": "+path+" frozen state/timer, no paused damage, original attack continues.");
            }
        }
        // A flying ranged attack is also a paused attack, not a free projectile erase.
        Place(hero,Origin+Vector3.forward*30);
        var ranged=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[4]),Origin+Vector3.up*.025f,Quaternion.identity);
        Place(hero,Origin+Vector3.forward*7f);
        yield return Wait(()=>FindDart(ranged),12,"Real leaf dart launched");
        var dart=FindDart(ranged);Vector3 dartAt=dart.transform.position;
        float life=(float)typeof(DomainSeedProjectile).GetField("remainingLifetime",Private).GetValue(dart);int beforeDart=hp.CurrentHealth;
        Open(flow,world,1);yield return new WaitForSecondsRealtime(.3f);
        Need(dart && dart.gameObject.activeInHierarchy && Vector3.Distance(dart.transform.position,dartAt)<.001f,"M freezes actual pooled dart instead of deleting it");
        Need(Mathf.Abs((float)typeof(DomainSeedProjectile).GetField("remainingLifetime",Private).GetValue(dart)-life)<.001f && hp.CurrentHealth==beforeDart,"Projectile lifetime and paused damage are frozen");
        Close(flow,world,1);yield return Wait(()=>vitals.EnemyCombatActive,5,"Map closes before dart resumes");
        yield return Wait(()=>hp.CurrentHealth<beforeDart,4,"Same paused dart resumes and can still hit");
        Object.Destroy(ranged);Place(hero,Origin+Vector3.forward*30);yield return null;
        log("PASS real pooled leaf projectile survives M, freezes position/lifetime/damage, then resumes real contact damage.");

        // Support keeps the same target and channel; menus cannot finish a heal off-screen.
        var guard=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]),Origin+Vector3.left*3+Vector3.up*.025f,Quaternion.identity);
        guard.GetComponent<StoneScoutRoadsideAI>().enabled=false;
        var groupRoot=new GameObject("QA pause support group");var group=groupRoot.AddComponent<RepairWispGroup>();group.Configure(new[]{guard.GetComponent<Health>()});
        var support=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Enemies/RepairWisp/Generated/RepairWisp.prefab"),Origin+Vector3.up*.025f,Quaternion.identity).GetComponent<RepairWispAI>();support.ConfigureGroup(group);
        Place(hero,Origin+Vector3.forward*9f);var recipient=guard.GetComponent<Health>();recipient.TryApplyDamage(new DamageInfo(20,hero.gameObject,guard.transform.position,Vector3.forward));
        yield return Wait(()=>support.CurrentPhase==RepairWispAI.Phase.Repair,8,"Real repair channel begins");
        int guardHp=recipient.CurrentHealth,repairs=support.SuccessfulRepairs,remaining=support.RemainingRepairs;
        Open(flow,world,0);yield return new WaitForSecondsRealtime(.3f);
        Need(support.CurrentPhase==RepairWispAI.Phase.Repair && support.RepairTarget==recipient && recipient.CurrentHealth==guardHp && support.RemainingRepairs==remaining,"J freezes repair target/channel/charges and no healing occurs paused");
        Close(flow,world,0);yield return Wait(()=>vitals.EnemyCombatActive,5,"Journal closes before repair resumes");
        yield return Wait(()=>support.SuccessfulRepairs==repairs+1,4,"Same repair channel resumes once");
        Need(recipient.CurrentHealth>guardHp && support.RemainingRepairs==remaining-1,"One resumed heal consumes exactly one charge");
        Object.Destroy(support.gameObject);Object.Destroy(guard);Object.Destroy(groupRoot);Object.Destroy(floor);yield return null;
        log("PASS healer preserves its channel/recipient across J, performs no paused heal and completes exactly once after resume.");
        Place(hero,flow.GetComponent<OverworldV2Layout>().CampPosition);
        log("COMPLETE actual J/M/Esc handlers during autonomous windups of Scout, DemonKing, Gel, charger, ranged Wisp and Prism.");
    }
    static MonoBehaviour AI(GameObject a)
    {
        var s=a.GetComponent<StoneScoutRoadsideAI>();if(s)return s;
        var g=a.GetComponent<GelBlobAI>();if(g)return g;
        var r=a.GetComponent<RailBeetleAI>();if(r)return r;
        return a.GetComponent<DomainPrototypeEnemy>();
    }
    static DomainSeedProjectile FindDart(GameObject owner)
    {foreach(var dart in Object.FindObjectsByType<DomainSeedProjectile>(FindObjectsSortMode.None))if(dart.Owner==owner)return dart;return null;}
    static string Phase(GameObject a)
    {
        var gel=a.GetComponent<GelBlobAI>();if(gel)return gel.Phase.ToString();
        return AI(a).GetType().GetField("state",Private).GetValue(AI(a)).ToString();
    }
    static float Timer(GameObject a)
    {var ai=AI(a);return (float)ai.GetType().GetField(ai is GelBlobAI?"age":"timer",Private).GetValue(ai);}
    static void Open(BaselineFlow f,BaselineWorld w,int mode)
    {
        if(mode==0){w.SetJournal(true);Need(w.JournalOpen,"J uses real journal handler");}
        else if(mode==1)Need(f.ShowMap() && f.MapOpen,"M uses real map handler");
        else{f.SetMenu(true);Need(f.MenuOpen,"Esc uses real pause handler");}
    }
    static void Close(BaselineFlow f,BaselineWorld w,int mode)
    {if(mode==0)w.SetJournal(false);else f.SetMenu(false);}
    static void Place(Transform hero,Vector3 p)
    {hero.GetComponent<PlayerMovement>().Relocate(p,Quaternion.Euler(0,180,0));hero.GetComponent<PlayerVitals>().Refill();Physics.SyncTransforms();}
    static IEnumerator Wait(Func<bool> condition,float seconds,string name)
    {float deadline=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;Need(condition(),name);}
    static void Need(bool ok,string text){if(!ok)throw new Exception(text);Debug.Log("PAUSE_QA PASS "+text);}
}
