using UnityEngine;

public partial class EnemyMovement
{
bool CanStartAttack()
    {
        if (player == null)
        {
            return false;
        }

        Collider hangingPlatform = GetHangingPlayerPlatform();
        if (hangingPlatform != null)
        {
            if (ledgeStrikeRetryCooldownTimer > 0f ||
                !IsStandingOnPlatform(hangingPlatform) ||
                Mathf.Abs(
                    transform.position.x - GetLedgePressureX()
                ) > Mathf.Max(
                    0.9f,
                    ledgeApproachTolerance + 0.55f
                ))
            {
                return false;
            }

            return true;
        }

        if (Mathf.Abs(player.position.y - transform.position.y) >
            attackVerticalTolerance)
        {
            return false;
        }

        return enemyAttack.IsTargetInRange(player);
    }

    bool CanStartUppercut()
    {
        if (player == null ||
            enemyAttack == null ||
            uppercutCooldownTimer > 0f ||
            GetHangingPlayerPlatform() != null ||
            !IsGrounded())
        {
            return false;
        }

        Vector3 offset = player.position - transform.position;
        offset.z = 0f;
        float minimumHeight = Mathf.Max(0f, uppercutMinimumHeight);
        float maximumHeight = Mathf.Max(
            minimumHeight,
            uppercutMaximumHeight
        );
        if (offset.y < minimumHeight ||
            offset.y > maximumHeight ||
            Mathf.Abs(offset.x) > uppercutHorizontalRange)
        {
            return false;
        }

        Vector2 attackCenter = new Vector2(
            transform.position.x,
            transform.position.y + 0.85f
        );
        Vector2 targetPosition = new Vector2(
            player.position.x,
            player.position.y
        );
        if (Vector2.Distance(attackCenter, targetPosition) >
            enemyAttack.uppercutRange)
        {
            return false;
        }

        Vector3 lineStart = transform.position + Vector3.up * 0.9f;
        Vector3 lineEnd = player.position + Vector3.up * 0.35f;
        return !HasBlockingSurfaceBetween(lineStart, lineEnd);
    }

    bool HasBlockingSurfaceBetween(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            start,
            direction / distance,
            distance,
            GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null ||
                IsFighterCollider(hitCollider) ||
                hitCollider.GetComponentInParent<PlayerDamage>() != null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    bool IsFighterCollider(Collider candidate)
    {
        if (candidate == null || fighterColliders == null)
            return false;

        foreach (Collider fighterCollider in fighterColliders)
        {
            if (candidate == fighterCollider)
                return true;
        }

        return false;
    }

    void BeginRegularAttack()
    {
        ledgePressureAttack = IsStandingOnHangingPlatform();
        if (ledgePressureAttack)
        {
            enemyAttack.StartLedgeAttack(playerController);
        }
        else
        {
            enemyAttack.StartCombo();
        }
    }

    bool CanStartWhiffPunish()
    {
        if (player == null ||
            playerAttack == null ||
            whiffPunishCooldownTimer > 0f ||
            !playerAttack.LastAttackResolved ||
            playerAttack.LastAttackConnected ||
            (currentState != EnemyState.Chase &&
                currentState != EnemyState.Reposition &&
                currentState != EnemyState.ThreatReact))
        {
            return false;
        }

        Vector2 offset = new Vector2(
            player.position.x - transform.position.x,
            player.position.y - transform.position.y
        );
        if (offset.sqrMagnitude >
            whiffPunishAwarenessRange * whiffPunishAwarenessRange)
        {
            return false;
        }

        if (!CanStartAttack() && !CanStartUppercut())
            return false;

        return Random.value <= Mathf.Clamp01(whiffPunishChance);
    }

    void ScheduleCombatDecision()
    {
        float minimum = Mathf.Max(0f, minimumReactionTime);
        float maximum = Mathf.Max(minimum, maximumReactionTime);
        decisionTimer = Random.Range(minimum, maximum);
    }

    void ScheduleThreatReaction()
    {
        float minimum = Mathf.Max(0f, minimumThreatReactionTime);
        float maximum = Mathf.Max(
            minimum,
            maximumThreatReactionTime
        );
        stateTimer = Random.Range(minimum, maximum);
    }

    void ScheduleWhiffPunish()
    {
        float minimum = Mathf.Max(0f, minimumWhiffPunishDelay);
        float maximum = Mathf.Max(
            minimum,
            maximumWhiffPunishDelay
        );
        stateTimer = Random.Range(minimum, maximum);
    }

    bool CanNoticePlayerAttack()
    {
        if (player == null ||
            playerAttack == null ||
            !playerAttack.IsMovementLocked ||
            !IsGrounded())
        {
            return false;
        }

        Vector3 offset = player.position - transform.position;
        offset.z = 0f;

        if (Mathf.Abs(offset.y) > attackVerticalTolerance + 0.45f)
            return false;

        float awarenessRange = Mathf.Max(0.1f, threatAwarenessDistance);
        return offset.sqrMagnitude <= awarenessRange * awarenessRange;
    }

    bool ShouldCreateSpace()
    {
        if (GetHangingPlayerPlatform() != null ||
            CanStartUppercut() ||
            player == null ||
            closeSpacingCooldownTimer > 0f ||
            !IsGrounded() ||
            Mathf.Abs(player.position.y - transform.position.y) >
                attackVerticalTolerance)
        {
            return false;
        }

        float spacingThreshold = Mathf.Min(
            Mathf.Max(0.1f, tooCloseDistance),
            Mathf.Max(0.1f, stopDistance - 0.05f)
        );
        float horizontalOffset =
            player.position.x - transform.position.x;

        if (Mathf.Abs(horizontalOffset) >= spacingThreshold)
            return false;

        float awayFromPlayer = horizontalOffset >= 0f ? -1f : 1f;
        return HasGroundAhead(awayFromPlayer);
    }

    void BeginCloseSpacing()
    {
        closeSpacingCooldownTimer = Mathf.Max(
            closeSpacingCooldownTimer,
            closeSpacingCooldown
        );
        RegisterTacticalAction(TacticalAction.Reposition);
        EnterState(EnemyState.Reposition);
        stateTimer = Mathf.Min(
            stateTimer,
            Mathf.Max(0.05f, closeSpacingDuration)
        );
    }

    void ResolveThreatReaction()
    {
        if (!CanNoticePlayerAttack())
        {
            EnterState(EnemyState.Chase);
            return;
        }

        float defendWeight = CanStartDefence()
            ? GetRememberedActionWeight(
                TacticalAction.Defend,
                defendChance
            )
            : 0f;
        float evadeWeight = CanStartEvade()
            ? GetRememberedActionWeight(
                TacticalAction.Evade,
                evadeChance
            )
            : 0f;
        bool canCounterUppercut = CanStartUppercut();
        bool canCounterRegular = CanStartAttack();
        float counterWeight = canCounterUppercut || canCounterRegular
            ? GetRememberedActionWeight(
                TacticalAction.Attack,
                counterAttackChance
            )
            : 0f;

        float horizontalDistance = Mathf.Abs(
            player.position.x - transform.position.x
        );
        if (horizontalDistance <= tooCloseDistance + 0.15f)
        {
            evadeWeight *= 1.35f;
            counterWeight *= 0.7f;
        }

        float waitWeight = Mathf.Max(
            0.18f,
            1f - defendWeight - evadeWeight - counterWeight
        );
        float totalWeight =
            defendWeight + evadeWeight + counterWeight + waitWeight;
        float choice = Random.value * totalWeight;

        if (choice < defendWeight)
        {
            StartDefence();
            return;
        }

        choice -= defendWeight;
        if (choice < evadeWeight)
        {
            StartEvade();
            return;
        }

        choice -= evadeWeight;
        if (choice < counterWeight)
        {
            if (canCounterUppercut)
            {
                enemyAttack.StartUppercut();
                uppercutCooldownTimer = Mathf.Max(
                    uppercutCooldownTimer,
                    uppercutCooldown
                );
                RegisterTacticalAction(TacticalAction.Uppercut);
            }
            else
            {
                BeginRegularAttack();
                RegisterTacticalAction(TacticalAction.Attack);
            }
            EnterState(EnemyState.Telegraph);
            return;
        }

        EnterState(EnemyState.Chase);
    }

    float GetRememberedActionWeight(
        TacticalAction action,
        float baseWeight
    )
    {
        float weight = Mathf.Clamp01(baseWeight);
        if (!HasRecentTacticalAction())
            return weight;

        float repeatMultiplier = Mathf.Clamp01(
            repeatedActionChanceMultiplier
        );

        if (lastTacticalAction == action)
        {
            return weight * repeatMultiplier;
        }

        if (IsDefensiveAction(lastTacticalAction) &&
            IsDefensiveAction(action))
        {
            return weight * Mathf.Lerp(1f, repeatMultiplier, 0.45f);
        }

        return weight;
    }

    bool HasRecentTacticalAction()
    {
        return lastTacticalAction != TacticalAction.None &&
            behaviourMemoryDuration > 0f &&
            Time.time - lastTacticalActionTime <=
                behaviourMemoryDuration;
    }

    bool WasRecentTacticalAction(TacticalAction action)
    {
        return HasRecentTacticalAction() &&
            lastTacticalAction == action;
    }

    void RegisterTacticalAction(TacticalAction action)
    {
        lastTacticalAction = action;
        lastTacticalActionTime = Time.time;
    }

    static bool IsDefensiveAction(TacticalAction action)
    {
        return action == TacticalAction.Defend ||
            action == TacticalAction.Evade;
    }

    bool ShouldContinueCombo()
    {
        if (ledgePressureAttack ||
            player == null ||
            !enemyAttack.LastAttackConnected ||
            Mathf.Abs(player.position.y - transform.position.y) >
                attackVerticalTolerance + 0.35f)
        {
            return false;
        }

        int completedStep = enemyAttack.CurrentComboStep;
        if (completedStep < 0 ||
            completedStep >= BattleFourHitCombo.HitCount - 1)
        {
            return false;
        }

        float continueChance = completedStep == 0
            ? continueAfterFirstHitChance
            : continueIntoFinisherChance;

        if (playerDamage != null)
        {
            continueChance += Mathf.Clamp01(
                playerDamage.damagePercent / 100f
            ) * highDamageComboBonus;
        }

        float distance = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(player.position.x, player.position.y)
        );
        float continuationRange = Mathf.Max(
            enemyAttack.attackRange,
            enemyAttack.comboContinueRange
        );
        float edgeOfRangePenalty = Mathf.InverseLerp(
            continuationRange * 0.78f,
            continuationRange,
            distance
        ) * 0.16f;
        continueChance -= edgeOfRangePenalty;

        return Random.value <= Mathf.Clamp01(continueChance);
    }

    bool CanStartEvade()
    {
        if (player == null ||
            evadeCooldownTimer > 0f ||
            !IsGrounded())
        {
            return false;
        }

        Vector3 offset = player.position - transform.position;
        offset.z = 0f;
        if (offset.sqrMagnitude >
            evadeTriggerDistance * evadeTriggerDistance)
        {
            return false;
        }

        return true;
    }

    void StartEvade()
    {
        evadeCooldownTimer = Mathf.Max(0f, evadeCooldown);
        RegisterTacticalAction(TacticalAction.Evade);
        EnterState(EnemyState.Reposition);
    }

    bool CanStartDefence()
    {
        if (player == null ||
            defendCooldownTimer > 0f ||
            !IsGrounded())
        {
            return false;
        }

        Vector3 offset = player.position - transform.position;
        offset.z = 0f;

        if (offset.sqrMagnitude >
            defendTriggerDistance * defendTriggerDistance)
        {
            return false;
        }

        return true;
    }

    void StartDefence()
    {
        RegisterTacticalAction(TacticalAction.Defend);
        EnterState(EnemyState.Defend);
    }

    public bool CanBlockAttack(float incomingDirection)
    {
        if (!IsDefending)
            return false;

        float normalisedIncoming =
            incomingDirection >= 0f ? 1f : -1f;

        return normalisedIncoming * facingDirection < 0f;
    }

    public void RegisterBlockedHit()
    {
        if (!IsDefending)
            return;

        guardVfx?.PlayBlockImpact(facingDirection);
        stateTimer = Mathf.Min(
            stateTimer,
            Mathf.Max(0f, defendReleaseAfterBlock)
        );
    }
}
