using UnityEngine;
using UnityEngine.UI;

// Display only. Combat, falling, healing and respawns remain owned by Health/PlayerVitals.
[DisallowMultipleComponent]
public sealed class OverworldHealthHud : MonoBehaviour
{
    Health health;
    BaselineFlow flow;
    GameObject canvasObject;
    SamProduction.BossArena.WayfarerHealthStrip strip;
    float trail, trailWait, flash;
    public bool Ready => canvasObject && strip != null;
    public bool Visible => Ready && canvasObject.activeSelf;

    void Awake()
    {
        health = GetComponent<Health>();
        foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        if(!health)return;
        canvasObject = new GameObject("Player health HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 6;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
        strip=new SamProduction.BossArena.WayfarerHealthStrip(canvasObject.transform);
        Synchronize(true); canvasObject.SetActive(false);
    }
    void OnEnable()
    {
        if (health) health.HealthChanged += Changed;
        if (Ready) Synchronize(true);
    }
    void OnDisable()
    {
        if (health) health.HealthChanged -= Changed;
        if (canvasObject) canvasObject.SetActive(false);
    }
    void Changed(Health target, int before, int after)
    {
        if (!Ready) return;
        if (after < before) { trailWait = .15f; flash = 1f; }
        else { trail = health.NormalizedHealth; trailWait = 0; }
        Synchronize(false);
    }
    void Synchronize(bool snap)
    {
        if (snap) { trail = health.NormalizedHealth; trailWait = 0; flash = 0; }
        strip.Update(health.NormalizedHealth,trail,flash,health.CurrentHealth,health.MaxHealth);
    }
    void LateUpdate()
    {
        if (!Ready) return;
        var travel = SideWorldTravel.Instance;
        bool visible = flow && flow.Started && !flow.WorldHudHidden &&
            (!travel || (!travel.IsAway && !travel.Busy));
        canvasObject.SetActive(visible);
        float dt = Time.unscaledDeltaTime;
        trailWait = Mathf.Max(0, trailWait - dt);
        if (trailWait <= 0) trail = Mathf.MoveTowards(trail, health.NormalizedHealth, dt * 1.8f);
        flash = Mathf.MoveTowards(flash, 0, dt * 4);
        Synchronize(false);
    }
    void OnDestroy()
    {
        if (canvasObject) Destroy(canvasObject);
    }
}
