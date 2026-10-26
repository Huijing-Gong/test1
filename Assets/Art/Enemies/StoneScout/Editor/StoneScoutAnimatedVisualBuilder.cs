// Builds a segmented, visual-only enemy from the already-authored OBJ part labels.
// It never edits the source OBJ, the Basic Motions package, or the existing prefab.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class StoneScoutAnimatedVisualBuilder
{
    private const string Root = "Assets/Art/Enemies/StoneScout";
    private const string SourceObj = Root + "/StoneScout_Blockout.obj";
    private const string MaterialPath = Root + "/StoneScout_URP.mat";
    private const string OutputFolder = Root + "/Animated";
    private const string MeshFolder = OutputFolder + "/Meshes";
    private const string AnimationFolder = OutputFolder + "/Animations";
    private const string PrefabPath = OutputFolder + "/StoneScoutAnimatedVisual.prefab";
    private const string ControllerPath = AnimationFolder + "/StoneScoutVisual.controller";
    private const string FallPivotName = "FallPivot";

    private static readonly string[] SegmentNames =
    {
        "Torso", "Head", "LeftUpperArm", "LeftForearm", "RightUpperArm",
        "RightForearm", "LeftThigh", "LeftShin", "RightThigh", "RightShin"
    };

    private sealed class SegmentData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
    }

    [InitializeOnLoadMethod]
    private static void BuildAfterImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                !AssetDatabase.IsValidFolder(OutputFolder))
                Build();
        };
    }

    [MenuItem("Tools/Art/Build Animated Stone Scout Visual")]
    public static void Build()
    {
        if (AssetDatabase.IsValidFolder(OutputFolder))
        {
            Debug.LogError("Animated Stone Scout output already exists. Review it before rebuilding; nothing was overwritten.");
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || !File.Exists(SourceObj))
        {
            Debug.LogError("Stone Scout needs its source OBJ and existing URP material.");
            return;
        }

        Dictionary<string, SegmentData> segments;
        try
        {
            segments = ParseSourceObj(SourceObj);
            foreach (string segmentName in SegmentNames)
            {
                if (!segments.TryGetValue(segmentName, out var data) || data.Triangles.Count == 0)
                    throw new InvalidDataException("Missing geometry for " + segmentName);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("Could not split Stone Scout OBJ: " + exception.Message);
            return;
        }

        AssetDatabase.CreateFolder(Root, "Animated");
        AssetDatabase.CreateFolder(OutputFolder, "Meshes");
        AssetDatabase.CreateFolder(OutputFolder, "Animations");

        var root = new GameObject("StoneScoutAnimatedVisual");
        root.transform.localScale = Vector3.one * 0.8f;
        try
        {
            var meshes = new Dictionary<string, Mesh>();
            foreach (string segmentName in SegmentNames)
            {
                var data = segments[segmentName];
                var mesh = new Mesh { name = "StoneScout_" + segmentName };
                mesh.SetVertices(data.Vertices);
                mesh.SetUVs(0, data.Uvs);
                mesh.SetNormals(data.Normals);
                mesh.SetTriangles(data.Triangles, 0);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, MeshFolder + "/" + mesh.name + ".asset");
                meshes.Add(segmentName, mesh);
            }

            var fallPivot = new GameObject(FallPivotName).transform;
            fallPivot.SetParent(root.transform, false);
            var torso = CreateSegment(fallPivot, "Torso", Vector3.zero, material, meshes);
            var head = CreateSegment(torso, "Head", GetPivot("Torso"), material, meshes);
            var leftUpperArm = CreateSegment(torso, "LeftUpperArm", GetPivot("Torso"), material, meshes);
            var leftForearm = CreateSegment(leftUpperArm, "LeftForearm", GetPivot("LeftUpperArm"), material, meshes);
            var rightUpperArm = CreateSegment(torso, "RightUpperArm", GetPivot("Torso"), material, meshes);
            var rightForearm = CreateSegment(rightUpperArm, "RightForearm", GetPivot("RightUpperArm"), material, meshes);
            var leftThigh = CreateSegment(fallPivot, "LeftThigh", Vector3.zero, material, meshes);
            var leftShin = CreateSegment(leftThigh, "LeftShin", GetPivot("LeftThigh"), material, meshes);
            var rightThigh = CreateSegment(fallPivot, "RightThigh", Vector3.zero, material, meshes);
            var rightShin = CreateSegment(rightThigh, "RightShin", GetPivot("RightThigh"), material, meshes);

            AddAnchor(head, "EyeVFXAnchor", new Vector3(0f, 1.55f, 0.26f) - GetPivot("Head"));
            AddAnchor(torso, "HitVFXAnchor", new Vector3(0f, 1.08f, 0.16f) - GetPivot("Torso"));
            AddAnchor(root.transform, "FootstepVFXAnchor", new Vector3(0f, 0.04f, 0f));

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = CreateController();
            animator.applyRootMotion = false;
            var motion = root.AddComponent<StoneScoutVisualMotion>();
            motion.Configure(animator, torso, head, leftUpperArm, leftForearm,
                rightUpperArm, rightForearm, leftThigh, leftShin, rightThigh, rightShin);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Created visual-only animated Stone Scout prefab at " + PrefabPath);
        }
        catch (Exception exception)
        {
            Debug.LogError("Animated Stone Scout build stopped; inspect the new output folder before retrying: " + exception);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static AnimatorController CreateController()
    {
        var idle = CreateClip("Idle", 2.4f, true);
        Rotate(idle, "Torso", "x", new[] { 0f, 1.2f, 2.4f }, new[] { 0f, 2f, 0f });
        Rotate(idle, "Torso/Head", "x", new[] { 0f, 1.2f, 2.4f }, new[] { 0f, -2f, 0f });
        SaveClip(idle);

        var walk = CreateClip("Walk", 0.8f, true);
        float[] steps = { 0f, 0.2f, 0.4f, 0.6f, 0.8f };
        Rotate(walk, "LeftThigh", "x", steps, new[] { 0f, 23f, 0f, -23f, 0f });
        Rotate(walk, "RightThigh", "x", steps, new[] { 0f, -23f, 0f, 23f, 0f });
        Rotate(walk, "LeftThigh/LeftShin", "x", steps, new[] { 0f, 0f, 0f, 18f, 0f });
        Rotate(walk, "RightThigh/RightShin", "x", steps, new[] { 0f, 18f, 0f, 0f, 0f });
        Rotate(walk, "Torso/LeftUpperArm", "x", steps, new[] { 0f, -17f, 0f, 17f, 0f });
        Rotate(walk, "Torso/RightUpperArm", "x", steps, new[] { 0f, 17f, 0f, -17f, 0f });
        Rotate(walk, "Torso", "z", steps, new[] { 0f, 2.5f, 0f, -2.5f, 0f });
        Position(walk, "Torso", "y", steps, new[] { 0.85f, 0.868f, 0.85f, 0.868f, 0.85f });
        SaveClip(walk);

        var attack = CreateClip("Attack", 0.72f, false);
        ApplyPunchPose(attack);
        SaveClip(attack);

        var hit = CreateClip("Hit", 0.38f, false);
        float[] recoil = { 0f, 0.15f, 0.38f };
        Rotate(hit, "Torso", "x", recoil, new[] { 0f, -16f, 0f });
        Rotate(hit, "Torso", "z", recoil, new[] { 0f, 8f, 0f });
        Rotate(hit, "Torso/Head", "x", recoil, new[] { 0f, 14f, 0f });
        Rotate(hit, "Torso/LeftUpperArm", "x", recoil, new[] { 0f, 20f, 0f });
        Rotate(hit, "Torso/RightUpperArm", "x", recoil, new[] { 0f, 20f, 0f });
        SaveClip(hit);

        var death = CreateClip("Death", 0.85f, false);
        ApplyDeathPose(death);
        SaveClip(death);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var idleState = machine.AddState("Idle");
        var walkState = machine.AddState("Walk");
        var attackState = machine.AddState("Attack");
        var hitState = machine.AddState("Hit");
        var deathState = machine.AddState("Death");
        idleState.motion = idle;
        walkState.motion = walk;
        attackState.motion = attack;
        hitState.motion = hit;
        deathState.motion = death;
        machine.defaultState = idleState;

        var startWalk = idleState.AddTransition(walkState);
        startWalk.hasExitTime = false;
        startWalk.duration = 0.12f;
        startWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
        var stopWalk = walkState.AddTransition(idleState);
        stopWalk.hasExitTime = false;
        stopWalk.duration = 0.12f;
        stopWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
        AddTrigger(machine, attackState, "Attack", 0.08f);
        AddTrigger(machine, hitState, "Hit", 0.04f);
        AddTrigger(machine, deathState, "Die", 0.04f);
        AddReturn(attackState, idleState);
        AddReturn(hitState, idleState);
        return controller;
    }

    [MenuItem("Tools/Art/Refresh Stone Scout Combat Clips")]
    public static void RefreshCombatClips()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + "/Attack.anim");
        AnimationClip hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + "/Hit.anim");
        if (!attack || !hit) throw new InvalidOperationException("Existing combat clips missing; nothing rebuilt.");
        ApplyPunchPose(attack);
        // The visual component adds a direction-aware root tilt; don't add a second large recoil.
        Rotate(hit, "Torso", "x", new[] { 0f, .15f, .38f }, new[] { 0f, -8f, 0f });
        Rotate(hit, "Torso", "z", new[] { 0f, .15f, .38f }, new[] { 0f, 0f, 0f });
        EditorUtility.SetDirty(attack); EditorUtility.SetDirty(hit); AssetDatabase.SaveAssets();
        Debug.Log("Stone Scout combat clips refreshed in place; death, meshes, prefab GUIDs retained.");
    }

    private static void ApplyPunchPose(AnimationClip attack)
    {
        float[] times = { 0f, .22f, .32f, .43f, .54f, .72f };
        Rotate(attack, "Torso/RightUpperArm", "x", times, new[] { 0f, 32f, 38f, -88f, -68f, 0f });
        Rotate(attack, "Torso/RightUpperArm/RightForearm", "x", times, new[] { 0f, -8f, -12f, -35f, -22f, 0f });
        Rotate(attack, "Torso/LeftUpperArm", "x", times, new[] { 0f, -12f, -12f, -20f, -15f, 0f });
        Rotate(attack, "Torso", "y", times, new[] { 0f, -9f, -12f, -12f, -5f, 0f });
        Rotate(attack, "Torso", "x", times, new[] { 0f, -2f, -3f, 4f, 2f, 0f });
    }

    private static AnimationClip CreateClip(string name, float duration, bool loop)
    {
        var clip = new AnimationClip { name = name, frameRate = 30f };
        string[] paths =
        {
            "Torso", "Torso/Head", "Torso/LeftUpperArm", "Torso/LeftUpperArm/LeftForearm",
            "Torso/RightUpperArm", "Torso/RightUpperArm/RightForearm",
            "LeftThigh", "LeftThigh/LeftShin", "RightThigh", "RightThigh/RightShin"
        };
        foreach (string path in paths)
        {
            foreach (string axis in new[] { "x", "y", "z" })
                Rotate(clip, path, axis, new[] { 0f, duration }, new[] { 0f, 0f });
        }
        Position(clip, "Torso", "x", new[] { 0f, duration }, new[] { 0f, 0f });
        Position(clip, "Torso", "y", new[] { 0f, duration }, new[] { 0.85f, 0.85f });
        Position(clip, "Torso", "z", new[] { 0f, duration }, new[] { 0f, 0f });
        AddNeutralFallPivot(clip, duration);
        AssetDatabase.CreateAsset(clip, AnimationFolder + "/" + name + ".anim");
        var serialized = new SerializedObject(clip);
        var loopTime = serialized.FindProperty("m_AnimationClipSettings.m_LoopTime");
        if (loopTime == null)
            throw new InvalidOperationException("Could not set animation loop flag for " + name);
        loopTime.boolValue = loop;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return clip;
    }

    private static void SaveClip(AnimationClip clip)
    {
        EditorUtility.SetDirty(clip);
    }

    private static void Rotate(AnimationClip clip, string path, string axis, float[] times, float[] values)
    {
        SetCurve(clip, FallPivotName + "/" + path, "localEulerAnglesRaw." + axis, times, values);
    }

    private static void Position(AnimationClip clip, string path, string axis, float[] times, float[] values)
    {
        SetCurve(clip, FallPivotName + "/" + path, "m_LocalPosition." + axis, times, values);
    }

    private static void AddNeutralFallPivot(AnimationClip clip, float duration)
    {
        float[] times = { 0f, duration };
        float[] zero = { 0f, 0f };
        foreach (string axis in new[] { "x", "y", "z" })
            SetCurve(clip, FallPivotName, "localEulerAnglesRaw." + axis, times, zero);
        SetCurve(clip, FallPivotName, "m_LocalPosition.y", times, zero);
    }

    private static void ApplyDeathPose(AnimationClip death)
    {
        float[] collapse = { 0f, 0.22f, 0.58f, 0.85f };
        // Tip the whole segmented body around its feet; the torso must not
        // sink through the separate legs as it did in the first prototype.
        SetCurve(death, FallPivotName, "localEulerAnglesRaw.x", collapse,
            new[] { 0f, 4f, 50f, 82f });
        SetCurve(death, FallPivotName, "localEulerAnglesRaw.y", collapse,
            new[] { 0f, 0f, 45f, 85f });
        SetCurve(death, FallPivotName, "m_LocalPosition.y", collapse,
            new[] { 0f, 0f, 0.05f, 0.12f });
        Rotate(death, "Torso", "x", collapse, new[] { 0f, -5f, -8f, 0f });
        Rotate(death, "Torso", "z", collapse, new[] { 0f, 0f, 0f, 0f });
        Position(death, "Torso", "y", collapse, new[] { 0.85f, 0.83f, 0.85f, 0.85f });
        Position(death, "Torso", "z", collapse, new[] { 0f, 0f, 0f, 0f });
        Rotate(death, "Torso/Head", "x", collapse, new[] { 0f, -3f, -8f, -10f });
        Rotate(death, "Torso/LeftUpperArm", "x", collapse, new[] { 0f, -5f, -22f, -35f });
        Rotate(death, "Torso/RightUpperArm", "x", collapse, new[] { 0f, -5f, -18f, -28f });
        Rotate(death, "LeftThigh", "x", collapse, new[] { 0f, -2f, -8f, -10f });
        Rotate(death, "RightThigh", "x", collapse, new[] { 0f, 2f, 7f, 8f });
        Rotate(death, "LeftThigh/LeftShin", "x", collapse, new[] { 0f, 4f, 16f, 20f });
        Rotate(death, "RightThigh/RightShin", "x", collapse, new[] { 0f, 4f, 14f, 18f });
    }

    private static void SetCurve(AnimationClip clip, string path, string property, float[] times, float[] values)
    {
        if (times.Length != values.Length)
            throw new ArgumentException("Animation key count mismatch.");
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new Keyframe(times[i], values[i]);
        clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(keys));
    }

    private static void AddTrigger(AnimatorStateMachine machine, AnimatorState target, string parameter, float duration)
    {
        var transition = machine.AddAnyStateTransition(target);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void AddReturn(AnimatorState source, AnimatorState idle)
    {
        var transition = source.AddTransition(idle);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0.1f;
    }

    private static Transform CreateSegment(
        Transform parent, string name, Vector3 parentPivot,
        Material material, Dictionary<string, Mesh> meshes)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        node.localPosition = GetPivot(name) - parentPivot;
        node.gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[name];
        node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        return node;
    }

    private static void AddAnchor(Transform parent, string name, Vector3 localPosition)
    {
        var anchor = new GameObject(name).transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = localPosition;
    }

    private static Vector3 GetPivot(string segment)
    {
        switch (segment)
        {
            case "Torso": return new Vector3(0f, 0.85f, 0f);
            case "Head": return new Vector3(0f, 1.42f, 0.015f);
            case "LeftUpperArm": return new Vector3(-0.34f, 1.28f, 0f);
            case "LeftForearm": return new Vector3(-0.405f, 0.94f, 0.015f);
            case "RightUpperArm": return new Vector3(0.34f, 1.28f, 0f);
            case "RightForearm": return new Vector3(0.405f, 0.94f, 0.015f);
            case "LeftThigh": return new Vector3(-0.16f, 0.82f, 0f);
            case "LeftShin": return new Vector3(-0.19f, 0.47f, 0.02f);
            case "RightThigh": return new Vector3(0.16f, 0.82f, 0f);
            case "RightShin": return new Vector3(0.19f, 0.47f, 0.02f);
            default: throw new InvalidDataException("Unknown Stone Scout segment: " + segment);
        }
    }

    private static string SegmentForPart(string part)
    {
        if (part == "moss_left_knee") return "LeftShin";
        if (part == "moss_left_shoulder") return "LeftUpperArm";
        if (part == "neck" || part == "moss_head" || part.StartsWith("head_", StringComparison.Ordinal) ||
            part == "pointed_chin" || part == "eye_socket" || part == "amber_eye" || part == "eye_glint")
            return "Head";

        string side;
        string suffix;
        if (part.StartsWith("L_", StringComparison.Ordinal))
        {
            side = "Left";
            suffix = part.Substring(2);
        }
        else if (part.StartsWith("R_", StringComparison.Ordinal))
        {
            side = "Right";
            suffix = part.Substring(2);
        }
        else
        {
            return "Torso";
        }

        if (suffix == "hip_joint" || suffix.StartsWith("thigh_", StringComparison.Ordinal))
            return side + "Thigh";
        if (suffix == "knee_joint" || suffix == "ankle_joint" || suffix == "foot" ||
            suffix.StartsWith("shin_", StringComparison.Ordinal))
            return side + "Shin";
        if (suffix == "shoulder_joint" || suffix == "shoulder_stone" ||
            suffix.StartsWith("upper_arm_", StringComparison.Ordinal))
            return side + "UpperArm";
        if (suffix == "elbow_joint" || suffix == "hand" || suffix == "arm_seam" ||
            suffix.StartsWith("forearm_", StringComparison.Ordinal))
            return side + "Forearm";
        throw new InvalidDataException("Unmapped Stone Scout part: " + part);
    }

    private static Dictionary<string, SegmentData> ParseSourceObj(string path)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var segments = new Dictionary<string, SegmentData>();
        foreach (string name in SegmentNames)
            segments.Add(name, new SegmentData());

        string part = null;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("# part: ", StringComparison.Ordinal))
            {
                part = line.Substring(8);
                continue;
            }
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] items = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                positions.Add(new Vector3(Number(items[1]), Number(items[2]), Number(items[3])));
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                string[] items = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                uvs.Add(new Vector2(Number(items[1]), Number(items[2])));
            }
            else if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                string[] items = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                normals.Add(new Vector3(Number(items[1]), Number(items[2]), Number(items[3])));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                if (part == null)
                    throw new InvalidDataException("An OBJ face has no part label.");
                string[] items = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (items.Length != 4)
                    throw new InvalidDataException("Only triangle faces are supported.");
                string segmentName = SegmentForPart(part);
                var data = segments[segmentName];
                for (int i = 1; i <= 3; i++)
                {
                    string[] indices = items[i].Split('/');
                    if (indices.Length != 3)
                        throw new InvalidDataException("Expected position/UV/normal face indices.");
                    Vector3 pivot = GetPivot(segmentName);
                    data.Vertices.Add(positions[Index(indices[0], positions.Count)] - pivot);
                    data.Uvs.Add(uvs[Index(indices[1], uvs.Count)]);
                    data.Normals.Add(normals[Index(indices[2], normals.Count)]);
                    data.Triangles.Add(data.Triangles.Count);
                }
            }
        }
        return segments;
    }

    private static int Index(string text, int count)
    {
        int index = int.Parse(text, CultureInfo.InvariantCulture);
        index = index > 0 ? index - 1 : count + index;
        if (index < 0 || index >= count)
            throw new InvalidDataException("OBJ face index is outside its data list.");
        return index;
    }

    private static float Number(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }
}
