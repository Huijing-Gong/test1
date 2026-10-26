using UnityEngine;

// Rotate only the visual child: collection and chest anchors stay fixed.
public sealed class CollectibleSpin : MonoBehaviour
{
    [SerializeField] float degreesPerSecond = 35;
    void Update() => transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
}
