#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;

// Only instantiated in the disposable benchmark scene built by EnemyPerformanceBuildChecks.
// No component is added to the game. Excluded from non-development player builds.
public sealed class EnemyPerformanceProbe : MonoBehaviour
{
    public GameObject dartPrefab;
    public GameObject[] enemyPrefabs;
    public Material floorMaterial;
    string directory;
    readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
    void Start()
    {
        directory = Path.Combine(Application.temporaryCachePath,"anca-enemy-performance");
        foreach (string argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith("--enemy-report=")) directory = argument.Substring("--enemy-report=".Length);
        Directory.CreateDirectory(directory); Application.runInBackground = true;
        // Fixture-only settings; never alter the real game's quality/frame-rate policy.
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        Debug.developerConsoleVisible = false;
        StartCoroutine(Guard(Tests()));
    }
    IEnumerator Guard(IEnumerator checks)
    {
        bool passed = true;
        while (true)
        {
            object item = null; bool next = false;
            try { next = checks.MoveNext(); if (next) item = checks.Current; }
            catch (Exception error) { passed = false; Log("FAIL " + error); }
            if (!next || !passed) break;
            yield return item;
        }
        Log(passed ? "NATIVE PASS: isolated enemy budget probe; not whole-map/browser FPS." : "NATIVE FAIL");
        File.WriteAllText(Path.Combine(directory,"native-report.txt"),report.ToString());
        Application.Quit(passed ? 0 : 1);
    }
    void Log(string line) { report.AppendLine(line); Debug.Log("EnemyNative: " + line); }
    static void Need(bool okay,string reason) { if (!okay) throw new InvalidOperationException(reason); }
    IEnumerator Tests()
    {
        Log("Unity " + Application.unityVersion + "; " + Application.platform + "; GPU=" + SystemInfo.graphicsDeviceName + "; fixture vSync off");
        var camera = new GameObject("Enemy-only probe camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(8,5,-12); camera.transform.LookAt(Vector3.up);
        camera.fieldOfView = 60; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.11f,.14f);
        var sun = new GameObject("One shared test sun").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(45,25,0); sun.intensity = 1.1f;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Disposable flat probe floor";
        floor.transform.position = Vector3.down * .15f; floor.transform.localScale = new Vector3(40,.3f,40);
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial; Physics.SyncTransforms();
        var actors = new GameObject[enemyPrefabs.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i] = Instantiate(enemyPrefabs[i],new Vector3((i-2)*2.5f,.02f,0),Quaternion.identity);
            var scout = actors[i].GetComponent<StoneScoutRoadsideAI>(); if (scout) scout.enabled = false;
            var domain = actors[i].GetComponent<DomainPrototypeEnemy>(); if (domain) domain.enabled = false;
            var beetle = actors[i].GetComponent<RailBeetleAI>(); if (beetle) beetle.enabled = false;
            var repair = actors[i].GetComponent<RepairWispAI>(); if (repair) repair.enabled = false;
            Need(actors[i].GetComponentsInChildren<Light>(true).Length == 0,"Enemy added a realtime light.");
        }
        // Player/UI/BCI/Boss/maps are not part of this build fixture; no save API is called.
        yield return null; yield return null;
        var warm = Instantiate(dartPrefab); warm.SetActive(false); Destroy(warm); yield return null;
        var clones = new GameObject[96]; var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < clones.Length; i++) { clones[i] = Instantiate(dartPrefab); clones[i].SetActive(false); }
        watch.Stop(); Log("96 raw dart creations: " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms");
        foreach (var clone in clones) Destroy(clone); yield return null;
        var owner = actors[0]; Vector3 muzzle = new Vector3(0,2,0);
        var pool = EnemyTransientPool.For(owner.scene);
        pool.Spawn(dartPrefab,owner,muzzle,Vector3.forward,9).Retire(); int created = pool.CreatedDarts;
        watch.Restart();
        for (int i = 0; i < 96; i++) pool.Spawn(dartPrefab,owner,muzzle,Vector3.forward,9).Retire();
        watch.Stop(); Need(pool.CreatedDarts == created,"Warm reuse created more darts.");
        Log("96 warm Spawn/Retire: " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms; new dart objects=0");
        for (int i = 0; i < EnemyTransientPool.ProjectileLimit; i++) pool.Spawn(dartPrefab,owner,muzzle,Vector3.forward,9);
        Need(!pool.Spawn(dartPrefab,owner,muzzle,Vector3.forward,9) && pool.ActiveDarts == 24,"Dart cap failed.");
        pool.Clear();
        for (int i = 0; i < 20; i++) pool.Impact(owner,muzzle,EnemyImpactVfx.SharedEnergyMaterial);
        Need(pool.ActiveImpacts == 8 && pool.CreatedImpacts <= 8,"Impact cap failed."); pool.Clear();
        Log("PASS native 24 darts / 8 impacts cap and clear.");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var actor in actors)
        {
            var prism = actor.GetComponent<DomainPrototypeEnemy>();
            if (!prism || prism.EnemyKind != DomainPrototypeEnemy.Kind.RuinPrism) continue;
            typeof(DomainPrototypeEnemy).GetMethod("MeasurePulseCover",flags).Invoke(prism,null);
            int samples = prism.PulseGroundSamples; object[] args = { 3f };
            watch.Restart();
            for (int i = 0; i < 120; i++) typeof(DomainPrototypeEnemy).GetMethod("DrawRing",flags).Invoke(prism,args);
            watch.Stop(); Need(prism.PulseGroundSamples == samples,"Wave repeated terrain samples.");
            Log("120 cached wave draws: " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms; extra terrain samples=0");
        }
        for (int i = 0; i < 30; i++) yield return null; // Shader/renderer warm-up outside samples.
        var cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
        var times = new double[180]; long gcTotal = 0, maxDraws = 0;
        created = pool.CreatedDarts;
        try
        {
            for (int i = 0; i < times.Length; i++)
            {
                if (i % 12 == 0)
                {
                    pool.Spawn(dartPrefab,owner,muzzle,Vector3.forward,9).Retire();
                    pool.Impact(owner,muzzle,EnemyImpactVfx.SharedEnergyMaterial);
                    foreach (var actor in actors)
                    {
                        var health = actor.GetComponent<Health>(); health.RestoreFullHealth();
                        health.TryApplyDamage(new DamageInfo(1,null,actor.transform.position + Vector3.up,Vector3.forward));
                    }
                }
                yield return null;
                times[i] = cpu.Valid ? cpu.LastValue / 1000000.0 : -1;
                if (gc.Valid) gcTotal += gc.LastValue;
                if (draws.Valid) maxDraws = Math.Max(maxDraws,draws.LastValue);
            }
            Need(pool.CreatedDarts == created,"Repeated native shots grew the warmed pool.");
            Array.Sort(times);
            Log("180-frame native 5-type hit/impact fixture: CPU median=" + times[90].ToString("F3") +
                " ms, p95=" + times[171].ToString("F3") + " ms; unavailable recorder=-1.");
            Log("Fixture-level GC counter=" + (gc.Valid ? gcTotal + " bytes (includes harness/reflection/audio, NOT per-shot allocations)" : "unavailable") +
                "; peak draw calls=" + (draws.Valid ? maxDraws.ToString() : "unavailable"));
        }
        finally { cpu.Dispose(); gc.Dispose(); draws.Dispose(); }
        Debug.developerConsoleVisible = false;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"native-five-enemies.png"));
        yield return null; yield return null;
        pool.Clear();
        Log("No per-core lights; bounded original models/shared materials; WebGL NOT tested by this native executable.");
    }
}
#endif
