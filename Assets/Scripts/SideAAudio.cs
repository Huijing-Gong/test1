using System.Collections.Generic;
using UnityEngine;

// Sound selection/levels adapted from teammate branch ecb2a67, not its old
// combat implementation. Presentation only: never writes physics or input.
[DisallowMultipleComponent]
public sealed class SideAAudio : MonoBehaviour
{
    const string Root = "Audio/SideA/";
    static SideAAudio instance;
    static readonly string[] Names = {
        "BattleBgm", "VictoryPizzicato", "DefeatSteel", "BunnyJumpSoft", "DemonJump",
        "BunnyLand", "DemonLand", "BunnyStep", "DemonStep", "PunchSwing", "UppercutSwing",
        "CarrotSmack", "DemonAttack", "BunnyHurt", "DemonHurt", "GuardField", "Parry",
        "ShieldBreak", "Interface", "CountdownBeat", "CountdownGo"
    };
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    readonly AudioSource[] voices = new AudioSource[8];
    readonly bool[] pausedVoices = new bool[8];
    readonly Walker[] walkers = new Walker[2];
    AudioSource music, ui;
    BattleManager battle;
    bool paused, musicWanted;
    int cursor;
    public int LoadedClipCount => clips.Count;
    public int ExpectedClipCount => Names.Length;
    public bool MusicPlaying => music && music.isPlaying;
    public bool Paused => paused;
    public static bool Available => instance != null;
    public int CountCue(string name) => counts.TryGetValue(name, out int count) ? count : 0;
    bool Playable => battle && battle.IsBattleActive && !battle.IsPaused && !battle.IsGameOver && Time.timeScale > 0;

    sealed class Walker
    {
        public Transform actor;
        public Rigidbody body;
        public BattlePlayerController player;
        public EnemyMovement enemy;
        public BattleDamageReceiver damage;
        public BattleStockLives stocks;
        public Vector3 previous;
        public float distance;
        public bool ready, grounded, airborne;
    }

    void Awake()
    {
        instance = this;
        battle = GetComponent<BattleManager>();
        foreach (string name in Names)
        {
            var clip = Resources.Load<AudioClip>(Root + name);
            if (clip) clips.Add(name, clip);
            else Debug.LogWarning("Side A audio missing: " + Root + name);
        }
        music = Source("Side A music", 100); music.loop = true; music.volume = .22f;
        ui = Source("Side A interface", 80);
        for (int i = 0; i < voices.Length; i++) voices[i] = Source("Side A effects " + i, 110);
    }
    AudioSource Source(string label, int priority)
    {
        var node = new GameObject(label); node.transform.SetParent(transform, false);
        var source = node.AddComponent<AudioSource>(); source.playOnAwake = false;
        source.spatialBlend = 0; source.priority = priority; return source;
    }
    void Start()
    {
        for (int i = 0; i < walkers.Length; i++)
        {
            var actor = GameObject.FindGameObjectWithTag(i == 0 ? "Player" : "Enemy");
            if (actor) walkers[i] = new Walker { actor = actor.transform, body = actor.GetComponent<Rigidbody>(),
                player = actor.GetComponent<BattlePlayerController>(), enemy = actor.GetComponent<EnemyMovement>(),
                damage = actor.GetComponent<BattleDamageReceiver>(), stocks = actor.GetComponent<BattleStockLives>() };
        }
    }
    void Update()
    {
        SetPaused(battle && battle.IsPaused);
        if (musicWanted && !paused && battle && !battle.IsGameOver && !music.isPlaying)
        {
            if (!music.clip && clips.TryGetValue("BattleBgm", out var bgm)) music.clip = bgm;
            if (music.clip && music.clip.loadState == AudioDataLoadState.Loaded) music.Play();
        }
        for (int i = 0; i < walkers.Length; i++) Track(walkers[i], i == 0);
    }
    void Track(Walker w, bool blue)
    {
        if (w == null || !w.actor || !w.body) return;
        bool live = battle && !battle.IsPaused && !battle.IsGameOver && !battle.IsAwaitingIntroduction &&
            Time.timeScale > 0 && !w.body.isKinematic && !(w.stocks && w.stocks.Respawning);
        if (!live) { w.ready = false; w.distance = 0; w.airborne = false; return; }
        bool grounded = w.player ? w.player.IsOnGround : w.enemy && w.enemy.IsOnGroundForAudio;
        Vector3 delta = w.actor.position - w.previous;
        w.previous = w.actor.position;
        if (!w.ready || delta.sqrMagnitude > 4)
        { w.ready = true; w.grounded = grounded; w.airborne = !grounded; w.distance = 0; return; }
        if (!grounded) w.airborne = true;
        else if (!w.grounded && w.airborne)
        { Play(blue ? "BunnyLand" : "DemonLand", blue ? .14f : .19f); w.airborne = false; }
        w.grounded = grounded;
        // No footsteps in midair, while hit-stunned, or while merely holding a
        // direction into a wall. Do not mistake stock teleportation for steps.
        bool moving = grounded && Playable && !(w.damage && w.damage.IsInHitstun) &&
            Mathf.Abs(w.body.linearVelocity.x) > .8f;
        if (!moving) { w.distance = 0; return; }
        w.distance += Mathf.Abs(delta.x);
        if (w.distance >= 1.8f)
        { w.distance %= 1.8f; Play(blue ? "BunnyStep" : "DemonStep", blue ? .09f : .12f); }
    }

    bool Play(string name, float volume, float pitch = 1f, bool interfaceCue = false)
    {
        if (!isActiveAndEnabled || !clips.TryGetValue(name, out var clip) || (!interfaceCue && paused)) return false;
        var voice = ui;
        if (!interfaceCue)
        {
            voice = null;
            foreach (var candidate in voices) if (!candidate.isPlaying) { voice = candidate; break; }
            if (!voice) voice = voices[cursor++ % voices.Length];
        }
        voice.Stop(); voice.loop = false; voice.clip = clip; voice.pitch = pitch; voice.volume = volume; voice.Play();
        counts[name] = CountCue(name) + 1;
        return true;
    }
    public void StartMusic()
    {
        musicWanted = true;
        if (clips.TryGetValue("BattleBgm", out var clip)) music.clip = clip;
    }
    public void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        if (paused) music.Pause(); else if (musicWanted) music.UnPause();
        for (int i = 0; i < voices.Length; i++)
            if (paused) { pausedVoices[i] = voices[i].isPlaying; if (pausedVoices[i]) voices[i].Pause(); }
            else { if (pausedVoices[i]) voices[i].UnPause(); pausedVoices[i] = false; }
    }
    public void Result(bool won)
    {
        musicWanted = false; music.Stop();
        foreach (var voice in voices) voice.Stop();
        System.Array.Clear(pausedVoices, 0, pausedVoices.Length);
        Play(won ? "VictoryPizzicato" : "DefeatSteel", won ? .55f : .5f, 1, true);
    }
    public void Countdown(bool go) => Play(go ? "CountdownGo" : "CountdownBeat", go ? .8f : .6f);
    public static void Interface() { if (instance) instance.Play("Interface", .22f, 1, true); }
    public static void Jump(bool enemy = false) { if (instance && instance.Playable) instance.Play(enemy ? "DemonJump" : "BunnyJumpSoft", enemy ? .42f : .48f); }
    public static bool Swing(BattleJMove move, int step, float variation=1f)
    {
        if (!instance || !instance.Playable) return false;
        return instance.Play(move == BattleJMove.Upper ? "UppercutSwing" : "PunchSwing",
            step == 3 ? .32f : .23f, (step == 3 ? .78f : 1f)*variation);
    }
    // Body weight is layered separately by J presentation; do not duplicate
    // the defender's hurt voice or the Parry already emitted by TakeHit.
    public static bool MeleeImpact(int step,float variation=1f) => instance && instance.Playable &&
        instance.Play("CarrotSmack",step==3?.28f:.18f,(step==3?.83f:1.08f-step*.035f)*variation);
    public static void EnemyAttack() { if (instance && instance.Playable) instance.Play("DemonAttack", .30f); }
    public static void Weapon() { if (instance && instance.Playable) instance.Play("CarrotSmack", .31f); }
    public static void Hit(bool enemy) { if (instance && instance.Playable) instance.Play(enemy ? "DemonHurt" : "BunnyHurt", enemy ? .45f : .40f); }
    public static void StockLost(bool enemy) { if (instance && instance.Playable) instance.Play(enemy ? "DemonHurt" : "BunnyHurt", .28f, .8f); }
    public static bool GuardRaise() => instance && instance.Playable && instance.Play("GuardField", .23f);
    public static bool Parry() => instance && instance.Playable && instance.Play("Parry", .40f);
    public static bool CounterFail() => instance && instance.Playable && instance.Play("ShieldBreak", .24f);

    void OnDisable()
    {
        musicWanted = false;
        if (music) music.Stop(); if (ui) ui.Stop();
        foreach (var voice in voices) if (voice) voice.Stop();
    }
    void OnDestroy() { if (instance == this) instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
}
