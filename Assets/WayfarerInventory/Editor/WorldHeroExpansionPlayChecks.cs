using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroExpansionPlayChecks
    {
        const string Folder="tmp/inventory/hero-expansion";
        static string report;
        static IEnumerator steps;static double next;static Keyboard testKeyboard;static InputSettings savedInputSettings;
        public static void Run(bool waterOnly=false)
        {
            if(!Application.isPlaying)throw new Exception("Enter Play Mode first");
            savedInputSettings=InputSystem.settings;
            InputSystem.settings=Object.Instantiate(savedInputSettings);
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            report=Folder+(waterOnly?"/water-tests.txt":"/live-tests.txt");
            Directory.CreateDirectory(Folder);File.WriteAllText(report,"Running\n");
            steps=Checks(waterOnly);next=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(!Application.isPlaying){Finish();return;}
            if(EditorApplication.timeSinceStartup<next)return;
            try{if(steps.MoveNext()){next=EditorApplication.timeSinceStartup+(float)steps.Current;return;}Note("ALL PASS");Finish();}
            catch(Exception e){Note("FAIL "+e);Debug.LogException(e);Finish();}
        }
        static void Finish(){EditorApplication.update-=Tick;if(testKeyboard!=null){InputSystem.RemoveDevice(testKeyboard);testKeyboard=null;}if(savedInputSettings){var temporary=InputSystem.settings;InputSystem.settings=savedInputSettings;savedInputSettings=null;Object.DestroyImmediate(temporary);}}
        static void Note(string text)=>File.AppendAllText(report,text+"\n");
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);Note("PASS "+text);}
        static void GroundContact(Transform player,string name)
        {
            float floorY=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(player.position+Vector3.up*.5f,Vector3.down,2f,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(player)&&hit.normal.y>.5f)floorY=Mathf.Max(floorY,hit.point.y);
            if(float.IsNegativeInfinity(floorY))throw new Exception("Ground missing");
            float min=float.PositiveInfinity;var mesh=new Mesh();
            foreach(var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!skin.enabled)continue;skin.BakeMesh(mesh);
                foreach(var v in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(v).y-floorY);
            }
            Object.DestroyImmediate(mesh);Note(name+" body clearance "+min.ToString("F4")+"m");Check(min>=-.015f,name+" body does not penetrate ground beyond 1.5cm tolerance");
        }
        static void Capture(string name,Transform player)
        {
            var go=new GameObject("Temporary world test camera");var cam=go.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
            cam.fieldOfView=40;cam.nearClipPlane=.05f;
            cam.transform.position=player.position+player.forward*3.7f+player.right*2+Vector3.up*1.5f;
            if(name.StartsWith("water-"))cam.transform.position=player.position+player.forward*2f+Vector3.up*4f;
            cam.transform.LookAt(player.position+Vector3.up*.85f);
            var rt=new RenderTexture(1200,900,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
            File.WriteAllBytes(Folder+"/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);
        }
        static IEnumerator Checks(bool waterOnly)
        {
            var f=Object.FindFirstObjectByType<BaselineFlow>();var w=f.GetComponent<BaselineWorld>();var p=w.player;
            f.GetComponent<InventoryLab>().Close();f.SetInventoryOpen(false);f.saveKeyPrefix="WorldHero.Expansion.Validation.20261025";f.NewGame();f.Tutorial.Cancel();
            var title=Object.FindFirstObjectByType<CloudGateTitleScreen>();if(title)title.Hide();yield return 1f;
            Cursor.lockState=CursorLockMode.Locked;
            var movement=p.GetComponent<PlayerMovement>();var actions=p.GetComponent<WorldHeroActions>();var combat=p.GetComponent<PlayerCombat>();var eq=p.GetComponent<PlayerEquipment>();var hp=p.GetComponent<Health>();var vitals=p.GetComponent<PlayerVitals>();var animator=p.GetComponentInChildren<Animator>();
            WorldHeroExpansionChecks.Run();Check(movement.IsStableGrounded,"Testing on actual overworld ground");
            var groundOrigin=p.position;
            if(!waterOnly){
            GameSession.Instance.GrantSword();yield return .3f;
            Check(combat.TryAttack(),"Original first attack remains immediate");yield return 1.2f;
            for(int i=0;i<30&&combat.IsAttacking;i++)yield return .1f;
            Check(actions.TryDraw(null),"Independent draw presentation starts");yield return .25f;
            Check(actions.CurrentAction=="DrawSword","Draw presentation plays independently of combo");
            yield return actions.Duration("DrawSword")+.1f;
            for(int i=0;i<100&&actions.Busy;i++)yield return .1f;
            Check(eq.SwordDrawn&&!combat.IsAttacking,"Draw does not change original attack flow");yield return .2f;
            Check(actions.TrySheathe(),"Sheathing starts");yield return actions.Duration("SheatheSword")+.2f;
            for(int i=0;i<100&&actions.Busy;i++)yield return .1f;
            Check(!eq.SwordDrawn&&!combat.IsAttacking&&!movement.IsGameplayInputLocked,"Sheathing completes and releases input");
            eq.SetWorldSwordDrawn(true);
            foreach(var name in new[]{"OpenChest","Pickup","Touch","Talk","HeavyLand"})
            {
                int committed=0;Check(actions.TryPlay(name,()=>committed++),name+" starts");yield return .2f;
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName(name),name+" plays on new rig");
                Check(!eq.PracticeSword.activeSelf,name+" hides held sword during interaction");
                GroundContact(p,name);Capture("live-"+name,p);
                yield return actions.Duration(name)+.2f;
                for(int i=0;i<30&&actions.Busy;i++)yield return .1f;
                Check(committed==1&&!movement.IsGameplayInputLocked,name+" commits once and unlocks");
                Check(eq.SwordDrawn&&eq.PracticeSword.activeSelf,name+" restores original held weapon");
            }
            int rest=0;Check(actions.TryRest(()=>rest++),"Rest sequence starts");yield return actions.Duration("RestBegin")+.4f;
            for(int i=0;i<100&&actions.CurrentAction=="RestBegin";i++)yield return .1f;
            Check(actions.CurrentAction=="RestLoop","Sitting loop reached");GroundContact(p,"RestLoop");Capture("live-rest",p);
            yield return 1.3f+actions.Duration("RestEnd");
            for(int i=0;i<100&&actions.Busy;i++)yield return .1f;
            Check(rest==1&&!actions.Busy,"Rest stands up and commits once");
            int cancelled=0;Check(actions.TryPlay("Pickup",()=>cancelled++),"Cancelable pickup starts");actions.Cancel();yield return 1f;
            Check(cancelled==0&&!movement.IsGameplayInputLocked,"Cancelled interaction grants no reward");
            WorldHeroSurfaceAudit.Run();
            Check(hp.TryApplyDamage(new DamageInfo(hp.MaxHealth,null,p.position,Vector3.zero)),"Lethal ground damage accepted");yield return .5f;
            Check(actions.Dying&&!vitals.DeathOpen,"Death animation precedes death menu");GroundContact(p,"Death");Capture("live-death",p);
            yield return actions.Duration("Death")+.4f;
            for(int i=0;i<100&&!vitals.DeathOpen;i++)yield return .1f;
            Check(vitals.DeathOpen,"Death menu follows completed animation");
            }
            f.NewGame();f.Tutorial.Cancel();yield return 1f;
            GameSession.Instance.GrantSword();yield return .2f;
            var water=Object.FindFirstObjectByType<WaterVolume>();var bounds=water.GetComponent<Collider>().bounds;Vector3 point=default;bool found=false;
            for(int x=3;x<10&&!found;x++)for(int z=6;z<10&&!found;z++)
            {
                var probe=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x/12f),water.WaterSurfaceY+3,Mathf.Lerp(bounds.min.z,bounds.max.z,z/12f));
                if(!water.ContainsSurfaceXZ(probe)||!Physics.Raycast(probe,Vector3.down,out var bottom,12,~0,QueryTriggerInteraction.Ignore))continue;
                if(water.WaterSurfaceY-bottom.point.y<1.7f)continue;
                point=new Vector3(probe.x,water.WaterSurfaceY-1.05f,probe.z);found=true;
            }
            Check(found,"Located deep water inside actual lake footprint");Note("Lake test point "+point);
            movement.Relocate(point,Quaternion.identity);Physics.SyncTransforms();yield return 2f;
            Check(movement.SwimmingActive,"Actual lake enters swimming mode");
            Capture("water-tread",p);var head=animator.GetBoneTransform(HumanBodyBones.Head);
            Note("Tread head relative to surface: "+(head.position.y-water.WaterSurfaceY).ToString("F3"));
            Check(!actions.TryPlay("OpenChest"),"Ground interaction blocked while swimming");
            var before=p.position;testKeyboard=InputSystem.AddDevice<Keyboard>();Cursor.lockState=CursorLockMode.Locked;
            InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(Key.W));yield return 2f;
            for(int i=0;i<50&&(movement.SwimAmount<=.7f||Vector3.Distance(before,p.position)<=.2f);i++)
            {testKeyboard.MakeCurrent();InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(Key.W));yield return .1f;}
            Note("Swim displacement "+Vector3.Distance(before,p.position).ToString("F3")+", blend "+movement.SwimAmount.ToString("F3")+", input lock "+movement.IsGameplayInputLocked);
            Check(movement.SwimAmount>.7f&&Vector3.Distance(before,p.position)>.2f,"Swimming input moves player through actual water");
            Capture("water-swim",p);Note("Swim head relative to surface: "+(head.position.y-water.WaterSurfaceY).ToString("F3"));
            Check(head.position.y>water.WaterSurfaceY-.03f,"Swimming head remains at water surface");
            InputSystem.QueueStateEvent(testKeyboard,new KeyboardState());yield return .6f;
            movement.Relocate(groundOrigin,Quaternion.identity);yield return 1f;
            Check(combat.TryAttack(),"Original attack works after leaving water");yield return 1.3f;
            for(int i=0;i<30&&combat.IsAttacking;i++)yield return .1f;
            movement.Relocate(point,Quaternion.identity);yield return 2f;
            Check(hp.TryApplyDamage(new DamageInfo(hp.MaxHealth,null,p.position,Vector3.zero)),"Water death triggers");yield return .5f;
            Check(actions.CurrentAction=="Drown"&&!vitals.DeathOpen,"Drowning precedes death menu");Capture("water-drown",p);
            yield return actions.Duration("Drown")+.3f;
            for(int i=0;i<100&&!vitals.DeathOpen;i++)yield return .1f;
            Check(actions.CurrentAction=="Drowned"&&vitals.DeathOpen,"Drowned hold and death menu complete");
            f.NewGame();f.Tutorial.Cancel();yield return 1f;Check(!actions.Dying&&!actions.Busy,"Respawn clears death and action locks");
        }
    }
}
