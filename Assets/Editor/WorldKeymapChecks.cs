using System;
using System.Linq;
using UnityEngine;

public static class WorldKeymapChecks
{
    public static string Profile()
    {
        var t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("WorldKeymap")).FirstOrDefault(x=>x!=null);
        if(t==null)throw new Exception("FAIL: no editable shared keymap exists.");
        var p=Activator.CreateInstance(t);
        string Get(object map,string id)=>(string)t.GetMethod("GetBinding").Invoke(map,new object[]{id});
        bool Set(string id,string key){var args=new object[]{id,key,null};return (bool)t.GetMethod("TrySet").Invoke(p,args);}
        void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
        Check(Get(p,"Bag")=="B"&&Get(p,"ReturnWorld")=="F8","bag/return defaults overlap or are stale");
        Check(Get(p,"Lock")=="MouseMiddle","target lock must default to middle mouse");
        Check(!Set("Attack","W")&&Get(p,"Attack")=="MouseLeft","conflict must reject atomically");
        Check(!Set("Guard","LeftCtrl")&&!Set("Guard","Escape"),"fixed UI access keys must remain available");
        Check(!Set("Guard","UpArrow"),"fixed movement arrows cannot be claimed by combat");
        Check(!Set("Bag","MouseLeft"),"UI primary click must not become a bag toggle");
        Check(!Set("Bag","I")&&!Set("Forward","B"),"basic controls must be read-only");
        Check(!Set("Dodge","RightShift"),"left/right modifier aliases must conflict");
        Check(Set("Attack","MouseBack"),"mouse remapping must work");
        Check(Set("Guard","MouseLeft"),"freed combat keys must be reusable");
        var json=(string)t.GetMethod("ToJson").Invoke(p,null);var restored=Activator.CreateInstance(t);
        Check((bool)t.GetMethod("LoadJson").Invoke(restored,new object[]{json}),"saved profile did not load");
        Check(Get(restored,"Bag")=="B"&&Get(restored,"Forward")=="W"&&Get(restored,"Attack")=="MouseBack"&&Get(restored,"Guard")=="MouseLeft","reload lost changes");
        Check(!(bool)t.GetMethod("LoadJson").Invoke(restored,new object[]{"{bad"}),"corrupt data must be rejected");
        Check(Get(restored,"Attack")=="MouseBack","bad load must preserve last valid profile");
        var legacy=json.Replace("\"version\":2","\"version\":1").Replace("\"MouseMiddle\"","\"MouseRight\"");
        Check((bool)t.GetMethod("LoadJson").Invoke(restored,new object[]{legacy})&&Get(restored,"Lock")=="MouseMiddle"&&Get(restored,"Guard")=="MouseLeft","legacy default lock migration must retain custom combat bindings");
        WorldControlsPanel.ShowOverview();Check(!WorldControlsPanel.Editing,"controls must open on overview");WorldControlsPanel.BeginEditing();Check(WorldControlsPanel.TryBack()&&!WorldControlsPanel.Editing&&!WorldControlsPanel.TryBack(),"editor back must stop at overview");
        return "PASS: middle mouse default, legacy migration, two-page navigation, defaults, conflicts, reserved UI keys, modifier aliases, mouse bindings, persistence and corrupt-load safety.";
    }
}
