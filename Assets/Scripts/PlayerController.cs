using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody rb;
    private Vector3 moveDirection;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        float x = 0f;
        float z = 0f;

        if (Keyboard.current.wKey.isPressed)
            z += 1f;

        if (Keyboard.current.sKey.isPressed)
            z -= 1f;

        if (Keyboard.current.aKey.isPressed)
            x -= 1f;

        if (Keyboard.current.dKey.isPressed)
            x += 1f;

        moveDirection = new Vector3(x, 0f, z).normalized;
    }

    void FixedUpdate()
    {
        rb.MovePosition(
            rb.position +
            moveDirection * moveSpeed * Time.fixedDeltaTime
        );
    }
}