using UnityEngine;

// A small idle acknowledgement; combat and locomotion keep full ownership of the hero pose.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class PaimonHeroGlance : MonoBehaviour
{
 Transform head;Vector3 faceAxis;float weight;PaimonCompanion companion;PlayerCombat combat;PlayerBattleActions actions;
 public float LookWeight => weight;
 void Start(){var animator=GetComponentInChildren<Animator>();if(animator&&animator.isHuman)head=animator.GetBoneTransform(HumanBodyBones.Head);if(head)faceAxis=Quaternion.Inverse(head.rotation)*transform.forward;combat=GetComponent<PlayerCombat>();actions=GetComponent<PlayerBattleActions>();}
 void LateUpdate()
 {
  if(!head)return;if(!companion)companion=PaimonCompanion.FindFor(transform);
  bool busy=(combat&&combat.IsAttacking)||(actions&&(actions.IsDodging||actions.FocusActive));if(busy){weight=0;return;}
  bool active=companion&&companion.isActiveAndEnabled&&companion.Equipped&&companion.Visible&&!companion.IsMoving;
  float goal=active?companion.InteractionBlend*.8f:0;weight=Mathf.MoveTowards(weight,goal,Time.unscaledDeltaTime/.45f);if(weight<.001f)return;
  Vector3 direction=companion.FacePosition-head.position;if(direction.sqrMagnitude<.001f)return;
  Quaternion desired=Quaternion.FromToRotation(head.rotation*faceAxis,direction.normalized)*head.rotation;
  head.rotation=Quaternion.Slerp(head.rotation,Quaternion.RotateTowards(head.rotation,desired,35),weight);
 }
}
