using System;
using UnityEngine;

namespace WayfarerInventory.Editor
{
    public static class ControlsPageChecks
    {
        public static void Run()
        {
            var entry=typeof(InventoryLab).GetMethod("ShowControls");
            if(entry==null)throw new Exception("Controls page entry is missing");
            if(!Application.isPlaying)return;
            var ui=UnityEngine.Object.FindFirstObjectByType<InventoryLab>();
            ui.Close();entry.Invoke(ui,null);
            if(!ui.IsOpen)throw new Exception("Controls must own the shared modal lock");
            ui.HandleShortcuts(true,false,false);
            if(ui.ControlsOpen)throw new Exception("Bag must replace controls");
            entry.Invoke(ui,null);ui.HandleShortcuts(false,false,true);
            if(ui.IsOpen)throw new Exception("Esc must close controls");
            Debug.Log("PASS Controls opens, switches to bag, and closes with Esc");
        }
    }
}
