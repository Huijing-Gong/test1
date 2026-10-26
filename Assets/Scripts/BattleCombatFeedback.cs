using UnityEngine;

public class BattleCombatFeedback : MonoBehaviour
{
    private const float HitStopTimeScale = 0.05f;

    private static readonly float[] HitStopDurations =
    {
        0.035f,
        0.045f,
        0.11f
    };

    private static BattleCombatFeedback instance;
    private bool hitStopActive;
    private float hitStopTimer;
    private float timeScaleBeforeHitStop = 1f;

    // Help/manual pause must resume normal simulation, not a transient 0.05
    // hit-stop whose unscaled timer may expire while the game is paused.
    public static float UnpausedTimeScale => instance != null && instance.hitStopActive
        ? instance.timeScaleBeforeHitStop : Time.timeScale;

    public static void PlayComboHit(
        Vector3 impactPosition,
        float horizontalDirection,
        int comboStep,
        bool isEnemyAttack,
        float targetDamagePercent,
        bool isUppercut = false
    )
    {
        EnsureInstance();
        int safeStep = Mathf.Clamp(
            comboStep,
            0,
            BattleFourHitCombo.HitCount - 1
        );
        float hitStopDuration = isUppercut
            ? 0.145f
            : HitStopDurations[safeStep];
        instance.StartHitStop(hitStopDuration);
        BattleContactPresentation.Emit(impactPosition,horizontalDirection,safeStep,isEnemyAttack,isUppercut);
    }

    static void EnsureInstance()
    {
        if (instance != null)
            return;

        GameObject feedbackObject =
            new GameObject("BattleCombatFeedback");
        instance = feedbackObject.AddComponent<BattleCombatFeedback>();
    }

    public static float JHitStopDuration(BattleJMove move,int step)
    {
        if(move==BattleJMove.Neutral)
            return step>=3 ? .10f : step==2 ? .040f : step==1 ? .030f : .025f;
        return HitStopDurations[move==BattleJMove.Side ? 1 : 0];
    }
    public static void PlayJHitStop(BattleJMove move,int step)
    {
        EnsureInstance();
        instance.StartHitStop(JHitStopDuration(move,step));
    }

    public static void PlayKHitStop(BattleSpecialKind kind,float charge)
    {
        EnsureInstance();
        instance.StartHitStop(kind==BattleSpecialKind.Rise?.145f:kind==BattleSpecialKind.Charge?
            (charge>.8f?.11f:.035f):.045f);
    }

    void StartHitStop(float duration)
    {
        if (Time.timeScale <= 0f)
            return;

        if (!hitStopActive)
        {
            timeScaleBeforeHitStop = Time.timeScale;
            hitStopActive = true;
            Time.timeScale = HitStopTimeScale;
        }

        hitStopTimer = Mathf.Max(hitStopTimer, duration);
    }

    void Update()
    {
        if (!hitStopActive)
            return;

        hitStopTimer -= Time.unscaledDeltaTime;
        if (hitStopTimer > 0f)
            return;

        hitStopActive = false;
        hitStopTimer = 0f;

        if (Mathf.Approximately(Time.timeScale, HitStopTimeScale))
        {
            Time.timeScale = timeScaleBeforeHitStop;
        }
    }

    void OnDestroy()
    {
        if (hitStopActive &&
            Mathf.Approximately(Time.timeScale, HitStopTimeScale))
        {
            Time.timeScale = timeScaleBeforeHitStop;
        }

        if (instance == this)
        {
            instance = null;
        }
    }
}
