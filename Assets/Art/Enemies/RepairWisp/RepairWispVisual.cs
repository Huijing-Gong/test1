using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class RepairWispVisual : MonoBehaviour
{
    [SerializeField] private Transform shell, leftWing, rightWing, core;
    [SerializeField] private Renderer coreRenderer;
    [SerializeField] private GameObject[] chargeNodes;
    private RepairWispAI ai;
    private Vector3 shellHome;
    private Quaternion leftHome, rightHome;
    private MaterialPropertyBlock block;
    private Renderer[] glowRenderers;
    private float hitTime, completeTime, deathTime;
    private bool dead;
    private Vector3 hitDirection;
    private float nextDistantIdle;
    public Vector3 CorePosition => core ? core.position : transform.position + Vector3.up;
    private void Awake()
    {
        block = new MaterialPropertyBlock();
        var glowing = new List<Renderer>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            if (coreRenderer && renderer.sharedMaterial == coreRenderer.sharedMaterial) glowing.Add(renderer);
        glowRenderers = glowing.ToArray();
        ai = GetComponent<RepairWispAI>(); shellHome = shell.localPosition;
        leftHome = leftWing.localRotation; rightHome = rightWing.localRotation;
    }
    private void Update()
    {
        float dt = Time.deltaTime;
        hitTime = Mathf.Max(0, hitTime - dt); completeTime = Mathf.Max(0, completeTime - dt);
        if (dead) deathTime += dt;
        bool details = EnemyPresentationBudget.Details(transform.position);
        bool essential = dead && deathTime < .8f || hitTime > 0f || completeTime > 0f ||
            ai.CurrentPhase == RepairWispAI.Phase.Prepare || ai.CurrentPhase == RepairWispAI.Phase.Repair ||
            ai.CurrentPhase == RepairWispAI.Phase.Stagger;
        if (!details && !essential && Time.time < nextDistantIdle) return;
        nextDistantIdle = Time.time + .12f;
        float opening = ai.CurrentPhase == RepairWispAI.Phase.Prepare ? Mathf.Clamp01(ai.PhaseProgress) :
            ai.CurrentPhase == RepairWispAI.Phase.Repair ? 1f : 0f;
        float death = dead ? Mathf.Clamp01(deathTime / .65f) : 0f;
        bool staggered = !dead && ai.CurrentPhase == RepairWispAI.Phase.Stagger;
        float bob = !details || dead || ai.CurrentPhase == RepairWispAI.Phase.Repair ? 0 : Mathf.Sin(Time.time * 3f) * .035f;
        shell.localPosition = shellHome + Vector3.up * (bob - death * .70f - (staggered ? .06f : 0f)) + hitDirection * (hitTime * .15f);
        shell.localRotation = Quaternion.Euler(hitTime * -24f + death * 15f + (staggered ? 10f : 0f), 0, dead ? death * 32f : details ? Mathf.Sin(Time.time * 2f) * 3f : 0f);
        leftWing.localRotation = leftHome * Quaternion.Euler(0, -opening * 18f, opening * -20f + death * 38f);
        rightWing.localRotation = rightHome * Quaternion.Euler(0, opening * 18f, opening * 20f - death * 38f);
        float intensity = dead ? 0f : staggered ? EnemyImpactVfx.StunPower(Time.time) :
            (ai.RemainingRepairs == 0 ? .18f : 1.2f) + opening * 1.1f + completeTime * 2f;
        foreach (Renderer renderer in glowRenderers)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", new Color(.55f, 1f, .17f) * intensity);
            if (dead) block.SetColor("_BaseColor", new Color(.1f, .15f, .12f));
            renderer.SetPropertyBlock(block);
        }
        for (int i = 0; i < chargeNodes.Length; i++)
            if (chargeNodes[i] && chargeNodes[i].activeSelf != (!dead && i < ai.RemainingRepairs))
                chargeNodes[i].SetActive(!dead && i < ai.RemainingRepairs);
    }
    public void Hit(Vector3 direction) { hitTime = .2f; hitDirection = transform.InverseTransformDirection(direction); hitDirection.y = 0; }
    public void CompletedRepair() { completeTime = .25f; }
    public void Die()
    {
        dead = true; deathTime = hitTime = completeTime = 0f;
        foreach (Renderer renderer in glowRenderers)
        {
            renderer.GetPropertyBlock(block); block.SetColor("_EmissionColor", Color.black);
            block.SetColor("_BaseColor", new Color(.1f, .15f, .12f)); renderer.SetPropertyBlock(block);
        }
    }
    public void ResetVisual()
    {
        dead = false; deathTime = hitTime = completeTime = 0;
        shell.localPosition = shellHome; shell.localRotation = Quaternion.identity;
        leftWing.localRotation = leftHome; rightWing.localRotation = rightHome;
        foreach (Renderer renderer in glowRenderers)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", renderer.sharedMaterial.GetColor("_BaseColor"));
            block.SetColor("_EmissionColor", new Color(.55f, 1f, .17f) * 1.2f);
            renderer.SetPropertyBlock(block);
        }
    }
}
