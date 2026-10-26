using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WayfarerInventory.Editor
{
    public static class InventoryChecks
    {
        [MenuItem("Tools/Wayfarer Inventory/Run behavior checks")]
        public static void Run()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("WayfarerInventory.InventoryModel")).FirstOrDefault(t => t != null);
            if (type == null) throw new Exception("FAIL: InventoryModel missing.");
            dynamic model = Activator.CreateInstance(type);
            model.Grant("beacon", "Recall beacon", "Supplies", 2);
            model.Grant("beacon", "Recall beacon", "Supplies", 3);
            if ((int)model.Count("beacon") != 5) throw new Exception("Stacking lost quantity.");
            bool rejected = model.Use("beacon", new Func<bool>(() => false));
            if (rejected || (int)model.Count("beacon") != 5) throw new Exception("Rejected use consumed an item.");
            bool accepted = model.Use("beacon", new Func<bool>(() => true));
            if (!accepted || (int)model.Count("beacon") != 4) throw new Exception("Successful use must consume exactly one.");
            model.Grant("sword", "Practice sword", "Equipment", 1);
            if ((int)model.VisibleCount("Equipment") != 1) throw new Exception("Category leaks supplies.");
            if ((bool)model.Use("missing", new Func<bool>(() => true))) throw new Exception("Missing item was usable.");
            bool invalid = false;
            try { model.Grant("bad", "Bad", "Supplies", -1); } catch (ArgumentOutOfRangeException) { invalid = true; }
            if (!invalid) throw new Exception("Negative grant accepted.");
            if (!(bool)model.ToggleEquipment("sword")) throw new Exception("Owned equipment cannot equip.");
            if (!(bool)model.IsEquipped("sword")) throw new Exception("Equipped state was not retained.");
            if (!(bool)model.ToggleEquipment("sword") || (bool)model.IsEquipped("sword"))
                throw new Exception("Unequip failed.");
            if ((bool)model.ToggleEquipment("beacon") || (bool)model.ToggleEquipment("missing"))
                throw new Exception("Supplies or missing gear accepted as equipment.");
            if ((int)model.Count("sword") != 1) throw new Exception("Equip changed ownership quantity.");
            model.Grant("trialA", "Blade core", "Equipment", 1);
            model.ToggleEquipment("sword");
            if (!(bool)model.ToggleEquipment("trialA") || !(bool)model.IsEquipped("sword"))
                throw new Exception("Blade core must preserve the equipped sword.");
            if ((string)model.EquippedInSlot("Main hand") != "sword")
                throw new Exception("Slot lookup lost equipped item.");
            if ((int)model.AvailableInSlot("Main hand").Count != 1)
                throw new Exception("Main-hand filter must contain the sword only.");
            if ((int)model.AvailableInSlot("Necklace").Count != 1)
                throw new Exception("Necklace slot must contain the independent blade core.");
            Debug.Log("Inventory checks PASS: stacking, failed action rollback, consumption, filtering, missing item, invalid quantity.");
        }
    }
}
