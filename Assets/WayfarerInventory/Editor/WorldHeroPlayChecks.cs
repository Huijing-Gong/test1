using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WayfarerInventory.Editor
{
    public static class WorldHeroPlayChecks
    {
        static IEnumerator steps;static double next;
        const string Report="tmp/inventory/world-hero-validation.txt";
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Enter Play Mode first");
            File.WriteAllText(Report,"Running\n");steps=Checks();next=0;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;
            try
            {
                if(steps.MoveNext()){next=EditorApplication.timeSinceStartup+(float)steps.Current;return;}
                EditorApplication.update-=Tick;File.AppendAllText(Report,"ALL PASS\n");
            }
            catch(Exception e){EditorApplication.update-=Tick;File.AppendAllText(Report,"FAIL: "+e+"\n");Debug.LogException(e);}
        }
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS: "+label+"\n");}
        static IEnumerator Checks()
        {
            var flow=Object.FindFirstObjectByType<BaselineFlow>();
            flow.GetComponent<InventoryLab>().Close();flow.SetInventoryOpen(false);
            flow.saveKeyPrefix="WorldHero.Validation.20261025";
            flow.NewGame();flow.Tutorial.Cancel();
            var title=Object.FindFirstObjectByType<CloudGateTitleScreen>();if(title)title.Hide();
            yield return 1f;
            var player=flow.GetComponent<BaselineWorld>().player;
            var movement=player.GetComponent<PlayerMovement>();var combat=player.GetComponent<PlayerCombat>();
            var actions=player.GetComponent<PlayerBattleActions>();var vitals=player.GetComponent<PlayerVitals>();
            var animator=player.GetComponentInChildren<Animator>();var inventory=flow.GetComponent<InventoryLab>();
            WorldHeroChecks.Run();Check(actions.Ready,"Sandbox controller and P09 avatar are bound");
            GameSession.Instance.GrantSword();Cursor.lockState=CursorLockMode.Locked;
            yield return .5f;
            Check(combat.TryAttack(),"First combo strike starts");yield return .15f;
            Check(combat.TryQueueAttack(),"Second strike buffers");yield return .55f;
            Check(combat.ComboIndex==1,"Second combo strike plays");yield return .08f;
            Check(combat.TryQueueAttack(),"Third strike buffers");yield return .55f;
            Check(combat.ComboIndex==2,"Third combo strike plays");yield return 1f;
            Check(!combat.IsAttacking&&!movement.IsGameplayInputLocked,"Combo releases movement lock");
            Check(combat.TryHeavyAttack(),"Source heavy attack starts");yield return .2f;
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("SwordHeavy"),"Heavy animation plays");yield return 1.1f;
            var directions=new[]{player.forward,-player.forward,player.right,-player.right};
            var states=new[]{"DodgeForward","DodgeBack","DodgeRight","DodgeLeft"};
            for(int i=0;i<4;i++)
            {
                var position=player.position;float stamina=vitals.Stamina;
                Check(actions.TryDodge(directions[i]),states[i]+" starts");yield return .24f;
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName(states[i]),states[i]+" animation");
                Check(Vector3.Distance(position,player.position)>.05f&&vitals.Stamina<stamina,states[i]+" moves and consumes stamina");
                yield return .8f;
            }
            vitals.Refill();
            var enemy=new GameObject("Hero validation target");enemy.SetActive(false);
            enemy.transform.position=player.position+player.forward*2;
            var health=enemy.AddComponent<Health>();var settings=new SerializedObject(health);
            settings.FindProperty("team").enumValueIndex=(int)HealthTeam.Enemy;settings.ApplyModifiedProperties();
            enemy.SetActive(true);
            Check(actions.TryDodge(player.right),"Perfect dodge starts");yield return .2f;
            int hp=player.GetComponent<Health>().CurrentHealth;
            Check(!player.GetComponent<Health>().TryApplyDamage(new DamageInfo(10,enemy,player.position,Vector3.back)),"Enemy hit evaded");
            Check(actions.FocusActive&&Mathf.Approximately(Time.timeScale,.32f)&&player.GetComponent<Health>().CurrentHealth==hp,"Perfect dodge activates link time without damage");
            yield return 1.2f;
            Check(actions.RushCount>0,"Automatic counter approach starts");
            Check(actions.ConfirmCounter(),"Counter attack starts");yield return .2f;
            Check(combat.IsFlurryAttacking,"Source counter animation sequence runs");
            yield return 1f;
            inventory.HandleShortcuts(false,true,false);yield return .3f;
            Check(inventory.IsOpen&&!actions.FocusActive&&!combat.IsAttacking&&Time.timeScale==0,"Character page cancels combat and pauses");
            inventory.HandleShortcuts(false,false,true);yield return .5f;
            Check(Time.timeScale==1&&!movement.IsGameplayInputLocked,"Esc restores normal time and movement");
            Object.Destroy(enemy);
            var camera=Object.FindFirstObjectByType<ThirdPersonCamera>();camera.ResetOrbit(player.eulerAngles.y+180,10);
            yield return .8f;
            ScreenCapture.CaptureScreenshot("tmp/inventory/world-hero.png");yield return .5f;
        }
    }
}
