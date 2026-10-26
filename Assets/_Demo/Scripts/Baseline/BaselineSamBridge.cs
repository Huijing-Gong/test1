using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using SamProduction.BossArena;

// Opt-in host. Main roots remain alive; only the exact scene loaded by this visit is unloaded.
[DefaultExecutionOrder(400)]
public sealed class BaselineSamBridge : MonoBehaviour
{
    public const string ScenePath = "Assets/SAM/BossArena/Scenes/SAM_BossArena.unity";
    public BaselineFlow Owner { get; private set; }
    public ArenaSession Session { get; private set; }
    public bool IsAway { get; private set; }
    public bool Busy { get; private set; }
    public float DamageSnapshot { get; private set; }
    public string LastMessage { get; private set; }
    public bool LastResourcesPreserved { get; private set; }
    public int RetryInputCount { get; private set; }
    public int ReturnInputCount { get; private set; }
    public event Action<string> Observed;
    Scene host, ownedScene;
    BaselineSamOutcomeBinding binding;
    SummitArenaTransition transition;
    readonly List<GameObject> suspended = new List<GameObject>();
    bool visitWon, completionSavedAtEntry;
    float oldTime;
    int oldFps, oldVsync, oldCapture;
    bool oldBackground, oldCursorVisible;
    CursorLockMode oldCursor;
    Health health;
    PlayerVitals vitals;
    int oldHealth, oldAid;
    float oldStamina;
    AmbientMode ambientMode;
    Color ambientLight, sky, equator, ground, fogColor, subtractive;
    float ambientIntensity, reflectionIntensity, fogDensity, fogStart, fogEnd;
    Material skybox;
    Light sun;
    SphericalHarmonicsL2 ambientProbe;
    bool fog;
    FogMode fogMode;
    DefaultReflectionMode reflectionMode;
    int reflectionResolution;
    Texture customReflection;

    public void Initialize(BaselineFlow owner) { Owner = owner; }
    void Trace(string message)
    {
        Observed?.Invoke(message);
        Debug.Log("DOT_SAM_HOST " + message);
    }
    public bool TryEnter()
    {
        if (!Owner || !Owner.Started || Busy || IsAway || Owner.BlockWorldInput ||
            (SideWorldTravel.Instance && (SideWorldTravel.Instance.Busy || SideWorldTravel.Instance.IsAway))) return false;
        if (!Application.CanStreamedLevelBeLoaded(ScenePath))
        { LastMessage = "The final challenge is unavailable in this build."; return false; }
        if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
        { LastMessage = "A final challenge is already open."; return false; }
        if (!Owner.SaveNow())
        { LastMessage = "Could not save before the challenge. Please retry Save progress."; return false; }
        completionSavedAtEntry = Owner.Progress.bossDefeated && GameSession.Instance && GameSession.Instance.BossDefeated;
        var world = Owner.GetComponent<BaselineWorld>();
        var loadout = world.player.GetComponent<BaselineLoadout>();
        DamageSnapshot = 18 + (loadout ? loadout.DamageBonus : 0);
        health = world.player.GetComponent<Health>(); vitals = world.player.GetComponent<PlayerVitals>();
        oldHealth = health ? health.CurrentHealth : 0;
        oldStamina = vitals ? vitals.Stamina : 0; oldAid = vitals ? vitals.AidCharges : 0;
        host = Owner.gameObject.scene;
        CaptureEnvironment();
        suspended.Clear();
        foreach (var root in host.GetRootGameObjects())
            if (root.activeSelf && root != gameObject) suspended.Add(root);
        IsAway = Busy = true; visitWon = false; LastMessage = null; LastResourcesPreserved = false;
        Time.timeScale = 1; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        SceneManager.sceneLoaded += Loaded;
        Trace("enter saved; damage=" + DamageSnapshot + " hp=" + oldHealth + " stamina=" + oldStamina + " aid=" + oldAid);
        StartCoroutine(Load());
        return true;
    }
    IEnumerator Load()
    {
        // Pump the visit so a presentation failure rolls back the same owned visit.
        var visit=LoadVisit(); bool failed=false;
        while(true)
        {
            bool next=false; object current=null;
            try { next=visit.MoveNext(); if(next)current=visit.Current; }
            catch(Exception error) { LastMessage="The transfer could not finish: "+error.Message; Debug.LogException(error); failed=true; }
            if(failed || !next)break;
            yield return current;
        }
        if(failed)
        {
            ReleaseTransition();
            SceneManager.sceneLoaded-=Loaded;
            ReleaseBinding();
            if(ownedScene.IsValid() && ownedScene.isLoaded)yield return SceneManager.UnloadSceneAsync(ownedScene);
            Restore(false);
        }
    }
    IEnumerator LoadVisit()
    {
        transition=new SummitArenaTransition(this);transition.Begin();
        // Load in parallel with the camera flight; the incoming scene is visually
        // isolated by Loaded until its reveal. Do not stop the shot to begin loading.
        AsyncOperation operation = null;
        try { operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive); }
        catch (Exception error) { LastMessage = "Could not load the final challenge: " + error.Message; }
        if(operation==null)throw new InvalidOperationException(LastMessage??"Could not start arena loading.");
        bool departed=false;
        while(!departed || !operation.isDone)
        {
            departed=transition.AdvanceDeparture(PresentationDelta());
            yield return null;
        }
        SceneManager.sceneLoaded -= Loaded;
        // SceneLoaded establishes identity, damage and subscriptions before ArenaSession.Start.
        if(!Session || !Session.hud || !Session.hud.Ready)yield return null;
        if (Session && binding != null)
        {
            // Start has now reset the arena camera and constructed its HUD.
            if(!Session.hud || !Session.hud.Ready || !Session.player || !Session.boss || !Session.view)
                throw new InvalidOperationException("Arena initialization did not complete; transfer cancelled.");
            transition.PrepareArrival();
            var arenaEnvironment=transition.ArrivalEnvironment;
            foreach(var root in suspended)if(root)root.SetActive(false);
            // World OnDisable hooks restore their lighting; reinstate the loaded arena.
            arenaEnvironment.Apply();
            transition.BeginArrival();
            while(!transition.AdvanceArrival(PresentationDelta()))yield return null;
            ReleaseTransition();
            Session.HostPresentationLocked=false;
            Session.automaticTick = true; Session.view.automaticTick = true;
            Busy = false;
            var ui = Session.gameObject.AddComponent<BaselineSamUIPresentation>(); ui.Initialize(this, Session);
            Trace("ready; attempt=" + Session.AttemptId + " sword=" + Session.player.swordDamage + " hp=" + Session.player.fighter.Health);
        }
        else
        {
            if (string.IsNullOrEmpty(LastMessage)) LastMessage = "The final challenge could not initialize.";
            if (ownedScene.IsValid() && ownedScene.isLoaded) yield return SceneManager.UnloadSceneAsync(ownedScene);
            Restore(false);
        }
    }
    void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsAway || !Busy || mode != LoadSceneMode.Additive || scene.path != ScenePath) return;
        ownedScene = scene;
        SceneManager.SetActiveScene(scene);
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentsInChildren<ArenaSession>(true);
                foreach (var candidate in found)
                {
                    if (Session) throw new InvalidOperationException("More than one Sam session.");
                    Session = candidate;
                }
            }
            if (!Session || !Session.player || !Session.boss || !Session.view || !Session.player.fighter)
                throw new InvalidOperationException("Required Sam actor/camera references are missing.");
            // Set the visit's canonical spawn before Start/Restart(false). Enter and
            // every retry then keep the same centreline position opposite the Boss.
            Session.playerStart=new Vector3(Session.bossStart.x,Session.playerStart.y,Session.bossStart.z-12f);
            Session.HostPresentationLocked=true;
            Session.automaticTick = false; Session.view.automaticTick = false;
            transition.HideLoadedCamera(Session);
            Session.player.swordDamage = DamageSnapshot;
            if(Session.effects && Session.effects.blackSun) Session.effects.blackSun.reducedMotion=Owner.TitleReducedMotion;
            binding = new BaselineSamOutcomeBinding(Owner, Session, outcome =>
            {
                if (outcome.State == ArenaRoundState.Victory) visitWon = true;
                Trace("consumed " + outcome.State + " attempt=" + outcome.AttemptId + " pending=" + Owner.EndingOutcomes.PendingSave);
            });
            Session.HostRetry = TryRetry;
            Trace("bound before Start; scene=" + scene.handle + " visit=" + binding.Visit);
        }
        catch (Exception error)
        {
            if (Session) { Session.automaticTick = false; Session.enabled = false; }
            LastMessage = "Could not initialize the final challenge: " + error.Message;
            Trace(LastMessage);
            Session = null;
        }
    }
    public bool TryRetry()
    {
        var presentation = Session ? Session.GetComponent<BaselineSamUIPresentation>() : null;
        if (presentation && presentation.IntroActive) return false;
        if (Session && !Session.CanAcceptEngageOrRetry) return false;
        if (!IsAway || Busy || binding == null || !binding.TryRetry()) return false;
        RetryInputCount++;
        Trace("retry after consume; attempt=" + Session.AttemptId + " sword=" + Session.player.swordDamage);
        return true;
    }
    public bool TryReturn()
    {
        if (!IsAway || Busy || binding == null || !binding.TryReturn()) return false;
        ReturnInputCount++; Busy = true;
        Session.HostRetry = null;
        Trace("return after consume; scene=" + ownedScene.handle);
        StartCoroutine(Return());
        return true;
    }
    IEnumerator Return()
    {
        if (host.IsValid() && host.isLoaded) SceneManager.SetActiveScene(host);
        // Sam render callbacks and audio are removed by unload before restoring host globals.
        if (ownedScene.IsValid() && ownedScene.isLoaded) yield return SceneManager.UnloadSceneAsync(ownedScene);
        Restore(visitWon);
    }
    void ReleaseBinding()
    {
        var previous=binding;binding=null;
        if(previous!=null)try{previous.Dispose();}catch(Exception error){Debug.LogException(error);}
    }
    void ReleaseTransition()
    {
        var release=transition;transition=null;
        if(release!=null)try{release.Dispose();}catch(Exception error){Debug.LogException(error);}
    }
    static float PresentationDelta() => Application.isFocused && Time.timeScale>0 ? Mathf.Min(Time.unscaledDeltaTime,.05f) : 0;
    void Restore(bool won)
    {
        ReleaseTransition();ReleaseBinding();
        SceneManager.sceneLoaded -= Loaded;
        if (host.IsValid() && host.isLoaded) SceneManager.SetActiveScene(host);
        Session = null; binding = null; ownedScene = default;
        RestoreEnvironment();
        foreach (var root in suspended) if (root) root.SetActive(true);
        suspended.Clear();
        LastResourcesPreserved = (!health || health.CurrentHealth == oldHealth) &&
            (!vitals || (vitals.Stamina == oldStamina && vitals.AidCharges == oldAid));
        IsAway = Busy = false;
        if (!LastResourcesPreserved) Trace("host resource contract failed");
        Trace("restored; resources=" + LastResourcesPreserved + " fps=" + Application.targetFrameRate);
        if (Owner) Owner.PresentSamReturn(won, completionSavedAtEntry, LastMessage);
    }
    public bool TryInterfaceBack()
    {
        if(!IsAway)return false;
        if(Busy)return true;
        if(Session){
            var intro=Session.GetComponent<GuardianIntroDialogue>();
            if(intro&&intro.TryInterfaceBack())return true;
            if(Session.Active)return false;
        }
        return TryReturn();
    }
    void Update()
    {
        if (IsAway && !Busy && WorldInputBindings.Pressed("ReturnWorld")) TryReturn();
    }
    void OnGUI()
    {
        if (transition!=null || !IsAway || (Session && Session.hud && Session.hud.Ready && !Busy)) return;
        GUI.Label(new Rect(20, Screen.height - 33, 540, 25), Busy ? "Travelling..." : WorldInputBindings.Label("ReturnWorld")+"  Return to the tower · RMB from result");
    }
    void OnDestroy()
    {
        ReleaseTransition();
        SceneManager.sceneLoaded -= Loaded;
        if (Session) { Session.HostRetry = null; Session.automaticTick = false; }
        ReleaseBinding();
        if(Application.isPlaying && IsAway && host.IsValid() && host.isLoaded)
        {
            SceneManager.SetActiveScene(host);
            if(ownedScene.IsValid() && ownedScene.isLoaded)SceneManager.UnloadSceneAsync(ownedScene);
            RestoreEnvironment();
            foreach(var root in suspended)if(root)root.SetActive(true);
            suspended.Clear();
        }
    }
    void CaptureEnvironment()
    {
        oldTime = Time.timeScale; oldFps = Application.targetFrameRate; oldVsync = QualitySettings.vSyncCount;
        oldCapture = Time.captureFramerate; oldBackground = Application.runInBackground;
        oldCursor = Cursor.lockState; oldCursorVisible = Cursor.visible;
        ambientMode = RenderSettings.ambientMode; ambientLight = RenderSettings.ambientLight;
        sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor; ground = RenderSettings.ambientGroundColor;
        ambientIntensity = RenderSettings.ambientIntensity; ambientProbe = RenderSettings.ambientProbe;
        skybox = RenderSettings.skybox; sun = RenderSettings.sun; subtractive = RenderSettings.subtractiveShadowColor;
        fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor;
        fogDensity = RenderSettings.fogDensity; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
        reflectionIntensity = RenderSettings.reflectionIntensity; reflectionMode = RenderSettings.defaultReflectionMode;
        reflectionResolution = RenderSettings.defaultReflectionResolution; customReflection = RenderSettings.customReflectionTexture;
    }
    void RestoreEnvironment()
    {
        Time.timeScale = oldTime; Application.targetFrameRate = oldFps; QualitySettings.vSyncCount = oldVsync;
        Time.captureFramerate = oldCapture; Application.runInBackground = oldBackground;
        Cursor.lockState = oldCursor; Cursor.visible = oldCursorVisible;
        RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientLight;
        RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
        RenderSettings.ambientIntensity = ambientIntensity; RenderSettings.ambientProbe = ambientProbe;
        RenderSettings.skybox = skybox; RenderSettings.sun = sun; RenderSettings.subtractiveShadowColor = subtractive;
        RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.reflectionIntensity = reflectionIntensity; RenderSettings.defaultReflectionMode = reflectionMode;
        RenderSettings.defaultReflectionResolution = reflectionResolution; RenderSettings.customReflectionTexture = customReflection;
    }
}
