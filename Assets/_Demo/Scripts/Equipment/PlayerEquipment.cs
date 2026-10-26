using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerEquipment : MonoBehaviour
{
    [SerializeField] private Transform weaponSocket;
    [SerializeField] private GameObject practiceSword;

    public bool HasSword => GameSession.Instance != null && GameSession.Instance.HasSword;
    public bool IsSwordEquipped => HasSword && GameSession.Instance.SwordEquipped && practiceSword != null;
    // Attack presentation only: drawing never changes equipment or save data.
    public bool SwordDrawn { get; private set; }
    public const float SwordIdleSeconds = .45f;
    private float swordIdleRemaining;
    public bool CanAttack => IsSwordEquipped && (!health || !health.IsDead);
    private PlayerCombat combat;
    private PlayerMovement movement;
    private Health health;
    private BaselineFlow flow;

    // Called only after PlayerCombat admits the original attack.
    public void DrawForAttack()
    {
        if (!CanAttack) return;
        if(!SwordDrawn)GetComponentInChildren<KingOfGraphic.Hero.HeroPoseDriver>()?.BeginOverlay("EQUIP_DRAW",.3f,true);
        SwordDrawn = true; swordIdleRemaining = SwordIdleSeconds;
        SetSwordVisible(true);
    }

    public void StowSword()
    {
        SwordDrawn = false; swordIdleRemaining = 0;
        SetSwordVisible(false);
    }

    private void LateUpdate()
    {
        if (!IsSwordEquipped || (health && health.IsDead) ||
            (movement && (!movement.isActiveAndEnabled || movement.SwimmingActive || movement.ClimbingActive || movement.MantlingActive)) ||
            (flow && flow.SamBridge && flow.SamBridge.IsAway) ||
            (SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy)))
        { StowSword(); return; }
        if (!SwordDrawn) return;
        if ((combat && combat.IsAttacking) || (GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>()?.KeepsSword??false)) swordIdleRemaining = SwordIdleSeconds;
        else if (Time.timeScale > 0 && (!flow || !flow.WorldHudHidden))
        {
            swordIdleRemaining -= Time.deltaTime;
            if (swordIdleRemaining <= 0 && GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>()?.Library?.authoredControls==true){StowSword();return;}
            if (swordIdleRemaining <= 0){var hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();if(hero){if(!hero.Busy&&!hero.HasPresentation("EQUIP_SHEATHE"))hero.RequestPresentation("EQUIP_SHEATHE",false,()=>{if(!hero.Attacking&&!hero.Guarding)StowSword();},KingOfGraphic.Hero.HeroPoseDriver.SheathHideFraction);}else StowSword();}
        }
    }
    public void SetWorldSwordDrawn(bool drawn) { SwordDrawn=drawn&&IsSwordEquipped;SetSwordVisible(SwordDrawn); }
    public Transform WeaponSocket => weaponSocket;
    public GameObject PracticeSword => practiceSword;

    private void Awake()
    {
        combat = GetComponent<PlayerCombat>(); movement = GetComponent<PlayerMovement>(); health = GetComponent<Health>();
        flow = FindFirstObjectByType<BaselineFlow>();
        SynchronizeSwordState();
    }

    private void OnEnable()
    {
        GameSession.SwordStateChanged += OnSwordStateChanged;
        GameSession.SwordEquipChanged += OnSwordEquipChanged;
        SynchronizeSwordState();
    }

    private void Start() => SynchronizeSwordState();

    private void OnDisable()
    {
        GameSession.SwordStateChanged -= OnSwordStateChanged;
        GameSession.SwordEquipChanged -= OnSwordEquipChanged;
        StowSword();
    }

    private void OnSwordStateChanged(bool hasSword) => SynchronizeSwordState();
    private void OnSwordEquipChanged(bool equipped) => SynchronizeSwordState();

    private void SynchronizeSwordState()
    {
        StowSword();
    }

    private void SetSwordVisible(bool visible)
    {
        var pose=GetComponentInChildren<KingOfGraphic.Hero.HeroPoseDriver>(true);
        if(pose){pose.SetSwordCarried(IsSwordEquipped&&isActiveAndEnabled&&(!health||!health.IsDead)&&(!movement||!movement.SwimmingActive&&!movement.ClimbingActive&&!movement.MantlingActive)&&(!flow||!flow.SamBridge||!flow.SamBridge.IsAway)&&!(SideWorldTravel.Instance&&(SideWorldTravel.Instance.IsAway||SideWorldTravel.Instance.Busy)));pose.SetSwordVisible(visible);}
        if (practiceSword != null && practiceSword.activeSelf != visible)
        {
            practiceSword.SetActive(visible);
        }
    }
}
