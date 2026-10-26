using System;
using UnityEngine;

public enum EnemySoundCue
{
    StoneScrape, StonePunch, StoneHit, StoneFall, CoreLock, DartFire, ShellHit,
    CorePowerDown, PrismCharge, PlateOpen, Pulse, BeetleCharge, BeetleRush,
    WallCrash, Stun, RepairPrepare, RepairLink, RepairSuccess, RepairBreak, RepairEmpty,
    GelCharge, GelJump, GelSplat, GelHit
}

// A fixed shared voice pool, not one looping source (or new GameObject) per enemy/hit.
// Four reserved warnings cannot be stolen by impact chatter. Six action voices are
// distance/priority-selected; all ten are fully 3D and have finite linear range.
public sealed class EnemyAudio : MonoBehaviour
{
    public const int WarningVoices = 4, ActionVoices = 6;
    static EnemyAudio instance;
    static readonly AudioClip[] clips = new AudioClip[Enum.GetNames(typeof(EnemySoundCue)).Length];
    static readonly string[] names = Enum.GetNames(typeof(EnemySoundCue));
    sealed class Voice
    {
        public AudioSource source;
        public EnemySoundEmitter owner;
        public EnemySoundCue cue;
        public int priority;
        public float gain, estimatedEnd;
        public bool paused;
        public Vector3 offset;
    }
    readonly Voice[] voices = new Voice[WarningVoices + ActionVoices];
    [SerializeField, Range(0f, 1f)] float masterVolume = 1f;
    AudioListener listener;
    float nextListenerSearch;
    public static int StartedCount { get; private set; }
    public static int RejectedCount { get; private set; }
    public static EnemySoundCue LastStartedCue { get; private set; }
    public static int PeakVoices { get; private set; }
    public static int ActiveVoices => instance ? instance.Count(null) : 0;
    public static int OwnerVoices(EnemySoundEmitter owner) => instance ? instance.Count(owner) : 0;
    public static int CountCue(EnemySoundCue cue)
    {
        if (!instance) return 0;
        int n = 0; foreach (Voice v in instance.voices) if (v.owner && v.cue == cue) n++;
        return n;
    }
    public static AudioClip Clip(EnemySoundCue cue)
    {
        int i = (int)cue;
        if (!clips[i]) clips[i] = Resources.Load<AudioClip>("EnemyAudio/" + names[i]);
        return clips[i];
    }
    public static bool IsWarning(EnemySoundCue cue) => cue == EnemySoundCue.StoneScrape ||
        cue == EnemySoundCue.CoreLock || cue == EnemySoundCue.PrismCharge || cue == EnemySoundCue.BeetleCharge || cue == EnemySoundCue.GelCharge;
    public static bool IsAfterDeath(EnemySoundCue cue) => cue == EnemySoundCue.CorePowerDown || cue == EnemySoundCue.StoneFall || cue == EnemySoundCue.GelSplat;
    static int Priority(EnemySoundCue cue) => IsWarning(cue) ? 0 :
        cue == EnemySoundCue.Pulse || cue == EnemySoundCue.DartFire || cue == EnemySoundCue.BeetleRush ? 1 :
        IsAfterDeath(cue) || cue == EnemySoundCue.WallCrash || cue == EnemySoundCue.Stun ||
        cue == EnemySoundCue.RepairSuccess || cue == EnemySoundCue.RepairBreak ? 2 : 3;

    public static bool Play(EnemySoundEmitter owner, EnemySoundCue cue, Vector3 position,
        float gain = 1f, float pitch = 1f, float warningSeconds = 0f)
    {
        if (!Application.isPlaying || !owner || !owner.CanSound || (owner.IsDead && !IsAfterDeath(cue))) return false;
        if (!instance)
        {
            var root = new GameObject("Shared enemy audio (10 bounded voices)");
            instance = root.AddComponent<EnemyAudio>(); DontDestroyOnLoad(root);
        }
        return instance.StartVoice(owner, cue, position, gain, pitch, warningSeconds);
    }
    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        for (int i = 0; i < voices.Length; i++)
        {
            var child = new GameObject(i < WarningVoices ? "Reserved warning " + i : "Shared action " + i);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = source.loop = false;
            source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f; source.reverbZoneMix = .15f;
            voices[i] = new Voice { source = source };
        }
    }
    bool StartVoice(EnemySoundEmitter owner, EnemySoundCue cue, Vector3 position, float gain, float pitch, float duration)
    {
        FindListener();
        AudioClip clip = Clip(cue);
        bool warning = IsWarning(cue);
        float far = warning ? 32f : cue == EnemySoundCue.WallCrash || cue == EnemySoundCue.Pulse ? 26f : 20f;
        if (!clip || !listener || AudioListener.pause ||
            Vector3.Distance(listener.transform.position, position) >= far)
        { RejectedCount++; return false; }
        int start = warning ? 0 : WarningVoices, end = warning ? WarningVoices : voices.Length;
        int priority = Priority(cue);
        float level = (warning ? .58f : .44f) * Mathf.Clamp(gain, 0f, 1.2f);
        float score = level * Attenuation(Vector3.Distance(listener.transform.position, position), warning ? 3f : 2f, far);
        Voice chosen = null;
        // At most one warning and one action per owner. Retriggers never pile up.
        for (int i = start; i < end; i++)
        {
            Voice v = voices[i];
            if (v.owner == owner)
            {
                if (v.priority < priority) { RejectedCount++; return false; }
                chosen = v; break;
            }
            if (!v.owner) chosen = v;
        }
        if (chosen == null)
        {
            float quietest = score;
            for (int i = start; i < end; i++)
            {
                Voice v = voices[i];
                if (v.priority < priority) continue;
                float existing = v.gain * Attenuation(Vector3.Distance(listener.transform.position,
                    v.source.transform.position), v.source.minDistance, v.source.maxDistance);
                if (v.priority > priority || existing < quietest * .8f)
                { chosen = v; quietest = existing; }
            }
        }
        if (chosen == null) { RejectedCount++; return false; }
        Stop(chosen);
        chosen.owner = owner; chosen.cue = cue; chosen.priority = priority;
        chosen.gain = level; chosen.offset = position - owner.transform.position;
        chosen.source.transform.position = position;
        chosen.source.minDistance = warning ? 3f : 2f; chosen.source.maxDistance = far;
        chosen.source.priority = warning ? 32 : 80 + priority * 24;
        // Warnings rise through the actual anticipation where possible, without loops.
        float basePitch = warning && duration > 0 ? Mathf.Clamp(clip.length / duration, .65f, 1.45f) : 1f;
        chosen.source.pitch = Mathf.Clamp(basePitch * pitch, .6f, 1.5f);
        chosen.source.volume = level;
        chosen.source.clip = clip; chosen.source.Play();
        chosen.estimatedEnd = Time.time + clip.length / chosen.source.pitch;
        LastStartedCue = cue; StartedCount++; PeakVoices = Mathf.Max(PeakVoices, Count(null));
        return true;
    }
    // Formula is also used to compare stealing candidates; Unity applies the same
    // finite linear rolloff to the actual fully spatialised sound, not a 2D shortcut.
    public static float Attenuation(float distance, float near, float far) =>
        Mathf.Clamp01((far - Mathf.Max(near, distance)) / Mathf.Max(.01f, far - near));
    void FindListener()
    {
        if (listener && listener.isActiveAndEnabled) return;
        if (Time.unscaledTime < nextListenerSearch) return;
        nextListenerSearch = Time.unscaledTime + .5f;
        foreach (AudioListener item in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (item.isActiveAndEnabled) { listener = item; break; }
    }
    void LateUpdate()
    {
        bool warningPresent = false;
        for (int i = 0; i < WarningVoices; i++) warningPresent |= voices[i].owner;
        foreach (Voice v in voices)
        {
            if (!v.owner) { if (v.source.clip) Stop(v); continue; }
            if (!v.owner.CanSound || (v.owner.IsDead && !IsAfterDeath(v.cue))) { Stop(v); continue; }
            bool hitStop = Time.timeScale == 0f;
            if (hitStop && !v.paused) { v.source.Pause(); v.paused = true; }
            else if (!hitStop && v.paused) { v.source.UnPause(); v.paused = false; }
            if (!hitStop && Time.time >= v.estimatedEnd) { Stop(v); continue; }
            v.source.transform.position = v.owner.transform.position + v.offset;
            v.source.volume = v.gain * (warningPresent && v.priority >= 3 ? .58f : 1f);
        }
        // Preserve room for warning transients in crowds. Clip peak <=.8 and total
        // source gain <=1.15 give conservative summing headroom within this pool.
        float warnings = 0f, actions = 0f;
        for (int i = 0; i < voices.Length; i++)
            if (voices[i].owner) { if (i < WarningVoices) warnings += voices[i].source.volume; else actions += voices[i].source.volume; }
        float warningScale = warnings > 0 ? Mathf.Min(1f, .78f / warnings) : 1f;
        float actionScale = actions > 0 ? Mathf.Min(1f, (1.15f - warnings * warningScale) / actions) : 1f;
        for (int i = 0; i < voices.Length; i++)
            voices[i].source.volume *= masterVolume * (i < WarningVoices ? warningScale : actionScale);
    }
    public static void StopOwner(EnemySoundEmitter owner, bool warningsOnly = false)
    {
        if (!instance) return;
        foreach (Voice v in instance.voices)
            if (v.owner == owner && (!warningsOnly || IsWarning(v.cue))) Stop(v);
    }
    public static void StopAction(EnemySoundEmitter owner)
    {
        if (!instance) return;
        for (int i = WarningVoices; i < instance.voices.Length; i++)
            if (instance.voices[i].owner == owner) Stop(instance.voices[i]);
    }
    public static void StopAll()
    { if (instance) foreach (Voice v in instance.voices) Stop(v); }
    static void Stop(Voice v)
    { v.source.Stop(); v.source.clip = null; v.owner = null; v.paused = false; }
    int Count(EnemySoundEmitter owner)
    { int count = 0; foreach (Voice v in voices) if (v.owner && (!owner || v.owner == owner)) count++; return count; }
    void OnDisable() { foreach (Voice v in voices) if (v != null) Stop(v); }
    void OnDestroy() { if (instance == this) instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null; Array.Clear(clips, 0, clips.Length);
        StartedCount = RejectedCount = PeakVoices = 0;
    }
}
