using UnityEngine;
using UnityEngine.InputSystem;

public partial class EnemyMovement
{
    [Header("Developer Animation Preview")]
    [Tooltip("Editor-only P / I / O animation previews. Keep disabled during normal play and gameplay tests.")]
    public bool enableAnimationShowcaseHotkeys;

void StopHorizontalMovement()
    {
        Vector3 velocity = rb.linearVelocity;
        velocity.x = 0f;
        velocity.z = 0f;
        rb.linearVelocity = velocity;
    }

    void ApplyAttackMovement()
    {
        Vector3 velocity = rb.linearVelocity;
        bool movementWouldLeavePlatform =
            IsGrounded() && !HasGroundAhead(facingDirection);
        velocity.x = ledgePressureAttack || movementWouldLeavePlatform
            ? 0f
            : facingDirection * enemyAttack.CurrentForwardSpeed;
        velocity.z = 0f;
        rb.linearVelocity = velocity;
    }

    void FacePlayer()
    {
        float direction =
            player.position.x >= transform.position.x ? 1f : -1f;

        SetFacing(direction);
    }

    bool IsGrounded()
    {
        return BattlePlatformPhysics.IsGrounded(
            transform,
            rb,
            isDroppingThroughPlatform,
            GroundLayerMask,
            1.1f
        );
    }

    void ConfigureAnimator()
    {
        if (animator == null)
            return;

        animator.applyRootMotion = false;
        hasJumpTrigger = HasAnimatorParameter(
            JumpTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
        hasGroundedParameter = HasAnimatorParameter(
            IsGroundedHash,
            AnimatorControllerParameterType.Bool
        );
        hasWalkingParameter = HasAnimatorParameter(
            IsWalkingHash,
            AnimatorControllerParameterType.Bool
        );
        hasDefendingParameter = HasAnimatorParameter(
            IsDefendingHash,
            AnimatorControllerParameterType.Bool
        );
        hasYesTrigger = HasAnimatorParameter(
            YesTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
        hasNoTrigger = HasAnimatorParameter(
            NoTriggerHash,
            AnimatorControllerParameterType.Trigger
        );
        visualRoot = animator.transform;

        while (visualRoot.parent != null &&
            visualRoot.parent != transform)
        {
            visualRoot = visualRoot.parent;
        }
    }

    public void FaceLaunchDirection(float direction)
    {
        SetFacing(direction >= 0f ? 1f : -1f);
    }

    void SetFacing(float direction)
    {
        facingDirection = direction >= 0f ? 1f : -1f;

        ApplyVisualFacingRotation();

        if (IsDefending)
        {
            guardVfx?.SetGuarding(true, facingDirection);
        }
    }

    void ApplyVisualFacingRotation()
    {

        if (visualRoot == null)
            return;

        float yRotation = facingDirection > 0f ? 90f : -90f;
        float zRotation = -facingDirection *
            Mathf.Max(0f, ledgeStrikeVisualTilt) *
            ledgeStrikePoseBlend;

        visualRoot.localRotation = Quaternion.Euler(
            0f,
            yRotation,
            zRotation
        );
    }

    void SetMovingAnimation(bool isMoving)
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return;
        }

        animator.SetBool(IsMovingHash, isMoving);
    }

    void SetWalkingAnimation(bool isWalking)
    {
        if (animator != null && hasWalkingParameter)
        {
            animator.SetBool(IsWalkingHash, isWalking);
        }
    }

    bool TryStartShowcaseAction()
    {
        // These old animation-preview keys are not player combat controls.
        // An explicit editor opt-in keeps them useful without letting normal
        // gameplay (or a published build) force the opponent to stop fighting.
        if (!Application.isEditor || !enableAnimationShowcaseHotkeys ||
            Keyboard.current == null ||
            animator == null ||
            !IsGrounded())
        {
            return false;
        }

        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            if (!hasDefendingParameter)
                return false;

            SetDefendingAnimation(true);
            StopHorizontalMovement();
            EnterState(EnemyState.Showcase);
            return true;
        }

        int triggerHash;
        bool hasTrigger;

        if (Keyboard.current.iKey.wasPressedThisFrame)
        {
            triggerHash = YesTriggerHash;
            hasTrigger = hasYesTrigger;
        }
        else if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            triggerHash = NoTriggerHash;
            hasTrigger = hasNoTrigger;
        }
        else
        {
            return false;
        }

        if (!hasTrigger)
            return false;

        animator.SetTrigger(triggerHash);
        StopHorizontalMovement();
        EnterState(EnemyState.Showcase);
        return true;
    }

    void SetDefendingAnimation(bool isDefending)
    {
        if (animator != null && hasDefendingParameter)
        {
            animator.SetBool(IsDefendingHash, isDefending);
        }
    }

    void PlayJumpAnimation()
    {
        SideAAudio.Jump(true);
        if (animator != null && hasJumpTrigger)
        {
            animator.SetTrigger(JumpTriggerHash);
        }
    }

    void SetGroundedAnimation(bool isGrounded)
    {
        if (animator != null && hasGroundedParameter)
        {
            animator.SetBool(IsGroundedHash, isGrounded);
        }
    }

    bool HasAnimatorParameter(
        int parameterHash,
        AnimatorControllerParameterType parameterType
    )
    {
        return BattleAnimatorUtility.HasParameter(
            animator,
            parameterHash,
            parameterType
        );
    }
}
