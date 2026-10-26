using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 8f, -8f);

    void LateUpdate()
    {
        if (target == null)
            return;

        transform.position = target.position + offset;
        transform.LookAt(target);
    }
}