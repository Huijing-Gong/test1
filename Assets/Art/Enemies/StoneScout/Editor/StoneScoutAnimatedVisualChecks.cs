using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class StoneScoutAnimatedVisualChecks
{
    private const string PrefabPath =
        "Assets/Art/Enemies/StoneScout/Animated/StoneScoutAnimatedVisual.prefab";

    [MenuItem("Tools/Art/Check Animated Stone Scout Visual")]
    public static void Run()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Animated Stone Scout prefab is missing.");
        if (prefab.GetComponent<StoneScoutVisualMotion>() == null)
            throw new InvalidOperationException("Visual motion component is missing.");
        if (Vector3.Distance(prefab.transform.localScale, Vector3.one * 0.8f) > 0.001f)
            throw new InvalidOperationException("Stone Scout visual should use the shared 0.8 scale.");
        Animator animator = prefab.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null || animator.applyRootMotion)
            throw new InvalidOperationException("Expected an in-place Animator with a controller.");
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null || controller.animationClips.Length != 5)
            throw new InvalidOperationException("Expected exactly five Animator clips.");
        if (prefab.transform.Find("FallPivot") == null)
            throw new InvalidOperationException("Missing whole-body fall pivot.");
        foreach (string name in new[] { "Idle", "Walk", "Attack", "Hit", "Death" })
        {
            AnimationClip clip = Array.Find(controller.animationClips, candidate => candidate.name == name);
            if (clip == null || AnimationUtility.GetCurveBindings(clip).Length == 0)
                throw new InvalidOperationException("Missing or empty animation clip: " + name);
            var clipData = new SerializedObject(clip);
            var loopTime = clipData.FindProperty("m_AnimationClipSettings.m_LoopTime");
            if (loopTime == null || loopTime.boolValue != (name == "Idle" || name == "Walk"))
                throw new InvalidOperationException("Incorrect animation loop setting: " + name);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != "FallPivot" &&
                    !binding.path.StartsWith("FallPivot/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Animation binding is outside the fall pivot: " + name);
            }
        }
        CheckSampledMotion(prefab, controller);
        if (prefab.GetComponentInChildren<Collider>(true) != null ||
            prefab.GetComponentInChildren<Rigidbody>(true) != null)
            throw new InvalidOperationException("Visual prefab must not contain physics components.");

        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (filters.Length != 10 || renderers.Length != 10)
            throw new InvalidOperationException("Expected ten movable mesh segments.");

        int triangleCount = 0;
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null)
                throw new InvalidOperationException("A segment is missing its mesh: " + filter.name);
            triangleCount += (int)filter.sharedMesh.GetIndexCount(0) / 3;
        }
        if (triangleCount != 1972)
            throw new InvalidOperationException("Triangle count changed: " + triangleCount);

        foreach (string anchor in new[] { "EyeVFXAnchor", "HitVFXAnchor", "FootstepVFXAnchor" })
        {
            if (FindChild(prefab.transform, anchor) == null)
                throw new InvalidOperationException("Missing VFX anchor: " + anchor);
        }

        var serialized = new SerializedObject(prefab.GetComponent<StoneScoutVisualMotion>());
        foreach (string propertyName in new[]
        {
            "animator", "torso", "head", "leftUpperArm", "leftForearm", "rightUpperArm",
            "rightForearm", "leftThigh", "leftShin", "rightThigh", "rightShin"
        })
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
                throw new InvalidOperationException("Missing motion bone: " + propertyName);
        }

        Debug.Log("Animated Stone Scout visual PASS: 10 movable segments, " +
            triangleCount + " triangles, five sampled Animator clips, all motion bones and VFX anchors assigned; no gameplay physics.");
    }

    private static void CheckSampledMotion(GameObject prefab, AnimatorController controller)
    {
        GameObject preview = UnityEngine.Object.Instantiate(prefab);
        try
        {
            Transform fallPivot = preview.transform.Find("FallPivot");
            Transform torso = fallPivot.Find("Torso");
            Transform head = fallPivot.Find("Torso/Head");
            Transform rightArm = fallPivot.Find("Torso/RightUpperArm");
            Transform leftThigh = fallPivot.Find("LeftThigh");
            StoneScoutVisualMotion motion = preview.GetComponent<StoneScoutVisualMotion>();
            Sample(controller, "Idle", preview, 1.2f);
            Vector3 restingFist = motion.RightFistWorldPosition;
            if (Quaternion.Angle(torso.localRotation, Quaternion.identity) < 1f)
                throw new InvalidOperationException("Idle clip did not move the torso.");
            Sample(controller, "Walk", preview, 0.2f);
            if (Quaternion.Angle(leftThigh.localRotation, Quaternion.identity) < 10f)
                throw new InvalidOperationException("Walk clip did not move the leg.");
            Sample(controller, "Attack", preview, 0.43f);
            if (Quaternion.Angle(rightArm.localRotation, Quaternion.identity) < 40f)
                throw new InvalidOperationException("Attack clip did not swing the arm.");
            float fistTravel = Vector3.Dot(preview.transform.forward,
                motion.RightFistWorldPosition - restingFist);
            if (fistTravel < 0.2f)
                throw new InvalidOperationException("The attack fist does not reach forward: " + fistTravel);
            Vector3 fistAtPeak = preview.transform.InverseTransformPoint(motion.RightFistWorldPosition);
            if (fistAtPeak.z < 0.45f || Mathf.Abs(fistAtPeak.x) > 0.45f)
                throw new InvalidOperationException("Attack fist is outside the close-range strike area: " + fistAtPeak);
            Sample(controller, "Hit", preview, 0.15f);
            if (Quaternion.Angle(torso.localRotation, Quaternion.identity) < 10f)
                throw new InvalidOperationException("Hit clip did not recoil.");
            Sample(controller, "Death", preview, 0.85f);
            if (Quaternion.Angle(fallPivot.localRotation, Quaternion.identity) < 70f ||
                torso.localPosition.y < 0.75f || head.position.y - preview.transform.position.y > 0.6f)
                throw new InvalidOperationException("Death clip did not tip the whole body coherently.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(preview);
        }
    }

    private static void Sample(AnimatorController controller, string name, GameObject preview, float time)
    {
        AnimationClip clip = Array.Find(controller.animationClips, candidate => candidate.name == name);
        clip.SampleAnimation(preview, time);
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
                return child;
            Transform nested = FindChild(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
