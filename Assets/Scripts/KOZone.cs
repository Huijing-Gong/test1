using UnityEngine;

public class KOZone : MonoBehaviour
{
    private static readonly int DeathTriggerHash =
        Animator.StringToHash("Death");

    private static readonly int WaveTriggerHash =
        Animator.StringToHash("Wave");

    private static readonly int IsMovingHash =
        Animator.StringToHash("IsMoving");

    private static readonly int IsWalkingHash =
        Animator.StringToHash("IsWalking");

    private const float MinimumDefeatedFighterSpacing = 4.5f;
    private const float FrontFacingRotationY = 180f;

    public BattleManager battleManager;

    [Header("Death Presentation")]
    [Min(MinimumDefeatedFighterSpacing)]
    public float defeatedFighterSpacing =
        MinimumDefeatedFighterSpacing;

    public float arenaCenterX = 0f;

    private bool koHandled;

    // The finite authored bottom trigger has horizontal ends. A fighter which
    // falls beyond them must still use the same KO/result/cleanup path.
    public void ResolveFallenFighter(GameObject fighter)
    {
        if (koHandled || !fighter || !battleManager ||
            !battleManager.IsBattleActive || battleManager.IsGameOver) return;
        Collider contact = fighter.GetComponent<Collider>();
        if (!contact) return;
        if (fighter.CompareTag("Enemy")) HandleKO(contact, true);
        else if (fighter.CompareTag("Player")) HandleKO(contact, false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (koHandled)
            return;

        // A fighter is intentionally placed above the arena during the
        // opening. Do not resolve an accidental ring-out until GO appears.
        if (battleManager != null && !battleManager.IsBattleActive)
            return;

        if (other.CompareTag("Enemy"))
        {
            HandleKO(other, playerWon: true);
        }
        else if (other.CompareTag("Player"))
        {
            HandleKO(other, playerWon: false);
        }
    }

    private void HandleKO(Collider other, bool playerWon)
    {
        GameObject fighter = GetFighterRoot(other);
        // Consume training falls before touching stocks or the one-shot KO
        // latch; a lesson must never complete a host trial or play a result.
        if (battleManager && battleManager.TryHandleTutorialRingOut(fighter)) return;
        var stocks = fighter.GetComponent<BattleStockLives>();
        if (stocks)
        {
            var result = stocks.LoseStock();
            if (result != BattleStockLives.RingOut.Eliminated) return;
        }
        koHandled = true;
        GameObject winner = FindWinner(playerWon);

        BattleKOPresentation.Play(fighter.transform.position,playerWon);

        PositionForDeathPresentation(fighter, winner);
        StopFighter(winner);
        StopFighter(fighter);
        FaceCamera(winner);
        PlayTriggeredAnimation(winner, WaveTriggerHash);
        PlayDeathAnimation(fighter);

        if (battleManager != null)
        {
            if (playerWon)
            {
                battleManager.PlayerWon();
            }
            else
            {
                battleManager.PlayerLost();
            }
        }
        else
        {
            Debug.LogError("KOZone requires a BattleManager reference.");
        }

    }

    private static GameObject FindWinner(bool playerWon)
    {
        string winnerTag = playerWon ? "Player" : "Enemy";

        return GameObject.FindGameObjectWithTag(winnerTag);
    }

    private void PositionForDeathPresentation(
        GameObject defeatedFighter,
        GameObject winner
    )
    {
        if (defeatedFighter == null)
            return;

        var stageObject = GameObject.Find("BattleGround");
        var stage = stageObject != null ? stageObject.GetComponent<Collider>() : null;
        if (stage != null && stage.enabled)
        {
            Bounds bounds = stage.bounds;
            float spacing = Mathf.Min(Mathf.Max(MinimumDefeatedFighterSpacing,
                defeatedFighterSpacing), Mathf.Max(1f, bounds.size.x - 2f));
            float winnerX = bounds.center.x - spacing * .5f;
            float loserX = bounds.center.x + spacing * .5f;
            // Both fighters can be offstage/in the air when KO fires. Using
            // the winner's current Y would freeze both outside the camera.
            PlaceOnStage(winner, stage, winnerX, bounds.center.z);
            PlaceOnStage(defeatedFighter, stage, loserX, bounds.center.z);
            FaceArenaCenter(defeatedFighter);
            return;
        }

        if (winner != null)
        {
            float presentationSpacing = Mathf.Max(
                MinimumDefeatedFighterSpacing,
                defeatedFighterSpacing
            );

            float directionTowardCenter =
                Mathf.Sign(arenaCenterX - winner.transform.position.x);

            if (Mathf.Approximately(directionTowardCenter, 0f))
            {
                directionTowardCenter =
                    defeatedFighter.transform.position.x >= arenaCenterX
                        ? 1f
                        : -1f;
            }

            defeatedFighter.transform.position = new Vector3(
                winner.transform.position.x +
                    directionTowardCenter * presentationSpacing,
                winner.transform.position.y,
                winner.transform.position.z
            );
        }

        FaceArenaCenter(defeatedFighter);
    }

    private static void PlaceOnStage(GameObject fighter, Collider stage, float x, float z)
    {
        if (fighter == null) return;
        float lowestY = fighter.transform.position.y;
        foreach (Collider bodyCollider in fighter.GetComponentsInChildren<Collider>())
            if (bodyCollider.enabled && !bodyCollider.isTrigger)
                lowestY = Mathf.Min(lowestY, bodyCollider.bounds.min.y);
        float feetOffset = fighter.transform.position.y - lowestY;
        var ray = new Ray(new Vector3(x, stage.bounds.max.y + 4f, z), Vector3.down);
        if (!stage.Raycast(ray, out RaycastHit ground, stage.bounds.size.y + 8f)) return;
        Vector3 pose = new Vector3(x, ground.point.y + feetOffset + .02f, z);
        var body = fighter.GetComponent<Rigidbody>();
        if (body != null) body.position = pose;
        fighter.transform.position = pose;
    }

    private void FaceArenaCenter(GameObject fighter)
    {
        Transform visualRoot = GetVisualRoot(fighter);

        if (visualRoot == null)
            return;

        float directionToCenter =
            arenaCenterX - fighter.transform.position.x;
        float yRotation = directionToCenter >= 0f ? 90f : -90f;

        visualRoot.localRotation = Quaternion.Euler(
            0f,
            yRotation,
            0f
        );
    }

    private static void FaceCamera(GameObject fighter)
    {
        BattleFighterUtility.FaceCamera(fighter, FrontFacingRotationY);
    }

    private static Transform GetVisualRoot(GameObject fighter)
    {
        return BattleFighterUtility.GetVisualRoot(fighter);
    }

    private static GameObject GetFighterRoot(Collider other)
    {
        if (other.attachedRigidbody != null)
        {
            return other.attachedRigidbody.gameObject;
        }

        return other.gameObject;
    }

    private static void PlayDeathAnimation(GameObject fighter)
    {
        PlayTriggeredAnimation(fighter, DeathTriggerHash);
    }

    private static void PlayTriggeredAnimation(
        GameObject fighter,
        int triggerHash
    )
    {
        if (fighter == null)
            return;

        Animator animator = fighter.GetComponentInChildren<Animator>();

        if (animator == null ||
            animator.runtimeAnimatorController == null ||
            !HasAnimatorTrigger(animator, triggerHash))
        {
            return;
        }

        BattleAnimatorUtility.ResetTriggers(animator);
        animator.SetTrigger(triggerHash);
    }

    private static void ResetAnimatorTriggers(Animator animator)
    {
        BattleAnimatorUtility.ResetTriggers(animator);
    }

    private static bool HasAnimatorTrigger(
        Animator animator,
        int triggerHash
    )
    {
        return BattleAnimatorUtility.HasParameter(
            animator,
            triggerHash,
            AnimatorControllerParameterType.Trigger
        );
    }

    private static void StopFighter(GameObject fighter)
    {
        if (fighter == null)
            return;

        DisableBehaviour<BattlePlayerController>(fighter);
        DisableBehaviour<BattleAttack>(fighter);
        DisableBehaviour<EnemyMovement>(fighter);
        DisableBehaviour<EnemyAttack>(fighter);

        Animator animator = fighter.GetComponentInChildren<Animator>();

        SetAnimatorBoolIfPresent(animator, IsMovingHash, false);
        SetAnimatorBoolIfPresent(animator, IsWalkingHash, false);

        Rigidbody fighterBody = fighter.GetComponent<Rigidbody>();

        if (fighterBody != null)
        {
            fighterBody.linearVelocity = Vector3.zero;
            fighterBody.angularVelocity = Vector3.zero;
            fighterBody.isKinematic = true;
            fighterBody.detectCollisions = false;
        }

        foreach (Collider fighterCollider in
            fighter.GetComponentsInChildren<Collider>(true))
        {
            fighterCollider.enabled = false;
        }
    }

    private static void SetAnimatorBoolIfPresent(
        Animator animator,
        int parameterHash,
        bool value
    )
    {
        BattleAnimatorUtility.SetBoolIfPresent(
            animator,
            parameterHash,
            value
        );
    }

    private static void DisableBehaviour<T>(GameObject fighter)
        where T : Behaviour
    {
        T behaviour = fighter.GetComponent<T>();

        if (behaviour != null)
        {
            behaviour.enabled = false;
        }
    }
}
