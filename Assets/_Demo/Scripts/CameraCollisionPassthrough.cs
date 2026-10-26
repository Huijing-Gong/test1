using UnityEngine;

// Transparent arena boundaries still block actors, but should not shorten the
// third-person camera boom. Put this on the boundary collider's parent.
[DisallowMultipleComponent]
public sealed class CameraCollisionPassthrough : MonoBehaviour
{
}
