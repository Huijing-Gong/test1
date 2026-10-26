using UnityEngine;

public sealed class NPCSimpleGestureAnimator : MonoBehaviour
{
    public enum PreviewMode
    {
        BreathingIdle,
        LightGesture,
        Pointing
    }

    public PreviewMode mode = PreviewMode.BreathingIdle;
    public float phaseOffset;

    private Animator animator;
    private Transform chest;
    private Transform rightUpperArm;
    private Transform rightLowerArm;
    private Transform leftUpperArm;
    private Quaternion chestBase;
    private Quaternion rightUpperArmBase;
    private Quaternion rightLowerArmBase;
    private Quaternion leftUpperArmBase;
    private bool ready;

    private void Awake()
    {
        CacheHumanoidBones();
    }

    private void LateUpdate()
    {
        ApplyPose(Time.time + phaseOffset);
    }

    public bool CacheHumanoidBones()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
            return ready = false;

        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        if (chest == null || rightUpperArm == null || rightLowerArm == null || leftUpperArm == null)
            return ready = false;

        chestBase = chest.localRotation;
        rightUpperArmBase = rightUpperArm.localRotation;
        rightLowerArmBase = rightLowerArm.localRotation;
        leftUpperArmBase = leftUpperArm.localRotation;
        return ready = true;
    }

    public void ApplyPose(float time)
    {
        if (!ready && !CacheHumanoidBones()) return;

        float breathe = Mathf.Sin(time * 1.65f) * 1.6f;
        chest.localRotation = chestBase * Quaternion.Euler(breathe, 0f, breathe * 0.35f);
        rightUpperArm.localRotation = rightUpperArmBase;
        rightLowerArm.localRotation = rightLowerArmBase;
        leftUpperArm.localRotation = leftUpperArmBase;

        if (mode == PreviewMode.LightGesture)
        {
            float wave = Mathf.Sin(time * 2.4f) * 11f;
            rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(-34f, 5f, -42f + wave);
            rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(0f, -18f, -38f + wave * 0.45f);
        }
        else if (mode == PreviewMode.Pointing)
        {
            rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(-68f, 4f, -18f);
            rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(0f, -3f, -8f);
            leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(8f, 0f, 5f);
        }
    }
}
