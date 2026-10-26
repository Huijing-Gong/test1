using UnityEngine;

// Optional explicitly wired adapter. The existing B guide owns player input;
// importing this component must not auto-attach to unrelated world portals.
public sealed class SideGamePortalTransition : MonoBehaviour
{
    public void EnterSideGameB()
    {
        if (SideWorldTravel.Instance) SideWorldTravel.Instance.Enter(true);
    }
}
