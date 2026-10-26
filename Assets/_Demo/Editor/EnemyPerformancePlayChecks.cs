using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Runtime-only fixtures, no scene edits. Timings are native Editor CPU microbenchmarks,
// not browser frame-rate claims. Keep the legacy creation path for repeatable comparisons.
public static class EnemyPerformancePlayChecks
{
    public static IEnumerator Tests(BaselineFlow flow, Transform player, Action<string> log)
    {
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        const string path = "Assets/Art/Enemies/DomainPrototypes/Generated/LeafSeedProjectile.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) throw new InvalidOperationException("Projectile prefab missing: " + path);
        var clones = new GameObject[96];
        // Warm shader/mesh creation once, outside the measured window.
        var warm = Object.Instantiate(prefab); Object.Destroy(warm); yield return null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < clones.Length; i++)
        { clones[i] = Object.Instantiate(prefab, new Vector3(18000, 120, 18000), Quaternion.identity); clones[i].SetActive(false); }
        watch.Stop();
        log($"PERF baseline 96 dart Instantiate: {watch.Elapsed.TotalMilliseconds:F2} ms; GC counter=" + Allocations(before) + " (creation only).");
        foreach (var clone in clones) Object.Destroy(clone);
        yield return null;
        var origin = new Vector3(18000, 120, 18000);
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
        player.position = origin + Vector3.back * 5f; controller.enabled = true;
        var camera = Camera.main; var follow = camera.GetComponent<ThirdPersonCamera>(); if (follow) follow.enabled = false;
        camera.transform.position = origin + new Vector3(3, 2, -6); camera.transform.LookAt(origin);
        yield return null;
        var owner = new GameObject("QA pool owner"); owner.transform.position = origin;
        var health = owner.AddComponent<Health>(); health.SetMaximumHealth(10000);
        var pool = EnemyTransientPool.For(owner.scene);
        var first = pool.Spawn(prefab, owner, origin, Vector3.forward, 9);
        first.Retire(); first.Retire(); int created = pool.CreatedDarts;
        before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        for (int i = 0; i < 96; i++) pool.Spawn(prefab, owner, origin, Vector3.forward, 9).Retire();
        watch.Stop();
        Need(pool.CreatedDarts == created && pool.ActiveDarts == 0, "Warm projectile reuse allocated new objects.");
        log($"PERF warm 96 dart Spawn/Retire: {watch.Elapsed.TotalMilliseconds:F2} ms; GC counter=" + Allocations(before) + "; new GameObjects=0.");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var expiry = pool.Spawn(prefab, owner, origin, Vector3.forward, 9);
        typeof(DomainSeedProjectile).GetField("remainingLifetime",flags).SetValue(expiry, 0f);
        typeof(DomainSeedProjectile).GetMethod("Update",flags).Invoke(expiry,null);
        Need(!expiry.gameObject.activeSelf && pool.ActiveDarts == 0, "Expired dart failed to return.");
        var reused = pool.Spawn(prefab, owner, origin, Vector3.forward, 9);
        Need(reused == expiry && (float)typeof(DomainSeedProjectile).GetField("remainingLifetime",flags).GetValue(reused) > 1f,
            "Reused dart retained expired lifetime.");
        Need(reused.GetComponent<TrailRenderer>().positionCount == 0 &&
            (float)typeof(DomainSeedProjectile).GetField("spin",flags).GetValue(reused) == 0f,
            "Reused dart retained old trail/spin.");
        reused.gameObject.SetActive(false);
        Need(pool.ActiveDarts == 0, "External disable leaked a pool lease.");
        for (int i = 0; i < EnemyTransientPool.ProjectileLimit; i++)
            Need(pool.Spawn(prefab, owner, origin, Vector3.forward, 9), "Projectile cap rejected too early.");
        Need(!pool.Spawn(prefab, owner, origin, Vector3.forward, 9) && pool.ActiveDarts == 24 && pool.CreatedDarts <= 24,
            "Projectile active/retained hard cap failed.");
        DomainSeedProjectile.ClearOwned(owner); Need(pool.ActiveDarts == 0, "Owner cleanup did not return every lease.");
        for (int i = 0; i < 20; i++) pool.Impact(owner,origin,EnemyImpactVfx.SharedEnergyMaterial);
        Need(pool.ActiveImpacts == 8 && pool.CreatedImpacts <= 8, "Impact cap failed.");
        DomainSeedProjectile.ClearOwned(owner);
        Need(pool.ActiveImpacts == 0, "Owner cancellation retained impact particles.");
        log("PASS pooled lifetime/spin/trail reset, external disable, 24 darts / 8 impacts hard caps and owner cleanup.");
        // Exercise the production swept collision path on a pooled (not raw) instance.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "QA pooled dart cover"; wall.transform.position = origin + Vector3.forward;
        wall.transform.localScale = new Vector3(3,3,.2f); Physics.SyncTransforms();
        var shot = pool.Spawn(prefab,owner,origin,Vector3.forward,9);
        int ticks = 0; while (shot.gameObject.activeSelf && ticks++ < 90) yield return null;
        Need(ticks < 90 && pool.ActiveDarts == 0, "Pooled swept dart penetrated wall.");
        Object.Destroy(wall); DomainSeedProjectile.ClearOwned(owner);
        shot = pool.Spawn(prefab,owner,origin,Vector3.forward,9);
        health.TryApplyDamage(new DamageInfo(20000,player.gameObject,origin,Vector3.forward));
        Need(pool.ActiveDarts == 0 && !shot.gameObject.activeSelf, "Owner death leaked pooled projectile.");
        health.RestoreFullHealth();
        pool.Spawn(prefab,owner,origin,Vector3.forward,9); pool.Impact(owner,origin,EnemyImpactVfx.SharedEnergyMaterial);
        flow.SetMenu(true); yield return null; yield return null;
        Need(pool.ActiveDarts == 0 && pool.ActiveImpacts == 0, "Menu retained pooled transient effects.");
        flow.SetMenu(false);
        float deadline = Time.realtimeSinceStartup + 4f;
        while (flow.BlockWorldInput && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
        pool.Spawn(prefab,owner,origin,Vector3.forward,9); pool.Impact(owner,origin,EnemyImpactVfx.SharedEnergyMaterial);
        flow.NewGame();
        Need(pool.ActiveDarts == 0 && pool.ActiveImpacts == 0, "NewGame retained pooled effects.");
        if (flow.Tutorial) flow.Tutorial.Skip(); flow.SetMenu(false);
        deadline = Time.realtimeSinceStartup + 4f;
        while (flow.BlockWorldInput && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("QA transient pool unload");
        var otherOwner = new GameObject("QA unload owner"); otherOwner.AddComponent<Health>();
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(otherOwner,scene);
        var otherPool = EnemyTransientPool.For(scene);
        otherPool.Spawn(prefab,otherOwner,origin,Vector3.forward,9);
        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        Need(!otherPool, "Scene-local pool survived world unload.");
        log("PASS pooled wall sweep, owner death, menu, NewGame and additive scene unload.");
        var prisms = Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var prism in prisms)
        {
            if (prism.GetType().GetField("kind", flags).GetValue(prism).ToString() != "RuinPrism") continue;
            var measure = prism.GetType().GetMethod("MeasurePulseCover", flags);
            var ring = prism.GetType().GetMethod("DrawRing", flags);
            measure.Invoke(prism, null);
            int queries = prism.PulseGroundSamples;
            object[] args = { 3f };
            before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
            for (int i = 0; i < 120; i++) ring.Invoke(prism, args);
            watch.Stop();
            Need(prism.PulseGroundSamples == queries,"DrawRing repeated ground sampling.");
            log($"PERF 120 wave DrawRing: {watch.Elapsed.TotalMilliseconds:F2} ms; 0 extra ground samples; GC counter=" + Allocations(before) + " (includes reflection harness).");
            break;
        }
        yield return MovingFloorCache(log);
        foreach (var actor in Object.FindObjectsByType<EnemyImpactVfx>(FindObjectsSortMode.None))
            Need(actor.GetComponentsInChildren<Light>(true).Length == 0,"Per-enemy realtime light found.");
        yield return Overdraw(flow,player,log);
        Object.Destroy(owner);
        log("PASS enemy pool/ground/render budgets; no per-core realtime lights.");
        log("PERF native Editor baseline recorded; WebGL module not installed, browser performance NOT tested.");
    }
    static string Allocations(long before)
    {
        long current = GC.GetAllocatedBytesForCurrentThread();
        return current == 0 ? "unavailable on this runtime (NOT zero-allocation proof)" : (current-before) + " bytes";
    }
    static void Need(bool okay,string message) { if (!okay) throw new InvalidOperationException(message); }
    static IEnumerator MovingFloorCache(Action<string> log)
    {
        // Separate scene without an authored layout: nearest physical floor is approved.
        // Instantiate there so Awake's EnemyGroundQuery belongs to the correct scene.
        var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("QA moving wave floor");
        DomainPrototypeEnemy prism;
        var position = new Vector3(19000,120,19000);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "QA small moving floor";
            floor.transform.position = position + Vector3.down * .15f;
            floor.transform.localScale = new Vector3(40,.3f,40); Physics.SyncTransforms();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab");
            prism = Object.Instantiate(prefab,position + Vector3.up * .015f,Quaternion.identity).GetComponent<DomainPrototypeEnemy>();
            prism.enabled = false;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var measure = typeof(DomainPrototypeEnemy).GetMethod("MeasurePulseCover",flags);
            var draw = typeof(DomainPrototypeEnemy).GetMethod("DrawRing",flags);
            var line = (LineRenderer)typeof(DomainPrototypeEnemy).GetField("warningLine",flags).GetValue(prism);
            object[] args = { 3f };
            measure.Invoke(prism,null); draw.Invoke(prism,args);
            int samples = prism.PulseGroundSamples; float oldY = line.GetPosition(0).y;
            floor.transform.position += Vector3.up * .1f; Physics.SyncTransforms();
            draw.Invoke(prism,args);
            Need(prism.PulseGroundSamples > samples && Mathf.Abs(line.GetPosition(0).y - oldY - .1f) < .015f,
                "Moved floor retained stale cached wave height.");
            samples = prism.PulseGroundSamples;
            for (int i = 0; i < 120; i++) draw.Invoke(prism,args);
            Need(prism.PulseGroundSamples == samples,"Moved floor cache failed to settle.");
        }
        finally { UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene); }
        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        log("PASS moving floor invalidates wave cache once; stationary floor resumes zero-query draws.");
    }
    static void Capture(Camera camera,RenderTexture rt,string path)
    {
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
        System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active = previous; Object.Destroy(image);
    }

    static IEnumerator Overdraw(BaselineFlow flow, Transform player, Action<string> log)
    {
        // Real URP transparent-overdraw debug view. Only runtime settings, restored below.
        var rendering = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
        var previousMode = rendering.overdrawMode; int previousMax = rendering.maxOverdrawCount;
        var camera = Camera.main; var rt = new RenderTexture(960,540,24); rt.Create();
        int previousMask = camera.cullingMask; var previousClear = camera.clearFlags; Color previousBackground = camera.backgroundColor;
        var changedLayers = new System.Collections.Generic.Dictionary<GameObject,int>();
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"anca-enemy-performance");
        System.IO.Directory.CreateDirectory(directory);
        var owner = Object.FindFirstObjectByType<RepairWispSupportVfx>();
        var actors = Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            foreach (var actor in actors)
            {
                if (actor.EnemyKind != DomainPrototypeEnemy.Kind.RuinPrism) continue;
                camera.transform.position = actor.transform.position + new Vector3(5,3,-7);
                camera.transform.LookAt(actor.transform.position + Vector3.up);
                yield return null;
                var highlight = actor.GetComponent<RepairSupportHighlight>();
                if (!highlight) highlight = actor.gameObject.AddComponent<RepairSupportHighlight>();
                highlight.Configure(owner.SupportMaterial);
                Need(highlight.OverlayCount > 0 && highlight.OverlayCount <= RepairSupportHighlight.MaximumOverlays,
                    "Repair highlight missing or exceeded main-plate budget.");
                // Repair AI sees the actual player on the same layer; no permanent fake gate.
                var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
                player.position = actor.transform.position + Vector3.back * 3f; controller.enabled = true;
                var repair = owner.GetComponent<RepairWispAI>();
                repair.GetType().GetField("player",flags).SetValue(repair,player.GetComponent<PlayerVitals>());
                repair.GetType().GetField("playerHealth",flags).SetValue(repair,player.GetComponent<Health>());
                actor.ResetForJourney();
                typeof(DomainPrototypeEnemy).GetMethod("BeginWarning",flags).Invoke(actor,null);
                highlight.Channel(owner,.5f); highlight.GetType().GetField("strength",flags).SetValue(highlight,1f);
                highlight.GetType().GetMethod("LateUpdate",flags).Invoke(highlight,null);
                Need(highlight.Visible, "Valid nearby repair highlight hidden.");
                Capture(camera,rt,System.IO.Path.Combine(directory,"prism-repair-warning-normal.png"));
                // Isolate enemy draw calls: custom terrain/tree shaders need not support
                // DEBUG_DISPLAY and must not be mistaken for enemy overdraw hotspots.
                foreach (Transform part in actor.GetComponentsInChildren<Transform>(true))
                { changedLayers[part.gameObject] = part.gameObject.layer; part.gameObject.layer = 30; }
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                rendering.overdrawMode = DebugOverdrawMode.Transparent; rendering.maxOverdrawCount = 8;
                Capture(camera,rt,System.IO.Path.Combine(directory,"prism-repair-warning-transparent-overdraw.png"));
                foreach (var pair in changedLayers) if (pair.Key) pair.Key.layer = pair.Value;
                changedLayers.Clear(); camera.cullingMask = previousMask; camera.clearFlags = previousClear; camera.backgroundColor = previousBackground;
                rendering.overdrawMode = previousMode;
                log("PASS transparent overdraw capture: warning fill/ring + " + highlight.OverlayCount + " main-plate repair overlays; " + directory);
                // Detail culling cannot retain overlays, particles, or suppress a warning.
                camera.transform.position = actor.transform.position + new Vector3(0,4,-40);
                yield return null;
                highlight.Channel(owner,.5f); highlight.GetType().GetMethod("LateUpdate",flags).Invoke(highlight,null);
                Need(!highlight.Visible,"Distant scan still rendered.");
                var warning = (LineRenderer)actor.GetType().GetField("warningLine",flags).GetValue(actor);
                Need(warning.enabled,"Distance culling suppressed an attack warning.");
                highlight.Clear(); actor.ResetForJourney();
                log("PASS distant secondary scan culled, critical pulse warning retained.");
                break;
            }
        }
        finally
        {
            foreach (var pair in changedLayers) if (pair.Key) pair.Key.layer = pair.Value;
            camera.cullingMask = previousMask; camera.clearFlags = previousClear; camera.backgroundColor = previousBackground;
            rendering.overdrawMode = previousMode; rendering.maxOverdrawCount = previousMax; rt.Release(); Object.Destroy(rt);
        }
    }
}
