using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace WayfarerInventory.Editor
{
    public static class WorldInventoryPlayChecks
    {
        static IEnumerator steps;static double next;
        const string Report="tmp/inventory/world-validation.txt";
        public const string SaveKey="InventoryIntegration.Validation.20261025";
        [MenuItem("Tools/Wayfarer Inventory/Validate main world in Play Mode")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Enter Play Mode first.");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,"Running\n");steps=Checks();next=0;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.5;
            try{if(steps.MoveNext())return;EditorApplication.update-=Tick;File.AppendAllText(Report,"ALL PASS\n");}
            catch(Exception e){EditorApplication.update-=Tick;File.AppendAllText(Report,"FAIL: "+e+"\n");Debug.LogException(e);}
        }
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Report,"PASS: "+label+"\n");}
        static IEnumerator Checks()
        {
            var f=Object.FindFirstObjectByType<BaselineFlow>();var v=f.GetComponent<InventoryLab>();
            Check(v && v.worldFlow==f,"Main scene binding");
            f.saveKeyPrefix=SaveKey;f.NewGame();f.Tutorial.Cancel();
            var title=Object.FindFirstObjectByType<CloudGateTitleScreen>();if(title)title.Hide();
            yield return null;yield return null;
            var m=(InventoryModel)typeof(InventoryLab).GetField("model",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
            Check(m.VisibleCount("All")==0,"New save starts empty");
            v.HandleShortcuts(true,false,false);yield return null;
            Check(v.IsOpen && f.InventoryOpen && Time.timeScale==0 && f.BlockWorldInput && f.WorldHudHidden,"Bag pauses and hides HUD");
            v.HandleShortcuts(true,false,false);yield return null;Check(v.IsOpen,"Repeated I stays open");
            v.HandleShortcuts(false,false,true);f.SendMessage("Update");
            Check(!v.IsOpen && !f.MenuOpen,"Esc closes without opening Journey");yield return null;
            Check(Time.timeScale>0 && !f.BlockWorldInput,"Closing restores gameplay");
            GameSession.Instance.GrantSword();yield return null;
            Check(m.Count("sword")==1,"Reward appears once in live bag");
            v.HandleShortcuts(false,true,false);yield return null;
            Check(f.InventoryOpen,"U opens character");v.HandleShortcuts(false,true,false);yield return null;
            Check(f.InventoryOpen,"Repeated U stays open");
            Check(f.TogglePracticeSword() && !GameSession.Instance.SwordEquipped,"Unequip writes game state");
            yield return null;Check(!m.IsEquipped("sword"),"Bag reflects unequip");
            v.HandleShortcuts(false,false,true);yield return null;
            Check(f.ContinueGame() && !GameSession.Instance.SwordEquipped,"Unequip survives save reload");yield return null;
            v.HandleShortcuts(false,true,false);yield return null;
            Check(f.TogglePracticeSword() && GameSession.Instance.SwordEquipped,"Re-equip writes game state");
            int before=f.Progress.recallBeaconsUsed;Check(!f.UseRecallBeacon() && f.Progress.recallBeaconsUsed==before,"No beacon cannot consume or teleport");
            ScreenCapture.CaptureScreenshot("tmp/inventory/main-character.png");yield return null;
            v.HandleShortcuts(false,false,true);yield return null;
            ScreenCapture.CaptureScreenshot("tmp/inventory/main-hud.png");
        }
    }
}
