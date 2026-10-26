using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reproducible setup and build entry points for the chest demo.</summary>
public static class ChestDemoSetup
{
    public const string ScenePath = "Assets/_Demo/Scenes/GameplayFeatureDemoScene.unity";
    public const string ChestPath = "Assets/_Demo/Prefabs/Chest/DigitalDissolveChest.prefab";
    private const string MaterialDirectory = "Assets/_Demo/Materials/Chest";
    private const string MeshDirectory = "Assets/_Demo/Models/Chest";

    [MenuItem("ArtLab/Chest/Install demo in gameplay scene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before installing the chest demo.");
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (activeScene.path != ScenePath)
        {
            if (activeScene.isDirty)
                throw new InvalidOperationException("Save the active scene before installing the chest demo.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        ChestUrpMigration.Migrate();
        EnsureDirectory(MaterialDirectory);
        EnsureDirectory(MeshDirectory);
        EnsureDirectory("Assets/_Demo/Prefabs/Chest");
        Shader shader = Shader.Find("ArtLab/Chest Dissolve");
        if (shader == null) throw new InvalidOperationException("Chest Dissolve shader failed to import.");
        Material wood = MakeMaterial("ChestDissolve_Wood", shader, new Color(.45f, .23f, .095f), new Color(.12f, .085f, .075f), .12f);
        Material metal = MakeMaterial("ChestDissolve_Brass", shader, new Color(.62f, .43f, .17f), new Color(.17f, .115f, .055f), .65f);

        // The prefab is created once. Re-running the installer preserves artist edits.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ChestPath) == null)
        {
            GameObject root = new GameObject("DigitalDissolveChest");
            try
            {
                for (int i = 0; i < 4; i++)
                    Box(root, "BodyPlank" + (i + 1), new Vector3(0, .13f + i * .158f, 0), new Vector3(1.6f, .15f, 1f), wood);
                Box(root, "BaseFrame", new Vector3(0, .075f, 0), new Vector3(1.7f, .13f, 1.1f), metal);
                Box(root, "LidRim", new Vector3(0, .71f, 0), new Vector3(1.7f, .095f, 1.1f), metal);
                MeshPart(root, "ArchedLid", MakeLidMesh("ArchedLid", 1.6f, .5f, .31f), new Vector3(0, .735f, 0), wood);
                foreach (float x in new[] { -.57f, .57f })
                {
                    MeshPart(root, "LidBand", MakeLidMesh("LidBand", .095f, .514f, .325f), new Vector3(x, .738f, 0), metal);
                    foreach (float z in new[] { -.516f, .516f })
                        Box(root, "BodyBand", new Vector3(x, .39f, z), new Vector3(.095f, .56f, .05f), metal);
                }
                Box(root, "LockPlate", new Vector3(0, .63f, -.56f), new Vector3(.19f, .26f, .075f), metal);
                Box(root, "Latch", new Vector3(0, .63f, -.612f), new Vector3(.07f, .105f, .075f), wood);
                foreach (float x in new[] { -.83f, .83f })
                    Box(root, "SideHandle", new Vector3(x, .43f, 0), new Vector3(.085f, .105f, .32f), metal);
                BoxCollider body = root.AddComponent<BoxCollider>();
                body.center = new Vector3(0, .54f, 0);
                body.size = new Vector3(1.77f, 1.08f, 1.15f);
                root.AddComponent<NonClimbableSurface>();
                root.AddComponent<ChestScanSpace>();
                root.AddComponent<ChestDissolveController>().Configure();
                PrefabUtility.SaveAsPrefabAsset(root, ChestPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        GameObject chest = GameObject.Find("DigitalDissolveChest");
        if (chest == null)
        {
            chest = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ChestPath));
            chest.transform.position = new Vector3(2.5f, .12f, -13.8f);
        }
        var controller = chest.GetComponent<ChestDissolveController>();
        controller.Configure(UnityEngine.Object.FindFirstObjectByType<PlayerMovement>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(chest.GetComponent<ChestScanSpace>());

        if (GameObject.Find("ChestDemoPlinth") == null)
        {
            GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "ChestDemoPlinth";
            plinth.transform.position = new Vector3(2.5f, .06f, -13.8f);
            plinth.transform.localScale = new Vector3(3.4f, .12f, 3f);
            Material ground = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ground.name = "ChestDemoPlinth";
            ground.SetColor("_BaseColor", new Color(.4f, .47f, .51f));
            ground.SetFloat("_Smoothness", .15f);
            string path = MaterialDirectory + "/ChestDemoPlinth.mat";
            Material saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (saved == null) { AssetDatabase.CreateAsset(ground, path); saved = ground; }
            else UnityEngine.Object.DestroyImmediate(ground);
            plinth.GetComponent<Renderer>().sharedMaterial = saved;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(chest.scene);
        EditorSceneManager.SaveScene(chest.scene);
        Debug.Log("CHEST_SETUP_COMPLETE: " + ChestPath + " in " + ScenePath);
    }

    [MenuItem("ArtLab/Chest/Build WebGL validation player")]
    public static void BuildWebGL()
    {
        ChestDissolveValidation.Validate();
        string output = Path.GetFullPath("Builds/ChestDissolveWebGL");
        Directory.CreateDirectory(output);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        Directory.CreateDirectory("ToolReports/ChestDissolve");
        File.WriteAllText("ToolReports/ChestDissolve/build-result.txt",
            report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings +
            "\nBytes: " + report.summary.totalSize + "\nDuration: " + report.summary.totalTime);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("WebGL build failed: " + report.summary.result);
        Debug.Log("CHEST_WEBGL_BUILD_SUCCEEDED: " + output);
    }

    private static Material MakeMaterial(string name, Shader shader, Color lit, Color shade, float highlight)
    {
        string path = MaterialDirectory + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", lit);
        material.SetColor("_ShadeColor", shade);
        material.SetFloat("_HighlightStrength", highlight);
        material.SetFloat("_Progress", 0);
        material.SetVector("_ScanDirection", new Vector4(0, -1, 0, 0));
        material.SetColor("_GlowColor", new Color(.08f, .8f, 1f));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Box(GameObject root, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        SetRenderer(part.GetComponent<Renderer>(), material);
    }

    private static void MeshPart(GameObject root, string name, Mesh mesh, Vector3 position, Material material)
    {
        GameObject part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        SetRenderer(part.GetComponent<Renderer>(), material);
    }

    private static void SetRenderer(Renderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static Mesh MakeLidMesh(string name, float width, float depthRadius, float heightRadius)
    {
        string path = MeshDirectory + "/" + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        const int segments = 10;
        float half = width * .5f;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI / segments;
            float b = (i + 1) * Mathf.PI / segments;
            Vector3 p = new Vector3(0, Mathf.Sin(a) * heightRadius, Mathf.Cos(a) * depthRadius);
            Vector3 q = new Vector3(0, Mathf.Sin(b) * heightRadius, Mathf.Cos(b) * depthRadius);
            AddQuad(vertices, triangles, p + Vector3.left * half, p + Vector3.right * half,
                q + Vector3.right * half, q + Vector3.left * half);
            AddTriangle(vertices, triangles, Vector3.left * half, p + Vector3.left * half, q + Vector3.left * half);
            AddTriangle(vertices, triangles, Vector3.right * half, q + Vector3.right * half, p + Vector3.right * half);
        }
        AddQuad(vertices, triangles, new Vector3(-half, 0, -depthRadius), new Vector3(half, 0, -depthRadius),
            new Vector3(half, 0, depthRadius), new Vector3(-half, 0, depthRadius));
        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int index = vertices.Count;
        vertices.AddRange(new[] { a, b, c, d });
        triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
    }

    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int index = vertices.Count;
        vertices.AddRange(new[] { a, b, c });
        triangles.AddRange(new[] { index, index + 1, index + 2 });
    }

    private static void EnsureDirectory(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
