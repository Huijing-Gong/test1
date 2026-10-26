using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(RepairWispAI)), DefaultExecutionOrder(100)]
public sealed class RepairWispSupportVfx : MonoBehaviour
{
    [SerializeField] Material supportMaterial;
    RepairWispAI ai;
    Health health;
    BaselineFlow flow;
    RepairSupportHighlight active;
    readonly List<RepairSupportHighlight> recipients = new List<RepairSupportHighlight>();
    int completed;
    public Material SupportMaterial => supportMaterial;
    public bool CombatAvailable => ai && ai.CombatAvailable && health && !health.IsDead;
    void Awake() { ai = GetComponent<RepairWispAI>(); health = GetComponent<Health>(); }
    void OnEnable()
    {
        if (!health) health = GetComponent<Health>();
        health.Died += OnDeath; flow = FindFirstObjectByType<BaselineFlow>();
        if (flow) flow.WorldEncountersReset += Clear;
    }
    void OnDisable()
    {
        if (health) health.Died -= OnDeath;
        if (flow) flow.WorldEncountersReset -= Clear;
        Clear();
    }
    void LateUpdate()
    {
        if (!ai || !ai.CombatAvailable || !health || health.IsDead || !supportMaterial) { Clear(); return; }
        if (ai.SuccessfulRepairs < completed) Clear();
        if (ai.SuccessfulRepairs > completed && active) active.Completed(this);
        completed = ai.SuccessfulRepairs;
        Health target = ai.BeamVisible ? ai.RepairTarget : null;
        if (active && (!target || active.gameObject != target.gameObject)) { active.Release(this, false); active = null; }
        if (!target || target.IsDead) return;
        if (!active)
        {
            active = target.GetComponent<RepairSupportHighlight>();
            if (!active) active = target.gameObject.AddComponent<RepairSupportHighlight>();
            active.Configure(supportMaterial);
            if (!recipients.Contains(active)) recipients.Add(active);
        }
        active.Channel(this, ai.PhaseProgress);
    }
    void OnDeath(Health actor, DamageInfo damage) => Clear();
    public void Clear()
    {
        foreach (RepairSupportHighlight recipient in recipients) if (recipient) recipient.Release(this, true);
        active = null; completed = ai ? ai.SuccessfulRepairs : 0;
    }
}
