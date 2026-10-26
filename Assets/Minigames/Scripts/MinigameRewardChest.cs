using UnityEngine;
using UnityEngine.Events;

public sealed class MinigameRewardChest : MonoBehaviour
{
    [Tooltip("Stable ID for the host's future reward/save adapter. Use a unique ID per instance.")]
    public string gameId;
    public Transform lid;
    public UnityEvent onPuzzleCompleted = new UnityEvent();
    [Tooltip("Notification only. No item or main-world save is written by this package.")]
    public UnityEvent onRewardClaimed = new UnityEvent();
    // Host must persist chapter receipt first. Missing/failed adapter keeps chest retryable.
    public System.Func<MinigameRewardChest,bool> TryCommitClaim;
    public string revealMessage="Challenge complete. A record chest has appeared.";
    public string completionMessage="Investigation recorded. No additional item granted.";
    bool opening;
    MinigameReceipt receipt;
    Quaternion closedRotation;
    bool captured;
    public bool Available => receipt != null && receipt.Solved && !receipt.Claimed;
    public void Prepare(MinigameReceipt state)
    {
        receipt = state;
        opening=false;
        StopAllCoroutines();
        if (!captured) { closedRotation = lid ? lid.localRotation : Quaternion.identity; captured = true; }
        if (lid) lid.localRotation = closedRotation;
        GetComponent<BaselineChestVisual>()?.Restore(false);
        gameObject.SetActive(false);
    }
    public void Reveal(Transform anchor)
    {
        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        gameObject.SetActive(true);
        onPuzzleCompleted.Invoke();
        MinigamePlayer.Find(gameObject.scene)?.Notify(revealMessage);
    }
    public bool Open()
    {
        if(opening||receipt==null||!receipt.Solved||receipt.Claimed||TryCommitClaim==null)return false;
        opening=true;
        try{
            if(!TryCommitClaim(this))return false;
            if(!receipt.TryClaim())return false;
            // Save succeeded before either visual or notification leaves this method.
            var visual=GetComponent<BaselineChestVisual>();
            if(visual)visual.PlayAfterReward();else StartCoroutine(OpenLid());
            MinigamePlayer.Find(gameObject.scene)?.Notify(completionMessage,8);
            onRewardClaimed.Invoke();return true;
        }catch(System.Exception error){Debug.LogException(error,this);return false;}
        finally{opening=false;}
    }
    System.Collections.IEnumerator OpenLid()
    {
        var open = closedRotation * Quaternion.Euler(-65, 0, 0);
        float elapsed = 0;
        while (elapsed < .65f)
        {
            elapsed += Time.deltaTime;
            if (lid) lid.localRotation = Quaternion.Slerp(closedRotation, open, Mathf.SmoothStep(0, 1, elapsed / .65f));
            yield return null;
        }
    }
}
