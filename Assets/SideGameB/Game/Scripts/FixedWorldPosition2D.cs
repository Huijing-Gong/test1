using UnityEngine;

/// <summary>Keeps decorative scene artwork fixed even if a parent is animated.</summary>
public sealed class FixedWorldPosition2D : MonoBehaviour
{
    private Vector3 fixedPosition;

    private void Awake()
    {
        fixedPosition = transform.position;
    }

    private void LateUpdate()
    {
        transform.position = fixedPosition;
    }
}
