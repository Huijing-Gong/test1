using System.Collections.Generic;
using UnityEngine;

// Presentation only: captures Animator output and adds world-space limb poses.
// No root transform, collider, Rigidbody, animation clock or combat data changes.
internal sealed class BattleHumanoidRig
{
    readonly Transform hips, torso;
    readonly Transform rightUpperArm, rightLowerArm, rightHand;
    readonly Transform leftUpperArm, leftLowerArm, leftHand;
    readonly Transform rightUpperLeg, rightLowerLeg, rightFoot;
    readonly Transform leftUpperLeg, leftLowerLeg, leftFoot;
    readonly Transform[] poseBones;
    readonly Vector3[] positions;
    readonly Quaternion[] rotations;
    readonly Vector3 rightPalmLocal, leftPalmLocal;
    Vector3 rightFootBeforePose, leftFootBeforePose;
    Quaternion rightFootRotationBeforePose, leftFootRotationBeforePose;
    bool captured;

    public static BattleHumanoidRig Create(Animator animator)
    {
        if (!animator || !animator.avatar || !animator.avatar.isValid ||
            !animator.avatar.isHuman || !animator.isHuman)
            return null;
        return new BattleHumanoidRig(animator);
    }

    BattleHumanoidRig(Animator animator)
    {
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        torso = spine ? spine : chest ? chest : hips;
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        var unique = new List<Transform>();
        foreach (Transform bone in new[] { hips, spine, chest, upperChest,
            rightUpperArm, rightLowerArm, rightHand, leftUpperArm, leftLowerArm, leftHand,
            rightUpperLeg, rightLowerLeg, rightFoot, leftUpperLeg, leftLowerLeg, leftFoot })
            if (bone && !unique.Contains(bone)) unique.Add(bone);
        poseBones = unique.ToArray();
        positions = new Vector3[poseBones.Length];
        rotations = new Quaternion[poseBones.Length];
        rightPalmLocal = PalmAxis(animator, rightHand, true);
        leftPalmLocal = PalmAxis(animator, leftHand, false);
    }

    public void CapturePose()
    {
        RestorePose();
        for (int i = 0; i < poseBones.Length; i++)
        {
            if (!poseBones[i]) continue;
            positions[i] = poseBones[i].localPosition;
            rotations[i] = poseBones[i].localRotation;
        }
        if (rightFoot)
        {
            rightFootBeforePose = rightFoot.position;
            rightFootRotationBeforePose = rightFoot.rotation;
        }
        if (leftFoot)
        {
            leftFootBeforePose = leftFoot.position;
            leftFootRotationBeforePose = leftFoot.rotation;
        }
        captured = true;
    }

    public void RestorePose()
    {
        if (!captured) return;
        for (int i = 0; i < poseBones.Length; i++)
        {
            if (!poseBones[i]) continue;
            poseBones[i].localPosition = positions[i];
            poseBones[i].localRotation = rotations[i];
        }
        captured = false;
    }

    public void ShiftHips(Vector3 worldOffset)
    {
        if (hips) hips.position += worldOffset;
    }

    public void LeanTorso(float facing, float degrees, float weight)
    {
        if (!torso) return;
        // Stage forward is +/-X. Rotating world up about Z bends toward that
        // direction regardless of the imported bone's local axis convention.
        torso.rotation = Quaternion.AngleAxis(-facing * degrees * weight,
            Vector3.forward) * torso.rotation;
    }

    public void AimArm(bool right, Vector3 target, Vector3 elbowPole, float weight)
    {
        SolveLimb(right ? rightUpperArm : leftUpperArm,
            right ? rightLowerArm : leftLowerArm, right ? rightHand : leftHand,
            target, elbowPole, weight);
    }

    public void TwistTorso(float degrees,float weight)
    {
        if(torso) torso.rotation=Quaternion.AngleAxis(degrees*weight,Vector3.up)*torso.rotation;
    }

    public void AimLeg(bool right, Vector3 target, Vector3 kneePole, float weight)
    {
        SolveLimb(right ? rightUpperLeg : leftUpperLeg,
            right ? rightLowerLeg : leftLowerLeg, right ? rightFoot : leftFoot,
            target, kneePole, weight);
    }

    public void PlantFeet(Vector3 kneePole, float weight, bool plantRight = true)
    {
        if (plantRight && rightFoot)
        {
            AimLeg(true, rightFootBeforePose, kneePole, weight);
            rightFoot.rotation = Quaternion.Slerp(rightFoot.rotation, rightFootRotationBeforePose, weight);
        }
        if (leftFoot)
        {
            AimLeg(false, leftFootBeforePose, kneePole, weight);
            leftFoot.rotation = Quaternion.Slerp(leftFoot.rotation, leftFootRotationBeforePose, weight);
        }
    }

    public void AimPalm(bool right, Vector3 direction, float weight)
    {
        Transform hand = right ? rightHand : leftHand;
        Vector3 palmLocal = right ? rightPalmLocal : leftPalmLocal;
        if (hand) AimBone(hand, hand.TransformDirection(palmLocal), direction, weight);
    }

    public Vector3 EmitterWorldDirection
    {
        get
        {
            Vector3 direction = Vector3.zero;
            if (rightHand) direction += rightHand.TransformDirection(rightPalmLocal).normalized;
            if (leftHand) direction += leftHand.TransformDirection(leftPalmLocal).normalized;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.zero;
        }
    }

    static Vector3 PalmAxis(Animator animator, Transform hand, bool right)
    {
        if (!hand) return Vector3.zero;
        Transform middle = animator.GetBoneTransform(right
            ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        Transform thumb = animator.GetBoneTransform(right
            ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
        Vector3 normal = Vector3.zero;
        if (middle && thumb)
        {
            Vector3 fingers = middle.position - hand.position;
            Vector3 thumbSide = thumb.position - hand.position;
            normal = right ? Vector3.Cross(fingers, thumbSide)
                : Vector3.Cross(thumbSide, fingers);
        }
        // Fingerless humanoids cannot define an anatomical palm plane. Cache
        // the initial visual-forward axis instead; verify that rig in-scene.
        if (normal.sqrMagnitude < .00000001f) normal = animator.transform.forward;
        return hand.InverseTransformDirection(normal.normalized).normalized;
    }

    static void SolveLimb(Transform upper, Transform lower, Transform end,
        Vector3 target, Vector3 pole, float weight)
    {
        if (!upper || !lower || !end || weight <= 0f) return;
        weight = Mathf.Clamp01(weight);
        Vector3 origin = upper.position;
        float upperLength = Vector3.Distance(origin, lower.position);
        float lowerLength = Vector3.Distance(lower.position, end.position);
        Vector3 direction = target - origin;
        float distance = direction.magnitude;
        if (upperLength < .0001f || lowerLength < .0001f || distance < .0001f) return;
        direction /= distance;
        // Preserve segment lengths. An unreachable visual target must never
        // stretch a bone or move the physical fighter root to reach it.
        distance = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + .0001f,
            upperLength + lowerLength - .0001f);
        Vector3 bend = Vector3.ProjectOnPlane(pole - origin, direction);
        if (bend.sqrMagnitude < .0001f)
            bend = Vector3.ProjectOnPlane(lower.position - origin, direction);
        if (bend.sqrMagnitude < .0001f)
            bend = Vector3.Cross(direction, Vector3.forward);
        if (bend.sqrMagnitude < .0001f)
            bend = Vector3.Cross(direction, Vector3.up);
        bend.Normalize();
        float cosine = Mathf.Clamp((upperLength * upperLength + distance * distance
            - lowerLength * lowerLength) / (2f * upperLength * distance), -1f, 1f);
        Vector3 desiredJoint = origin + direction * (upperLength * cosine)
            + bend * (upperLength * Mathf.Sqrt(Mathf.Max(0f, 1f - cosine * cosine)));
        AimBone(upper, lower.position - origin, desiredJoint - origin, weight);
        Vector3 reachableTarget = origin + direction * distance;
        AimBone(lower, end.position - lower.position, reachableTarget - lower.position, weight);
    }

    static void AimBone(Transform bone, Vector3 from, Vector3 to, float weight)
    {
        if (!bone || from.sqrMagnitude < .0001f || to.sqrMagnitude < .0001f) return;
        Quaternion target = Quaternion.FromToRotation(from, to) * bone.rotation;
        bone.rotation = Quaternion.Slerp(bone.rotation, target, Mathf.Clamp01(weight));
    }

    public static Transform FindGenericEndpoint(Animator animator, string name,
        string legacyFingerName = null)
    {
        if (!animator) return null;
        Transform finger = null;
        foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == name) return bone;
            // The old monster rig has no Hand bone. Its explicitly named
            // middle-finger carrier provides the forearm's distal point.
            if (legacyFingerName != null && bone.name == legacyFingerName) finger = bone;
        }
        return finger;
    }
}
