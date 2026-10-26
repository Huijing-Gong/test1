using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Two authored map facilities. These never create progress, grant rewards or move the respawn camp.
[DefaultExecutionOrder(400)]
[DisallowMultipleComponent]
public sealed class BaselineWaystation : MonoBehaviour
{
    public enum Role { ReturnOnly, SupplyAndReturn }
    [SerializeField] Role role;
    [SerializeField, Min(.5f)] float interactionRadius = 3.5f;
    [Tooltip("Optional clear interaction anchor. Place at ground level; the visibility ray is 0.9m above it.")]
    [SerializeField] Transform interactionPoint;
    [SerializeField] BaselineFlow flow;
    static readonly List<BaselineWaystation> activeStations = new List<BaselineWaystation>();
    readonly RaycastHit[] visibilityHits = new RaycastHit[16];
    BaselineWorld world;
    DialogueUI dialogue;
    PlayerInteraction playerInteraction;

    public Role StationRole => role;
    public bool OffersSupplies => role == Role.SupplyAndReturn;
    public float InteractionRadius => Mathf.Max(.5f, interactionRadius);
    public Vector3 InteractionPosition => interactionPoint ? interactionPoint.position : transform.position;
    public BaselineFlow Host => flow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStations() { activeStations.Clear(); }
    void OnEnable()
    {
        if (!activeStations.Contains(this)) activeStations.Add(this);
        ResolveReferences();
    }
    void OnDisable() { activeStations.Remove(this); }
    void ResolveReferences()
    {
        if (!flow)
            foreach (var candidate in FindObjectsByType<BaselineFlow>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { flow = candidate; break; }
        world = flow ? flow.GetComponent<BaselineWorld>() : null;
        playerInteraction = world && world.player ? world.player.GetComponent<PlayerInteraction>() : null;
        foreach (var candidate in FindObjectsByType<DialogueUI>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { dialogue = candidate; break; }
    }
    void Update()
    {
        if (!flow || !world) ResolveReferences();
        if (!CanInteract(flow) || (playerInteraction && playerInteraction.SelectedNPC)) return;
        if (dialogue) dialogue.OfferPrompt(OffersSupplies ?
            "[E] Free return to camp   [H] Rest" :
            "[E] Return to central camp (free)", 15);
        var keys = Keyboard.current;
        if (keys != null && WorldInputBindings.Pressed("Interact")) TryReturn();
    }
    public bool CanInteract(BaselineFlow caller)
    {
        if (!isActiveAndEnabled || !caller || caller != flow || !world || !world.player ||
            caller.gameObject.scene != gameObject.scene || !caller.CanUseWaystations) return false;
        if (Vector3.Distance(world.player.position, InteractionPosition) > InteractionRadius) return false;
        var health = world.player.GetComponent<Health>();
        if (health && health.IsDead) return false;
        // Reject use through a nearby wall or from the opposite side of a tower floor.
        var origin = world.player.position + Vector3.up * .9f;
        var offset = InteractionPosition + Vector3.up * .9f - origin;
        if (offset.sqrMagnitude < .01f) return true;
        int count = Physics.RaycastNonAlloc(origin, offset.normalized, visibilityHits, offset.magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        if (count == visibilityHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = visibilityHits[i].transform;
            if (!hit || hit.IsChildOf(world.player)) continue;
            return false;
        }
        return true;
    }
    public bool TryReturn() => flow && flow.UseWaystationReturn(this);
    public static BaselineWaystation FindSupplyStation(BaselineFlow caller)
    {
        if (!caller || !caller.CanUseWaystations) return null;
        foreach (var station in activeStations)
            if (station && station.OffersSupplies && station.CanInteract(caller)) return station;
        return null;
    }
}
