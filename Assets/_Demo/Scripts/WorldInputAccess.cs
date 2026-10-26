using UnityEngine;

// Owns only the temporary Ctrl cursor release. Modal pages retain their own locks.
[DefaultExecutionOrder(-10000)]
public sealed class WorldInputAccess:MonoBehaviour
{
    static WorldInputAccess instance;
    BaselineFlow flow;ThirdPersonCamera look;bool pointer,oldLookLock,oldVisible;CursorLockMode oldLock;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install(){if(instance)return;var go=new GameObject("World input and HUD access");instance=go.AddComponent<WorldInputAccess>();DontDestroyOnLoad(go);}
    void Update()
    {
        WorldInputBindings.TickCapture();
        if(!flow)flow=FindFirstObjectByType<BaselineFlow>();
        if(!look&&Camera.main)look=Camera.main.GetComponent<ThirdPersonCamera>();
        var mouse=UnityEngine.InputSystem.Mouse.current;
        if(!WorldInputBindings.ConsumedInterfaceInput&&mouse!=null&&mouse.rightButton.wasPressedThisFrame){
            TryNavigateBack();
        }
        bool desired=flow&&flow.Started&&!flow.BlockWorldInput&&!flow.WorldHudHidden&&WorldInputBindings.PointerModifierHeld;
        if(desired){
            WorldInputBindings.MarkPointerFrame();
            if(!pointer){oldLock=Cursor.lockState;oldVisible=Cursor.visible;oldLookLock=look&&look.IsLookInputLocked;pointer=true;}
            if(look)look.SetLookInputLocked(true);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }else if(pointer){
            pointer=false;
            if(!flow||!flow.BlockWorldInput){if(look)look.SetLookInputLocked(oldLookLock);Cursor.lockState=oldLock;Cursor.visible=oldVisible;}
        }
    }
    public bool TryNavigateBack()
    {
        if(WorldInputBindings.ConsumedInterfaceInput)return false;
        if(!flow)flow=FindFirstObjectByType<BaselineFlow>();
        bool handled=false;
        var travel=SideWorldTravel.Instance;
        var sam=flow?flow.SamBridge:FindFirstObjectByType<BaselineSamBridge>();
        if(travel&&travel.IsAway)handled=travel.TryInterfaceBack();
        else if(sam&&sam.IsAway)handled=sam.TryInterfaceBack();
        else {
            var inventory=flow?flow.GetComponent<WayfarerInventory.InventoryLab>():null;
            handled=inventory&&inventory.isActiveAndEnabled&&inventory.NavigateBack();
            if(!handled)foreach(var intro in FindObjectsByType<GuardianIntroDialogue>(FindObjectsSortMode.None))if(intro.TryInterfaceBack()){handled=true;break;}
            if(!handled&&flow)handled=flow.TryInterfaceBack();
            if(!handled){var battle=FindFirstObjectByType<BattleManager>();if(battle)handled=battle.TryInterfaceBack();}
        }
        if(handled)WorldInputBindings.ConsumeInterfaceClick();
        return handled;
    }
    void OnApplicationFocus(bool focused){if(!focused&&pointer){pointer=false;if(look)look.SetLookInputLocked(oldLookLock);}}
    void OnDestroy(){if(instance==this)instance=null;}
}
