using System;
using System.Reflection;
using UnityEngine;

public static class InterfaceBackChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static bool Back(object owner)
    {
        var method=owner.GetType().GetMethod("TryInterfaceBack");
        if(method==null)throw new Exception("FAIL: "+owner.GetType().Name+" has no contextual back action.");
        return (bool)method.Invoke(owner,null);
    }
    static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
    public static string SideAPause()
    {
        var root=new GameObject("Side A back regression");root.SetActive(false);float time=Time.timeScale;
        try {
            var manager=root.AddComponent<BattleManager>();
            typeof(BattleManager).GetField("helpOpen",Fields).SetValue(manager,true);
            typeof(BattleManager).GetField("manualPaused",Fields).SetValue(manager,true);
            typeof(BattleManager).GetField("isPaused",Fields).SetValue(manager,true);
            Check(Back(manager)&&!manager.HelpOpen&&manager.IsPaused,"help must return to pause without resuming combat");
            Check(Back(manager)&&!manager.IsPaused,"pause must return to combat");
            Check(!Back(manager),"active combat must not become an exit action");
            return "PASS: Side A help -> pause -> combat, one level per back action.";
        } finally {UnityEngine.Object.DestroyImmediate(root);Time.timeScale=time;}
    }
    public static string SideBTutorial()
    {
        var root=new GameObject("Side B tutorial back regression");root.SetActive(false);
        try {
            var tutorial=root.AddComponent<GameControlsTutorial>();
            var page=typeof(GameControlsTutorial).GetField("page",Fields);page.SetValue(tutorial,2);
            Check(Back(tutorial)&&(int)page.GetValue(tutorial)==1,"tutorial back must show its previous page");
            Check(Back(tutorial)&&(int)page.GetValue(tutorial)==0,"tutorial must return to first page");
            Check(!Back(tutorial),"first page must delegate to host return rather than starting the game");
            return "PASS: Side B tutorial returns one page and delegates only at root.";
        } finally {UnityEngine.Object.DestroyImmediate(root);}
    }
}
