using UnityEngine;

namespace SamProduction
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class SamCharacterMotor : MonoBehaviour
    {
        public float walkSpeed = 2;
        public float runSpeed = 5;
        public float jumpHeight = 1.25f;
        public float gravity = -18;
        private CharacterController capsule;
        private float verticalSpeed;
        public bool Grounded => capsule != null && capsule.isGrounded;
        public CharacterController Capsule => capsule;
        private void Awake() { capsule = GetComponent<CharacterController>(); }

        public void MoveInput(Vector2 input, bool run, bool jump, Transform cameraTransform, float delta)
        {
            if (delta <= 0) return;
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1);
            if (Grounded && verticalSpeed < 0) verticalSpeed = -1;
            if (Grounded && jump) verticalSpeed = Mathf.Sqrt(jumpHeight * -2 * gravity);
            verticalSpeed += gravity * delta;
            capsule.Move((direction * (run ? runSpeed : walkSpeed) + Vector3.up * verticalSpeed) * delta);
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540 * delta);
        }

        public void ApplySkillPosition(Vector3 position, Quaternion rotation)
        {
            // The motor alone applies trajectory deltas; collision may constrain them.
            capsule.Move(position - transform.position);
            transform.rotation = rotation;
            verticalSpeed = 0;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            capsule.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            capsule.enabled = true;
            verticalSpeed = 0;
        }
    }
}
