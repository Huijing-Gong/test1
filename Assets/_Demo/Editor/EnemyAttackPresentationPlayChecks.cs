using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Actual AI runs on a supported test floor. No scene, prefab or player save is written.
public static class EnemyAttackPresentationPlayChecks
{
    static readonly Vector3 Origin=new Vector3(22000,100,22000);
    static readonly string Output=Path.Combine(Path.GetTempPath(),"enemy-attack-vfx-play");
    static readonly string[] Prefabs={
        "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab",
        "Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab",
        "Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab",
        "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab",
        "Assets/Art/Enemies/RepairWisp/Generated/RepairWisp.prefab",
        "Assets/Art/Enemies/GelBlob/Generated/GelBlob.prefab"
    };
    public static IEnumerator Tests(BaselineFlow flow,Transform player,Action<string> log)
    {
        EnemyAttackPresentationChecks.Compile();
        Directory.CreateDirectory(Output);
        // This disposable physics floor is outside the authored navigation bake.
        foreach(var nav in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None)) nav.enabled=false;
        foreach(var x in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) x.enabled=false;
        foreach(var x in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) x.enabled=false;
        foreach(var x in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) x.enabled=false;
        foreach(var x in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) x.enabled=false;
        foreach(var x in Object.FindObjectsByType<GelBlobAI>(FindObjectsSortMode.None)) x.enabled=false;
        player.GetComponent<PlayerMovement>().enabled=false;
        var controller=player.GetComponent<CharacterController>();
        var hp=player.GetComponent<Health>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name="QA attack effects floor"; floor.transform.position=Origin+Vector3.down*.2f; floor.transform.localScale=new Vector3(40,.4f,40);
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floorMaterial.SetColor("_BaseColor",new Color(.68f,.65f,.53f)); floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var layout=new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var surfaces=layout.FindProperty("navigationSurfaces"); surfaces.arraySize++;
        surfaces.GetArrayElementAtIndex(surfaces.arraySize-1).objectReferenceValue=floor.GetComponent<Collider>();
        layout.ApplyModifiedPropertiesWithoutUndo();
        var camera=Camera.main; var follow=camera?camera.GetComponent<ThirdPersonCamera>():null;
        if(follow) follow.enabled=false;
        for(int i=0;i<Prefabs.Length;i++)
        {
            if(SessionState.GetBool("QA.HealingStatusOnly",false) && i!=4) continue;
            if(SessionState.GetBool("QA.ChargeArrowOnly",false) && i!=3) continue;
            controller.enabled=false;
            player.SetPositionAndRotation(Origin+Vector3.forward*(i==0?1.55f:i==3?5f:3f),Quaternion.Euler(0,180,0));
            controller.enabled=true; hp.RestoreFullHealth(); Physics.SyncTransforms();
            if(camera) { camera.transform.position=Origin+new Vector3(5,3,-7); camera.transform.LookAt(Origin+Vector3.up*.6f); }
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[i]),Origin+Vector3.up*.025f,Quaternion.identity);
            GameObject ally=null,groupObject=null;
            if(i==4)
            {
                ally=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[2]),Origin+new Vector3(2.2f,.025f,0),Quaternion.identity);
                ally.GetComponent<DomainPrototypeEnemy>().enabled=false;
                groupObject=new GameObject("QA bounded repair group");
                var group=groupObject.AddComponent<RepairWispGroup>(); group.Configure(new[]{ally.GetComponent<Health>()});
                actor.GetComponent<RepairWispAI>().ConfigureGroup(group);
                ally.GetComponent<Health>().TryApplyDamage(new DamageInfo(20,player.gameObject,ally.transform.position,Vector3.right));
                group.Alert();
            }
            yield return null; yield return null;
            var vfx=actor.GetComponent<EnemyAttackPresentation>();
            Need(vfx,"Missing presentation on "+actor.name);
            bool observed=false; float until=Time.realtimeSinceStartup+16;
            while(Time.realtimeSinceStartup<until)
            {
                if(i==5 && actor.GetComponent<GelBlobAI>().Phase==GelBlobAI.State.Flight)
                {
                    controller.enabled=false; player.position=Origin+new Vector3(2,0,3); controller.enabled=true;
                }
                controller.Move(Vector3.down*.03f); hp.RestoreFullHealth();
                if(i==0) observed=vfx.ImpactVisible;
                if(i==1) observed=actor.GetComponent<DomainPrototypeEnemy>().RangedShots>0 && vfx.CueEvents>0;
                if(i==2) observed=actor.GetComponent<DomainPrototypeEnemy>().PulseActive && actor.GetComponent<DomainPrototypeEnemy>().PulseProgress>.3f;
                if(i==3) observed=SessionState.GetBool("QA.ChargeArrowOnly",false) ?
                    actor.GetComponentInChildren<RailBeetleVisual>().WarningArrow && actor.GetComponentInChildren<RailBeetleVisual>().WarningArrow.Visible : vfx.ActiveGhostCount==2;
                if(i==4) observed=actor.GetComponent<RepairWispAI>().BeamVisible;
                if(i==5) observed=actor.GetComponent<GelBlobAI>().LandingCount>0 && vfx.VisibleCueCount>0;
                if(observed) break;
                yield return null;
            }
            Need(observed,"Actual AI never produced attack VFX for "+actor.name+" cues="+vfx.CueEvents);
            if(i==3 && SessionState.GetBool("QA.ChargeArrowOnly",false))
            {
                yield return new WaitForSeconds(.25f);
                Capture(actor,Origin+Vector3.up,"08-ChargeArrow.png");
                yield return CheckChargeArrow(actor,floor,log);
            }
            if(i==4)
            {
                yield return new WaitForSeconds(.22f);
                var status=ally.GetComponent<HealingRecipientStatus>();
                Need(status && status.VisibleCount==4,"Healing volume and three plus signs missing on recipient.");
                Capture(ally,ally.transform.position+Vector3.up*.8f,"07-HealingRecipient.png");
            }
            vfx.SendMessage("LateUpdate");
            Need(vfx.VisibleCueCount<=8 && vfx.ActiveGhostCount<=2,"Unbounded effects on "+actor.name);
            Capture(actor,Origin+new Vector3(0,.65f,1.1f),(i+1).ToString("00")+"-"+actor.name.Replace("(Clone)","")+".png");
            if(i==2)
            {
                var prism=actor.GetComponent<DomainPrototypeEnemy>();
                Need(prism.AttackLine.positionCount==49,"Wave lost terrain/cover vertices.");
                Need(prism.AttackLine.sharedMaterial.shader.name=="Enemies/AttackEnergy","Wave shader not bound.");
            }
            if(i==4)
            {
                var healer=actor.GetComponent<RepairWispAI>();
                Need(healer.RepairLine.sharedMaterial.shader.name=="Enemies/AttackEnergy","Repair shader not bound.");
                actor.GetComponent<Health>().TryApplyDamage(new DamageInfo(1,player.gameObject,actor.transform.position,Vector3.right));
                Need(!healer.BeamVisible,"Interrupted repair still channels.");
                actor.GetComponent<RepairWispSupportVfx>().SendMessage("LateUpdate");
                Need(ally.GetComponent<HealingRecipientStatus>().VisibleCount==0,"Interrupted recipient status lingered.");
                float deadline=Time.realtimeSinceStartup+14;
                while(healer.SuccessfulRepairs==0 && Time.realtimeSinceStartup<deadline) yield return null;
                Need(healer.SuccessfulRepairs>0,"Real healing never completed after interruption.");
                yield return new WaitForSeconds(.65f);
                Need(ally.GetComponent<HealingRecipientStatus>().VisibleCount==0,"Completed healing status lingered.");
                ally.GetComponent<Health>().TryApplyDamage(new DamageInfo(20,player.gameObject,ally.transform.position,Vector3.right));
                deadline=Time.realtimeSinceStartup+10;
                while(!healer.BeamVisible && Time.realtimeSinceStartup<deadline) yield return null;
                Need(healer.BeamVisible,"No second healing channel for death test.");
                yield return new WaitForSeconds(.15f);
                ally.GetComponent<Health>().TryApplyDamage(new DamageInfo(9999,player.gameObject,ally.transform.position,Vector3.right));
                Need(ally.GetComponent<HealingRecipientStatus>().VisibleCount==0,"Dead recipient retained healing status.");
                log("PASS recipient: volume + three plus signs, interruption, completion fade and death cleanup.");
            }
            vfx.Clear();
            Need(vfx.VisibleCueCount==0 && vfx.ActiveGhostCount==0,"Clear left attack cues.");
            actor.SetActive(false);
            Need(vfx.VisibleCueCount==0 && vfx.ActiveGhostCount==0,"Disable left attack cues.");
            Object.Destroy(actor); if(ally) Object.Destroy(ally); if(groupObject) Object.Destroy(groupObject);
            yield return null;
            log("PASS "+(i+1)+"/6 real AI attack, bounded cues, clear/disable; screenshot "+Output);
        }
        Object.Destroy(floor); Object.Destroy(floorMaterial);
        bool healingOnly=SessionState.GetBool("QA.HealingStatusOnly",false);
        bool arrowOnly=SessionState.GetBool("QA.ChargeArrowOnly",false);
        SessionState.EraseBool("QA.HealingStatusOnly");
        SessionState.EraseBool("QA.ChargeArrowOnly");
        log(arrowOnly?"PASS bounded charge arrow; Unity render captured.":healingOnly?"PASS actual healing recipient status; Unity render captured.":"PASS six attack/support presentations; captures are Unity renders, not generated art.");
    }
    static void Need(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    static IEnumerator CheckChargeArrow(GameObject actor,GameObject floor,Action<string> log)
    {
        var ai=actor.GetComponent<RailBeetleAI>(); var visual=actor.GetComponentInChildren<RailBeetleVisual>();
        var arrow=visual.WarningArrow;
        Need(arrow && arrow.Visible,"Real windup did not show arrow.");
        float until=Time.realtimeSinceStartup+3;
        while(ai.CurrentState==RailBeetleAI.BehaviourState.Windup && Time.realtimeSinceStartup<until) yield return null;
        Need(!arrow.Visible,"Arrow remained visible after charge started.");
        ai.enabled=false;
        var player=Object.FindFirstObjectByType<PlayerVitals>();
        var controller=player.GetComponent<CharacterController>();
        controller.enabled=false; player.transform.position=Origin+Vector3.left*12; controller.enabled=true; Physics.SyncTransforms();
        var type=typeof(RailBeetleAI); const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        Vector3 start=actor.GetComponent<Rigidbody>().position;
        type.GetField("chargeHeading",flags).SetValue(ai,Vector3.forward);
        foreach(float distance in new[]{16f,3.35f,1.2f})
        {
            type.GetField("activeChargeDistance",flags).SetValue(ai,distance);
            type.GetMethod("BuildWarningPath",flags).Invoke(ai,null);
            var path=(Vector3[])type.GetField("warningPath",flags).GetValue(ai);
            int count=(int)type.GetField("warningPathCount",flags).GetValue(ai);
            visual.SetWarningPath(path,count);
            Need(arrow.Visible && arrow.Length<=distance+.005f,"Arrow exceeded charge budget "+distance);
            if(distance==16f)
            {
                Need(Mathf.Abs(arrow.Length-16f)<.01f,"Open-ground warning did not match full 16m path.");
                Capture(actor,Origin+Vector3.up,"09-ChargeArrow-FullRange.png");
                float phase=arrow.AnimationPhase;
                var endpoint=arrow.Endpoint; int vertexCount=arrow.WarningMesh.vertexCount;
                for(int frame=0;frame<15;frame++)
                {
                    visual.SetWarningPath(path,count);
                    yield return new WaitForSeconds(.065f);
                    Capture(actor,Origin+Vector3.up,"ChargeFlow-"+frame.ToString("00")+".png");
                }
                Need(arrow.AnimationPhase>phase+.1f,"Arrow animation did not advance.");
                Need(arrow.Endpoint==endpoint && arrow.WarningMesh.vertexCount==vertexCount,"Animation changed warning bounds.");
                float scale=Time.timeScale; Time.timeScale=0;
                yield return null; yield return null;
                phase=arrow.AnimationPhase;
                yield return new WaitForSecondsRealtime(.12f);
                Need(Mathf.Abs(arrow.AnimationPhase-phase)<.001f,"Paused warning kept animating.");
                Time.timeScale=scale;
                log("PASS dynamic shader: animation advances within fixed geometry and freezes with gameplay time; 15 Unity frames captured.");
            }
            foreach(var vertex in arrow.WarningMesh.vertices)
                Need(Vector3.Dot(vertex-start,Vector3.forward)<=distance+.005f,"Arrow tip or border overshot range.");
        }
        type.GetField("activeChargeDistance",flags).SetValue(ai,16f);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position=start+new Vector3(0,1.5f,4); wall.transform.localScale=new Vector3(6,3,.3f);
        Physics.SyncTransforms();
        type.GetMethod("BuildWarningPath",flags).Invoke(ai,null);
        var points=(Vector3[])type.GetField("warningPath",flags).GetValue(ai);
        int n=(int)type.GetField("warningPathCount",flags).GetValue(ai);
        visual.SetWarningPath(points,n);
        Need(arrow.Visible && arrow.Endpoint.z<wall.GetComponent<Collider>().bounds.min.z,"Arrow crossed a wall.");
        Object.Destroy(wall); yield return null;
        floor.transform.localScale=new Vector3(40,.4f,12); Physics.SyncTransforms();
        type.GetMethod("BuildWarningPath",flags).Invoke(ai,null);
        n=(int)type.GetField("warningPathCount",flags).GetValue(ai); visual.SetWarningPath(points,n);
        Need(arrow.Visible && arrow.Endpoint.z<Origin.z+6,"Arrow extended beyond supported cliff.");
        visual.PlayDeath(); Need(!arrow.Visible,"Death retained arrow.");
        visual.ResetVisual(); Need(!arrow.Visible,"Reset retained arrow.");
        floor.transform.localScale=new Vector3(40,.4f,40); Physics.SyncTransforms();
        log("PASS arrow: actual windup, charge-start clear, 16/3.35/1.2m distance caps, all vertices bounded, wall/cliff truncation, death/reset.");
    }
    static void Capture(GameObject actor,Vector3 focus,string name)
    {
        var go=new GameObject("QA attack capture"); var c=go.AddComponent<Camera>();
        if(actor.GetComponent<RailBeetleAI>()) focus=actor.transform.position-actor.transform.forward*.8f+Vector3.up*.5f;
        c.transform.position=focus+new Vector3(5,2.5f,3.2f); c.transform.LookAt(focus);
        c.fieldOfView=actor.GetComponent<DomainPrototypeEnemy>()?.EnemyKind==DomainPrototypeEnemy.Kind.RuinPrism ? 53 : 36;
        var warning=actor.GetComponentInChildren<RailBeetleVisual>()?.WarningArrow;
        if(warning && warning.Visible)
        {
            focus=Vector3.Lerp(actor.transform.position,warning.Endpoint,.45f);
            float scale=Mathf.Max(.38f,warning.Length/13f);
            c.transform.position=focus+new Vector3(10,11,-7)*scale; c.transform.LookAt(focus); c.fieldOfView=53;
        }
        c.clearFlags=CameraClearFlags.SolidColor; c.backgroundColor=new Color(.12f,.18f,.23f);
        c.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var gameplay=Camera.main;
        Vector3 savedPosition=gameplay ? gameplay.transform.position : Vector3.zero;
        Quaternion savedRotation=gameplay ? gameplay.transform.rotation : Quaternion.identity;
        if(gameplay) gameplay.transform.SetPositionAndRotation(c.transform.position,c.transform.rotation);
        actor.GetComponent<GelBlobVisual>()?.SendMessage("LateUpdate");
        actor.GetComponent<LeafWispPresentation>()?.SendMessage("LateUpdate");
        actor.GetComponent<EnemyAttackPresentation>()?.SendMessage("LateUpdate");
        actor.GetComponent<RepairSupportHighlight>()?.SendMessage("LateUpdate");
        var target=new RenderTexture(1280,720,24); target.Create();
        RenderPipeline.SubmitRenderRequest(c,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
        var before=RenderTexture.active; RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());
        if(gameplay) gameplay.transform.SetPositionAndRotation(savedPosition,savedRotation);
        RenderTexture.active=before; target.Release(); Object.Destroy(target); Object.Destroy(image); Object.Destroy(go);
    }
}

