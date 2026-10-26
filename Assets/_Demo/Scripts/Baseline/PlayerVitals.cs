using UnityEngine;
using UnityEngine.InputSystem;

// Opt-in on the overworld player; native side-world controllers keep their own rules.
[DefaultExecutionOrder(500)]
[RequireComponent(typeof(Health), typeof(PlayerMovement))]
[DisallowMultipleComponent]
public sealed class PlayerVitals : MonoBehaviour
{
    [Header("Stamina")]
    [SerializeField, Min(1)] float maximumStamina = 100;
    [SerializeField, Min(0)] float sprintDrain = 12, climbDrain = 10, swimDrain = 7, treadDrain = 4;
    [SerializeField, Min(0)] float recoveryRate = 22, recoveryDelay = 1.25f;
    [SerializeField, Range(.01f, 1)] float exhaustionRecoveryFraction = .2f;
    [Header("Environment damage")]
    [SerializeField, Min(0)] float safeFallDistance = 4, fallDamagePerUnit = 12;
    [Header("Field aid (session supply; camp refills)")]
    [SerializeField, Min(1)] int maximumAidCharges = 3, aidHealing = 35;
    Health health;
    float baseMaximumStamina; int baseMaximumHealth;
    OverworldHealthHud healthHud;
    MainWorldDeathUI deathUI;
    public bool DeathOpen => deathUI && deathUI.IsOpen;
    public bool CustomHealthHudVisible => healthHud && healthHud.Visible;
    PlayerMovement movement;
    PlayerCombat combat;
    BaselineFlow flow;
    OverworldV2Layout layout;
    BaselineLoadout loadout;
    float recoveryWait, fallPeak, damageFlash;
    bool deathPending, exhausted;
    GUIStyle label;
    float staminaLastActiveTime=-10, staminaHudAlpha;
    public float StaminaHudAlpha => staminaHudAlpha;
    public bool StaminaLow => Stamina/Mathf.Max(1,maximumStamina)<=.25f;
    public Rect StaminaHudRect { get; private set; }
    void UpdateStaminaPresentation()
    {
        bool active=Stamina<maximumStamina-.01f || movement.SprintingActive || movement.SwimmingActive || movement.ClimbingActive || movement.MantlingActive;
        if(active)staminaLastActiveTime=Time.unscaledTime;
        float target=active || Time.unscaledTime-staminaLastActiveTime<.6f?1:0;
        staminaHudAlpha=Mathf.MoveTowards(staminaHudAlpha,target,Time.unscaledDeltaTime/(target>0?.08f:.2f));
    }
    public float Stamina { get; private set; }
    public float MaximumStamina => maximumStamina;
    public int AidCharges { get; private set; }
    public bool CanExert => !exhausted && Stamina > 0 && !health.IsDead;
    public bool IsExhausted => exhausted;
    public bool IsAtCamp => flow && !(flow.endingRoom && flow.endingRoom.IsInside) &&
        movement.CanStartInteraction && Vector3.Distance(transform.position, layout ? layout.CampPosition : BaselineSaveData.DefaultSafePosition) < 5;
    public bool IsAtSupplyStation => BaselineWaystation.FindSupplyStation(flow) != null;
    bool WorldCombatAvailable => isActiveAndEnabled && flow && flow.Started && !flow.BlockWorldInput &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy));
    public bool SimulationActive => WorldCombatAvailable && Time.timeScale > 0 && !movement.HasExternalInputLock;
    // A sword swing locks movement; its short hit-stop freezes enemy attacks without cancelling them.
    public bool EnemyCombatActive => WorldCombatAvailable &&
        (Time.timeScale > 0 || (combat && combat.IsApplyingHitStop)) &&
        !movement.HasExternalInputLock;

    void Awake()
    {
        health = GetComponent<Health>(); movement = GetComponent<PlayerMovement>(); combat = GetComponent<PlayerCombat>();
        baseMaximumStamina=maximumStamina; baseMaximumHealth=health.MaxHealth;
        healthHud = GetComponent<OverworldHealthHud>();
        if (!healthHud) healthHud = gameObject.AddComponent<OverworldHealthHud>();
        deathUI = GetComponent<MainWorldDeathUI>();
        if (!deathUI) deathUI = gameObject.AddComponent<MainWorldDeathUI>();
        flow = FindFirstObjectByType<BaselineFlow>();
        layout = flow ? flow.GetComponent<OverworldV2Layout>() : null;
        loadout = GetComponent<BaselineLoadout>();
        Stamina = maximumStamina; AidCharges = maximumAidCharges; ResetTraversal();
    }
    public void SetTrialBBonus(int bonus)
    {
        bonus=Mathf.Clamp(bonus,0,30);
        health.SetMaximumHealthPreservingCurrent(baseMaximumHealth+bonus);
        maximumStamina=baseMaximumStamina+bonus;
        Stamina=Mathf.Min(Stamina,maximumStamina);
    }
    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; ResetTraversal(); }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; }
    void OnDamaged(Health target, DamageInfo damage) { damageFlash = .45f; }
    bool HeroDeathReady => GetComponent<WorldHeroActions>() ? GetComponent<WorldHeroActions>().DeathAnimationFinished : (!GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>() || GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>().DeathAnimationFinished);
    void OnDied(Health target, DamageInfo damage)
    {
        deathPending = true;
        if (flow && flow.Started && HeroDeathReady) deathUI.Show(flow, movement);
    }

    void LateUpdate()
    {
        UpdateStaminaPresentation();
        if (deathPending && flow && flow.Started)
        {
            // The player chooses Retry or Restore at Safe Point on the death UI.
            if (!DeathOpen && HeroDeathReady) deathUI.Show(flow, movement);
            return;
        }
        // Modal screens freeze resource use.
        if (!SimulationActive) return;
        float dt = Time.deltaTime;
        damageFlash = Mathf.Max(0, damageFlash - dt);
        bool swimming = movement.SwimmingActive;
        bool climbing = movement.ClimbingActive || movement.MantlingActive || (GetComponent<KingOfGraphic.Hero.HeroTraversal>()?.Exerting??false);
        float drain = swimming ? (movement.SwimAmount > .1f ? swimDrain : treadDrain) :
            climbing ? climbDrain : movement.SprintingActive ? sprintDrain * (loadout ? loadout.SprintDrainMultiplier : 1f) : 0;
        if (drain > 0)
        {
            Stamina = Mathf.Max(0, Stamina - drain * dt); recoveryWait = recoveryDelay;
            if (Stamina <= 0) exhausted = true;
        }
        else if (movement.IsStableGrounded)
        {
            recoveryWait = Mathf.Max(0, recoveryWait - dt);
            if (recoveryWait <= 0) Stamina = Mathf.Min(maximumStamina, Stamina + recoveryRate * dt);
            if (Stamina >= maximumStamina * exhaustionRecoveryFraction) exhausted = false;
        }
        if (swimming && Stamina <= 0)
        {
            // Exhaustion is lethal immediately, independent of remaining HP.
            ApplyEnvironmentDamage(health.MaxHealth);
            return;
        }

        if (swimming || climbing) fallPeak = transform.position.y;
        else
        {
            fallPeak = Mathf.Max(fallPeak, transform.position.y);
            if (movement.IsStableGrounded)
            {
                float drop = fallPeak - transform.position.y;
                fallPeak = transform.position.y;
                if (drop > safeFallDistance) ApplyEnvironmentDamage(Mathf.CeilToInt((drop - safeFallDistance) * fallDamagePerUnit));
            }
        }
        if (!deathPending && Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) TryUseRecovery();
    }

    public int LastFieldAidUseFrame {get;private set;}=-100;
    public bool TrySpendCombatStamina(float amount)
    {
        if(amount < 0 || !CanExert || Stamina < amount)return false;
        Stamina-=amount;recoveryWait=recoveryDelay;if(Stamina<=0)exhausted=true;return true;
    }
    public bool TryUseRecovery()
    {
        if(!SimulationActive||health.IsDead||!movement.CanStartInteraction)return false;
        var actions=GetComponent<WorldHeroActions>();
        if(actions && (IsAtCamp||IsAtSupplyStation))return actions.TryRest(()=>CommitRecovery());
        var hero=GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>();
        if(hero){if(!IsAtCamp&&!IsAtSupplyStation&&(AidCharges<=0||health.CurrentHealth>=health.MaxHealth))return false;return hero.PresentInteraction(IsAtCamp||IsAtSupplyStation?"INTERACT_REST_ENTER":"INTERACT_USE_ITEM",()=>CommitRecovery());}
        return CommitRecovery();
    }
    bool CommitRecovery()
    {
        if (!SimulationActive || health.IsDead || !movement.CanStartInteraction) return false;
        if (IsAtCamp)
        {
            Refill(); Notice("Rested at camp. Health, stamina and field aid replenished."); return true;
        }
        if (IsAtSupplyStation)
        {
            Refill(); Notice("Health, stamina and field aid replenished."); return true;
        }
        if (AidCharges <= 0) { Notice("No field aid left. Rest at camp to refill."); return false; }
        if (health.CurrentHealth >= health.MaxHealth) { Notice("Health is already full. Field aid kept."); return false; }
        if (!health.TryHeal(aidHealing)) return false;
        AidCharges--;LastFieldAidUseFrame=Time.frameCount; Notice("Field aid: +" + aidHealing + " health (up to maximum)."); return true;
    }
    // Teammate's transient Prism drop, within the existing field-aid capacity.
    public bool TryGrantPrismAid()
    {
        if (!EnemyCombatActive || health.IsDead || AidCharges >= maximumAidCharges) return false;
        AidCharges++;
        Notice("Prism core salvaged: +1 field aid charge.");
        return true;
    }
    public bool ApplyEnvironmentDamage(int amount)
    {
        return SimulationActive && health.TryApplyDamage(new DamageInfo(amount, null, transform.position, Vector3.zero));
    }
    public void FellOutOfWorld()
    {
        if (SimulationActive) ApplyEnvironmentDamage(health.MaxHealth);
    }
    public void Refill()
    {
        health.RestoreFullHealth();GetComponent<WorldHeroActions>()?.ResetForRespawn(); Stamina = maximumStamina; AidCharges = maximumAidCharges;
        exhausted = deathPending = false; recoveryWait = 0; damageFlash = 0; ResetTraversal();
        if(GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>()?.DeathAnimationFinished??false)GetComponent<KingOfGraphic.Hero.HeroWorldAdapter>().ResetForRespawn();
    }
    // Relocation resets fall measurement, never health, charges or stamina.
    public void ResetTraversal()
    {
        fallPeak = transform.position.y;
    }
    void Notice(string message) { if (flow) flow.GetComponent<BaselineWorld>().ShowNotice(message); }
    void OnGUI()
    {
        StaminaHudRect=default;
        WayfarerUITheme.Apply();
        if (!flow || !flow.Started || flow.WorldHudHidden ||
            (SideWorldTravel.Instance && SideWorldTravel.Instance.IsAway)) return;
        var camera = Camera.main;
        Vector3 torso = camera ? camera.WorldToScreenPoint(transform.position + Vector3.up * 1.1f) : Vector3.zero;
        var matrix = GUI.matrix; var color = GUI.color;
        float scale = Mathf.Clamp(Screen.height / 800f, .65f, 1.5f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float width = Screen.width / scale, height = Screen.height / scale;
        if (label == null) label = new GUIStyle(GUI.skin.label) {
            fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        // Health is anchored to the screen, independent of character/camera movement.
        float hpX = width * .5f - 100;
        float hpY = height - 55;
        Color hpColor = health.NormalizedHealth <= .25f ? new Color(.95f,.25f,.22f) : new Color(.48f,.83f,.29f);
        if (damageFlash > 0) hpColor = new Color(.9f,.43f,.35f);
        if (!healthHud || !healthHud.Ready)
        {
            Bar(new Rect(hpX, hpY, 200, 9), health.NormalizedHealth, hpColor, false);
            Text(new Rect(hpX, hpY + 10, 200, 20), health.CurrentHealth + " / " + health.MaxHealth);
            if (health.NormalizedHealth <= .25f) Text(new Rect(hpX, hpY + 29, 200, 20), "LOW HEALTH");
        }

        // Presentation follows the existing resource simulation; no balance changes.
        StaminaHudRect=default;
        if(camera && torso.z>0 && staminaHudAlpha>.001f)
        {
            float x=Mathf.Clamp(torso.x/scale+50,48,width-72);
            float y=Mathf.Clamp((Screen.height-torso.y)/scale-35,18,height-120);
            StaminaHudRect=new Rect((x-22)*scale,(y-4)*scale,94*scale,105*scale);
            Color tint=StaminaLow?new Color(1,.38f,.15f):new Color(1,.81f,.2f);
            float fraction=Mathf.Clamp01(Stamina/maximumStamina);
            var baseMatrix=GUI.matrix;
            for(int i=0;i<28;i++)
            {
                float angle=Mathf.Lerp(-58,58,i/27f)*Mathf.Deg2Rad;
                Vector2 point=new Vector2(x+Mathf.Cos(angle)*34,y+38+Mathf.Sin(angle)*40);
                // Positions already describe the arc in scaled GUI coordinates. Rotating
                // GUI.matrix around each point applies the HUD scale twice in IMGUI.
                GUI.matrix=baseMatrix;
                GUI.color=new Color(.04f,.08f,.06f,staminaHudAlpha*.75f);GUI.DrawTexture(new Rect(point.x-2,point.y-2.1f,5,4.2f),Texture2D.whiteTexture);
                if((27-i)/28f<fraction){GUI.color=new Color(tint.r,tint.g,tint.b,staminaHudAlpha);GUI.DrawTexture(new Rect(point.x-1,point.y-1.6f,3,3.2f),Texture2D.whiteTexture);}
            }
            GUI.matrix=baseMatrix;
            if(StaminaLow)Text(new Rect(x-22,y+78,94,22),exhausted?"EXHAUSTED":"LOW",staminaHudAlpha);
        }
        if(!movement.ClimbingActive && !movement.SwimmingActive && !movement.MantlingActive)
            {
            Text(new Rect(width-300,height-75,280,24),"Space Jump · Shift / RMB Hold run");
            var equipment=GetComponent<PlayerEquipment>();
            if(equipment && equipment.IsSwordEquipped)
                Text(new Rect(width-360,height-102,340,24),WorldInputBindings.Label("Attack")+"  Combo   ·   "+WorldInputBindings.Label("Guard")+"  Guard");
            Text(new Rect(width-300,height-129,280,24),WorldInputBindings.Label("Dodge")+" + direction  Dodge");
        }
        Text(new Rect(width-300,height-48,280,24),IsAtCamp?"H  Rest & refill at camp":IsAtSupplyStation?"H  Rest & refill supplies":"H  Field aid  "+AidCharges+" / "+maximumAidCharges);
        GUI.color = color; GUI.matrix = matrix;
    }
    void Text(Rect rect, string text, float alpha=1)
    {
        GUI.color = new Color(0,0,0,.85f*alpha);
        GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),text,label);
        GUI.color = new Color(1,1,1,alpha); GUI.Label(rect,text,label);
    }
    static void Bar(Rect rect, float fraction, Color fill, bool vertical, float alpha=1)
    {
        GUI.color = new Color(.04f,.06f,.07f,.85f*alpha);
        GUI.DrawTexture(new Rect(rect.x-2,rect.y-2,rect.width+4,rect.height+4),Texture2D.whiteTexture);
        fraction = Mathf.Clamp01(fraction);
        fill.a*=alpha; GUI.color = fill;
        GUI.DrawTexture(vertical ? new Rect(rect.x,rect.y+rect.height*(1-fraction),rect.width,rect.height*fraction) :
            new Rect(rect.x,rect.y,rect.width*fraction,rect.height),Texture2D.whiteTexture);
    }
}
