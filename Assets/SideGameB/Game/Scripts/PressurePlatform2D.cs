using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class PressurePlatform2D : MonoBehaviour
{
    [SerializeField] private float sinkDistance = 0.18f;
    [SerializeField] private float sinkSpeed = 0.65f;
    [SerializeField] private float returnSpeed = 0.42f;

    private readonly HashSet<PlayerController2D> riders = new();
    private Rigidbody2D body;
    private Vector2 restingPosition;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        restingPosition = body.position;
        ConfigureRequestedPlatformsAsOneWay();
    }

    private void ConfigureRequestedPlatformsAsOneWay()
    {
        if (name != "Small Pressure Stone Platform (3)" &&
            name != "Small Pressure Stone Platform (4)")
            return;

        Collider2D platformCollider = GetComponent<Collider2D>();
        platformCollider.enabled = true;
        PlatformEffector2D effector = GetComponent<PlatformEffector2D>();
        if (effector == null)
            effector = gameObject.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.useSideFriction = false;
        effector.surfaceArc = 160f;
        platformCollider.usedByEffector = true;
    }

    private void FixedUpdate()
    {
        bool pressed = riders.Count > 0;
        Vector2 target = restingPosition + Vector2.down * (pressed ? sinkDistance : 0f);
        float speed = pressed ? sinkSpeed : returnSpeed;
        body.MovePosition(Vector2.MoveTowards(body.position, target, speed * Time.fixedDeltaTime));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        PlayerController2D player = collision.collider.GetComponentInParent<PlayerController2D>();
        if (player != null && collision.transform.position.y > transform.position.y)
            riders.Add(player);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        PlayerController2D player = collision.collider.GetComponentInParent<PlayerController2D>();
        if (player != null)
            riders.Remove(player);
    }
}
