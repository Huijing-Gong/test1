using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Explicit disposable-project test. No real host, save slot, direct damage or rewards.
[InitializeOnLoad]
public static class SideALoadingChecks
{
    const string Key="SideALoadingChecks.Active";
    const string Output="LocalDocs/SideA/LoadingCream-20261025";
    static SideALoadingChecks(){EditorApplication.playModeStateChanged+=Changed;}
    public static void Run()
    {
        if(!Application.dataPath.StartsWith("/private/tmp/sidea-loading-20261025.",StringComparison.Ordinal))throw new Exception("Use isolated QA project.");
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"/validation.txt","Real Unity Play checks, controlled no-save host fixture.\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true);Application.runInBackground=true;
        EditorApplication.ExecuteMenuItem("Window/General/Game");EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            var host=new GameObject("Loading test host");var travel=host.AddComponent<SideWorldTravel>();
            travel.StartCoroutine(Tests(travel));
        }
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Pass",false)?0:1);}
    }
    static void Need(bool pass,string text)
    {
        File.AppendAllText(Output+"/validation.txt",(pass?"PASS ":"FAIL ")+text+"\n");
        Debug.Log("LOADING_QA "+(pass?"PASS ":"FAIL ")+text);
        if(!pass){EditorApplication.Exit(1);throw new Exception(text);}
    }
    static IEnumerator Tests(SideWorldTravel travel)
    {
        yield return null;
        float shownAt=Time.realtimeSinceStartup;
        var screen=SideALoadingScreen.Show();
        Need(screen==SideALoadingScreen.Show(),"repeated Show owns only one overlay");
        Need(screen.GetComponent<Canvas>().sortingOrder==32000,"loading sorts above menu and blocks pointer input");
        Need(screen.GetComponent<CanvasGroup>().blocksRaycasts,"opaque screen owns pointer raycasts");
        screen.SetProgress(.61f);screen.SetProgress(.1f);Need(screen.Progress==.61f&&screen.LitDiamonds==3,"real progress monotonic and three of five diamonds lit");
        Time.timeScale=0;
        yield return null;
        Need(SideALoadingScreen.IsVisible,"overlay works when game time is paused");
        yield return Capture("01_Cream_Progress.png");
        yield return screen.Finish();
        Need(Time.realtimeSinceStartup-shownAt>=SideALoadingScreen.MinimumDisplaySeconds,"cover remains visible at least one real-time second even while paused");
        Need(!SideALoadingScreen.IsVisible&&Time.timeScale==0,"unscaled completion cleans screen without stealing pause state");
        Time.timeScale=1;
        bool collision=Physics.GetIgnoreLayerCollision(6,7);
        travel.Enter(false);
        Need(travel.Busy&&SideALoadingScreen.IsVisible,"real additive entry shows cover before first IO frame");
        float deadline=Time.realtimeSinceStartup+90;
        while(travel.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
        Need(!travel.Busy&&travel.IsAway&&!SideALoadingScreen.IsVisible,"additive entry completes without leaking loading UI");
        Need(SceneManager.GetActiveScene().path==SideWorldTravel.BattlePath,"real Side A scene becomes active");
        var manager=Object.FindFirstObjectByType<BattleManager>();
        Need(manager&&manager.trialMenu.IsQuickGuideOpen,"entry still arrives at existing quick guide, not a running match");
        yield return Capture("02_After_Load_QuickGuide.png");
        manager.RestartBattle();
        Need(travel.Busy&&SideALoadingScreen.IsVisible,"retry covers before unloading previous arena");
        deadline=Time.realtimeSinceStartup+90;
        while(travel.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
        Need(!travel.Busy&&travel.IsAway&&!SideALoadingScreen.IsVisible,"retry recreates arena and removes cover");
        travel.ReturnToMain();
        deadline=Time.realtimeSinceStartup+30;
        while(travel.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
        Need(!travel.IsAway&&!SideALoadingScreen.IsVisible,"return restores host without loading UI");
        Need(Physics.GetIgnoreLayerCollision(6,7)==collision&&Time.timeScale==1,"host collision and time ownership restored");
        SideALoadingScreen.Reload(SideWorldTravel.BattlePath);
        Need(SideALoadingScreen.IsVisible,"standalone async reload shows the same cover");
        deadline=Time.realtimeSinceStartup+90;
        while(SideALoadingScreen.IsVisible&&Time.realtimeSinceStartup<deadline)yield return null;
        manager=Object.FindFirstObjectByType<BattleManager>();
        Need(!SideALoadingScreen.IsVisible&&manager&&manager.SelectionOpen,"standalone load arrives at normal selection and releases input");
        yield return null;
        manager.trialMenu.SelectTrial(2);
        shownAt=Time.realtimeSinceStartup;
        manager.BeginSelectedTrial();
        Need(SideALoadingScreen.IsVisible&&manager.SelectionOpen,"choosing a stage shows cream loading before starting its intro");
        yield return Capture("03_Selected_Stage_Cover.png");
        deadline=Time.realtimeSinceStartup+20;
        while(SideALoadingScreen.IsVisible&&Time.realtimeSinceStartup<deadline)
        {
            Need(manager.SelectionOpen,"countdown stays deferred behind the selection loading screen");
            yield return null;
        }
        yield return null;yield return null;
        Need(Time.realtimeSinceStartup-shownAt>=1&&!manager.SelectionOpen,"selected stage begins only after one-second cover and fade complete");
        Need(manager.GetComponent<BattleArenaStages>().ActiveIndex==2,"selected Eclipse stage is preserved through the loading transition");
        screen=SideALoadingScreen.Show();screen.Abort();yield return null;
        Need(!SideALoadingScreen.IsVisible,"failed-load cleanup immediately releases overlay");
        File.AppendAllText(Output+"/validation.txt","FINAL PASS\n");SessionState.SetBool(Key+"Pass",true);EditorApplication.ExitPlaymode();
    }
    static IEnumerator Capture(string filename)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)yield break;
        Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
        var texture=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Output+"/"+filename,texture.EncodeToPNG());Object.Destroy(texture);
    }
}
