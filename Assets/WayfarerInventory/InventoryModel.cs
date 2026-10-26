using System;
using System.Collections.Generic;
using System.Linq;

namespace WayfarerInventory
{
    public sealed class InventoryModel
    {
        public sealed class Entry
        {
            public string Id, Name, Category;
            public int Quantity;
            public bool Equipped;
        }
        readonly List<Entry> entries = new List<Entry>();
        public IEnumerable<Entry> Visible(string category) =>
            entries.Where(e => e.Quantity > 0 && (category == "All" || e.Category == category));
        public int VisibleCount(string category) => Visible(category).Count();
        public int Count(string id) => entries.FirstOrDefault(e => e.Id == id)?.Quantity ?? 0;
        public bool IsEquipped(string id) => entries.Any(e => e.Id == id && e.Quantity > 0 && e.Equipped);
        public static string SlotFor(string id)
        {
            switch(id)
            {
                case "sword": return "Main hand";
                case "trialA": return "Necklace";
                case "guard": return "Off hand";
                case "island": return "Relic";
                case "ridge": return "Charm";
                case "trialB": return "Sigil";
                default: return "";
            }
        }
        public List<Entry> AvailableInSlot(string slot) =>
            entries.Where(e => e.Quantity>0 && e.Category=="Equipment" && SlotFor(e.Id)==slot).ToList();
        public string EquippedInSlot(string slot) =>
            entries.FirstOrDefault(e => e.Quantity>0 && e.Equipped && SlotFor(e.Id)==slot)?.Id ?? "";
        public bool ToggleEquipment(string id)
        {
            var entry = entries.FirstOrDefault(e => e.Id == id && e.Quantity > 0 && e.Category == "Equipment");
            if (entry == null) return false;
            if(!entry.Equipped)
                foreach(var other in entries.Where(e => SlotFor(e.Id)==SlotFor(id))) other.Equipped=false;
            entry.Equipped = !entry.Equipped;
            return true;
        }
        public void SetOwned(string id,string name,string category,int quantity,bool equipped)
        {
            var entry=entries.FirstOrDefault(e=>e.Id==id);
            if(entry==null){entry=new Entry{Id=id};entries.Add(entry);}
            entry.Name=name;entry.Category=category;entry.Quantity=Math.Max(0,quantity);
            entry.Equipped=entry.Quantity>0 && equipped;
        }
        public void Grant(string id, string name, string category, int amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null) entries.Add(new Entry { Id = id, Name = name, Category = category, Quantity = amount });
            else entry.Quantity = checked(entry.Quantity + amount);
        }
        public bool Use(string id, Func<bool> action)
        {
            var entry = entries.FirstOrDefault(e => e.Id == id && e.Quantity > 0);
            if (entry == null || action == null || !action()) return false;
            entry.Quantity--;
            return true;
        }
    }
}
