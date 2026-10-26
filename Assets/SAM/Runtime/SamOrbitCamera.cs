using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction
{
    public sealed class SamOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 6;
        public float height = 1.4f;
        public float yaw = 155;
        public float pitch = 12;
        public bool referenceView;
        public bool inputEnabled = true;
        private void LateUpdate()
        {
            if (target == null) return;
            if (inputEnabled && Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                yaw += delta.x * .14f;
                pitch = Mathf.Clamp(pitch - delta.y * .14f, -15, 65);
            }
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 focus = target.position + Vector3.up * height;
            Vector3 wanted = focus - rotation * Vector3.forward * distance;
            transform.SetPositionAndRotation(wanted, rotation);
        }
    }
}
