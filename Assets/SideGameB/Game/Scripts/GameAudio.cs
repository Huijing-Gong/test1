using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameSound
{
    PlayerHurt, ArrowShot, EnemyAlert, EnemyAttack, FighterPunch,
    ChestOpen, ButtonPress, PrincessAttack, PlayerDeath, Heal,
    FootstepStone, RuneLight, FootstepGrass, ItemGet, DiamondGet,
    Jump, Dash, DoorOpen
}

/// <summary>Side Game B audio, owned by its scene and unloaded on return or retry.</summary>
public sealed class GameAudio : MonoBehaviour
{
    private static readonly Dictionary<GameSound, string> ClipNames = new()
    {
        { GameSound.PlayerHurt, "Audio/player_hurt" },
        { GameSound.ArrowShot, "Audio/arrow_shot" },
        { GameSound.EnemyAlert, "Audio/enemy_alert" },
        { GameSound.EnemyAttack, "Audio/enemy_attack" },
        { GameSound.FighterPunch, "Audio/fighter_punch" },
        { GameSound.ChestOpen, "Audio/chest_open" },
        { GameSound.ButtonPress, "Audio/button_press" },
        { GameSound.PrincessAttack, "Audio/princess_attack" },
        { GameSound.PlayerDeath, "Audio/player_death" },
        { GameSound.Heal, "Audio/heal" },
        { GameSound.FootstepStone, "Audio/footstep_stone" },
        { GameSound.RuneLight, "Audio/rune_light" },
        { GameSound.FootstepGrass, "Audio/footstep_grass" },
        { GameSound.ItemGet, "Audio/item_get" },
        { GameSound.DiamondGet, "Audio/diamond_get" },
        { GameSound.Jump, "Audio/jump" },
        { GameSound.Dash, "Audio/dash" },
        // The chest/stone mechanism sound also suits the final stone door.
        { GameSound.DoorOpen, "Audio/chest_open" }
    };

    private static GameAudio instance;
    private readonly Dictionary<GameSound, AudioClip> clips = new();
    private AudioSource effectsSource;
    private AudioSource musicSource;
    private AudioSource standbyMusicSource;
    private bool musicCrossfading;
    private bool musicPausedForDeath;
    private bool preserveForDifficultyTransition;
    private Coroutine musicCrossfadeRoutine;
    private const float MusicVolume = 0.22f;
    private const float MusicLoopPoint = 28f;
    private const float MusicCrossfadeDuration = 0.75f;
    public static bool IsMusicPlaying => instance && instance.musicSource != null &&
                                         instance.musicSource.isPlaying;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneAudio()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsSideGameBScene(scene)) return;
        if (instance && instance.preserveForDifficultyTransition)
        {
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
            instance.preserveForDifficultyTransition = false;
            return;
        }
        Create(scene);
    }

    private static bool IsSideGameBScene(Scene scene)
    {
        return scene.path == SideWorldTravel.SideGameBPath ||
               scene.path == SideGameBDifficulty.EasyScenePath ||
               scene.path == SideGameBDifficulty.NormalScenePath ||
               scene.path == SideGameBDifficulty.HardScenePath;
    }

    private static void Create()
    {
        if (instance != null) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsSideGameBScene(scene))
            {
                Create(scene);
                return;
            }
        }
    }

    private static void Create(Scene scene)
    {
        if (instance != null) return;
        if (!scene.IsValid() || !scene.isLoaded) return;
        GameObject audioObject = new("Game Audio");
        SceneManager.MoveGameObjectToScene(audioObject, scene);
        instance = audioObject.AddComponent<GameAudio>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (FindFirstObjectByType<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>();
        effectsSource = gameObject.AddComponent<AudioSource>();
        effectsSource.playOnAwake = false;
        effectsSource.volume = 0.72f;
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = false;
        musicSource.volume = MusicVolume;
        musicSource.clip = Resources.Load<AudioClip>("Audio/background_music");
        standbyMusicSource = gameObject.AddComponent<AudioSource>();
        standbyMusicSource.playOnAwake = false;
        standbyMusicSource.loop = false;
        standbyMusicSource.volume = 0f;
        standbyMusicSource.clip = musicSource.clip;
        if (musicSource.clip != null) StartCoroutine(PlayMusicWhenReady());
    }

    public static void PreserveForDifficultyTransition()
    {
        if (!instance) return;
        instance.preserveForDifficultyTransition = true;
        DontDestroyOnLoad(instance.gameObject);
    }

    private void Update()
    {
        if (musicPausedForDeath || musicCrossfading || musicSource == null ||
            musicSource.clip == null || !musicSource.isPlaying)
            return;

        float loopPoint = Mathf.Min(MusicLoopPoint, musicSource.clip.length - 0.1f);
        if (musicSource.time >= loopPoint - MusicCrossfadeDuration)
            musicCrossfadeRoutine = StartCoroutine(CrossfadeMusicLoop());
    }

    public static void PauseMusicForDeath()
    {
        if (instance == null) return;
        instance.musicPausedForDeath = true;
        if (instance.musicCrossfadeRoutine != null)
        {
            instance.StopCoroutine(instance.musicCrossfadeRoutine);
            instance.musicCrossfadeRoutine = null;
        }
        instance.musicCrossfading = false;
        if (instance.musicSource != null) instance.musicSource.Pause();
        if (instance.standbyMusicSource != null) instance.standbyMusicSource.Pause();
    }

    public static void RestartMusicFromBeginning()
    {
        if (instance == null) return;
        instance.musicPausedForDeath = false;
        instance.musicCrossfading = false;
        if (instance.musicCrossfadeRoutine != null)
        {
            instance.StopCoroutine(instance.musicCrossfadeRoutine);
            instance.musicCrossfadeRoutine = null;
        }
        instance.standbyMusicSource.Stop();
        instance.standbyMusicSource.time = 0f;
        instance.standbyMusicSource.volume = 0f;
        instance.musicSource.Stop();
        instance.musicSource.time = 0f;
        instance.musicSource.volume = MusicVolume;
        instance.musicSource.Play();
    }

    public static void Play(GameSound sound, float volume = 1f)
    {
        if (instance == null) Create();
        if (instance == null || instance.effectsSource == null) return;
        if (!instance.clips.TryGetValue(sound, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>(ClipNames[sound]);
            instance.clips[sound] = clip;
        }
        if (clip != null) instance.StartCoroutine(instance.PlayEffectWhenReady(clip, Mathf.Clamp01(volume)));
    }

    public static void PlayFootstep(Collider2D surface)
    {
        string surfaceName = FullName(surface != null ? surface.transform : null).ToLowerInvariant();
        bool longFlowerGrass = surface != null &&
                               surface.gameObject.name == "Long Flower Grass Platform";
        bool mossStone = surfaceName.Contains("moss stone platform 02");
        bool flowerPressureGrass = surfaceName.Contains("flower pressure platform");
        bool mainPlatformGrass = surfaceName.Contains("main platform a") ||
                                 surfaceName.Contains("main platform b");
        bool grass = !mossStone && (longFlowerGrass || flowerPressureGrass || mainPlatformGrass ||
                     surfaceName.Contains("grass") || surfaceName.Contains("forest") ||
                     surfaceName.Contains("log") || surfaceName.Contains("moss"));
        Play(grass ? GameSound.FootstepGrass : GameSound.FootstepStone, 0.55f);
    }

    private static string FullName(Transform item)
    {
        string result = string.Empty;
        while (item != null)
        {
            result += " " + item.name;
            item = item.parent;
        }
        return result;
    }

    private IEnumerator PlayMusicWhenReady()
    {
        if (musicSource.clip.loadState == AudioDataLoadState.Unloaded)
            musicSource.clip.LoadAudioData();
        while (musicSource.clip.loadState == AudioDataLoadState.Loading)
            yield return null;
        if (musicSource.clip.loadState == AudioDataLoadState.Loaded)
            musicSource.Play();
        else
            Debug.LogError("Background music could not be loaded.");
    }

    private IEnumerator CrossfadeMusicLoop()
    {
        musicCrossfading = true;
        standbyMusicSource.time = 0f;
        standbyMusicSource.volume = 0f;
        standbyMusicSource.Play();

        float elapsed = 0f;
        while (elapsed < MusicCrossfadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float blend = Mathf.Clamp01(elapsed / MusicCrossfadeDuration);
            musicSource.volume = Mathf.Lerp(MusicVolume, 0f, blend);
            standbyMusicSource.volume = Mathf.Lerp(0f, MusicVolume, blend);
            yield return null;
        }

        musicSource.Stop();
        musicSource.volume = 0f;
        AudioSource previous = musicSource;
        musicSource = standbyMusicSource;
        standbyMusicSource = previous;
        musicSource.volume = MusicVolume;
        musicCrossfading = false;
        musicCrossfadeRoutine = null;
    }

    private IEnumerator PlayEffectWhenReady(AudioClip clip, float volume)
    {
        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading)
            yield return null;
        if (clip.loadState == AudioDataLoadState.Loaded)
            effectsSource.PlayOneShot(clip, volume);
        else
            Debug.LogError($"Sound effect could not be loaded: {clip.name}");
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
