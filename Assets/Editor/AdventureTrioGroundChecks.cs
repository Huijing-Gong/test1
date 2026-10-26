using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Edit-mode execution of the production ground query; no simulation, nav bake,
// asset creation or scene save. Temporary geometry lives in its own remote scene.
public static class AdventureTrioGroundChecks
{
    const string Report = AdventureTrioInstaller.Output + "/ground-validation.txt";
    static readonly Vector3 Origin = new Vector3(27000, 100, 27000);
    static readonly List<Mesh> meshes = new List<Mesh>();
    static int unsupportedWarnings;

    public static void RunAutomation()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal))
            throw new InvalidOperationException("Disposable adventure-trio scratch project only.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before edit-mode ground checks.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene edits present; checks discarded nothing.");
        Directory.CreateDirectory(AdventureTrioInstaller.Output);
        File.WriteAllText(Report, "Adventure trio production ground-query checks in Edit Mode. Temporary scene/meshes; no scene or asset saved.\n");
        string sceneHash = Hash(AdventureTrioInstaller.ScenePath);
        Scene fixture = default, main = default;
        OverworldV2Layout fixtureLayout = null, mainLayout = null;
        Collider[] originalSurfaces = null;
        string originalLayout = null;
        TerrainData terrainData = null;
        bool passed = false;
        unsupportedWarnings = 0; Application.logMessageReceived += ObserveLog;
        try
        {
            AdventureTrioInstaller.Validate();
            Log("PASS existing authored encounter placement validation (read-only installer.Validate).");
            main = SceneManager.GetSceneByPath(AdventureTrioInstaller.ScenePath);
            mainLayout = InScene<OverworldV2Layout>(main).Single();
            originalLayout = EditorJsonUtility.ToJson(mainLayout);
            fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);
            var root = new GameObject("QA temporary ground-only fixtures");
            fixtureLayout = root.AddComponent<OverworldV2Layout>();
            originalSurfaces = Surfaces(fixtureLayout);
            var probe = new GameObject("QA ground query actor"); probe.transform.SetParent(root.transform, false);
            probe.transform.position = Origin + Vector3.up * .015f;

            var floor = Quad(root.transform, "Approved nonconvex flat floor", Origin, 3f, 3f, Quaternion.identity);
            var wall = Quad(root.transform, "Approved nonconvex vertical wall", Origin + Vector3.right * 7f,
                2f, 2f, Quaternion.Euler(0, 0, 90));
            var unapproved = Quad(root.transform, "Unapproved nonconvex floor", Origin + Vector3.right * 14f, 2f, 2f, Quaternion.identity);
            var ledge = Quad(root.transform, "Approved narrow ledge", Origin + Vector3.right * 21f, .35f, .35f, Quaternion.identity);
            var steep = Quad(root.transform, "Approved steep local face", Origin + Vector3.right * 28f, 2f, 2f, Quaternion.Euler(0, 0, 50));
            Vector3 tiledCentre = Origin + Vector3.right * 36f;
            Vector3[] offsets = { Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
                Vector3.right + Vector3.forward, Vector3.right + Vector3.back, Vector3.left + Vector3.forward, Vector3.left + Vector3.back };
            var pads = new MeshCollider[offsets.Length];
            for (int i = 0; i < pads.Length; i++)
                pads[i] = Quad(root.transform, "Approved footprint sample " + i, tiledCentre + offsets[i] * .6f, .17f, .17f, Quaternion.identity);

            terrainData = new TerrainData { name = "QA temporary flat terrain", heightmapResolution = 33, size = new Vector3(4, 2, 4) };
            terrainData.SetHeights(0, 0, new float[33, 33]);
            var terrainObject = new GameObject("Approved temporary TerrainCollider"); terrainObject.transform.SetParent(root.transform, false);
            terrainObject.transform.position = Origin + Vector3.right * 44f;
            var terrain = terrainObject.AddComponent<TerrainCollider>(); terrain.terrainData = terrainData;
            SetSurfaces(fixtureLayout, new Collider[] { floor, wall, ledge, steep, terrain }.Concat(pads).ToArray());
            Physics.SyncTransforms();
            var query = new EnemyGroundQuery(probe.transform);
            Vector3 feet = Origin + Vector3.up * .015f;

            Need(!floor.convex && query.IsSurface(floor), "flat fixture is an explicitly approved nonconvex mesh");
            Need(query.AllowsSupportContact(floor, feet), "approved nonconvex flat floor accepts its local support face");
            Need(query.TrySample(feet, .3f, .6f, 35, out var sample) && sample.collider == floor && sample.normal.y > .99f,
                "production floor sampling returns the actual approved top face");
            Need(query.TryFootprint(feet, Quaternion.identity, Vector2.one * .6f, 35, .3f, .6f, out float highest) && Mathf.Abs(highest - Origin.y) < .01f,
                "full square footprint supports centre plus all eight perimeter samples");
            Need(query.TryFootprint(feet, Quaternion.Euler(0, 37, 0), Vector2.one * .6f, 35, .3f, .6f, out _, true),
                "rotated round nine-point footprint remains supported on a broad flat floor");

            Need(query.IsSurface(wall) && !query.AllowsSupportContact(wall, wall.transform.position + Vector3.up * .015f),
                "approved nonconvex vertical wall is not globally whitelisted as floor support");
            Need(query.IsSurface(steep) && !query.AllowsSupportContact(steep, steep.transform.position + Vector3.up * .015f),
                "approved steep mesh rejects a local normal below the support threshold");
            Need(!query.IsSurface(unapproved) && !query.AllowsSupportContact(unapproved, unapproved.transform.position + Vector3.up * .015f),
                "unapproved mesh rejects support even when its top face is horizontal");
            Need(!query.TrySample(unapproved.transform.position + Vector3.up * .015f, .3f, .6f, 35, out _),
                "floor sampling does not silently accept an unapproved physical mesh");

            Need(!query.AllowsSupportContact(floor, Origin + Vector3.up * .22f), "local support ray rejects a floor too far beneath the feet");
            Need(!query.AllowsSupportContact(floor, Origin - Vector3.up * .10f), "local support ray rejects floor penetration above the allowed height");
            Vector3 ledgeFeet = ledge.transform.position + Vector3.up * .015f;
            Need(query.TrySample(ledgeFeet, .3f, .6f, 35, out _) &&
                !query.TryFootprint(ledgeFeet, Quaternion.identity, Vector2.one * .6f, 35, .3f, .6f, out _, true),
                "a supported ledge centre cannot approve a body whose perimeter hangs beyond the edge");

            Vector3 tiledFeet = tiledCentre + Vector3.up * .015f;
            for (int i = 0; i < offsets.Length; i++)
                Need(query.TrySample(tiledFeet + offsets[i] * .6f, .3f, .6f, 35, out _), "independent footprint support ray " + i + " hits");
            Need(query.TryFootprint(tiledFeet, Quaternion.identity, Vector2.one * .6f, 35, .3f, .6f, out _),
                "all nine independently backed footprint points approve the body");
            pads[8].enabled = false; Physics.SyncTransforms();
            for (int i = 0; i < 8; i++)
                Need(query.TrySample(tiledFeet + offsets[i] * .6f, .3f, .6f, 35, out _), "removing ninth corner preserves support ray " + i);
            Need(!query.TrySample(tiledFeet + offsets[8] * .6f, .3f, .6f, 35, out _) &&
                !query.TryFootprint(tiledFeet, Quaternion.identity, Vector2.one * .6f, 35, .3f, .6f, out _),
                "missing only the ninth footprint point still rejects body support");

            Vector3 terrainFeet = terrain.transform.position + new Vector3(2, .015f, 2);
            Need(query.IsSurface(terrain) && query.AllowsSupportContact(terrain, terrainFeet),
                "approved TerrainCollider uses its actual local top face without ClosestPoint");
            Need(query.TryFootprint(terrainFeet, Quaternion.identity, Vector2.one * .6f, 35, .3f, .6f, out _),
                "approved flat TerrainCollider supports the complete nine-point footprint");
            Need(unsupportedWarnings == 0, "no unsupported Physics.ClosestPoint warning occurred in placement or synthetic checks");
            passed = true;
        }
        catch (Exception error) { Log("FAIL " + error); }
        finally
        {
            try
            {
                if (fixtureLayout && originalSurfaces != null) SetSurfaces(fixtureLayout, originalSurfaces);
                if (main.IsValid() && main.isLoaded) SceneManager.SetActiveScene(main);
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                foreach (var mesh in meshes) if (mesh) Object.DestroyImmediate(mesh); meshes.Clear();
                if (terrainData) Object.DestroyImmediate(terrainData);
                if (Hash(AdventureTrioInstaller.ScenePath) != sceneHash) { passed = false; Log("FAIL authored scene bytes changed"); }
                if (mainLayout && EditorJsonUtility.ToJson(mainLayout) != originalLayout) { passed = false; Log("FAIL original layout references changed"); }
                if (main.IsValid() && main.isLoaded && main.isDirty) { passed = false; Log("FAIL original scene became dirty"); }
                Log("Cleanup: temporary scene/objects/meshes removed; temporary navigationSurfaces restored; original scene/layout unchanged=" + passed);
            }
            catch (Exception error) { passed = false; Log("FAIL cleanup " + error); }
            Application.logMessageReceived -= ObserveLog;
        }
        Log(passed ? "FINAL PASS; edit-mode production query checks, original scene bytes/references unchanged, no nav bake or scene save." : "FINAL FAIL");
        if (!passed) throw new InvalidOperationException("Adventure trio ground checks failed; see " + Report);
    }

    static MeshCollider Quad(Transform parent, string name, Vector3 position, float halfX, float halfZ, Quaternion rotation)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(position, rotation);
        var mesh = new Mesh { name = name + " temporary mesh", vertices = new[] { new Vector3(-halfX, 0, -halfZ),
            new Vector3(-halfX, 0, halfZ), new Vector3(halfX, 0, halfZ), new Vector3(halfX, 0, -halfZ) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
        var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = false; return collider;
    }
    static Collider[] Surfaces(OverworldV2Layout layout)
    {
        var property = new SerializedObject(layout).FindProperty("navigationSurfaces");
        return Enumerable.Range(0, property.arraySize).Select(i => property.GetArrayElementAtIndex(i).objectReferenceValue as Collider).ToArray();
    }
    static void SetSurfaces(OverworldV2Layout layout, Collider[] surfaces)
    {
        var data = new SerializedObject(layout); var property = data.FindProperty("navigationSurfaces"); property.arraySize = surfaces.Length;
        for (int i = 0; i < surfaces.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = surfaces[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    static T[] InScene<T>(Scene scene) where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include,
        FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).ToArray();
    static void ObserveLog(string text, string stack, LogType type)
    { if (text.IndexOf("Physics.ClosestPoint can only be used", StringComparison.Ordinal) >= 0) unsupportedWarnings++; }
    static string Hash(string path)
    { using (var digest = SHA256.Create()) return Convert.ToBase64String(digest.ComputeHash(File.ReadAllBytes(path))); }
    static void Need(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); Log("PASS " + message); }
    static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log("ADVENTURE_GROUND_QA " + message); }
}
