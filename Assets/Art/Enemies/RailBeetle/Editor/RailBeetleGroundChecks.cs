using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Disposable native-physics slope fixtures; no map, prefab, material or mesh asset is saved.
public static class RailBeetleGroundChecks
{
    private const string PrefabPath = "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";
    private static readonly Vector3 Origin = new Vector3(12000f, 500f, 12000f);
    private static readonly int Emission = Shader.PropertyToID("_EmissionColor");

    [MenuItem("Tools/Art/Check Rail Beetle Grounding")]
    public static void Run()
    {
        Prepare();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab, "Build the Rail Beetle prefab first.");
        var sources = new List<SampleMesh>();
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true)) sources.Add(new SampleMesh(filter));
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int frames = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach (float angle in new[] { 0f, -12f, 12f, -22f, 22f })
                frames += CheckSlope(prefab, angle);
            foreach (SampleMesh source in sources) source.CheckBindVertices();
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            Physics.SyncTransforms();
        }
        Debug.Log("Rail Beetle grounding PASS: five real-collider slopes, " + frames +
            " live mesh poses, planted soles, physical X/Z envelope, grounded corpse, exact bind reset, " +
            "red-dominant charge and extinguished death emission. No scene or asset was saved.");
    }

    private static int CheckSlope(GameObject prefab, float angle)
    {
        GameObject floorObject = new GameObject("Rail Beetle QA slope " + angle);
        GameObject instance = null;
        try
        {
            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            Vector3 normal = rotation * Vector3.up;
            floorObject.transform.SetPositionAndRotation(Origin - normal * .5f, rotation);
            BoxCollider floor = floorObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(30f, 1f, 30f);
            instance = UnityEngine.Object.Instantiate(prefab);
            instance.GetComponent<RailBeetleAI>().enabled = false;
            RailBeetleVisual visual = instance.GetComponentInChildren<RailBeetleVisual>(true);
            var data = new SerializedObject(visual);
            data.FindProperty("impactClip").objectReferenceValue = null;
            data.FindProperty("windupClip").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            BoxCollider box = instance.GetComponent<BoxCollider>();
            float high = (box.size.z * .5f - .03f) * instance.transform.lossyScale.z * Mathf.Abs(Mathf.Tan(angle * Mathf.Deg2Rad));
            instance.transform.position = Origin + Vector3.up * (high + .015f);
            Physics.SyncTransforms();
            Require(floor.Raycast(new Ray(Origin + Vector3.up * 2f, Vector3.down), out RaycastHit hit, 4f),
                "QA slope has no native support surface.");
            Plane ground = new Plane(hit.normal, hit.point);
            visual.ResetVisual(); // Initialises the same bind cache used by the runtime, without private-field reflection.
            var bind = new List<Pose>();
            bind.Add(new Pose((Transform)new SerializedObject(visual).FindProperty("visualBody").objectReferenceValue));
            MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
            var meshes = new List<SampleMesh>();
            foreach (MeshFilter filter in filters) { meshes.Add(new SampleMesh(filter)); bind.Add(new Pose(filter.transform)); }
            SerializedProperty legs = new SerializedObject(visual).FindProperty("legs");
            var feet = new HashSet<Transform>();
            for (int i = 0; i < legs.arraySize; i++) feet.Add((Transform)legs.GetArrayElementAtIndex(i).objectReferenceValue);
            Require(feet.Count == 4, "All four leg references are needed for sole checks.");
            var root = new RootState(instance);
            int frames = 0;
            for (int mode = 0; mode < 6; mode++)
            {
                visual.ResetVisual();
                visual.SetMotion(mode == 1 || mode == 4 ? 2.5f : mode == 3 ? 9f : 0f);
                visual.SetAttack(mode == 2 || mode == 3 ? 1f : 0f, mode == 3);
                visual.SetStunned(mode == 5);
                visual.RefreshGroundingForChecks();
                for (int frame = 0; frame < 64; frame++)
                {
                    if (mode == 4 && frame % 12 == 0) visual.PlayHit(instance.transform.position + Vector3.left);
                    visual.SamplePoseForChecks(1f / 60f);
                    CheckMeshes(meshes, feet, ground, instance.transform, box, true,
                        angle + " deg mode " + mode + " frame " + frame);
                    root.Check(instance);
                    frames++;
                }
                if (mode == 2) CheckEmission(visual, false);
            }
            visual.PlayDeath();
            for (int frame = 0; frame < 42; frame++)
            {
                visual.SamplePoseForChecks(1f / 60f);
                CheckMeshes(meshes, feet, ground, instance.transform, box, false,
                    angle + " deg death frame " + frame);
                root.Check(instance);
            }
            SampleMesh chassis = meshes.Find(value => value.Filter.name == "Graphite chassis");
            Require(chassis != null, "Missing chassis mesh for final corpse support check.");
            float corpseGap = MinimumGap(chassis, ground);
            Require(corpseGap >= .005f && corpseGap <= .055f,
                angle + " deg corpse chassis must settle near .025 m, actual gap " + corpseGap.ToString("F4"));
            CheckEmission(visual, true);
            visual.ResetVisual();
            foreach (Pose pose in bind) pose.Check();
            root.Check(instance);
            foreach (SampleMesh mesh in meshes)
                mesh.CheckBindVertices(); // A reused instance-only deformation mesh is allowed; source assets are checked above.
            return frames;
        }
        finally
        {
            if (instance) UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(floorObject);
            Physics.SyncTransforms();
        }
    }

    private static void CheckMeshes(List<SampleMesh> meshes, HashSet<Transform> feet, Plane ground,
        Transform root, BoxCollider box, bool live, string context)
    {
        foreach (SampleMesh mesh in meshes)
        {
            float lowest = float.PositiveInfinity;
            Matrix4x4 world = mesh.Filter.transform.localToWorldMatrix;
            foreach (Vector3 vertex in mesh.ReadCurrent())
            {
                Vector3 point = world.MultiplyPoint3x4(vertex);
                lowest = Mathf.Min(lowest, ground.GetDistanceToPoint(point) / ground.normal.y);
                Vector3 local = root.InverseTransformPoint(point) - box.center;
                Require(Mathf.Abs(local.x) <= box.size.x * .5f + .008f &&
                        Mathf.Abs(local.z) <= box.size.z * .5f + .008f,
                    context + ": geometry exceeds its actual foot/shell collider: " + mesh.Filter.name +
                    ", bind=" + vertex.ToString("F4") + ", root local=" + local.ToString("F4"));
            }
            Require(lowest >= -.005f, context + ": ground penetration on " + mesh.Filter.name + ": " + lowest.ToString("F4"));
            if (live && feet.Contains(mesh.Filter.transform))
                Require(lowest <= .06f, context + ": floating sole on " + mesh.Filter.name + ": " + lowest.ToString("F4"));
        }
    }

    private static float MinimumGap(SampleMesh mesh, Plane ground)
    {
        float lowest = float.PositiveInfinity;
        foreach (Vector3 vertex in mesh.ReadCurrent())
            lowest = Mathf.Min(lowest, ground.GetDistanceToPoint(mesh.Filter.transform.TransformPoint(vertex)) / ground.normal.y);
        return lowest;
    }

    private static void CheckEmission(RailBeetleVisual visual, bool dead)
    {
        SerializedProperty cores = new SerializedObject(visual).FindProperty("coreRenderers");
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < cores.arraySize; i++)
        {
            Renderer renderer = (Renderer)cores.GetArrayElementAtIndex(i).objectReferenceValue;
            renderer.GetPropertyBlock(block);
            Color emission = block.GetColor(Emission);
            if (dead) Require(Mathf.Max(emission.r, emission.g, emission.b) <= .005f,
                "A dead beetle must extinguish its core emission.");
            else Require(emission.r > .5f && emission.r > emission.g * 2f && emission.b < .35f &&
                !(emission.r >= 1f && emission.g >= 1f && emission.b >= 1f),
                "Full windup emission must remain red-dominant orange, not clipped white: " + emission);
        }
    }

    private sealed class SampleMesh
    {
        public readonly MeshFilter Filter; public readonly Mesh Asset; public readonly Vector3[] Vertices;
        private readonly List<Vector3> current = new List<Vector3>();
        public SampleMesh(MeshFilter filter) { Filter = filter; Asset = filter.sharedMesh; Vertices = Asset.vertices; }
        public List<Vector3> ReadCurrent() { Filter.sharedMesh.GetVertices(current); return current; }
        public void CheckBindVertices() { List<Vector3> actual = ReadCurrent();
            Require(actual.Count == Vertices.Length, "Grounding must preserve mesh topology: " + Filter.name);
            for (int i = 0; i < actual.Count; i++) Require((actual[i] - Vertices[i]).sqrMagnitude < .00000001f,
                "Source or reset bind vertices changed: " + Filter.name + " vertex " + i); }
    }
    private readonly struct Pose
    {
        private readonly Transform target; private readonly Vector3 position, scale; private readonly Quaternion rotation;
        public Pose(Transform value) { target = value; position = value.localPosition; scale = value.localScale; rotation = value.localRotation; }
        public void Check() { Require(Vector3.Distance(target.localPosition, position) < .0001f &&
            Vector3.Distance(target.localScale, scale) < .0001f && Quaternion.Angle(target.localRotation, rotation) < .01f,
            "Reset must restore the exact bind pose: " + target.name); }
    }
    private readonly struct RootState
    {
        private readonly Vector3 position, scale, capsuleCenter, boxCenter, boxSize;
        private readonly Vector3 bodyPosition; private readonly Quaternion bodyRotation;
        private readonly Quaternion rotation; private readonly float radius, height;
        public RootState(GameObject value) { var c = value.GetComponent<CapsuleCollider>(); var b = value.GetComponent<BoxCollider>();
            position = value.transform.position; rotation = value.transform.rotation; scale = value.transform.localScale;
            var body = value.GetComponent<Rigidbody>(); bodyPosition = body.position; bodyRotation = body.rotation;
            capsuleCenter = c.center; radius = c.radius; height = c.height; boxCenter = b.center; boxSize = b.size; }
        public void Check(GameObject value) { var c = value.GetComponent<CapsuleCollider>(); var b = value.GetComponent<BoxCollider>();
            var body = value.GetComponent<Rigidbody>();
            Require(value.transform.position == position && value.transform.rotation == rotation && value.transform.localScale == scale &&
                body.position == bodyPosition && body.rotation == bodyRotation &&
                c.enabled && b.enabled && c.center == capsuleCenter && c.radius == radius && c.height == height &&
                b.center == boxCenter && b.size == boxSize, "Cosmetic grounding must not alter the root or physical collision shapes."); }
    }

    private static void Prepare()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before running disposable ground checks.");
        bool untitled = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            Require(!scene.isDirty, "Save modified scenes before checking; existing edits were left untouched.");
            untitled |= string.IsNullOrEmpty(scene.path);
        }
        if (!untitled) return;
        Require(Application.isBatchMode, "Save the untitled scene or open OverworldBaseline first; no edits were discarded.");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); // Read-only clean batch startup.
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
