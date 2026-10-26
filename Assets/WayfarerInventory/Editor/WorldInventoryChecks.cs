using System;
using UnityEditor;
using UnityEngine;
namespace WayfarerInventory.Editor
{
    public static class WorldInventoryChecks
    {
        [MenuItem("Tools/Wayfarer Inventory/Run world snapshot checks")]
        public static void Run()
        {
            var method=typeof(InventoryModel).GetMethod("SetOwned");
            if(method==null)throw new Exception("FAIL: live inventory cannot synchronize an authoritative save snapshot.");
            var m=new InventoryModel();
            Action<string,int,bool> sync=(id,n,e)=>method.Invoke(m,new object[]{id,id,id=="beacon"?"Supplies":"Equipment",n,e});
            sync("beacon",3,false);sync("beacon",3,false);
            if(m.Count("beacon")!=3)throw new Exception("Refresh duplicated supplies.");
            sync("beacon",0,false);if(m.VisibleCount("All")!=0)throw new Exception("Spent supplies still visible.");
            sync("sword",1,true);sync("trialA",1,true);
            if(!m.IsEquipped("sword")||!m.IsEquipped("trialA"))throw new Exception("Snapshot changed saved equipment.");
            if(InventoryModel.SlotFor("sword")==InventoryModel.SlotFor("trialA"))throw new Exception("Blade core must coexist with practice sword.");
            sync("sword",0,false);if(m.IsEquipped("sword")||m.Count("sword")!=0)throw new Exception("New game retained sword.");
            Debug.Log("World inventory snapshot checks PASS.");
        }
    }
}
