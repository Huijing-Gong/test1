using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Query regressions use disposable scenes/data. Never rebakes or saves the authored map.
public static class EnemyNavigationChecks
{
    [MenuItem("Tools/Enemies/Check Shared Safe Navigation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before editor checks.");
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty) throw new InvalidOperationException("Save edited scenes before disposable checks.");
        if (string.IsNullOrEmpty(previous.path))
            previous = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath, OpenSceneMode.Single);
        Scene actual = SceneManager.GetSceneByPath(EnemyNavigationBuilder.ScenePath);
        bool opened = !actual.IsValid() || !actual.isLoaded;
        if (opened) actual = EditorSceneManager.OpenScene(EnemyNavigationBuilder.ScenePath, OpenSceneMode.Additive);
        EnemyNavigationWorld world = EnemyNavigationWorld.Find(actual);
        Require(world, "Shared navigation root missing.");
        for (int i = 0; i < 4; i++)
            Require(world.Ready((EnemyNavigationWorld.Profile)i), "Missing navigation data for profile " + i);
        Scene fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var data = new List<NavMeshData>(); var registrations = new List<NavMeshDataInstance>();
        try
        {
            SceneManager.SetActiveScene(fixture);
            Vector3 origin = new Vector3(18000f, 100f, 18000f);
            var sources = new List<NavMeshBuildSource> {
                Box(origin + Vector3.down * .15f, new Vector3(32f, .3f, 32f), 0),
                Box(origin + Vector3.up * 1.5f, new Vector3(1f, 3f, 8f), 1)
            };
            var root = new GameObject("QA query-only navigation"); var service = root.AddComponent<EnemyNavigationWorld>();
            service.enabled = false;
            var serialized = new SerializedObject(service);
            var assets = serialized.FindProperty("bakedData"); assets.arraySize = 4;
            var ids = serialized.FindProperty("agentTypes"); ids.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                var settings = EnemyNavigationBuilder.Settings(i);
                var mesh = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(origin, new Vector3(34f, 10f, 34f)),
                    Vector3.zero, Quaternion.identity);
                Require(mesh, "Fixture bake failed."); data.Add(mesh);
                assets.GetArrayElementAtIndex(i).objectReferenceValue = mesh;
                ids.GetArrayElementAtIndex(i).intValue = settings.agentTypeID;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo(); service.enabled = true;
            foreach (EnemyNavigationWorld.Profile profile in Enum.GetValues(typeof(EnemyNavigationWorld.Profile)))
            {
                var route = new EnemyNavigationRoute(); Vector3 position = origin + Vector3.left * 7f;
                Vector3 goal = origin + Vector3.right * 7f; float worstXInWall = 0f; int ticks;
                for (ticks = 0; ticks < 1400 && Vector3.Distance(position, goal) > .5f; ticks++)
                {
                    Require(route.Direction(fixture, profile, position, goal, .02f, out Vector3 direction),
                        "Route stopped before reaching target: " + profile + " at " + (position - origin));
                    position += direction * .05f;
                    Vector3 local = position - origin;
                    if (Mathf.Abs(local.z) < 4f && Mathf.Abs(local.x) < .5f) worstXInWall++;
                }
                Require(ticks < 1400 && worstXInWall == 0f && route.Complete, "Profile failed wall detour: " + profile);
                Debug.Log("Enemy navigation PASS: " + profile + " routes around solid wall, " + ticks + " bounded query steps.");
                route.Clear();
                Require(!route.Available && !route.Complete && !route.WaitingAtEnd, "Reset kept a stale path.");
                bool unsafeRoute = route.Direction(fixture, profile, origin, origin + Vector3.right * 25f, .02f, out _);
                Require(!unsafeRoute, "Off-island goal incorrectly produced a reachable path.");
                route.Clear();
                Require(!route.Direction(fixture, profile, origin + Vector3.left * 7f,
                    goal + Vector3.up * 2f, .02f, out _), "Route silently selected terrain on a different floor layer: " + profile);
                // A body near, but not on, the mesh can seek its safe entry point without warping.
                route.Clear(); route.Direction(fixture, profile, origin + Vector3.left * 7f, goal, .02f, out Vector3 first);
                route.Blocked(origin + Vector3.left * 7f, first);
                Require(route.Direction(fixture, profile, origin + Vector3.left * 7f, goal, .8f, out _), "Bounded blocked-route replan failed.");
            }
            // Four disconnected islands: complete versus partial path must be distinguished.
            var partialSources = new List<NavMeshBuildSource> {
                Box(origin + new Vector3(0f, -.15f, 50f), new Vector3(20f, .3f, 20f), 0),
                Box(origin + new Vector3(30f, -.15f, 50f), new Vector3(20f, .3f, 20f), 0)
            };
            var partial = NavMeshBuilder.BuildNavMeshData(EnemyNavigationBuilder.Settings(3), partialSources,
                new Bounds(origin + new Vector3(15f, 0f, 50f), new Vector3(52f, 10f, 22f)), Vector3.zero, Quaternion.identity);
            data.Add(partial); registrations.Add(NavMesh.AddNavMeshData(partial));
            var partialRoute = new EnemyNavigationRoute();
            Require(partialRoute.Direction(fixture, EnemyNavigationWorld.Profile.RailBeetle,
                origin + new Vector3(0f, 0f, 50f), origin + new Vector3(30f, 0f, 50f), .02f, out _) && !partialRoute.Complete,
                "Disconnected ledge must have only a partial route, never a complete attack route.");
            Debug.Log("Enemy navigation PASS: disconnected ledge is partial; off-map goals rejected; four reset/replan cases passed.");
        }
        finally
        {
            foreach (var registration in registrations) if (registration.valid) registration.Remove();
            EditorSceneManager.CloseScene(fixture, true);
            foreach (var mesh in data) if (mesh) Object.DestroyImmediate(mesh);
            if (opened) EditorSceneManager.CloseScene(actual, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        Debug.Log("Enemy navigation FINAL PASS: four authored profiles and disposable wall/ledge/reset/replan checks; scene unchanged.");
    }

    public static void RunAll()
    {
        Run(); RailBeetleChecks.Run(); RailBeetleCornerChecks.RunFocused();
        StoneScoutCombatPrototypeChecks.Run(); DomainPrototypeChecks.Run();
    }

    private static NavMeshBuildSource Box(Vector3 center, Vector3 size, int area) =>
        new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one), size = size, area = area };
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
