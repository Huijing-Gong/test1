using System.Collections;
using UnityEngine;

// Per-fighter, per-round stocks. Scene reload/retry creates a fresh two lives.
public sealed class BattleStockLives : MonoBehaviour
{
    public int Remaining { get; private set; } = 2;
    public bool Respawning { get; private set; }
    float protectedUntil;
    LineRenderer halo;
    Material haloMaterial;
    public bool IsProtected => Respawning || Time.time < protectedUntil || Remaining == 0;
    public enum RingOut { Ignored, Respawn, Eliminated }

    public RingOut LoseStock()
    {
        var trainingManager = FindFirstObjectByType<BattleManager>();
        if (trainingManager && trainingManager.IsTutorialActive)
        {
            trainingManager.TryHandleTutorialRingOut(gameObject);
            return RingOut.Ignored;
        }
        if (IsProtected) return RingOut.Ignored;
        Remaining--;
        if (Remaining == 0) return RingOut.Eliminated;
        SideAAudio.StockLost(CompareTag("Enemy"));
        Respawning = true; // Synchronous guard against trigger/fallback duplicates.
        StartCoroutine(ReturnToArena());
        return RingOut.Respawn;
    }

    // Only the isolated lesson's stock demonstration may drive the real HUD.
    // Formal matches always get a newly loaded component with two stocks.
    public void SetTutorialRemaining(int value)
    {
        var manager = FindFirstObjectByType<BattleManager>();
        if (!manager || !manager.IsTutorialActive) return;
        StopAllCoroutines();
        Remaining = Mathf.Clamp(value, 0, 2);
        Respawning = false;
        protectedUntil = 0;
    }

    IEnumerator ReturnToArena()
    {
        var body = GetComponent<Rigidbody>();
        var damage = GetComponent<BattleDamageReceiver>();
        var player = GetComponent<BattlePlayerController>();
        var enemy = GetComponent<EnemyMovement>();
        var attack = GetComponent<BattleAttack>();
        var enemyAttack = GetComponent<EnemyAttack>();
        var special = GetComponent<BattleSpecialMoves>();
        Behaviour[] behaviours = { player, enemy, attack, enemyAttack, special };
        bool[] enabled = new bool[behaviours.Length];
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i]) { enabled[i] = behaviours[i].enabled; behaviours[i].enabled = false; }
        enemyAttack?.CancelCombo(); special?.Cancel();
        GetComponent<BattleAfterimages>()?.Clear();
        damage?.ResetForStockRespawn();
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        var manager = FindFirstObjectByType<BattleManager>();
        var stages = manager ? manager.GetComponent<BattleArenaStages>() : null;
        var deck = stages && stages.mainGround ? stages.mainGround.GetComponent<Collider>() : null;
        var capsule = GetComponent<CapsuleCollider>();
        float foot = capsule ? body.position.y - capsule.bounds.min.y : 1f;
        Vector3 top = deck ? new Vector3(deck.bounds.center.x, deck.bounds.max.y, deck.bounds.center.z) : new Vector3(-2, 0, 0);
        var opponent = GameObject.FindGameObjectWithTag(CompareTag("Player") ? "Enemy" : "Player");
        float side = opponent ? (opponent.transform.position.x >= top.x ? -1 : 1) : (CompareTag("Player") ? -1 : 1);
        body.position = top + new Vector3(side * 4, foot + .04f, 0);
        transform.position = body.position;
        Physics.SyncTransforms();
        if (!halo)
        {
            var ring = new GameObject("Respawn protection ring"); ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Vector3.up * (-foot + .07f);
            halo = ring.AddComponent<LineRenderer>(); halo.useWorldSpace = false;
            halo.loop = true; halo.positionCount = 32; halo.widthMultiplier = .055f;
            haloMaterial = new Material(Shader.Find("Sprites/Default")); halo.sharedMaterial = haloMaterial;
            halo.startColor = halo.endColor = new Color(.52f,.90f,.80f,.85f);
            for (int i=0;i<32;i++) { float angle=i*Mathf.PI*2/32; halo.SetPosition(i,new Vector3(Mathf.Cos(angle)*.8f,0,Mathf.Sin(angle)*.8f)); }
        }
        var animator = GetComponentInChildren<Animator>();
        if (animator)
        {
            animator.speed = 1;
            BattleAnimatorUtility.ResetTriggers(animator);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsGrounded"), true);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsMoving"), false);
            BattleAnimatorUtility.SetBoolIfPresent(animator, Animator.StringToHash("IsWalking"), false);
        }
        BattleFighterUtility.FaceDirection(gameObject, -side);
        yield return new WaitForSeconds(.4f);
        // The other fighter may have lost their final stock during this return.
        if (!manager || manager.IsGameOver) { Respawning = false; yield break; }
        body.isKinematic = false; body.useGravity = true;
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        enemy?.ResetForStockRespawn();
        if (player) player.FaceLaunchDirection(-side);
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i]) behaviours[i].enabled = enabled[i];
        protectedUntil = Time.time + 1.5f;
        Respawning = false;
    }
    void LateUpdate() { if (halo) halo.enabled = Remaining > 0 && IsProtected; }
    void OnDestroy() { if (haloMaterial) Destroy(haloMaterial); }
}
