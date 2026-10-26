using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StoneScoutVariantChecks
{
    private const string VariantFolder = "Assets/Art/Enemies/StoneScout/Variants/";
    private const string VisualPath = "Assets/Art/Enemies/StoneScout/Animated/StoneScoutAnimatedVisual.prefab";
    private const string CombatPath = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";

    [MenuItem("Tools/Art/Check Stone Scout Variants")]
    public static void Run()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(VariantFolder + "StoneScoutPaletteLit.shader");
        if (shader == null)
            throw new InvalidOperationException("Stone Scout variant shader is missing.");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity.ToString() == "Error")
                throw new InvalidOperationException("Stone Scout shader compile error: " + message.message);
        if (!shader.isSupported)
            throw new InvalidOperationException("Stone Scout variant shader is unsupported by this editor platform.");

        Material[] materials =
        {
            AssetDatabase.LoadAssetAtPath<Material>(VariantFolder + "StoneScout_Forest.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(VariantFolder + "StoneScout_Sand.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(VariantFolder + "StoneScout_Awakened.mat")
        };
        foreach (Material material in materials)
            if (material == null || material.shader != shader)
                throw new InvalidOperationException("A Stone Scout variant material is missing or uses another shader.");

        GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
        GameObject combat = AssetDatabase.LoadAssetAtPath<GameObject>(CombatPath);
        if (visual == null || combat == null)
            throw new InvalidOperationException("Stone Scout visual or combat prefab is missing.");
        MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 10)
            throw new InvalidOperationException("Stone Scout no longer has ten animated mesh pieces.");
        foreach (MeshRenderer renderer in renderers)
            if (renderer.sharedMaterial != materials[0])
                throw new InvalidOperationException("Animated visual is not using the forest default material.");

        StoneScoutAppearance appearance = combat.GetComponent<StoneScoutAppearance>();
        if (appearance == null)
            throw new InvalidOperationException("Combat prefab has no cosmetic variant selector.");
        GameObject preview = UnityEngine.Object.Instantiate(combat);
        try
        {
            for (int i = 0; i < materials.Length; i++)
            {
                preview.GetComponent<StoneScoutAppearance>().SetLook((StoneScoutAppearance.Look)i);
                foreach (MeshRenderer renderer in preview.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.sharedMaterial != materials[i])
                        throw new InvalidOperationException("Variant did not recolor every body piece.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(preview); }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Transform group = null;
            MeshCollider ground = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "_EnemyCombatPreview") group = root.transform;
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.name == "LakeTerrain_REPLACE_OR_SCULPT") ground = collider;
            }
            bool regional = EnemyDistributionInstaller.HasLayout(scene);
            if (group == null || group.childCount != 6 || (!regional && ground == null))
                throw new InvalidOperationException("Expected six grouped Stone Scouts and the terrain collider.");

            int[] counts = new int[3];
            Bounds area = new Bounds(group.GetChild(0).position, Vector3.zero);
            for (int i = 0; i < group.childCount; i++)
            {
                Transform enemy = group.GetChild(i);
                StoneScoutAppearance look = enemy.GetComponent<StoneScoutAppearance>();
                if (look == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(enemy.gameObject) != CombatPath)
                    throw new InvalidOperationException("A grouped enemy is not the shared combat prefab.");
                counts[(int)look.CurrentLook]++;
                area.Encapsulate(enemy.position);
                if (!regional && (!ground.Raycast(new Ray(enemy.position + Vector3.up * 80f, Vector3.down),
                        out RaycastHit hit, 160f) ||
                    Mathf.Abs(enemy.position.y - hit.point.y - 0.015f) > 0.05f ||
                    Vector3.Angle(hit.normal, Vector3.up) > 35f))
                    throw new InvalidOperationException(enemy.name + " is not on walkable terrain.");
            }
            if (counts[0] != 2 || counts[1] != 2 || counts[2] != 2 ||
                (!regional && (area.size.x > 20f || area.size.z > 30f)))
                throw new InvalidOperationException("Stone Scouts are not grouped two per look in one area.");
            if (regional) EnemyDistributionInstaller.ValidateAuthoring();
            Debug.Log("Stone Scout variants PASS: one shader, three materials, shared prefab, six grounded instances; regional placement preserved when authored.");
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }
}
