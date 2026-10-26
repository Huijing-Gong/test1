using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Read-only smoke checks for the authored-motion + living-world handoff.
// No installers, source saves, PlayerPrefs changes, or simulated gameplay.
public static class TeamIntegrationChecks
{
    const string World = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    const string Report = "IntegrationReports/20261024/static-validation.txt";
    static readonly StringBuilder Results = new StringBuilder();
    static void Need(bool ok, string label)
    {
        Results.AppendLine((ok ? "PASS " : "FAIL ") + label);
        if (!ok) throw new InvalidOperationException(label);
    }

    [MenuItem("Tools/Integration/Validate Living World Handoff")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before validation.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your scene edits first; validation never discards them.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Results.Clear();
        Results.AppendLine("Read-only Unity integration smoke check; NOT a full gameplay regression.");
        Results.AppendLine("Unity=" + Application.unityVersion + "; graphics=" + SystemInfo.graphicsDeviceType);
        var worldHash = File.ReadAllBytes(World);
        try
        {
            var scene = EditorSceneManager.OpenScene(World);
            CheckMissingScripts(scene.GetRootGameObjects());
            Need(Object.FindObjectsByType<WorldVegetationWind>(FindObjectsSortMode.None).Length == 1, "one shared wind host");
            var wind = Object.FindFirstObjectByType<WorldVegetationWind>();
            Need(Mathf.Abs(wind.interactionRadius - .72f) < .001f, "contact radius remains 0.72 m");
            Need(wind.driftingLeafMaterial && wind.driftingLeafMesh, "canopy debris references resolve");
            var life = Object.FindFirstObjectByType<WorldSmallWildlife>();
            var audio = Object.FindFirstObjectByType<RegionalNatureAudio>();
            Need(life && life.birdAnchors.Length == 6 && life.butterflyAnchors.Length == 8, "six bird and eight butterfly anchors");
            Need(life.birdBodyMesh && life.birdWingMesh && life.butterflyMesh && life.birdMaterial && life.butterflyMaterial,
                "wildlife meshes and materials resolve");
            Need(audio && audio.waterLoop && audio.foliageLoop && audio.BirdClipCount >= 2, "nature recordings resolve");
            Need(Object.FindObjectsByType<FishmanAI>(FindObjectsSortMode.None).Length == 1, "one Fishman encounter");
            Need(Object.FindObjectsByType<SmilingMushroomAI>(FindObjectsSortMode.None).Length == 1, "one SmilingMushroom encounter");
            Need(Object.FindObjectsByType<BishopKnightAI>(FindObjectsSortMode.None).Length == 1, "one BishopKnight encounter");
            Need(Object.FindObjectsByType<DemonKingCombatVisual>(FindObjectsSortMode.None).Length == 1, "one DemonKing visual replacement");
            foreach (var path in new[] {
                "Assets/Art/Enemies/AdventureTrio/Generated/Fishman.prefab",
                "Assets/Art/Enemies/AdventureTrio/Generated/SmilingMushroom.prefab",
                "Assets/Art/Enemies/AdventureTrio/Generated/BishopKnight.prefab",
                "Assets/Art/Enemies/DemonKingTrial/DemonKingScoutTrial.prefab",
                "Assets/OwnHero/HeroPlayerVisual.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Need(prefab, "prefab loads: " + path);
                CheckMissingScripts(new[] { prefab });
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Need(renderer.sharedMaterials.All(m => m), "renderer materials resolve: " + renderer.name);
                    if (renderer is SkinnedMeshRenderer skin) Need(skin.sharedMesh, "skin mesh resolves: " + renderer.name);
                    if (renderer is MeshRenderer) Need(renderer.GetComponent<MeshFilter>()?.sharedMesh, "mesh resolves: " + renderer.name);
                }
            }
            Need(Resources.Load<UnityEngine.ScriptableObject>("HeroActions"), "target HeroActions library retained");
            Need(File.ReadAllText(World).Contains("mantleActionId:"), "target authored traversal schema retained");
            Need(!scene.isDirty && worldHash.SequenceEqual(File.ReadAllBytes(World)), "world scene remains byte-identical");
            EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
            CheckMissingScripts(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects());
            var stages = Object.FindFirstObjectByType<BattleArenaStages>();
            Need(stages && stages.IsAvailable(0) && stages.IsAvailable(1) && stages.IsAvailable(2), "all three Side A stages available");
            Need(stages.Apply(2), "third stage assembles through production API");
            var suspension = stages.GetComponentInChildren<BattleEclipseLevitation>();
            Need(suspension && suspension.Ready && suspension.Renderers.Length == 3, "three visual-only suspension effects assembled");
            Need(suspension.GetComponentsInChildren<Collider>(true).Length == 0, "suspension effects add no gameplay colliders");
            foreach (var name in new[] { "SideA/Eclipse/EclipseAtmosphere", "SideA/Eclipse/EclipseFighterAura", "SideA/Eclipse/EclipseLevitation" })
            {
                var shader = Resources.Load<Shader>(name);
                Need(shader && !ShaderUtil.ShaderHasError(shader), "Eclipse shader imports without errors: " + name);
            }
            foreach (var index in new[] { 0, 1, 2, 0 }) Need(stages.Apply(index), "stage switch: " + index);
            Results.AppendLine("FINAL PASS (static assembly only; no claim of combat / WebGL / full playthrough validation)");
        }
        catch (Exception error)
        {
            Results.AppendLine("FINAL FAIL " + error);
            throw;
        }
        finally
        {
            // Only changes to the unsaved test scene are discarded.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, Results.ToString());
            Debug.Log(Results.ToString());
        }
    }

    static void CheckMissingScripts(IEnumerable<GameObject> roots)
    {
        int inspected = 0;
        foreach (var root in roots)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    Need(false, "missing script: " + t.name);
                inspected++;
            }
        Need(inspected > 0, "no missing scripts across " + inspected + " inspected objects");
    }
}
