using UnityEngine;

public sealed class RotatingHazard2D : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 90f;

    public void Configure(float speed)
    {
        degreesPerSecond = speed;
    }

    private void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
