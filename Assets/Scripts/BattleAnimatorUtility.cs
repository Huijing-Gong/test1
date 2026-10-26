using UnityEngine;

internal static class BattleAnimatorUtility
{
    public static bool HasParameter(
        Animator animator,
        int parameterHash,
        AnimatorControllerParameterType parameterType
    )
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in
            animator.parameters)
        {
            if (parameter.nameHash == parameterHash &&
                parameter.type == parameterType)
            {
                return true;
            }
        }

        return false;
    }

    public static void SetBoolIfPresent(
        Animator animator,
        int parameterHash,
        bool value
    )
    {
        if (HasParameter(
                animator,
                parameterHash,
                AnimatorControllerParameterType.Bool
            ))
        {
            animator.SetBool(parameterHash, value);
        }
    }

    public static void SetTriggerIfPresent(
        Animator animator,
        int parameterHash
    )
    {
        if (HasParameter(
                animator,
                parameterHash,
                AnimatorControllerParameterType.Trigger
            ))
        {
            animator.SetTrigger(parameterHash);
        }
    }

    public static void ResetTriggers(Animator animator)
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in
            animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(parameter.nameHash);
            }
        }
    }
}
