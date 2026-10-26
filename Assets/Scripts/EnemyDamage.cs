using UnityEngine;

public class EnemyDamage : BattleDamageReceiver
{
    [Header("Perfect Guard Reaction")]
    [Min(0f)]
    public float parryStunDuration = 2f;

    private EnemyMovement enemyMovement;

    protected override bool IsEnemyFighter => true;
    protected override string DamageLabel => "ENEMY";

    protected override void OnReceiverInitialised()
    {
        enemyMovement = GetComponent<EnemyMovement>();
    }

    protected override bool TryBlockHit(float direction)
    {
        if (enemyMovement == null ||
            !enemyMovement.CanBlockAttack(direction))
        {
            return false;
        }

        enemyMovement.RegisterBlockedHit();
        return true;
    }

    protected override void FaceLaunchDirection(float direction)
    {
        enemyMovement?.FaceLaunchDirection(direction);
    }

    public void TakeParryStun()
    {
        ApplyForcedHitstun(parryStunDuration, true);
    }
}
