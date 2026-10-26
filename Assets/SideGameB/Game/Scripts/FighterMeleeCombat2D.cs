using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController2D))]
public sealed class FighterMeleeCombat2D : MonoBehaviour
{
    [SerializeField] private float attackCooldown = 0.48f;
    [SerializeField] private float attackAnimationDuration = 0.42f;
    [SerializeField] private float attackRange = 0.72f;
    [SerializeField] private Vector2 attackBoxSize = new(0.72f, 0.72f);
    [SerializeField] private int damage = 90;
    [SerializeField] private int redOrcDamage = 60;

    private PlayerController2D controller;
    private float nextAttackTime;

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
        GameAudio.Play(GameSound.FighterPunch);
        controller.PlayAttack(attackAnimationDuration);

        float direction = controller.FacingDirection;
        FighterSlashEffect2D.Spawn(transform, direction);
        Vector2 centre = (Vector2)transform.position + new Vector2(direction * attackRange, 0.08f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(centre, attackBoxSize, 0f);
        HashSet<EnemyHealth> damagedEnemies = new();
        foreach (Collider2D hit in hits)
        {
            EnemyHealth health = hit.GetComponentInParent<EnemyHealth>();
            if (health != null && damagedEnemies.Add(health))
            {
                bool isRedOrc = health.gameObject.name.Contains("Red Orc");
                health.TakeDamage(isRedOrc ? redOrcDamage : damage);
            }
        }
    }
}
