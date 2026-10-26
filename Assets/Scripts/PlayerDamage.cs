using UnityEngine;

public class PlayerDamage : BattleDamageReceiver
{
    private BattlePlayerController playerController;

    protected override bool IsEnemyFighter => false;
    protected override string DamageLabel => "PLAYER";

    protected override void OnReceiverInitialised()
    {
        playerController = GetComponent<BattlePlayerController>();
    }

    protected override bool TryBlockHit(float direction, Transform attackSource)
    {
        BattleSpecialMoves specials = GetComponent<BattleSpecialMoves>();
        return specials != null && specials.TryCounterHit(direction, attackSource);
    }

    protected override void BeforeHitApplied()
    {
        playerController?.CancelDefence();
        playerController?.ReleaseLedgeForHit();
        GetComponent<BattleSpecialMoves>()?.InterruptForHit();
    }

    protected override void FaceLaunchDirection(float direction)
    {
        // Hit animation/knockback already show the launch direction. Keep the
        // player's chosen attack/guard facing; a finisher must not silently
        // turn their next attack away from the opponent.
        if (playerController != null)
            playerController.FaceLaunchDirection(playerController.FacingDirection);
    }
}
