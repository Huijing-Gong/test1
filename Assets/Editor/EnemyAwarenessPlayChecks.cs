using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Controlled native Play fixtures, not simulated human gameplay. No scene/prefab writes.
public static class EnemyAwarenessPlayChecks
{
    const string Out="LocalDocs/Enemies/Awareness-20261020";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Vector3 Origin=new Vector3(23000,100,23000);
    public static void Prepare()
    {
        const string folder="Assets/Art/Enemies/Shared/Resources";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Art/Enemies/Shared","Resources");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Enemies/Shared/EnemyAwarenessIndicator.shader");
        if(!shader || ShaderUtil.ShaderHasError(shader))throw new Exception("Awareness shader failed compilation.");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/EnemyAwarenessIndicator.mat");
        if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,folder+"/EnemyAwarenessIndicator.mat");}
        else {mat.shader=shader;EditorUtility.SetDirty(mat);}
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out+"/asset-validation.txt","PASS native glyph shader compiles; referenced Resources material includes it in player builds.\n");
    }
    public static IEnumerator Tests(BaselineFlow flow,Transform hero,Action<string> log)
    {
        Directory.CreateDirectory(Out);
        foreach(var a in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None))a.enabled=false;
        foreach(var a in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None))a.enabled=false;
        foreach(var a in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None))a.enabled=false;
        foreach(var a in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None))a.enabled=false;
        foreach(var a in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None))a.enabled=false;
        foreach(var a in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None))a.enabled=false;
        var movement=hero.GetComponent<PlayerMovement>();movement.enabled=false;movement.ClearImpact();
        var player=hero.GetComponent<PlayerVitals>();var hp=hero.GetComponent<Health>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA awareness ground";
        floor.transform.position=Origin+Vector3.down*.15f;floor.transform.localScale=new Vector3(70,.3f,70);
        var layout=flow.GetComponent<OverworldV2Layout>();var so=new SerializedObject(layout);
        var surfaces=so.FindProperty("navigationSurfaces");int n=surfaces.arraySize;surfaces.arraySize++;
        surfaces.GetArrayElementAtIndex(n).objectReferenceValue=floor.GetComponent<Collider>();so.ApplyModifiedPropertiesWithoutUndo();
        Place(hero,Origin+Vector3.forward*30);yield return null;
        string[] paths={"Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab",
            "Assets/Art/Enemies/DemonKingTrial/DemonKingScoutTrial.prefab",
            "Assets/Art/Enemies/GelBlob/Generated/GelBlob.prefab",
            "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab",
            "Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab",
            "Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab",
            "Assets/Art/Enemies/RepairWisp/Generated/RepairWisp.prefab"};
        foreach(string path in paths)
        {
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),Origin+Vector3.up*.025f,Quaternion.identity);
            yield return null;var aw=actor.GetComponent<EnemyAwareness>();Need(aw,path+" binds perception");
            // Deterministic ticks in this single coroutine step: no yielded frames/AI steps.
            // Keep timeScale positive so the real production combat gate remains open.
            Time.timeScale=1f;aw.Clear();Place(hero,Origin+Vector3.back*8);
            for(int i=0;i<150;i++)aw.Tick(player,true,10f,.02f);
            Need(aw.State==EnemyAwareness.AwarenessState.Unaware,"Rear player must not auto-aggro: "+path);
            Place(hero,Origin+Vector3.forward*9f);
            for(int i=0;i<25;i++)aw.Tick(player,true,10f,.02f);
            Need(aw.State==EnemyAwareness.AwarenessState.Suspicious && !aw.Ready,"Far view has a usable question-mark window: "+path+" state="+aw.State+" evidence="+aw.Evidence+" visible="+aw.Visible+" allowed="+aw.DisplayAllowed+" enemy="+actor.transform.position+" player="+hero.position+" fwd="+actor.transform.forward);
            float evidence=aw.Evidence;
            for(int i=0;i<50;i++)aw.Tick(player,false,10f,.02f);
            Need(!aw.Engaged && aw.Evidence<evidence,"Cover can interrupt acquisition: "+path);
            for(int i=0;i<150;i++)aw.Tick(player,false,10f,.02f);
            Need(aw.State==EnemyAwareness.AwarenessState.Unaware,"Suspicion fully cools down: "+path);
            for(int i=0;i<230;i++)aw.Tick(player,true,10f,.02f);
            Need(aw.Ready && aw.ConfirmationCount==1,"Sustained sight confirms exactly once: "+path);
            for(int i=0;i<40;i++)aw.Tick(player,false,10f,.02f);
            Vector3 seen=aw.InterestPosition;Place(hero,Origin+Vector3.right*7f);
            aw.Tick(player,false,10f,.02f);
            Need(aw.State==EnemyAwareness.AwarenessState.Searching && aw.InterestPosition==seen,"Search remembers last sight, not hidden live player: "+path);
            for(int i=0;i<200;i++)aw.Tick(player,false,10f,.02f);
            Need(!aw.Engaged,"Search expires: "+path);
            aw.Clear();aw.Tick(player,true,10f,.02f);actor.GetComponent<Health>().TryApplyDamage(new DamageInfo(1,hero.gameObject,actor.transform.position,Vector3.forward));
            Need(aw.Engaged,"A real player hit alerts even from behind: "+path);
            actor.GetComponent<Health>().TryApplyDamage(new DamageInfo(9999,hero.gameObject,actor.transform.position,Vector3.forward));
            Need(!aw.Engaged,"Death clears perception: "+path);
            Time.timeScale=1f;Object.Destroy(actor);yield return null;
            log("PASS shared perception + real hit/death hooks: "+path);
        }

        // Native species decisions, not just the common component's deterministic ticks.
        for(int i=0;i<6;i++)
        {
            Place(hero,Origin+Vector3.forward*30);hp.RestoreFullHealth();
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]),Origin+Vector3.up*.025f,Quaternion.identity);
            var aw=actor.GetComponent<EnemyAwareness>();
            Place(hero,Origin+Vector3.forward*(i==5?4f:6.7f));
            yield return Wait(()=>aw.Curious,3f,"Native species suspicion "+paths[i]);
            Need(!ActiveAttack(actor),"No attack during '?' "+paths[i]);
            yield return Wait(()=>aw.Ready,6f,"Native species confirmation "+paths[i]);
            float attackUntil=Time.time+16f;
            while(!ActiveAttack(actor) && Time.time<attackUntil)yield return null;
            Need(ActiveAttack(actor),"Original attack still reachable after confirmation "+paths[i]+" awareness="+aw.State+" visible="+aw.Visible+" phase="+Describe(actor)+" enemy="+actor.transform.position+" hero="+hero.position+" hp="+hp.CurrentHealth);
            Place(hero,Origin+Vector3.forward*30);Object.Destroy(actor);yield return null;
            log("PASS native perception-to-attack integration: "+paths[i]);
        }

        Place(hero,Origin+Vector3.forward*9f);
        var guard=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]),Origin+Vector3.left*3+Vector3.up*.025f,Quaternion.identity);
        guard.GetComponent<StoneScoutRoadsideAI>().enabled=false;
        var groupRoot=new GameObject("QA local repair awareness group");var group=groupRoot.AddComponent<RepairWispGroup>();group.Configure(new[]{guard.GetComponent<Health>()});
        var helper=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[6]),Origin+Vector3.up*.025f,Quaternion.identity).GetComponent<RepairWispAI>();helper.ConfigureGroup(group);
        guard.GetComponent<Health>().TryApplyDamage(new DamageInfo(20,hero.gameObject,guard.transform.position,Vector3.forward));
        yield return Wait(()=>helper.SuccessfulRepairs>0,8f,"Local guard alarm still enables actual support heal");
        Need(helper.GetComponent<EnemyAwareness>().Engaged,"Support shares only its guard group's alarm");
        Object.Destroy(helper.gameObject);Object.Destroy(guard);Object.Destroy(groupRoot);yield return null;
        log("PASS native healer group alarm, real healing and perception integration.");

        // Actual autonomous scout acquisition, real ray-cast cover and rendered billboards.
        Place(hero,Origin+Vector3.forward*30);
        var demon=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[1]),Origin+Vector3.up*.025f,Quaternion.identity);
        var ai=demon.GetComponent<StoneScoutRoadsideAI>();var perception=demon.GetComponent<EnemyAwareness>();
        var c=Camera.main;var follow=c.GetComponent<ThirdPersonCamera>();if(follow)follow.enabled=false;
        c.transform.position=Origin+new Vector3(6,4,8);c.transform.LookAt(Origin+Vector3.up*1.6f);c.fieldOfView=38;
        Place(hero,Origin+Vector3.forward*6.7f);
        yield return Wait(()=>perception.State==EnemyAwareness.AwarenessState.Suspicious,3f,"Actual AI '?' entry");
        yield return new WaitForSeconds(.18f);Capture(c,"01-Question.png");
        var icon=demon.GetComponentInChildren<EnemyAwarenessIndicator>();Need(icon && icon.Visible,"Question mesh is actually visible");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="QA real solid sight blocker";wall.transform.position=Origin+new Vector3(0,1.5f,3);wall.transform.localScale=new Vector3(5,3,.35f);Physics.SyncTransforms();
        yield return new WaitForSeconds(2.5f);
        Need(!perception.Engaged && perception.State==EnemyAwareness.AwarenessState.Unaware,"Solid world cover prevents confirmation");
        Object.Destroy(wall);yield return null;
        yield return Wait(()=>perception.Engaged,12,"Actual AI confirmation after patrol can turn back towards the observer");
        yield return new WaitForSeconds(.08f);Capture(c,"02-Exclamation.png");Need(icon.Visible,"Confirmation glyph is visible");
        yield return Wait(()=>Field(ai,"state").ToString()=="Chase"||Field(ai,"state").ToString()=="Windup",3,"Perception releases real chase");
        Place(hero,Origin+Vector3.forward*30);
        yield return Wait(()=>!perception.Engaged,8f,"AI disengages after player escapes");
        ai.ResetToHome();Need(perception.State==EnemyAwareness.AwarenessState.Unaware,"Home reset clears marker/evidence");
        Place(hero,Origin+Vector3.forward*6.7f);yield return Wait(()=>perception.Curious,4,"Re-entering can start a fresh suspicion");
        var pauseState=perception.State;float pauseEvidence=perception.Evidence;
        flow.SetMenu(true);yield return null;yield return null;
        Need(!icon.Visible && perception.State==pauseState && Mathf.Abs(perception.Evidence-pauseEvidence)<.001f,"Pause menu hides marker but freezes acquisition instead of clearing it");
        flow.SetMenu(false);Time.timeScale=1f;
        Object.Destroy(demon);Object.Destroy(floor);yield return null;
        yield return CaptureWorld(flow,hero,log);
        hp.RestoreFullHealth();Place(hero,layout.CampPosition);
        log("PASS native AI: suspicion, physical cover, confirmation, chase, escape, reset and modal cancellation.");
        log("Screenshots are native controlled runtime fixtures, not AI artwork or a human playthrough.");
    }
    static IEnumerator CaptureWorld(BaselineFlow flow,Transform hero,Action<string> log)
    {
        var world=flow.GetComponent<BaselineWorld>();world.RefreshNavigation();
        DemonKingCombatVisual visual=Object.FindFirstObjectByType<DemonKingCombatVisual>();
        if(!visual)yield break;
        var ai=visual.GetComponentInParent<StoneScoutRoadsideAI>();ai.enabled=true;ai.ResetToHome();
        yield return new WaitForSeconds(.15f);
        Vector3 home=ai.transform.position;Quaternion facing=ai.transform.rotation;bool clear=false;
        for(int radius=0;radius<3 && !clear;radius++)
            for(int angle=-60;angle<=60 && !clear;angle+=20)
            {
                Vector3 p=home+facing*Quaternion.Euler(0,angle,0)*Vector3.forward*(4.5f+radius);
                if(!world.NavigationGround(p,out var ground) || Mathf.Abs(ground.y-home.y)>1.5f)continue;
                Place(hero,ground);clear=(bool)typeof(StoneScoutRoadsideAI).GetMethod("HasClearSight",Flags).Invoke(ai,null);
            }
        if(!clear){ai.enabled=false;log("INFO no clear authored terrain preview location; controlled native captures remain available.");yield break;}
        var aw=ai.GetComponent<EnemyAwareness>();aw.Clear();
        var c=Camera.main;Vector3 focus=home+Vector3.up*1.4f;
        c.transform.position=focus+facing*new Vector3(4,2.5f,7);c.transform.LookAt(focus);c.fieldOfView=38;
        yield return Wait(()=>aw.Curious,4,"Authored world '?' is reachable without moving the enemy");
        yield return null;Capture(c,"03-World-Question.png");
        yield return Wait(()=>aw.Engaged,6,"Authored world '!' confirms");yield return null;Capture(c,"04-World-Exclamation.png");
        ai.enabled=false;log("PASS authored-world native captures; enemy home/scene were not edited.");
    }
    static object Field(object o,string f)=>o.GetType().GetField(f,Flags).GetValue(o);
    static string Describe(GameObject actor)
    {
        var scout=actor.GetComponent<StoneScoutRoadsideAI>();if(scout)return Field(scout,"state").ToString();
        var gel=actor.GetComponent<GelBlobAI>();if(gel)return gel.Phase.ToString();
        var rail=actor.GetComponent<RailBeetleAI>();if(rail)return rail.CurrentState.ToString();
        var domain=actor.GetComponent<DomainPrototypeEnemy>();return domain?Field(domain,"state").ToString():"unknown";
    }
    static bool ActiveAttack(GameObject actor)
    {
        var scout=actor.GetComponent<StoneScoutRoadsideAI>();if(scout){string phase=Field(scout,"state").ToString();return phase=="Windup"||phase=="HitWindow"||phase=="Recover";}
        var gel=actor.GetComponent<GelBlobAI>();if(gel)return gel.Phase==GelBlobAI.State.Windup || gel.Phase==GelBlobAI.State.Flight || gel.Phase==GelBlobAI.State.Recover;
        var rail=actor.GetComponent<RailBeetleAI>();if(rail)return rail.CurrentState==RailBeetleAI.BehaviourState.Windup || rail.IsCharging || rail.CurrentState==RailBeetleAI.BehaviourState.Recover;
        var domain=actor.GetComponent<DomainPrototypeEnemy>();return domain && (domain.AttackCharge>0f || domain.PulseActive || domain.RangedShots>0);
    }
    static void Need(bool result,string why){if(!result)throw new Exception(why);Debug.Log("AWARENESS_QA PASS "+why);}
    static void Place(Transform player,Vector3 position)
    {
        player.GetComponent<PlayerMovement>().Relocate(position,Quaternion.Euler(0,180,0));
        player.GetComponent<PlayerVitals>().Refill();Physics.SyncTransforms();
    }
    static IEnumerator Wait(Func<bool> condition,float seconds,string why)
    {
        float until=Time.time+seconds,deadline=Time.realtimeSinceStartup+seconds*3f+3f;
        while(!condition() && Time.time<until && Time.realtimeSinceStartup<deadline)
        {
            if(Time.timeScale<=0f)
            {
                var flow=Object.FindFirstObjectByType<BaselineFlow>();
                var hero=flow.GetComponent<BaselineWorld>().player;
                Need(hero.GetComponent<PlayerVitals>().EnemyCombatActive,"QA gameplay unexpectedly paused: "+why+" menu="+flow.MenuOpen+" death="+flow.DeathOpen+" blocked="+flow.BlockWorldInput+" hp="+hero.GetComponent<Health>().CurrentHealth);
            }
            yield return null;
        }
        Need(condition(),why);
    }
    static void Capture(Camera c,string name)
    {
        var rt=new RenderTexture(1600,900,24);var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);var old=RenderTexture.active;var previous=c.targetTexture;
        try{c.targetTexture=rt;c.aspect=16f/9;c.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();File.WriteAllBytes(Out+"/"+name,texture.EncodeToPNG());}
        finally{RenderTexture.active=old;c.targetTexture=previous;rt.Release();Object.Destroy(rt);Object.Destroy(texture);}
    }
}
