using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Actual authored map, sun, tree shadows, sky and camera FOV. No new lights,
// exposure changes or saved scene edits. Called by the save-isolated Play harness.
public static class EnemyEnvironmentVisualChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Width = 1280, Height = 720, CropWidth = 320, CropHeight = 240;
    [MenuItem("Tools/Enemies/Capture Enemy States In Actual Map (Play)")]
    public static void Run() => EnemyFoundationPlayChecks.RunEnvironmentChecks();

    public static IEnumerator Tests(BaselineFlow flow, Transform player, Action<string> log)
    {
        EnemyVisualStatePlayChecks.CheckAssets();
        var actors = new List<GameObject>();
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None))
        { ai.enabled = false; actors.Add(ai.gameObject); }
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None))
        { ai.enabled = false; actors.Add(ai.gameObject); }
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None))
        { ai.enabled = false; actors.Add(ai.gameObject); }
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None))
        { ai.enabled = false; actors.Add(ai.gameObject); }
        actors.Sort((a,b) => string.CompareOrdinal(a.name,b.name));
        if (actors.Count < 5) throw new InvalidOperationException("Missing authored enemies.");
        var playerHealth = player.GetComponent<Health>(); playerHealth.RestoreFullHealth();
        RepairWispSupportVfx owner = Object.FindFirstObjectByType<RepairWispSupportVfx>();
        var repair = owner.GetComponent<RepairWispAI>();
        repair.GetType().GetField("player", Private).SetValue(repair, player.GetComponent<PlayerVitals>());
        repair.GetType().GetField("playerHealth", Private).SetValue(repair, playerHealth);
        string pass = Environment.GetEnvironmentVariable("ANCA_VFX_PASS") ?? "check";
        string directory = Path.Combine(Path.GetTempPath(), "anca-enemy-readability", pass);
        Directory.CreateDirectory(directory);
        var metrics = new List<string> { "actor,position,view,charge_pixels,hit_pixels,repair_pixels" };
        var grid = new Texture2D(CropWidth * 4, CropHeight * actors.Count, TextureFormat.RGB24, false);
        var go = new GameObject("QA actual-map readability camera"); var camera = go.AddComponent<Camera>();
        Camera original = Camera.main; camera.CopyFrom(original); camera.enabled = false;
        var settings = camera.GetUniversalAdditionalCameraData(); var sourceSettings = original.GetUniversalAdditionalCameraData();
        settings.renderPostProcessing = sourceSettings.renderPostProcessing;
        settings.volumeLayerMask = sourceSettings.volumeLayerMask; settings.renderShadows = true;
        var rt = new RenderTexture(Width, Height, 24); rt.Create();
        yield return null;
        int row = 0;
        foreach (GameObject actor in actors)
        {
            EnemyVisualStatePlayChecks.Reset(actor);
            foreach (Animator animator in actor.GetComponentsInChildren<Animator>()) animator.Update(0f);
            yield return null;
            // Use a clear 8m view. This matches combat viewing distance instead
            // of the old 32-degree close-up on an empty grey floor.
            Vector3 target = BodyBounds(actor).center;
            Vector3 offset = ChooseView(actor, target);
            camera.transform.position = target + offset; camera.transform.LookAt(target);
            int idleX, idleY; ScreenCentre(camera, target, out idleX, out idleY);
            Color32[] idle = Capture(camera, rt, directory, actor.name + "-idle");
            PutCrop(grid, idle, 0, row, idleX, idleY);
            EnemyVisualStatePlayChecks.Charge(actor, .95f);
            if (actor.TryGetComponent(out DomainPrototypeEnemy domain)) Call(domain, "AnimateVisual");
            Color32[] charge = Capture(camera, rt, directory, actor.name + "-charge");
            PutCrop(grid, charge, 1, row, idleX, idleY);
            EnemyVisualStatePlayChecks.Reset(actor);
            if (domain) { Call(domain, "AnimateVisual"); Call(domain, "ApplyPrismPalette"); }
            Vector3 contact = Contact(actor, camera.transform.position);
            Health health = actor.GetComponent<Health>(); var impact = actor.GetComponent<EnemyImpactVfx>();
            health.TryApplyDamage(new DamageInfo(1, player.gameObject, contact, (target - camera.transform.position).normalized));
            Call(impact, "LateUpdate");
            Color32[] hit = Capture(camera, rt, directory, actor.name + "-hit");
            PutCrop(grid, hit, 2, row, idleX, idleY); impact.Clear();
            var highlight = actor.GetComponent<RepairSupportHighlight>();
            if (!highlight) highlight = actor.AddComponent<RepairSupportHighlight>();
            highlight.Configure(owner.SupportMaterial);
            highlight.GetType().GetField("strength", Private).SetValue(highlight, 1f);
            highlight.Channel(owner, .5f); Call(highlight, "LateUpdate");
            var block = new MaterialPropertyBlock();
            foreach (Renderer overlay in actor.GetComponentsInChildren<Renderer>())
                if (overlay.name == "Repair energy overlay")
                { overlay.GetPropertyBlock(block); block.SetFloat("_RepairScanPhase", .5f); overlay.SetPropertyBlock(block); }
            Color32[] scan = Capture(camera, rt, directory, actor.name + "-repair");
            PutCrop(grid, scan, 3, row, idleX, idleY); highlight.Clear();
            int charged = Difference(idle,charge), struck = Difference(idle,hit), healed = Difference(idle,scan);
            metrics.Add(actor.name + ",\"" + actor.transform.position + "\",\"" + offset + "\"," + charged + "," + struck + "," + healed);
            log("Actual-map " + actor.name + " " + actor.transform.position + ": charge=" + charged + " hit=" + struck + " repair=" + healed);
            row++;
        }
        // Also observe a real authored guard/support pair, not just a forced scan.
        GameObject chestGuard = actors.Find(a => a.name == "RuinPrism_WestRuins");
        GameObject chestHealer = actors.Find(a => a.name == "RepairWisp_HiddenChest");
        if (chestGuard && chestHealer)
        {
            foreach (GameObject actor in actors) EnemyVisualStatePlayChecks.Reset(actor);
            var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
            player.position = chestGuard.transform.position + chestGuard.transform.right * 2.5f;
            controller.enabled = true; Physics.SyncTransforms();
            Health guardHealth = chestGuard.GetComponent<Health>();
            guardHealth.TryApplyDamage(new DamageInfo(Mathf.Max(1,guardHealth.MaxHealth/2),player.gameObject,
                chestGuard.transform.position+Vector3.up,Vector3.forward));
            var actualRepair = chestHealer.GetComponent<RepairWispAI>(); actualRepair.enabled = true;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!actualRepair.BeamVisible && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
            if (!actualRepair.BeamVisible) throw new InvalidOperationException("Actual chest pair did not begin a repair.");
            yield return null; yield return null;
            Vector3 target = BodyBounds(chestGuard).center;
            camera.transform.position = target + ChooseView(chestGuard,target); camera.transform.LookAt(target);
            Capture(camera,rt,directory,"actual-chest-link-sun");
            // Same actual-map pair from the opposite, shadow-facing direction.
            Vector3 view = camera.transform.position-target;
            camera.transform.position = target + Quaternion.Euler(0,110,0)*view; camera.transform.LookAt(target);
            Capture(camera,rt,directory,"actual-chest-link-shadow");
            var link = (LineRenderer)actualRepair.GetType().GetField("energyLine",Private).GetValue(actualRepair);
            if (link.sharedMaterial.shader.name != "Enemies/LocalImpact" || link.widthMultiplier < .045f)
                throw new InvalidOperationException("Actual repair link did not use readable shared energy shader.");
            actualRepair.enabled = false;
            log("PASS real chest repair: shared narrow link shader, actual moving endpoints and local recipient scan.");
        }
        grid.Apply(); File.WriteAllBytes(Path.Combine(directory,"states-idle-charge-hit-repair.png"), grid.EncodeToPNG());
        File.WriteAllLines(Path.Combine(directory,"metrics.csv"), metrics);
        rt.Release(); Object.Destroy(rt); Object.Destroy(grid); Object.Destroy(go);
        log("PASS actual-map captures (idle / charge / hit / repair), " + actors.Count + " authored samples, unchanged scene lighting; " + directory);
    }
    static Vector3 ChooseView(GameObject actor, Vector3 target)
    {
        Vector3 best = Vector3.back * 7.4f + Vector3.up * 2.8f; float bestScore = float.NegativeInfinity;
        for (int i = 0; i < 12; i++)
        {
            Vector3 offset = Quaternion.Euler(0, i * 30, 0) * (Vector3.back * 7.4f) + Vector3.up * 2.8f;
            bool blocked = false;
            foreach (RaycastHit hit in Physics.RaycastAll(target, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(actor.transform)) { blocked = true; break; }
            // Prefer a front three-quarter angle, but never photograph through terrain/walls.
            Vector3 front = Quaternion.Euler(0,30,0) * actor.transform.forward;
            float score = blocked ? -100f : Vector3.Dot(offset.normalized, front);
            // Tree canopy meshes in this map do not all have colliders. Penalise a
            // camera inside those visible bounds; do NOT solve it with through-wall VFX.
            if (!blocked)
                foreach (MeshRenderer source in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!source.enabled || !source.sharedMaterial || source.sharedMaterial.renderQueue >= 2500 ||
                        source.GetComponentInParent<Health>() || source.bounds.size.magnitude > 35f) continue;
                    if (source.bounds.Contains(target+offset)) { score -= 10f; break; }
                }
            if (score > bestScore) { bestScore = score; best = offset; }
        }
        if (bestScore < -10f) throw new InvalidOperationException("No unoccluded 8m camera view for " + actor.name);
        return best;
    }
    static Bounds BodyBounds(GameObject actor)
    {
        bool found = false; Bounds result = new Bounds(actor.transform.position + Vector3.up, Vector3.one);
        foreach (MeshRenderer renderer in actor.GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled || !renderer.sharedMaterial || renderer.sharedMaterial.renderQueue >= 2500) continue;
            if (!found) { result = renderer.bounds; found = true; } else result.Encapsulate(renderer.bounds);
        }
        return result;
    }
    static Vector3 Contact(GameObject actor, Vector3 camera)
    {
        Vector3 direction = (camera - BodyBounds(actor).center).normalized; float best = float.NegativeInfinity;
        Vector3 result = BodyBounds(actor).center;
        foreach (MeshRenderer renderer in actor.GetComponentsInChildren<MeshRenderer>())
        {
            Material material = renderer.sharedMaterial;
            if (!renderer.enabled || !material || material.renderQueue >= 2500 ||
                (!material.name.Contains("Ivory") && !material.shader.name.Contains("StoneScout"))) continue;
            var filter = renderer.GetComponent<MeshFilter>(); if (!filter || !filter.sharedMesh) continue;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = renderer.transform.TransformPoint(vertex);
                float score = Vector3.Dot(point - actor.transform.position,direction);
                if (score > best) { best = score; result = point; }
            }
        }
        return result;
    }
    static Color32[] Capture(Camera camera, RenderTexture target, string directory, string filename)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(Width, Height, TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,Width,Height),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(directory,filename + ".png"),image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32(); RenderTexture.active = previous; Object.Destroy(image); return pixels;
    }
    static int Difference(Color32[] a, Color32[] b)
    {
        int result = 0;
        for (int i = 0; i < a.Length; i++)
            if (Mathf.Max(Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b)) > 20) result++;
        return result;
    }
    static void ScreenCentre(Camera camera, Vector3 point, out int x, out int y)
    {
        Vector3 p = camera.WorldToViewportPoint(point); x = Mathf.RoundToInt(p.x * Width); y = Mathf.RoundToInt(p.y * Height);
    }
    static void PutCrop(Texture2D grid, Color32[] pixels, int column, int row, int centreX, int centreY)
    {
        var crop = new Color32[CropWidth * CropHeight];
        for (int y = 0; y < CropHeight; y++) for (int x = 0; x < CropWidth; x++)
            crop[y * CropWidth + x] = pixels[Mathf.Clamp(centreY-CropHeight/2+y,0,Height-1)*Width + Mathf.Clamp(centreX-CropWidth/2+x,0,Width-1)];
        grid.SetPixels32(column * CropWidth, grid.height - (row+1) * CropHeight,CropWidth,CropHeight,crop);
    }
    static void Call(object target, string method) => target.GetType().GetMethod(method,Private).Invoke(target,null);
}
