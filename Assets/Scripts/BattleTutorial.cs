using UnityEngine;

/// <summary>
/// Opt-in Side A practice. Lessons observe the real combat and physics code;
/// they do not inject damage, simulated input, altered move data or rewards.
/// A scene reload, owned by BattleManager, is the boundary back to a real duel.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(240)]
public sealed class BattleTutorial : MonoBehaviour
{
    public enum Lesson { Move, Jump, Attack, Combo, Energy, Recovery, Stocks }

    public bool IsRunning { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsReviewing { get; private set; }
    public int CurrentStepIndex { get; private set; }
    public int StepCount => 7;
    public Lesson CurrentLesson => (Lesson)CurrentStepIndex;
    public float StepAge { get; private set; }
    public int GroundJumps { get; private set; }
    public int AirJumps { get; private set; }
    public int ComboHits { get; private set; }
    public bool ShortBeamComplete { get; private set; }
    public bool ChargedBeamComplete { get; private set; }
    public int TutorialRingOuts { get; private set; }
    public bool RecoveryLaunched { get; private set; }
    public bool StepSucceeded => stepSucceeded;
    public bool HasPassed(int index) => index >= 0 && index < passed.Length && passed[index];
    public bool WasSkipped(int index) => index >= 0 && index < skipped.Length && skipped[index];
    public float ChargeProgress => IsRunning && !IsComplete &&
        CurrentLesson == Lesson.Energy && specials && specials.IsCharging
            ? specials.Charge01 : -1f;
    public Vector3? TargetWorldPosition => !IsRunning || IsComplete ? null : targetPoint;

    public string Title
    {
        get
        {
            if (IsComplete) return "PRACTICE COMPLETE";
            switch (CurrentLesson)
            {
                case Lesson.Move: return "MOVE WITH INTENT";
                case Lesson.Jump: return "ONE MORE JUMP";
                case Lesson.Attack: return "LAND YOUR FIRST HIT";
                case Lesson.Combo: return "FOUR-HIT COMBO  /  OPTIONAL";
                case Lesson.Energy: return "TAP, THEN CHARGE";
                case Lesson.Recovery: return "RETURN TO THE STAGE";
                default: return "TWO LIVES. ONE WINNER.";
            }
        }
    }

    public string Instruction
    {
        get
        {
            if (IsComplete) return "You are ready. Start a fresh two-life duel whenever you like.";
            if (stepSucceeded) return "Nicely done. Next lesson...";
            switch (CurrentLesson)
            {
                case Lesson.Move: return "Use A / D to reach the mint marker.";
                case Lesson.Jump: return "Jump from the ground, then press Space again while airborne.";
                case Lesson.Attack: return "Approach the rival and land a normal punch. A / D is fine; release W / S.";
                case Lesson.Combo: return "Keep W / S released. Hold A / D if needed; click again as each punch connects.";
                case Lesson.Energy: return ShortBeamComplete
                    ? "Face the rival. With no direction held, hold special until fully charged."
                    : "With no direction held, briefly tap special and hit the rival.";
                case Lesson.Recovery: return controller && controller.IsLedgeHanging && !RecoveryLaunched
                    ? "Hanging on the edge? Tap A again to let go, then use W + special and steer D."
                    : RecoveryLaunched
                    ? "Steer right with D and land safely on a platform."
                    : "Walk off the left edge with A. Use W + special, then steer right with D.";
                default: return TutorialRingOuts == 0
                    ? "Push the rival off the right edge twice. This is practice: your own falls are free."
                    : "One life remains. The rival returned at 0%. Knock them out once more.";
            }
        }
    }

    public string Keys
    {
        get
        {
            if (IsComplete) return "START DUEL  /  Fresh lives + countdown";
            switch (CurrentLesson)
            {
                case Lesson.Move: return "A / D  MOVE";
                case Lesson.Jump: return "SPACE  >  SPACE IN AIR";
                case Lesson.Attack: return "LEFT CLICK  /  J";
                case Lesson.Combo: return "LEFT CLICK x 4  /  J x 4";
                case Lesson.Energy: return "RIGHT CLICK  /  K   |   TAP > HOLD";
                case Lesson.Recovery: return "A OFF EDGE  >  W + RIGHT CLICK / K  >  D";
                default: return "NORMAL + SPECIAL ATTACKS  /  KNOCK OUT x 2";
            }
        }
    }

    public string Progress
    {
        get
        {
            if (IsComplete) return "Practice finished  |  " + PassedCount + " passed, " + SkippedCount + " skipped";
            if (stepSucceeded) return "COMPLETE";
            switch (CurrentLesson)
            {
                case Lesson.Move: return "Reach the marker  |  " +
                    (player ? Mathf.Abs(player.transform.position.x - targetPoint.GetValueOrDefault().x).ToString("0.0") : "--") + " m";
                case Lesson.Jump: return "Ground jump " + Mathf.Min(GroundJumps, 1) + "/1   |   Air jump " + Mathf.Min(AirJumps, 1) + "/1";
                case Lesson.Attack: return "Confirmed punch 0/1";
                case Lesson.Combo: return "Confirmed combo hits " + ComboHits + "/4  |  You may skip this challenge";
                case Lesson.Energy: return "Tap " + (ShortBeamComplete ? "1/1" : "0/1") + "   |   Full charge " + (ChargedBeamComplete ? "1/1" : "0/1");
                case Lesson.Recovery: return RecoveryLaunched ? "Recovery launched  |  Land safely on a platform" : "Leave edge > Rise special > Return";
                default: return "Rival ring-outs " + TutorialRingOuts + "/2   |   Rival lives " + Mathf.Max(0, 2 - TutorialRingOuts) + "/2";
            }
        }
    }

    public string Detail
    {
        get
        {
            if (IsComplete) return "Review any lesson, retry the final lesson, or start a fresh real match. Practice has no trial time, rewards or progression.";
            if (!string.IsNullOrEmpty(transientMessage) && StepAge < transientMessageUntil)
                return transientMessage;
            if (StepAge >= 15f && quietTime >= 8f) return FocusedHelp();
            switch (CurrentLesson)
            {
                case Lesson.Move: return "No timer. The practice rival will not attack. H opens controls; Esc pauses.";
                case Lesson.Jump: return "The second jump is available once in the air. Touch the ground to refresh it.";
                case Lesson.Attack: return "Damage % is not health. Higher % means stronger knockback. Only real, unblocked hits count.";
                case Lesson.Combo: return "Each punch must hit to continue. The fourth hit is the heavy finisher. A miss resets the chain.";
                case Lesson.Energy: return "Keep A / D / W / S released for energy. Let go for an early beam; full charge fires automatically.";
                case Lesson.Recovery: return "Rise is available once per airborne trip, not endlessly. If you fall, you return here without losing a life.";
                default: return "Each real duel starts with two lives. First fall: respawn at 0%. Second fall: OUT. This lesson cannot end or reward a trial.";
            }
        }
    }

    BattleManager manager;
    GameObject player, enemy;
    Rigidbody playerBody, enemyBody;
    BattlePlayerController controller;
    BattleAttack attack;
    BattleSpecialMoves specials;
    BattleCombatInput combatInput;
    EnemyMovement enemyMovement;
    EnemyAttack enemyAttack;
    BattleDamageReceiver playerDamage, enemyDamage;
    BattleStockLives playerStocks, enemyStocks;
    BattleArenaStages stages;
    Vector3? targetPoint;
    readonly RaycastHit[] recoveryGroundHits = new RaycastHit[12];
    readonly bool[] passed = new bool[7];
    readonly bool[] skipped = new bool[7];
    bool pendingBegin, stepSucceeded, beamPreviouslyActive, beamScored;
    bool previousJLanded, previousJActive, previousJResolved, recoveryOffstage, recoveryRiseStartedOffstage;
    bool beamNeededShort;
    BattleJMove previousJMove;
    BattleSpecialKind previousSpecialKind;
    int beginAfterFrame, jumpSerial, airJumpSerial, enemyPresentationSerial, previousJStep;
    int consecutiveWhiffs, transientPriority;
    float advanceDelay, previousJElapsed, quietTime, playerRingOutUntil, enemyRingOutUntil;
    float transientMessageUntil, guidanceCooldownUntil, observedBeamCharge;
    string transientMessage;

    int PassedCount { get { int count = 0; foreach (bool value in passed) if (value) count++; return count; } }
    int SkippedCount { get { int count = 0; foreach (bool value in skipped) if (value) count++; return count; } }

    public void Bind(BattleManager owner, GameObject playerFighter, GameObject enemyFighter)
    {
        manager = owner;
        player = playerFighter;
        enemy = enemyFighter;
        playerBody = player ? player.GetComponent<Rigidbody>() : null;
        enemyBody = enemy ? enemy.GetComponent<Rigidbody>() : null;
        controller = player ? player.GetComponent<BattlePlayerController>() : null;
        attack = player ? player.GetComponent<BattleAttack>() : null;
        specials = player ? player.GetComponent<BattleSpecialMoves>() : null;
        combatInput = player ? player.GetComponent<BattleCombatInput>() : null;
        enemyMovement = enemy ? enemy.GetComponent<EnemyMovement>() : null;
        enemyAttack = enemy ? enemy.GetComponent<EnemyAttack>() : null;
        playerDamage = player ? player.GetComponent<BattleDamageReceiver>() : null;
        enemyDamage = enemy ? enemy.GetComponent<BattleDamageReceiver>() : null;
        playerStocks = player ? player.GetComponent<BattleStockLives>() : null;
        enemyStocks = enemy ? enemy.GetComponent<BattleStockLives>() : null;
        stages = manager ? manager.GetComponent<BattleArenaStages>() : null;
    }

    public void Begin()
    {
        if (!manager || !player || !enemy || !playerBody || !enemyBody || !controller)
        {
            Debug.LogError("Side A tutorial requires the bound live player and rival.", this);
            return;
        }
        IsRunning = true;
        IsComplete = IsReviewing = false;
        CurrentStepIndex = 0;
        for (int i = 0; i < passed.Length; i++) passed[i] = skipped[i] = false;
        playerRingOutUntil = enemyRingOutUntil = 0f;
        // The manager enables input immediately after Begin. Defer lesson reset
        // until newly enabled fighter Start methods have initialised their rigs.
        pendingBegin = true;
        beginAfterFrame = Time.frameCount + 1;
        targetPoint = null;
        StepAge = 0f;
    }

    public void EndSession()
    {
        if (!IsRunning) return;
        // Explicit live exits may clean other components. Scene teardown and
        // editor play-stop must only release our own state: Unity destroyed
        // objects are not null to C#'s null-conditional operator.
        bool canClean = Application.isPlaying && this && gameObject &&
            gameObject.scene.IsValid() && gameObject.scene.isLoaded && player && enemy;
        if (canClean && playerStocks) playerStocks.SetTutorialRemaining(2);
        if (canClean && enemyStocks) enemyStocks.SetTutorialRemaining(2);
        IsRunning = pendingBegin = IsReviewing = false;
        targetPoint = null;
        if (!canClean) return;
        if (specials) specials.Cancel();
        if (attack) attack.CancelForInputFocusLoss();
        BattleAfterimages playerGhosts = player ? player.GetComponent<BattleAfterimages>() : null;
        BattleAfterimages enemyGhosts = enemy ? enemy.GetComponent<BattleAfterimages>() : null;
        if (playerGhosts) playerGhosts.Clear();
        if (enemyGhosts) enemyGhosts.Clear();
    }

    bool CanTick => IsRunning && manager && manager.IsBattleActive &&
        !manager.IsPaused && !manager.HelpOpen && !manager.IsGameOver && Time.timeScale > 0f;

    void FixedUpdate()
    {
        // Disabling decisions must not change the rival's authored airborne
        // trajectory. Its AI normally owns this extra gravity in FixedUpdate.
        if (CanTick && !pendingBegin && enemyMovement && !enemyMovement.enabled &&
            enemyBody && !enemyBody.isKinematic)
            BattlePlatformPhysics.ApplyExtraGravity(enemyBody,
                enemyMovement.riseGravityMultiplier, enemyMovement.fallGravityMultiplier);
    }

    void LateUpdate()
    {
        if (!CanTick) return;
        if (pendingBegin)
        {
            if (Time.frameCount < beginAfterFrame) return;
            pendingBegin = false;
            EnterStep(CurrentStepIndex);
            return;
        }
        if (IsComplete) return;
        StepAge += Time.deltaTime;
        if (stepSucceeded)
        {
            advanceDelay -= Time.deltaTime;
            if (advanceDelay <= 0f) AdvanceStep();
            return;
        }

        ObserveActions();
        switch (CurrentLesson)
        {
            case Lesson.Move:
                if (targetPoint.HasValue && controller.IsOnGround &&
                    Mathf.Abs(player.transform.position.x - targetPoint.Value.x) < .58f &&
                    OnMainDeck(player)) Succeed();
                break;
            case Lesson.Jump:
                GroundJumps = Mathf.Max(0, (controller.JumpSerial - jumpSerial) - (controller.AirJumpSerial - airJumpSerial));
                AirJumps = Mathf.Max(0, controller.AirJumpSerial - airJumpSerial);
                if (GroundJumps > 0 && AirJumps > 0) Succeed();
                break;
            case Lesson.Energy:
                ObserveEnergy();
                break;
            case Lesson.Recovery:
                ObserveRecovery();
                break;
        }
        MaintainPracticePartner();
    }

    void ObserveActions()
    {
        bool active = attack && attack.IsJAttack;
        bool started = active && (!previousJActive || attack.CurrentJStep != previousJStep ||
            attack.CurrentJMove != previousJMove || attack.JElapsed < previousJElapsed - .001f);
        bool landed = active && attack.LastAttackLanded;
        bool freshLanded = landed && (!previousJLanded || attack.CurrentJStep != previousJStep || attack.JElapsed < previousJElapsed - .001f);
        bool freshContact = enemyDamage && enemyDamage.PresentationSerial != enemyPresentationSerial;
        if (freshContact) quietTime = 0f;
        else quietTime += Time.deltaTime;
        bool punchLesson = CurrentLesson == Lesson.Attack || CurrentLesson == Lesson.Combo;
        if (started && punchLesson && attack.CurrentJMove != BattleJMove.Neutral)
        {
            ComboHits = 0;
            Guide("W + J is uppercut; S + J is sweep. Release W / S for the four-hit combo; A / D can stay held.");
        }
        else if (started && CurrentLesson == Lesson.Energy)
            Guide("That was a normal punch. Energy uses RIGHT CLICK or K with all direction keys released.");

        BattleSpecialKind specialKind = specials ? specials.Kind : BattleSpecialKind.None;
        if (specialKind != BattleSpecialKind.None && specialKind != previousSpecialKind)
        {
            if (CurrentLesson == Lesson.Recovery && specialKind == BattleSpecialKind.Rise)
            {
                // Capture this exact move's startup, not an old excursion from
                // an earlier airborne trip. Ground-started rises do not count.
                recoveryRiseStartedOffstage = IsRecoveryOffstageNow();
                if (recoveryRiseStartedOffstage) recoveryOffstage = true;
            }
            if (punchLesson)
            {
                ComboHits = 0;
                Guide("That was a special move. Use LEFT CLICK or J for punches; release W / S, but A / D can stay held.");
            }
            else if (CurrentLesson == Lesson.Energy && specialKind != BattleSpecialKind.Charge)
                Guide(specialKind == BattleSpecialKind.Dash
                    ? "A / D + special triggers a dash. Release all direction keys to fire energy."
                    : specialKind == BattleSpecialKind.Rise
                        ? "W + special is your rising recovery. Release all direction keys to fire energy."
                        : "S + special is a counter. Release all direction keys to fire energy.");
            else if (CurrentLesson == Lesson.Recovery && specialKind != BattleSpecialKind.Rise)
                Guide("For recovery, hold W and tap RIGHT CLICK or K. Then steer D back toward a platform.");
            else if (CurrentLesson == Lesson.Recovery && !recoveryRiseStartedOffstage)
                Guide("First walk off the left edge. Then use W + special in the air to recover.");
        }

        bool missed = active && attack.LastAttackResolved && !attack.LastAttackConnected &&
            (started || !previousJResolved);
        if (missed && punchLesson && attack.CurrentJMove == BattleJMove.Neutral)
        {
            consecutiveWhiffs++;
            if (CurrentLesson == Lesson.Combo && ComboHits > 0)
            {
                ComboHits = 0;
                Guide("That punch missed, so the chain reset. Step closer, release W / S, and restart with punch one.");
            }
            else if (consecutiveWhiffs >= 2)
                Guide("Two punches missed. Face the rival and step closer before clicking. A miss cannot extend the combo.");
        }
        if (CurrentLesson == Lesson.Combo && ComboHits > 0 && !stepSucceeded &&
            (!attack || attack.CurrentJStep < 0 || started && attack.CurrentJStep == 0))
        {
            ComboHits = 0;
            Guide("The combo window closed. No penalty: start again and click the next punch as the last one connects.");
        }
        if (freshLanded && freshContact && attack.CurrentJMove == BattleJMove.Neutral)
        {
            consecutiveWhiffs = 0;
            if (CurrentLesson == Lesson.Attack) Succeed();
            else if (CurrentLesson == Lesson.Combo)
            {
                int hit = attack.CurrentJStep;
                ComboHits = hit == ComboHits ? hit + 1 : hit == 0 ? 1 : 0;
                if (ComboHits >= BattleJMoveSet.HitCount) Succeed();
            }
        }
        previousJLanded = landed;
        previousJActive = active;
        previousJResolved = active && attack.LastAttackResolved;
        previousJMove = attack ? attack.CurrentJMove : BattleJMove.Neutral;
        previousJStep = attack ? attack.CurrentJStep : -1;
        previousJElapsed = attack ? attack.JElapsed : 0f;
        previousSpecialKind = specialKind;
        enemyPresentationSerial = enemyDamage ? enemyDamage.PresentationSerial : 0;
    }

    void ObserveEnergy()
    {
        bool active = specials && specials.IsBeamActive;
        if (active && !beamPreviouslyActive)
        {
            beamScored = false;
            beamNeededShort = !ShortBeamComplete;
            observedBeamCharge = specials.Charge01;
        }
        if (active && !beamScored && specials.BeamHitCount > 0)
        {
            beamScored = true;
            if (specials.Charge01 < .30f) ShortBeamComplete = true;
            if (specials.Charge01 >= .99f && ShortBeamComplete) ChargedBeamComplete = true;
            quietTime = 0f;
            if (ShortBeamComplete && ChargedBeamComplete) Succeed();
        }
        if (beamPreviouslyActive && !active && specials && specials.Kind == BattleSpecialKind.Charge &&
            specials.Phase == BattleSpecialPhase.Recovery && !stepSucceeded)
        {
            if (!beamScored)
                Guide("The beam missed. Face the rival and move into range before firing; a hit is needed for this goal.");
            else if (beamNeededShort && observedBeamCharge >= .30f)
                Guide("That was a charged beam. For the first goal, use a quick tap; save the hold for the second goal.");
            else if (!beamNeededShort && observedBeamCharge < .99f)
                Guide("Released early. Keep holding special until the full-charge beam fires automatically.");
            else if (beamNeededShort && ShortBeamComplete)
                Message("Tap confirmed. Now hold special and let the full-charge beam fire by itself.", 4f, 1);
        }
        beamPreviouslyActive = active;
    }

    void ObserveRecovery()
    {
        if (IsRecoveryOffstageNow()) recoveryOffstage = true;
        if (recoveryOffstage && recoveryRiseStartedOffstage && specials && specials.Kind == BattleSpecialKind.Rise &&
            specials.Phase == BattleSpecialPhase.Active && playerBody.linearVelocity.y > 2f)
            RecoveryLaunched = true;
        bool landed = OnArenaPlatform(player) && controller.IsOnGround;
        if (landed)
        {
            if (RecoveryLaunched) Succeed();
            else recoveryOffstage = recoveryRiseStartedOffstage = false;
        }
        else if (RecoveryLaunched && specials && specials.RecoverySpent && combatInput &&
            combatInput.SpecialPressed && BattleSpecialMoves.ReadKind(UnityEngine.InputSystem.Keyboard.current) == BattleSpecialKind.Rise)
            Guide("Rise is already spent for this airborne trip. Steer toward a platform, or use RETRY for a free reset.");
    }

    bool IsRecoveryOffstageNow()
    {
        if (!player || !controller || controller.IsOnGround) return false;
        Bounds deck = DeckBounds(), shape = FighterBounds(player);
        return shape.center.x < deck.min.x && shape.min.y < deck.max.y + .55f;
    }

    void MaintainPracticePartner()
    {
        if (stepSucceeded || CurrentLesson == Lesson.Stocks || CurrentLesson == Lesson.Recovery) return;
        bool fightingLesson = CurrentLesson == Lesson.Attack || CurrentLesson == Lesson.Combo || CurrentLesson == Lesson.Energy;
        if (!fightingLesson || quietTime < .8f || attack && (attack.IsMovementLocked || attack.CurrentJStep >= 0) ||
            specials && specials.IsBusy || enemyDamage && enemyDamage.IsInHitstun) return;
        Bounds deck = DeckBounds();
        bool displaced = Mathf.Abs(enemy.transform.position.x - player.transform.position.x) >
            (CurrentLesson == Lesson.Energy ? 3.5f : 2f) || !OnMainDeck(enemy);
        if (!displaced && !(CurrentLesson == Lesson.Energy && enemyDamage && enemyDamage.damagePercent > 0f)) return;
        // Never move a target during an active hit/combo window. Idle resets
        // keep the real knockback intact without stranding a new player.
        ResetPracticePair(CurrentLesson);
        CaptureActionBaselines();
        quietTime = 0f;
        Message("Practice positions reset. Your completed goals are kept.", 2.8f);
    }

    public bool HandleRingOut(GameObject fighter)
    {
        if (!IsRunning || !fighter || fighter != player && fighter != enemy) return false;
        // Consume every tutorial ring-out, even duplicate trigger/fallback calls
        // and calls during a menu. None may reach a real stock/reward path.
        if (fighter == player)
        {
            if (Time.time < playerRingOutUntil) return true;
            playerRingOutUntil = Time.time + .4f;
            ResetPlayerForLesson();
            if (CurrentLesson == Lesson.Recovery) RecoveryLaunched = recoveryOffstage = recoveryRiseStartedOffstage = false;
            CaptureActionBaselines();
            Message("Free practice reset. No life lost. Try again when ready.", 3f);
            return true;
        }
        if (Time.time < enemyRingOutUntil) return true;
        enemyRingOutUntil = Time.time + .4f;
        bool counts = CanTick && !pendingBegin && !IsComplete && !stepSucceeded && CurrentLesson == Lesson.Stocks;
        if (counts)
        {
            TutorialRingOuts = Mathf.Min(2, TutorialRingOuts + 1);
            if (enemyStocks) enemyStocks.SetTutorialRemaining(2 - TutorialRingOuts);
        }
        ResetPracticePair(CurrentLesson);
        CaptureActionBaselines();
        if (counts && TutorialRingOuts == 1)
            Message("FIRST FALL: one life left. Respawn restores damage to 0%.", 5f);
        else if (counts && TutorialRingOuts == 2) Succeed();
        else Message("The practice rival is back at 0%. No real life or result was recorded.", 3f);
        return true;
    }

    public void RetryStep()
    {
        if (!CanTick || pendingBegin) return;
        IsComplete = false;
        if (!IsReviewing) passed[CurrentStepIndex] = skipped[CurrentStepIndex] = false;
        EnterStep(CurrentStepIndex);
    }

    /// <summary>Review one chosen lesson without erasing the completed visit.</summary>
    public void ReviewStep(int index)
    {
        if (!CanTick || pendingBegin || !IsComplete || index < 0 || index >= StepCount) return;
        IsReviewing = true;
        EnterStep(index);
        Message("Review mode. Complete this lesson or skip to return to your summary. Previous results are kept.", 4f, 1);
    }

    public void SkipStep()
    {
        if (!CanTick || pendingBegin || IsComplete) return;
        if (IsReviewing) { ReturnToSummary(); return; }
        if (!stepSucceeded) skipped[CurrentStepIndex] = true;
        AdvanceStep();
    }

    public void StartDuel()
    {
        if (IsRunning && manager) manager.FinishTutorialAndStartTrial();
    }

    void Succeed()
    {
        if (stepSucceeded || IsComplete) return;
        stepSucceeded = true;
        passed[CurrentStepIndex] = true;
        skipped[CurrentStepIndex] = false;
        transientMessage = null;
        advanceDelay = .70f;
    }

    void AdvanceStep()
    {
        if (IsReviewing) ReturnToSummary();
        else if (CurrentStepIndex + 1 < StepCount) EnterStep(CurrentStepIndex + 1);
        else
        {
            IsComplete = true;
            stepSucceeded = false;
            targetPoint = null;
        }
    }

    void ReturnToSummary()
    {
        IsReviewing = false;
        IsComplete = true;
        stepSucceeded = false;
        CurrentStepIndex = StepCount - 1;
        targetPoint = null;
        transientMessage = null;
    }

    void EnterStep(int index)
    {
        CurrentStepIndex = Mathf.Clamp(index, 0, StepCount - 1);
        IsComplete = stepSucceeded = false;
        StepAge = quietTime = advanceDelay = guidanceCooldownUntil = 0f;
        consecutiveWhiffs = transientPriority = 0;
        GroundJumps = AirJumps = ComboHits = TutorialRingOuts = 0;
        ShortBeamComplete = ChargedBeamComplete = RecoveryLaunched = recoveryOffstage = recoveryRiseStartedOffstage = false;
        transientMessage = null;
        if (playerStocks) playerStocks.SetTutorialRemaining(2);
        if (enemyStocks) enemyStocks.SetTutorialRemaining(2);
        ResetPracticePair(CurrentLesson);
        Bounds deck = DeckBounds();
        targetPoint = CurrentLesson == Lesson.Move ? new Vector3(deck.center.x - .8f, deck.max.y, deck.center.z) :
            CurrentLesson == Lesson.Recovery ? RecoveryTarget(deck) : (Vector3?)null;
        CaptureActionBaselines();
    }

    void CaptureActionBaselines()
    {
        jumpSerial = controller ? controller.JumpSerial : 0;
        airJumpSerial = controller ? controller.AirJumpSerial : 0;
        previousJLanded = attack && attack.IsJAttack && attack.LastAttackLanded;
        previousJActive = attack && attack.IsJAttack;
        previousJResolved = previousJActive && attack.LastAttackResolved;
        previousJMove = attack ? attack.CurrentJMove : BattleJMove.Neutral;
        previousSpecialKind = specials ? specials.Kind : BattleSpecialKind.None;
        previousJStep = attack ? attack.CurrentJStep : -1;
        previousJElapsed = attack ? attack.JElapsed : 0f;
        enemyPresentationSerial = enemyDamage ? enemyDamage.PresentationSerial : 0;
        beamPreviouslyActive = specials && specials.IsBeamActive;
        beamScored = false;
    }

    void ResetPracticePair(Lesson lesson)
    {
        if (lesson == Lesson.Recovery)
            RecoveryLaunched = recoveryOffstage = recoveryRiseStartedOffstage = false;
        Bounds deck = DeckBounds();
        float playerX = deck.center.x - 3.4f, enemyX = deck.center.x + 3.4f;
        if (lesson == Lesson.Attack || lesson == Lesson.Combo)
        { playerX = deck.center.x - 1.3f; enemyX = playerX + 1.55f; }
        else if (lesson == Lesson.Energy)
        { playerX = deck.center.x - 2f; enemyX = playerX + 3f; }
        else if (lesson == Lesson.Recovery)
        { playerX = deck.min.x + .9f; enemyX = deck.center.x + 3.4f; }
        else if (lesson == Lesson.Stocks)
        { playerX = deck.max.x - 3.1f; enemyX = deck.max.x - 1.4f; }
        ResetFighter(player, playerX, deck, 1f);
        ResetFighter(enemy, enemyX, deck, -1f);
        if (enemyMovement) enemyMovement.enabled = false;
        if (enemyAttack) enemyAttack.enabled = false;
        Physics.SyncTransforms();
    }

    void ResetPlayerForLesson()
    {
        Bounds deck = DeckBounds();
        float x = CurrentLesson == Lesson.Recovery ? deck.min.x + .9f :
            CurrentLesson == Lesson.Stocks ? deck.max.x - 3.1f : deck.center.x - 3.4f;
        ResetFighter(player, x, deck, 1f);
        Physics.SyncTransforms();
    }

    static void ResetFighter(GameObject fighter, float x, Bounds deck, float facing)
    {
        if (!fighter) return;
        Rigidbody body = fighter.GetComponent<Rigidbody>();
        Bounds shape = FighterBounds(fighter);
        float footOffset = Mathf.Clamp(fighter.transform.position.y - shape.min.y, .05f, 4f);
        if (body)
        {
            body.isKinematic = false;
            body.detectCollisions = body.useGravity = true;
        }
        var playerController = fighter.GetComponent<BattlePlayerController>();
        var fighterSpecials = fighter.GetComponent<BattleSpecialMoves>();
        var normalAttack = fighter.GetComponent<BattleAttack>();
        var rivalAttack = fighter.GetComponent<EnemyAttack>();
        var rivalMovement = fighter.GetComponent<EnemyMovement>();
        var receiver = fighter.GetComponent<BattleDamageReceiver>();
        var ghosts = fighter.GetComponent<BattleAfterimages>();
        var normalPose = fighter.GetComponent<BattleJAttackPose>();
        var specialPose = fighter.GetComponent<BattleSpecialPose>();
        if (playerController) playerController.ResetForTutorial();
        if (fighterSpecials) fighterSpecials.Cancel();
        if (normalAttack) normalAttack.CancelForInputFocusLoss();
        if (rivalAttack) rivalAttack.CancelCombo();
        if (rivalMovement) rivalMovement.ResetForStockRespawn();
        if (receiver) receiver.ResetForStockRespawn();
        if (ghosts) ghosts.Clear();
        if (normalPose) normalPose.Restore();
        if (specialPose) specialPose.Restore();
        Vector3 position = new Vector3(x, deck.max.y + footOffset + .035f, deck.center.z);
        fighter.transform.position = position;
        if (body)
        {
            body.position = position;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
        BattleFighterUtility.FaceDirection(fighter, facing);
        if (playerController) playerController.FaceLaunchDirection(facing);
        var animator = fighter.GetComponentInChildren<Animator>();
        if (animator)
        {
            animator.applyRootMotion = false;
            animator.speed = 1f;
            BattleAnimatorUtility.ResetTriggers(animator);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsGrounded"), true);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsMoving"), false);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsWalking"), false);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsDefending"), false);
        }
    }

    Bounds DeckBounds()
    {
        Collider deck = stages && stages.mainGround ? stages.mainGround.GetComponent<Collider>() : null;
        return deck ? deck.bounds : new Bounds(new Vector3(-2f, -.25f, 0f), new Vector3(24f, .5f, 5.8f));
    }

    static Bounds FighterBounds(GameObject fighter)
    {
        if (!fighter) return new Bounds(Vector3.zero, Vector3.one * 2f);
        Collider shape = fighter.GetComponent<CapsuleCollider>();
        if (!shape) shape = fighter.GetComponent<Collider>();
        return shape ? shape.bounds : new Bounds(fighter.transform.position, Vector3.one * 2f);
    }

    bool OnMainDeck(GameObject fighter)
    {
        if (!fighter) return false;
        Bounds deck = DeckBounds(), shape = FighterBounds(fighter);
        return shape.center.x > deck.min.x + .15f && shape.center.x < deck.max.x - .15f &&
            Mathf.Abs(shape.min.y - deck.max.y) < .23f;
    }

    bool OnArenaPlatform(GameObject fighter)
    {
        if (!fighter || !controller || !controller.IsOnGround) return false;
        Bounds feet = FighterBounds(fighter);
        Vector3 origin = new Vector3(feet.center.x, feet.min.y + .12f, feet.center.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, recoveryGroundHits,
            .36f, 1 << 0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = recoveryGroundHits[i];
            Collider surface = hit.collider;
            if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy ||
                hit.normal.y < .5f || Mathf.Abs(feet.min.y - hit.point.y) > .24f)
                continue;
            if (BelongsToPlatform(surface, stages ? stages.mainGround : null)) return true;
            if (stages && stages.upperPlatforms != null)
                foreach (Transform platform in stages.upperPlatforms)
                    if (BelongsToPlatform(surface, platform)) return true;
        }
        return false;
    }

    static bool BelongsToPlatform(Collider surface, Transform platform) =>
        surface && platform && (surface.transform == platform || surface.transform.IsChildOf(platform));

    Vector3 RecoveryTarget(Bounds deck)
    {
        Collider leftSurface = null;
        if (stages && stages.upperPlatforms != null)
            foreach (Transform platform in stages.upperPlatforms)
            {
                if (!platform) continue;
                foreach (Collider surface in platform.GetComponentsInChildren<Collider>())
                {
                    if (!surface.enabled || surface.isTrigger || !surface.gameObject.activeInHierarchy ||
                        surface.bounds.size.x < 1f || surface.bounds.center.x >= deck.center.x)
                        continue;
                    if (!leftSurface || surface.bounds.center.x < leftSurface.bounds.center.x ||
                        Mathf.Approximately(surface.bounds.center.x, leftSurface.bounds.center.x) &&
                        surface.bounds.max.y > leftSurface.bounds.max.y)
                        leftSurface = surface;
                }
            }
        return leftSurface ? new Vector3(leftSurface.bounds.center.x, leftSurface.bounds.max.y, leftSurface.bounds.center.z) :
            new Vector3(deck.min.x + 1.7f, deck.max.y, deck.center.z);
    }

    string FocusedHelp()
    {
        switch (CurrentLesson)
        {
            case Lesson.Move: return "No time limit. Hold D to walk right to the mint circle; release it when you arrive.";
            case Lesson.Jump: return "Take your time: tap Space on the stone, release it, then tap it again before you land.";
            case Lesson.Attack: return "Stand close and face the rival. Release W / S, then click LEFT or tap J; A / D can stay held.";
            case Lesson.Combo: return "Land punch one, then click again as it connects. Keep W / S released; A / D is allowed.";
            case Lesson.Energy: return ShortBeamComplete
                ? "Face the rival first. Release directions, then keep RIGHT CLICK / K held until the beam fires automatically."
                : "Face the rival and stay close. Release directions, then tap and immediately release RIGHT CLICK / K.";
            case Lesson.Recovery: return "There is no countdown. Walk off when ready; W + special rises once, then D steers toward the mint target.";
            default: return "No time limit. Hit the rival toward the right edge. Higher damage % makes knockback stronger.";
        }
    }

    void Guide(string message)
    {
        // Mistake feedback outranks quiet position/reset notices, but does not
        // flash a new paragraph for every click. It never fails a lesson.
        if (StepAge < guidanceCooldownUntil) return;
        guidanceCooldownUntil = StepAge + .8f;
        Message(message, 4.5f, 2);
    }

    void Message(string message, float seconds, int priority = 0)
    {
        if (!string.IsNullOrEmpty(transientMessage) && StepAge < transientMessageUntil && priority < transientPriority)
            return;
        transientMessage = message;
        transientMessageUntil = StepAge + seconds;
        transientPriority = priority;
    }

    // Other fighter components may already be gone when Unity invokes this.
    // Their own disable/destroy hooks clean their VFX; never traverse them here.
    void OnDisable()
    {
        IsRunning = pendingBegin = IsReviewing = false;
        targetPoint = null;
    }
}
