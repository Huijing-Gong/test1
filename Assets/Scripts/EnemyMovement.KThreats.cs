using UnityEngine;

// A small addition to the existing opponent, not an input-reading perfect bot.
public partial class EnemyMovement
{
    BattleSpecialMoves visibleSpecials;
    float beamNoticeTimer;
    float beamResponseCooldown;
    bool sawVisibleCharge;
    public int VisibleBeamResponses { get; private set; }
    public int VisibleSpecialResponses => VisibleBeamResponses;

    void TickVisibleBeamReaction()
    {
        if (beamResponseCooldown > 0f)
            beamResponseCooldown -= Time.deltaTime;
        if (!player || !rb || !IsGrounded() ||
            (currentState != EnemyState.Chase && currentState != EnemyState.Reposition))
        {
            sawVisibleCharge = false;
            beamNoticeTimer = 0f;
            return;
        }
        if (!visibleSpecials) visibleSpecials = player.GetComponent<BattleSpecialMoves>();
        bool visible = visibleSpecials && visibleSpecials.TryGetVisibleChargeThreat(
            transform.position + Vector3.up * .22f, 14.5f, out _);
        if (!visible)
        {
            sawVisibleCharge = false;
            beamNoticeTimer = 0f;
            return;
        }
        if (!sawVisibleCharge)
        {
            sawVisibleCharge = true;
            beamNoticeTimer = Random.Range(.14f, .24f);
        }
        if (beamResponseCooldown > 0f) return;
        beamNoticeTimer -= Time.deltaTime;
        if (beamNoticeTimer > 0f) return;

        // Commit once per opportunity, including the choice not to evade.
        // React to the already visible energy gathering, not to a future release
        // button. Early/late release and deliberately holding charge can beat this jump.
        beamResponseCooldown = 1.25f;
        if (Random.value > .65f || Time.time < nextJumpTime) return;
        Vector3 head = transform.position + Vector3.up * 1.1f;
        if (Physics.SphereCast(head, .35f, Vector3.up, out _, 1.5f,
            GroundLayerMask, QueryTriggerInteraction.Ignore)) return;
        if (!HasGroundAhead(facingDirection)) return;

        Vector3 velocity = rb.linearVelocity;
        velocity.y = jumpForce;
        rb.linearVelocity = velocity;
        nextJumpTime = Time.time + .9f;
        wasGrounded = false;
        VisibleBeamResponses++;
        PlayJumpAnimation();
        SetGroundedAnimation(false);
    }
}
