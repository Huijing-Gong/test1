using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController2D))]
public sealed class PlayerCombat2D : MonoBehaviour
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float attackCooldown = 0.65f;
    [SerializeField] private float attackAnimationDuration = 0.5f;
    [SerializeField] private Vector2 castOffset = new(0.6f, 0.15f);

    private PlayerController2D controller;
    private float nextAttackTime;

    public void Configure(GameObject prefab)
    {
        projectilePrefab = prefab;
    }

    private void Awake()
    {
        controller = GetComponent<PlayerController2D>();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + attackCooldown;
        GameAudio.Play(GameSound.PrincessAttack);
        controller.PlayAttack(attackAnimationDuration);

        float direction = controller.FacingDirection;
        Vector3 spawnPosition = transform.position + new Vector3(castOffset.x * direction, castOffset.y, 0f);
        GameObject projectileObject = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
        projectileObject.GetComponent<MagicProjectile2D>().Launch(direction);
    }
}
