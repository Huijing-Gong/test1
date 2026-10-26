using UnityEngine;

// A persistent gate rather than an Exit Time crossing: moving input arriving
// later in the landing clip must be able to interrupt it too.
public sealed class BattleLandingState : StateMachineBehaviour
{
    static readonly int Ready = Animator.StringToHash("LandingReady");
    static readonly int Moving = Animator.StringToHash("IsMoving");
    public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layer)
    { animator.SetBool(Ready, animator.GetBool(Moving)); }
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo info, int layer)
    { animator.SetBool(Ready, animator.GetBool(Moving) || info.normalizedTime * info.length >= .1f); }
    public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layer)
    { animator.SetBool(Ready, false); }
}
