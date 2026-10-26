using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Installs cosmetic variants only after Unity has successfully imported their shader.
public static class StoneScoutVariantSetup
{
    private const string Folder = "Assets/Art/Enemies/StoneScout/Variants/";
    private const string VisualPath = "Assets/Art/Enemies/StoneScout/Animated/StoneScoutAnimatedVisual.prefab";
    private const string CombatPath = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";

    [MenuItem("Tools/Art/Set Up Stone Scout Variants")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before setting up variants.");

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "StoneScoutPaletteLit.shader");
        if (shader == null)
            throw new InvalidOperationException("Stone Scout shader was not imported.");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity.ToString() == "Error")
                throw new InvalidOperationException("Stone Scout shader: " + message.message);
        if (!shader.isSupported)
            throw new InvalidOperationException("Stone Scout shader is unsupported; original appearance was left alone.");

        Material forest = LoadMaterial("StoneScout_Forest.mat", shader);
        Material sand = LoadMaterial("StoneScout_Sand.mat", shader);
        Material awakened = LoadMaterial("StoneScout_Awakened.mat", shader);
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        StoneScoutVariantPlacement.Preflight();

        InstallVisual(forest);
        InstallCombat(forest, sand, awakened);
        StoneScoutVariantPlacement.Place();
        StoneScoutVariantChecks.Run();
        Debug.Log("Stone Scout variants installed: Forest, Sand and Awakened together in the eastern encounter area.");
    }

    private static Material LoadMaterial(string file, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + file);
        if (material == null || material.shader != shader)
            throw new InvalidOperationException("Missing or invalid variant material: " + file);
        return material;
    }

    private static void InstallVisual(Material forest)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(VisualPath);
        if (root == null)
            throw new InvalidOperationException("Animated Stone Scout visual prefab is missing.");
        try
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length != 10)
                throw new InvalidOperationException("Expected ten animated Stone Scout body pieces.");
            foreach (MeshRenderer renderer in renderers)
                renderer.sharedMaterial = forest;
            PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void InstallCombat(Material forest, Material sand, Material awakened)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CombatPath);
        if (root == null)
            throw new InvalidOperationException("Stone Scout combat prefab is missing.");
        try
        {
            StoneScoutAppearance appearance = root.GetComponent<StoneScoutAppearance>();
            if (appearance == null)
                appearance = root.AddComponent<StoneScoutAppearance>();
            var serialized = new SerializedObject(appearance);
            serialized.FindProperty("forestMaterial").objectReferenceValue = forest;
            serialized.FindProperty("sandMaterial").objectReferenceValue = sand;
            serialized.FindProperty("awakenedMaterial").objectReferenceValue = awakened;
            serialized.FindProperty("look").enumValueIndex = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            appearance.Apply();
            PrefabUtility.SaveAsPrefabAsset(root, CombatPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
