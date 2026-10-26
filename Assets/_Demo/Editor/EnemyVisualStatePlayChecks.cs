using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Save-isolated real Play fixture. No map, prefab or user's save is written.
public static class EnemyVisualStatePlayChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Domain = "Assets/Art/Enemies/DomainPrototypes/Generated/";
    static readonly Vector3 Origin = new Vector3(19000, 100, 19000);
    [MenuItem("Tools/Enemies/Check Five Enemy Visual State Priorities (Play)")]
    public static void Run() => EnemyFoundationPlayChecks.RunVisualChecks();
    public static void CheckAssets()
    {
        foreach (string path in new[] {
            "Assets/Art/Enemies/Shared/Resources/EnemyLocalImpact.shader",
            "Assets/Art/Enemies/Shared/Resources/EnemyReadableCore.shader",
            "Assets/Art/Enemies/RepairWisp/RepairSupportShell.shader",
            "Assets/Art/Enemies/StoneScout/Variants/StoneScoutPaletteLit.shader" })
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Need(shader && !ShaderUtil.ShaderHasError(shader), "Shader failed: " + path);
        }
        Need(Resources.Load<Shader>("EnemyLocalImpact"), "Runtime/build shader inclusion missing.");
        Need(Resources.Load<Shader>("EnemyReadableCore"), "Readable core runtime/build inclusion missing.");
        Debug.Log("EnemyVisualState: PASS four shaders and runtime resource inclusion.");
    }
    public static IEnumerator Tests(BaselineFlow flow, Transform player, Action<string> log)
    {
        CheckAssets();
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
        player.position = Origin + new Vector3(-18, .02f, -18); controller.enabled = true;
        var vitals = player.GetComponent<PlayerVitals>(); var playerHealth = player.GetComponent<Health>();
        // Presentation LOD measures the real gameplay camera. Keep it near this
        // synthetic visual arena rather than rendering from the distant authored spawn.
        var gameplayView = Camera.main; var follow = gameplayView ? gameplayView.GetComponent<ThirdPersonCamera>() : null;
        if (follow) follow.enabled = false;
        if (gameplayView) { gameplayView.transform.position = Origin + new Vector3(3,2.5f,-4.5f); gameplayView.transform.LookAt(Origin + Vector3.up); }
        playerHealth.RestoreFullHealth(); Physics.SyncTransforms(); yield return null;
        Need(vitals.EnemyCombatActive, "Visual fixture gameplay gate closed.");
        foreach (string path in new[] { "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab", Domain + "LeafWisp.prefab",
            Domain + "RuinPrism.prefab", "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab", RepairWispBuilder.PrefabPath })
        {
            GameObject actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), Origin + Vector3.up * .02f, Quaternion.identity);
            yield return null;
            Health health = actor.GetComponent<Health>(); EnemyImpactVfx impact = actor.GetComponent<EnemyImpactVfx>();
            CombatFeedback feedback = actor.GetComponent<CombatFeedback>();
            Need(impact && (!feedback || feedback.UsesLocalizedEnemyVisuals), "Missing local feedback: " + path);
            DomainPrototypeEnemy domain = actor.GetComponent<DomainPrototypeEnemy>();
            RailBeetleAI beetle = actor.GetComponent<RailBeetleAI>(); RepairWispAI healer = actor.GetComponent<RepairWispAI>();
            StoneScoutRoadsideAI scout = actor.GetComponent<StoneScoutRoadsideAI>();
            if (domain) domain.enabled = false; if (beetle) beetle.enabled = false;
            if (healer) healer.enabled = false; if (scout) scout.enabled = false;
            float early = Charge(actor, .1f), late = Charge(actor, .9f);
            Need(late > early * 1.1f, "Charge does not ramp core brightness: " + path + " " + early + "/" + late);
            Need(Stun(actor) < .8f, "Stagger/recovery core is not dim/flickering: " + path);
            Reset(actor);
            if (domain) domain.enabled = true; if (beetle) beetle.enabled = true;
            if (healer) healer.enabled = true; if (scout) scout.enabled = true;
            yield return null;
            MeshRenderer source = null;
            foreach (MeshRenderer item in actor.GetComponentsInChildren<MeshRenderer>())
                if (item.enabled && item.sharedMaterial && item.sharedMaterial.renderQueue < 2500 &&
                    (item.sharedMaterial.name.Contains("Ivory") || item.sharedMaterial.shader.name.Contains("StoneScout"))) { source = item; break; }
            Need(source, "No original ceramic renderer: " + path);
            Vector3 contact = source.bounds.center + Vector3.back * source.bounds.extents.z;
            if (domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism)
            {
                // Contact the visible/front ceramic, not the first hierarchy panel
                // (which can be hidden behind the core after a different spin phase).
                Vector3 view = new Vector3(3, 2.5f, -4.5f).normalized; float front = float.NegativeInfinity;
                foreach (MeshRenderer candidate in actor.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!candidate.enabled || !candidate.sharedMaterial || !candidate.sharedMaterial.name.Contains("Ivory")) continue;
                    foreach (Vector3 vertex in candidate.GetComponent<MeshFilter>().sharedMesh.vertices)
                    {
                        Vector3 point = candidate.transform.TransformPoint(vertex); float score = Vector3.Dot(point - actor.transform.position, view);
                        if (score > front) { front = score; contact = point; source = candidate; }
                    }
                }
            }
            var before = new MaterialPropertyBlock(); source.GetPropertyBlock(before);
            health.TryApplyDamage(new DamageInfo(1, player.gameObject, contact, Vector3.forward));
            // An offscreen render runs in this coroutine, before Unity's normal
            // LateUpdate/render phase. Synchronise the local patch to the current pose.
            Call(impact, "LateUpdate");
            Need(impact.HitVisible && impact.VisibleOverlayCount >= 1 && impact.VisibleOverlayCount <= 2 && impact.ParticleCount <= 12,
                "Local patch/particle budget failed: " + path);
            var after = new MaterialPropertyBlock(); source.GetPropertyBlock(after);
            Need(before.GetColor("_BaseColor") == after.GetColor("_BaseColor"), "Hit overwrote original shell palette: " + path);
            if (domain && domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism)
            {
                Color32[] hitPixels = Capture(actor, "anca-local-hit.png");
                impact.Clear(); Color32[] baseline = Capture(actor, "anca-local-hit-baseline.png");
                int changed = ChangedPixels(hitPixels, baseline);
                if (changed <= 10)
                {
                    foreach (MeshRenderer patch in actor.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!patch.name.StartsWith("Reusable local hit patch")) continue;
                        var info = new MaterialPropertyBlock(); patch.GetPropertyBlock(info);
                        log("GPU patch diagnostic: " + patch.transform.parent.name + " shader=" + patch.sharedMaterial.name +
                            " point=" + info.GetVector("_ImpactPoint") + " radius=" + info.GetFloat("_ImpactRadius") +
                            " strength=" + info.GetFloat("_ImpactStrength") + " bounds=" + patch.bounds);
                        info.SetFloat("_ImpactRadius", 100000f); info.SetFloat("_ImpactStrength", 1f);
                        patch.SetPropertyBlock(info); patch.enabled = true;
                    }
                    Color32[] diagnostic = Capture(actor, "anca-hit-diagnostic.png");
                    log("GPU diagnostic full coverage difference=" + ChangedPixels(diagnostic, baseline));
                }
                Need(changed > 10 && changed < hitPixels.Length / 8, "Rendered hit patch invisible or full-body: " + changed);
                log("PASS GPU contact patch changes only " + changed + "/" + hitPixels.Length + " pixels.");
            }
            yield return new WaitForSeconds(.24f);
            yield return null; // Coroutine resumes before LateUpdate on the expiry frame.
            Need(!impact.HitVisible && impact.VisibleOverlayCount == 0, "Hit patch lingered: " + path +
                " timer=" + Field(impact, "hitTime") + " dt=" + Time.deltaTime + " scale=" + Time.timeScale);
            health.TryApplyDamage(new DamageInfo(1, player.gameObject, contact, Vector3.forward));
            health.TryApplyDamage(new DamageInfo(1000, player.gameObject, contact, Vector3.forward));
            Need(!impact.HitVisible && impact.VisibleOverlayCount == 0 && impact.ParticleCount == 0, "Death failed to override hit: " + path);
            yield return new WaitForSeconds(.2f);
            Need(CorePower(actor) < .05f, "Dead core still illuminated: " + path);
            Reset(actor); yield return null;
            Need(!health.IsDead && !impact.HitVisible && impact.VisibleOverlayCount == 0 && impact.ParticleCount == 0,
                "Reset retained death/hit debris: " + path);
            health.TryApplyDamage(new DamageInfo(1, player.gameObject, contact, Vector3.forward)); yield return null;
            actor.SetActive(false);
            Need(!impact.HitVisible && impact.VisibleOverlayCount == 0 && impact.ParticleCount == 0, "Disable retained hit effect.");
            Object.Destroy(actor); yield return null;
            log("PASS visual " + path + ": charge ramp, local patch, <=12 chips, unchanged shell palette, death/reset/disable priority.");
        }

        GameObject guard = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Domain + "RuinPrism.prefab"), Origin, Quaternion.identity);
        GameObject support = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RepairWispBuilder.PrefabPath), Origin + Vector3.left * 2f, Quaternion.identity);
        yield return null;
        guard.GetComponent<DomainPrototypeEnemy>().enabled = false;
        RepairWispAI aiRepair = support.GetComponent<RepairWispAI>(); aiRepair.enabled = false;
        Set(aiRepair, "player", vitals); Set(aiRepair, "playerHealth", playerHealth);
        RepairWispSupportVfx owner = support.GetComponent<RepairWispSupportVfx>();
        var highlight = guard.AddComponent<RepairSupportHighlight>(); highlight.Configure(owner.SupportMaterial);
        Set(highlight, "strength", 1f);
        highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
        Need(highlight.Visible && highlight.OverlayCount > 0, "Local repair scan missing.");
        // Capture the scan as it crosses the torso centre, not a between-panels phase.
        var scanBlock = new MaterialPropertyBlock();
        foreach (MeshRenderer overlay in guard.GetComponentsInChildren<MeshRenderer>())
            if (overlay.name == "Repair energy overlay")
            { overlay.GetPropertyBlock(scanBlock); scanBlock.SetFloat("_RepairScanPhase", .5f); overlay.SetPropertyBlock(scanBlock); }
        Color32[] scanPixels = Capture(guard, "anca-repair-scan.png");
        highlight.Clear(); Color32[] scanBaseline = Capture(guard, "anca-repair-scan-baseline.png");
        int scanned = ChangedPixels(scanPixels, scanBaseline);
        Need(scanned > 5 && scanned < scanPixels.Length / 25, "Rendered repair scan invisible or shield-like: " + scanned);
        log("PASS GPU narrow repair scan changes only " + scanned + "/" + scanPixels.Length + " pixels.");
        highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
        Health guardedHealth = guard.GetComponent<Health>();
        guardedHealth.TryApplyDamage(new DamageInfo(1, player.gameObject, Origin + Vector3.up, Vector3.forward));
        highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
        Need(!highlight.Visible, "Repair overlay masked a real hit.");
        yield return new WaitForSeconds(.24f);
        yield return null;
        highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
        Need(highlight.Visible, "Repair scan did not resume after the hit: timer=" + Field(guard.GetComponent<EnemyImpactVfx>(), "hitTime"));
        guardedHealth.TryApplyDamage(new DamageInfo(1000, player.gameObject, Origin, Vector3.forward));
        Need(!highlight.Visible, "Death retained repair scan.");
        guard.GetComponent<DomainPrototypeEnemy>().ResetForJourney();
        highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
        owner.enabled = false; Call(highlight, "LateUpdate"); Need(!highlight.Visible, "Disabled owner retained repair scan.");
        Object.Destroy(guard); Object.Destroy(support); yield return null;
        log("PASS repair priority: narrow scan, real hit temporarily suppresses it, resumes safely, death/disabled owner clear immediately.");
    }
    internal static float Charge(GameObject actor, float progress)
    {
        if (actor.TryGetComponent(out DomainPrototypeEnemy domain))
        {
            SetState(domain, "state", "Warning");
            float windup = (float)Field(domain, "windup") * (domain.EnemyKind == DomainPrototypeEnemy.Kind.RuinPrism ? .9f : 1f);
            Set(domain, "timer", windup * (1f - progress)); Call(domain, "ApplyPrismPalette");
        }
        else if (actor.TryGetComponent(out RailBeetleAI beetle))
        { var visual = actor.GetComponentInChildren<RailBeetleVisual>(); visual.SetAttack(progress, false); Call(visual, "TickVisual", .02f); }
        else if (actor.TryGetComponent(out RepairWispAI repair))
        {
            SetState(repair, "<CurrentPhase>k__BackingField", "Prepare");
            Set(repair, "timer", (float)Field(repair, "prepareSeconds") * (1f - progress));
            Call(actor.GetComponent<RepairWispVisual>(), "Update");
        }
        else { var visual = actor.GetComponentInChildren<StoneScoutVisualMotion>(); visual.SetChargeProgress(progress); Call(visual, "LateUpdate"); }
        return CorePower(actor);
    }
    static float CorePower(GameObject actor)
    {
        var block = new MaterialPropertyBlock();
        if (actor.TryGetComponent(out StoneScoutRoadsideAI scout))
        {
            var renderer = actor.GetComponentInChildren<StoneScoutVisualMotion>().GetComponentInChildren<MeshRenderer>(true);
            renderer.GetPropertyBlock(block); return block.GetFloat("_CorePower") * (1f + 2f * block.GetFloat("_ChargeStrength"));
        }
        float maximum = 0f;
        foreach (MeshRenderer renderer in actor.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material material = renderer.sharedMaterial;
            if (!material || !material.HasProperty("_EmissionColor") || material.GetColor("_EmissionColor").maxColorComponent <= 0f) continue;
            renderer.GetPropertyBlock(block);
            maximum = Mathf.Max(maximum, block.HasColor("_EmissionColor") ? block.GetColor("_EmissionColor").maxColorComponent : material.GetColor("_EmissionColor").maxColorComponent);
        }
        return maximum;
    }
    static float Stun(GameObject actor)
    {
        if (actor.TryGetComponent(out DomainPrototypeEnemy domain))
        { SetState(domain, "state", "Staggered"); Call(domain, "AnimateVisual"); Call(domain, "ApplyPrismPalette"); }
        else if (actor.TryGetComponent(out RailBeetleAI beetle))
        { var visual = actor.GetComponentInChildren<RailBeetleVisual>(); visual.SetAttack(0f, false); visual.SetStunned(true); Call(visual, "TickVisual", .02f); }
        else if (actor.TryGetComponent(out RepairWispAI healer))
        { SetState(healer, "<CurrentPhase>k__BackingField", "Stagger"); Call(actor.GetComponent<RepairWispVisual>(), "Update"); }
        else
        { var visual = actor.GetComponentInChildren<StoneScoutVisualMotion>(); visual.SetChargeProgress(0f); visual.PlayHit(Vector3.right); Call(visual, "Update"); Call(visual, "LateUpdate"); }
        Need(Mathf.Abs(EnemyImpactVfx.StunPower(.4f) - EnemyImpactVfx.StunPower(.47f)) > .01f, "Stun power is constant.");
        return CorePower(actor);
    }
    internal static void Reset(GameObject actor)
    {
        if (actor.TryGetComponent(out StoneScoutCombatTarget scout)) scout.ResetForJourney();
        else if (actor.TryGetComponent(out DomainPrototypeEnemy domain)) domain.ResetForJourney();
        else if (actor.TryGetComponent(out RailBeetleAI beetle)) beetle.ResetForJourney();
        else actor.GetComponent<RepairWispAI>().ResetForJourney();
    }
    static Color32[] Capture(GameObject actor, string filename)
    {
        GameObject cameraObject = new GameObject("QA state capture"); Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = actor.transform.position + new Vector3(3f, 2.5f, -4.5f);
        camera.transform.LookAt(actor.transform.position + Vector3.up); camera.fieldOfView = 32f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.17f,.22f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var target = new RenderTexture(800, 600, 24); target.Create();
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(800, 600, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0,0,800,600),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), filename), image.EncodeToPNG());
        Debug.Log("EnemyVisualState capture: " + Path.Combine(Path.GetTempPath(), filename));
        Color32[] pixels = image.GetPixels32();
        RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(image); Object.Destroy(cameraObject);
        return pixels;
    }
    static int ChangedPixels(Color32[] first, Color32[] second)
    {
        int changed = 0;
        for (int i = 0; i < first.Length; i++)
            if (Mathf.Max(Mathf.Abs(first[i].r - second[i].r), Mathf.Abs(first[i].g - second[i].g), Mathf.Abs(first[i].b - second[i].b)) > 12) changed++;
        return changed;
    }
    static object Field(object actor, string name) => actor.GetType().GetField(name, Private).GetValue(actor);
    static void Set(object actor, string name, object value) => actor.GetType().GetField(name, Private).SetValue(actor, value);
    static object Call(object actor, string name, params object[] args) => actor.GetType().GetMethod(name, Private).Invoke(actor, args);
    static void SetState(object actor, string name, string value)
    { var field = actor.GetType().GetField(name, Private); field.SetValue(actor, Enum.Parse(field.FieldType, value)); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
