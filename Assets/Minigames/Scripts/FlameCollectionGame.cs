using UnityEngine;

public sealed class FlameCollectionGame : MonoBehaviour
{
    public BoxCollider activationArea;
    public GameObject[] flames;
    [Tooltip("One clear ground-level chest anchor beside EACH flame, in matching order.")]
    public Transform[] chestAnchors;
    public MinigameRewardChest chest;
    public float collectionRadius = 1.35f;
    FlameCollectionState state;
    public FlameCollectionState State => state ?? (state = new FlameCollectionState(flames.Length));
    void Start() { ResetRun(); MinigamePlayer.Find(gameObject.scene); }
    public void ResetRun()
    {
        enabled=true;State.Reset();
        foreach (var flame in flames) flame.SetActive(false);
        chest.Prepare(State);
    }
    public void RestoreRun(bool known)
    {
        ResetRun();
        if(!known)return;
        State.Activate();for(int i=0;i<flames.Length;i++)State.Collect(i);
        State.TryClaim(); // restore only, noevents or visual reward
        enabled=false;
    }
    public void Activate()
    {
        if (!State.Activate()) return;
        foreach (var flame in flames) flame.SetActive(true);
        MinigamePlayer.Find(gameObject.scene)?.Notify("Collect the blue survey traces to recover the boundary record.", 7);
    }
    public bool IsInActivationArea(Vector3 position)
    {
        if (!activationArea || !activationArea.enabled || !activationArea.gameObject.activeInHierarchy) return false;
        Vector3 point = activationArea.transform.InverseTransformPoint(position) - activationArea.center;
        Vector3 half = activationArea.size * .5f;
        return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
    }
    void Update()
    {
        if (State.Solved) return;
        var player = MinigamePlayer.Find(gameObject.scene);
        if (!player || !player.CanAct) return;
        if (!State.Active)
        {
            if (IsInActivationArea(player.transform.position)) Activate();
            return;
        }
        for (int i = 0; i < flames.Length; i++)
        {
            if (!flames[i].activeSelf) continue;
            var offset = player.transform.position - flames[i].transform.position;
            if (Mathf.Abs(offset.y) > 2 || Vector3.ProjectOnPlane(offset, Vector3.up).sqrMagnitude > collectionRadius * collectionRadius) continue;
            if (!player.CanSee(flames[i].transform)) continue;
            Collect(i);
        }
    }
    public bool Collect(int index)
    {
        if (!State.Collect(index)) return false;
        flames[index].SetActive(false);
        if (State.Solved) chest.Reveal(chestAnchors[State.LastCollected]);
        else MinigamePlayer.Find(gameObject.scene)?.Notify("Blue orbs collected: " + State.Count + " / " + flames.Length);
        return true;
    }
}
