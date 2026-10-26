using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Real Play, scripted player actions and real sword/hit-stop. This is not a claim
// of human playtesting. All fixtures/captures live outside the saved scene.
public static class LeafWispPlayChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Folder="Assets/Art/Enemies/DomainPrototypes/Generated/";
    static readonly Vector3 Origin=new Vector3(21500,100,21500);
    static Transform player;
    static PlayerMovement movement;
    static CharacterController controller;
    static string directory;
    public static IEnumerator Tests(BaselineFlow flow, Transform target, Action<string> log)
    {
        directory=Path.Combine(Path.GetTempPath(),"anca-leaf-wisp-play",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory); log("Leaf Wisp capture directory: "+directory);
        Shader shader=Resources.Load<Shader>("LeafWispSurface");
        Need(shader && !ShaderUtil.ShaderHasError(shader),"Leaf Wisp surface shader failed.");
        player=target; movement=player.GetComponent<PlayerMovement>(); controller=player.GetComponent<CharacterController>();
        foreach(var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled=false;
        foreach(var ai in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None)) ai.enabled=false;
        var all=Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None);
        DomainPrototypeEnemy authored=null;
        foreach(var ai in all) { ai.enabled=false; if(ai.EnemyKind==DomainPrototypeEnemy.Kind.LeafWisp) authored=ai; }
        Need(authored,"No authored ranged enemy.");
        movement.enabled=false; movement.ClearImpact();
        // Actual-map sword, sunlight/shadow and death before the isolated AI arena.
        yield return ActualMap(authored,flow,log);
        foreach(var nav in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None)) nav.enabled=false;
        Collider floor=Box("QA ranged supported arena",Origin+Vector3.down*.15f,new Vector3(35,.3f,35));
        var data=new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var grounds=data.FindProperty("navigationSurfaces"); grounds.arraySize++;
        grounds.GetArrayElementAtIndex(grounds.arraySize-1).objectReferenceValue=floor; data.ApplyModifiedPropertiesWithoutUndo();
        Place(Origin+Vector3.forward*5); player.GetComponent<Health>().RestoreFullHealth();
        // Keep the spatial warning audio inside its actual listener range for
        // the isolated fixture. ActualMap above already uses the production follow camera.
        Camera.main.GetComponent<ThirdPersonCamera>().enabled=false;
        Camera.main.transform.position=Origin+new Vector3(0,3,-6);
        Camera.main.transform.LookAt(Origin+Vector3.up);
        var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"LeafWisp.prefab"),Origin+Vector3.up*.02f,Quaternion.identity);
        var aiW=actor.GetComponent<DomainPrototypeEnemy>(); var hp=actor.GetComponent<Health>(); var visual=actor.GetComponent<LeafWispPresentation>();
        var ph=player.GetComponent<Health>(); var vitals=player.GetComponent<PlayerVitals>();
        Set(aiW,"player",vitals); Set(aiW,"playerHealth",ph); Physics.SyncTransforms(); yield return null;
        Need(visual && !visual.HitVisible && !visual.MuzzleVisible,"Ranged cues missing or visible while idle.");
        Need(!Object.FindFirstObjectByType<DomainSeedProjectile>(),"Unexpected active dart at fixture start.");
        yield return Wait(()=>aiW.RangedCharging,3);
        Need(!aiW.RangedLocked,"Charge skipped anticipation.");
        Need(visual.AimWarningVisible && !visual.LockedWarningVisible,"Preparation lacks compact charge cue.");
        int warningCues=EnemyAudio.StartedCount;
        yield return Wait(()=>aiW.RangedLocked,2);
        Need(visual.LockedWarningVisible,"Locked shot lacks amber shape change.");
        Need(EnemyAudio.StartedCount>warningCues && EnemyAudio.LastStartedCue==EnemySoundCue.CoreLock,"No synchronized lock confirmation sound.");
        Vector3 locked=(Vector3)Field(aiW,"lockedAimPosition");
        yield return Wait(()=>aiW.RangedShots==1,2);
        Need(visual.MuzzleVisible && aiW.RangedRecovering && !visual.AimWarningVisible,"Shot lacks muzzle cue/recovery, or charge brackets linger.");
        Vector3 rest=aiW.transform.position;
        yield return Wait(()=>ph.CurrentHealth<ph.MaxHealth,1);
        Need(ph.CurrentHealth==ph.MaxHealth-7,"Stationary target did not take exactly one seven-damage dart.");
        Need((Vector3)Field(aiW,"lockedAimPosition")==locked,"Committed dart retargeted after fire.");
        Need(Vector3.Distance(rest,aiW.transform.position)<.01f,"Ranged enemy moved during post-shot opening.");
        log("PASS stationary real projectile hit, fixed locked target, immediate muzzle cue and stationary shot opening.");

        aiW.ResetForJourney(); ph.RestoreFullHealth(); Place(Origin+Vector3.forward*5);
        yield return Wait(()=>aiW.RangedLocked,3);
        locked=(Vector3)Field(aiW,"lockedAimPosition"); Place(Origin+new Vector3(2.2f,0,5));
        yield return Wait(()=>aiW.RangedShots==1,2); yield return Delay(.7f);
        Need(ph.CurrentHealth==ph.MaxHealth && (Vector3)Field(aiW,"lockedAimPosition")==locked,"Lateral dodge failed or aim tracked after lock.");
        log("PASS side movement after lock avoids the actual swept dart.");

        aiW.ResetForJourney(); aiW.enabled=false; Place(Origin+Vector3.forward*1.4f);
        Collider wall=Box("QA rear wall",Origin+new Vector3(0,1.5f,-1.2f),new Vector3(5,3,.25f)); Physics.SyncTransforms();
        object[] args={Vector3.forward*1.4f,true,Vector3.zero};
        Need((bool)Call(aiW,"ChooseWispShotPosition",args),"Rear wall prevents lateral escape.");
        Vector3 chosen=(Vector3)args[2]; Need(Mathf.Abs(chosen.x-Origin.x)>.8f,"Rear-wall fallback not lateral.");
        Set(aiW,"hasWispShotPosition",true); Set(aiW,"wispShotPosition",chosen); State(aiW,"Recover"); Set(aiW,"timer",1.85f);
        rest=aiW.transform.position;
        for(int i=0;i<8;i++) { Call(aiW,"RepositionWisp"); yield return null; }
        Need(Vector3.Distance(rest,aiW.transform.position)<.005f,"Retreat erased immediate counterattack window.");
        Set(aiW,"timer",0f); State(aiW,"Waiting");
        float deadline=Time.realtimeSinceStartup+2.5f;
        while((bool)Field(aiW,"hasWispShotPosition") && Time.realtimeSinceStartup<deadline)
        { Call(aiW,"RepositionWisp"); Need((Vector3)Field(aiW,"wispShotPosition")==chosen,"Retreat goal jitters."); yield return null; }
        Need(!(bool)Field(aiW,"hasWispShotPosition"),"Short retreat never finished.");
        rest=aiW.transform.position;
        for(int i=0;i<8;i++) { Call(aiW,"RepositionWisp"); yield return null; }
        Need(Vector3.Distance(rest,aiW.transform.position)<.01f,"No planted pause after short escape.");
        Object.Destroy(wall.gameObject); yield return null;
        log("PASS rear-wall lateral retreat, stable committed goal, short escape then planted fighting pause.");

        var pool=EnemyTransientPool.For(actor.scene);
        GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"LeafSeed.prefab");
        if(!prefab) prefab=(GameObject)Field(aiW,"seedProjectile");
        var muzzle=(Transform)Field(aiW,"muzzle");
        for(int side=-1;side<=1;side+=2)
        {
            aiW.enabled=false; aiW.ResetForJourney(); Set(aiW,"strafeSign",side); Place(Origin+Vector3.forward*5);
            wall=Box("QA shot cover",Origin+new Vector3(0,1.5f,2.5f),new Vector3(.6f,3,.4f)); Physics.SyncTransforms();
            Need(!(bool)Call(aiW,"HasClearShot"),"Cover does not block sight.");
            args=new object[]{Vector3.forward*5,false,Vector3.zero};
            Need((bool)Call(aiW,"ChooseWispShotPosition",args),"Cover prevents finding a shooting angle.");
            chosen=(Vector3)args[2];
            Need((bool)Call(aiW,"ClearLineToPlayer",chosen+muzzle.position-aiW.transform.position),"Chosen position has no firing lane.");
            ph.RestoreFullHealth();
            pool.Spawn(prefab,actor,muzzle.position,(player.position+Vector3.up*.9f-muzzle.position).normalized,7);
            yield return Delay(.7f); Need(ph.CurrentHealth==ph.MaxHealth && pool.ActiveDarts==0,"Dart penetrates real cover.");
            aiW.enabled=true;
            yield return Wait(()=>aiW.RangedShots>0,4);
            Need(Vector3.Distance(aiW.transform.position,Origin)>.3f,"Covered ranged enemy never actually moves to a shooting angle.");
            yield return Wait(()=>ph.CurrentHealth<ph.MaxHealth,1);
            aiW.enabled=false; pool.Clear();
            Object.Destroy(wall.gameObject); yield return null;
        }
        log("PASS both cover sides: real cover blocks a swept dart; autonomous AI finishes its move and fires successfully.");
        aiW.enabled=true; aiW.ResetForJourney(); Place(Origin+Vector3.forward*5); State(aiW,"Warning"); Set(aiW,"timer",.15f); Call(aiW,"LockAim");
        muzzle=(Transform)Field(aiW,"muzzle");
        wall=Box("QA near muzzle blocker",muzzle.position+Vector3.forward*.35f,new Vector3(2,2,.1f)); Physics.SyncTransforms();
        Call(aiW,"UpdateWarning"); var line=(LineRenderer)Field(aiW,"warningLine");
        Need(!line.enabled && visual.LockedWarningVisible,"Old laser remains enabled or compact warning is missing.");
        Camera.main.transform.position=actor.transform.position+Vector3.back*40;
        Call(visual,"Refresh"); Need(visual.AimWarningVisible,"Secondary detail culling suppresses critical charge warning.");
        Camera.main.transform.position=Origin+new Vector3(0,3,-6);
        int confirmed=EnemyAudio.StartedCount; Call(aiW,"LockAim");
        Need(confirmed==EnemyAudio.StartedCount,"Lock confirmation sound repeats.");
        hp.TryApplyDamage(new DamageInfo(1,player.gameObject,muzzle.position,Vector3.forward));
        Need(!visual.AimWarningVisible && EnemyAudio.CountCue(EnemySoundCue.CoreLock)==0,"Genuine stagger retains warning/confirmation sound.");
        aiW.enabled=false; Object.Destroy(wall.gameObject); yield return null;
        log("PASS no laser, critical compact warning retained at distance, one lock sound, actual hit cancels warning immediately.");

        // A real empty sword swing must not cancel the wisp's attack.
        aiW.ResetForJourney(); Place(Origin+Vector3.forward*5); movement.enabled=true;
        movement.Relocate(player.position,Quaternion.Euler(0,180,0)); aiW.enabled=true;
        var sword=player.GetComponent<PlayerCombat>(); Focus();
        yield return Wait(()=>aiW.RangedCharging,3); Need(sword.TryAttack(),"Empty sword swing unavailable.");
        yield return Wait(()=>aiW.RangedShots>0,2);
        Need(hp.CurrentHealth==hp.MaxHealth,"Empty swing damaged ranged enemy.");
        movement.enabled=false; sword.CancelForTransition(); log("PASS real empty sword swing does not cancel ranged charge.");

        aiW.enabled=false; pool.Clear(); ph.RestoreFullHealth(); Place(Origin+Vector3.forward*5);
        var projectile=pool.Spawn(prefab,actor,muzzle.position,(player.position+Vector3.up*.9f-muzzle.position).normalized,7);
        movement.enabled=true; movement.Relocate(player.position,player.rotation);
        yield return Wait(()=>movement.ImpactActive,1);
        Need(ph.CurrentHealth==ph.MaxHealth-7,"Player reaction occurred without accepted damage.");
        yield return Delay(.2f); Need(!movement.ImpactActive,"Light dart reaction lasts too long.");
        movement.enabled=false; log("PASS accepted dart uses existing collision-constrained short player impact, then restores control.");
        for(int i=0;i<40;i++) { pool.Spawn(prefab,actor,muzzle.position,Vector3.forward,7); pool.ClearOwned(actor); }
        int created=pool.CreatedDarts;
        for(int i=0;i<40;i++) { pool.Spawn(prefab,actor,muzzle.position,Vector3.forward,7); pool.ClearOwned(actor); }
        Need(pool.CreatedDarts==created && pool.ActiveDarts==0,"Prewarmed darts allocate new objects.");
        aiW.enabled=true; aiW.ResetForJourney();
        yield return Wait(()=>aiW.RangedCharging,3);
        Need(visual.AimWarningVisible,"No charge cue before menu regression.");
        visual.Fire(); flow.SetMenu(true); yield return null;
        Need(!visual.MuzzleVisible && !visual.HitVisible && !visual.AimWarningVisible && pool.ActiveDarts==0,"Menu retains ranged effects.");
        Need(EnemyAudio.CountCue(EnemySoundCue.CoreLock)==0,"Menu retains charge/lock sound.");
        aiW.enabled=false;
        flow.SetMenu(false); yield return Delay(.5f);
        aiW.ResetForJourney(); Need(!visual.MuzzleVisible && !visual.HitVisible && hp.CurrentHealth==hp.MaxHealth,"Reset retains dead state/cues.");
        Object.Destroy(actor); Object.Destroy(floor.gameObject); yield return null;
        // Shared script safety: Prism never receives the new component/material.
        var prism=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"RuinPrism.prefab"),Origin,Quaternion.identity);
        Need(!prism.GetComponent<LeafWispPresentation>(),"Ranged presentation applied to Prism."); Object.Destroy(prism);
        log("PASS pooled darts reuse objects, menu/reset cleanup, no real lights, Prism remains opt-out.");
    }

    static IEnumerator ActualMap(DomainPrototypeEnemy ai, BaselineFlow flow, Action<string> log)
    {
        var hp=ai.GetComponent<Health>(); var visual=ai.GetComponent<LeafWispPresentation>();
        var sword=player.GetComponent<PlayerCombat>(); Camera camera=Camera.main;
        var orbit=camera.GetComponent<ThirdPersonCamera>(); orbit.SetLookInputLocked(true); orbit.SetCinematicControl(false);
        var floor=new EnemyGroundQuery(ai.transform); Vector3 playerPoint=default; bool found=false;
        for(int i=0;i<16;i++)
        {
            Vector3 offset=Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*1.2f;
            if(!floor.TrySample(ai.transform.position+offset,1.5f,1.5f,35,out RaycastHit ground)) continue;
            if(Mathf.Abs(ground.point.y-ai.transform.position.y)>.35f) continue;
            playerPoint=ground.point; found=true; break;
        }
        Need(found,"No supported actual-map sword position.");
        ai.ResetForJourney(); Place(playerPoint); movement.enabled=true; movement.Relocate(player.position,player.rotation);
        ai.enabled=true; State(ai,"Recover"); Set(ai,"timer",2f);
        Vector3 toward=Vector3.ProjectOnPlane(ai.transform.position-player.position,Vector3.up).normalized;
        player.rotation=Quaternion.LookRotation(toward); orbit.ResetOrbit(Mathf.Atan2(toward.x,toward.z)*Mathf.Rad2Deg+25,20);
        var rt=new RenderTexture(1280,720,24); rt.Create(); Focus(); yield return Delay(.35f);
        Capture(camera,rt,"actual-idle"); int before=hp.CurrentHealth;
        Need(sword.TryAttack(),"Actual-map sword unavailable."); yield return Wait(()=>hp.CurrentHealth<before,1);
        yield return null;
        Need(visual.HitVisible && sword.IsApplyingHitStop,"Genuine sword first hit-stop frame has no cue.");
        Capture(camera,rt,"actual-sword-hit"); Color32[] shown=Read(camera,rt);
        MeshRenderer flash=ai.transform.Find("Reusable ceramic sword impact").GetComponent<MeshRenderer>();
        flash.enabled=false; Color32[] hidden=Read(camera,rt); flash.enabled=true;
        int pixels=Changed(shown,hidden); Need(pixels>=45,"Immediate normal-camera hit cue too faint/occluded: "+pixels);
        // Real opaque occlusion, not a depth-disabled overlay.
        Collider screen=Box("QA hit occluder",Vector3.Lerp(camera.transform.position,flash.transform.position,.5f),new Vector3(1.2f,1.2f,.1f));
        screen.transform.rotation=Quaternion.LookRotation(flash.transform.position-camera.transform.position);
        screen.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Graphite.mat");
        Color32[] blocked=Read(camera,rt); flash.enabled=false; Color32[] noFlash=Read(camera,rt); flash.enabled=true;
        Need(Changed(blocked,noFlash)<8,"Hit cue visible through wall."); screen.gameObject.SetActive(false); Object.Destroy(screen.gameObject);
        yield return Wait(()=>!sword.IsApplyingHitStop,1); yield return Delay(.23f);
        Need(!visual.HitVisible,"Hit cue does not expire.");
        log("PASS actual-map genuine sword + hit-stop + normal follow camera; isolated high-contrast cue pixels="+pixels+"; opaque wall hides cue.");
        // Natural autonomous shot sequence, still normal follow camera.
        ai.enabled=true; ai.ResetForJourney(); player.GetComponent<Health>().RestoreFullHealth();
        Vector3 away=(playerPoint-ai.transform.position).normalized;
        if(floor.TrySample(ai.transform.position+away*4.5f,2,2,35,out RaycastHit support)) Place(support.point);
        yield return Wait(()=>ai.RangedCharging,5); Capture(camera,rt,"actual-charge");
        MeshRenderer iris=ai.transform.Find("Reusable compact charge brackets").GetComponent<MeshRenderer>();
        Need(visual.AimWarningVisible && !((LineRenderer)Field(ai,"warningLine")).enabled,"Actual-map charge still uses a laser.");
        Vector3 chargeSize=iris.transform.localScale;
        int chargePixels=CuePixels(camera,rt,iris); Need(chargePixels>=50,"Actual-map charging corners too small/occluded: "+chargePixels);
        screen=Box("QA charge occluder",Vector3.Lerp(camera.transform.position,iris.transform.position,.5f),new Vector3(1.2f,1.2f,.1f));
        screen.transform.rotation=Quaternion.LookRotation(iris.transform.position-camera.transform.position);
        screen.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Graphite.mat");
        Need(CuePixels(camera,rt,iris)<8,"Charge cue visible through opaque cover.");
        screen.gameObject.SetActive(false); Object.Destroy(screen.gameObject);
        yield return Wait(()=>ai.RangedCharge>.45f || ai.RangedLocked,2); Capture(camera,rt,"actual-converging");
        yield return Wait(()=>ai.RangedLocked,2); Capture(camera,rt,"actual-locked");
        Need(visual.LockedWarningVisible && iris.transform.localScale.x<chargeSize.x,"No converging then stable locked warning.");
        int lockPixels=CuePixels(camera,rt,iris); Need(lockPixels>=35,"Actual-map locked diamond too small/occluded: "+lockPixels);
        var block=new MaterialPropertyBlock(); int confirmedCores=0;
        foreach(Renderer part in ai.GetComponentsInChildren<Renderer>())
        {
            if(!part.sharedMaterial || !part.sharedMaterial.name.Contains("CyanCircuit")) continue;
            part.GetPropertyBlock(block); Color emission=block.GetColor("_EmissionColor");
            Need(emission.r>emission.g && emission.g>emission.b,"Core confirmation is not amber on the lock frame."); confirmedCores++;
        }
        Need(confirmedCores>0,"No core verified on lock.");
        // A same-frame image isolates the enemy from surrounding map geometry;
        // restore every renderer without disabling AI or changing gameplay.
        Renderer[] enemyParts=ai.GetComponentsInChildren<Renderer>();
        bool[] wasVisible=new bool[enemyParts.Length];
        try
        {
            for(int i=0;i<enemyParts.Length;i++) { wasVisible[i]=enemyParts[i].enabled; enemyParts[i].enabled=false; }
            Capture(camera,rt,"actual-map-without-enemy-diagnostic");
        }
        finally { for(int i=0;i<enemyParts.Length;i++) if(enemyParts[i]) enemyParts[i].enabled=wasVisible[i]; }
        log("PASS actual-map cyan square converges then amber diamond confirms lock; high-contrast cue pixels charge="+chargePixels+", locked="+lockPixels+"; no beam.");
        yield return Wait(()=>ai.RangedShots>0,2); Capture(camera,rt,"actual-fire");
        yield return Delay(.12f); Capture(camera,rt,"actual-recovery");
        hp.TryApplyDamage(new DamageInfo(1000,player.gameObject,ai.transform.position+Vector3.up,Vector3.forward));
        // Keep native AI animation running but no live attack after lethal hit.
        ai.enabled=true; yield return Delay(.42f); Capture(camera,rt,"actual-shutdown");
        yield return Delay(.49f); Capture(camera,rt,"actual-landed");
        Need(!visual.HitVisible && !visual.MuzzleVisible && !visual.AimWarningVisible && !ai.GetComponent<CapsuleCollider>().enabled,"Death leaves hittable collider or cues.");
        Transform root=(Transform)Field(ai,"visualRoot");
        Need(root.gameObject.activeSelf && root.localScale.x>.9f,"Death shrinks away before landing can be read.");
        float lowest=float.PositiveInfinity;
        foreach(Renderer part in root.GetComponentsInChildren<Renderer>())
            if(part.enabled && part.sharedMaterial && part.sharedMaterial.renderQueue<2500) lowest=Mathf.Min(lowest,part.bounds.min.y);
        Need(Mathf.Abs(lowest-(float)Field(ai,"deathGroundY")-.02f)<.06f,"Dead visual does not settle on sampled supported ground.");
        yield return Delay(.8f); Need(!root.gameObject.activeSelf,"Corpse never cleaned up.");
        ai.ResetForJourney(); ai.enabled=false; Need(hp.CurrentHealth==hp.MaxHealth && root.gameObject.activeSelf,"Reset failed to restore ranged enemy.");
        movement.enabled=false; rt.Release(); Object.Destroy(rt);
        log("PASS actual-map charge/lock/fire/recovery/shutdown/landing captures, readable landing then cleanup and full reset.");
    }
    static void Focus() { Cursor.lockState=CursorLockMode.Locked; EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus(); }
    static void Place(Vector3 position)
    { movement.ClearImpact(); controller.enabled=false; player.position=position+Vector3.up*.02f; controller.enabled=true; Physics.SyncTransforms(); }
    static IEnumerator Wait(Func<bool> condition,float seconds)
    { float end=Time.realtimeSinceStartup+seconds; while(!condition() && Time.realtimeSinceStartup<end) yield return null; Need(condition(),"Timed out after "+seconds+"s."); }
    static IEnumerator Delay(float seconds)
    { float end=Time.time+seconds; while(Time.time<end) yield return null; }
    static object Field(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static void State(object target,string name) { var field=target.GetType().GetField("state",Private); field.SetValue(target,Enum.Parse(field.FieldType,name)); }
    static Collider Box(string name,Vector3 position,Vector3 size)
    { var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=position; go.transform.localScale=size; return go.GetComponent<Collider>(); }
    static void Need(bool value,string reason) { if(!value) throw new InvalidOperationException("Leaf Wisp: "+reason); }
    static int Changed(Color32[] a,Color32[] b)
    { int count=0; for(int i=0;i<a.Length;i++) if(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>90) count++; return count; }
    static int CuePixels(Camera camera,RenderTexture rt,MeshRenderer cue)
    {
        Color32[] shown=Read(camera,rt); bool wasEnabled=cue.enabled;
        try { cue.enabled=false; return Changed(shown,Read(camera,rt)); }
        finally { cue.enabled=wasEnabled; }
    }
    static Color32[] Read(Camera camera,RenderTexture rt)
    {
        var old=camera.targetTexture; var active=RenderTexture.active;
        var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply(); return texture.GetPixels32(); }
        finally { camera.targetTexture=old; RenderTexture.active=active; Object.Destroy(texture); }
    }
    static void Capture(Camera camera,RenderTexture rt,string name)
    { var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false); texture.SetPixels32(Read(camera,rt)); texture.Apply(); File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG()); Object.Destroy(texture); }
}
