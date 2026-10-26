using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NPCDialogue))]
public sealed class SideWorldEntrance : MonoBehaviour
{
    public bool isSideWorldB;
    public const string GuideAName="CombatArchive", GuideBName="TraversalArchive";
    static SideWorldEntrance owner;
    public static bool ConfirmationOpen=>owner!=null;
    public static int InputConsumedFrame{get;private set;}=-1;
    public enum ReturnOutcome{Unknown,Completed,PracticeCompleted,Failed,LeftEarly}
    static readonly ReturnOutcome[] lastReturn=new ReturnOutcome[2];
    NPCDialogue dialogue; BaselineFlow flow; PlayerMovement movement; ThirdPersonCamera look;
    bool captured,oldMovementLock,oldLookLock,oldCursorVisible,selectedEnter=true,closing;
    CursorLockMode oldCursor; int openedFrame;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics(){owner=null;InputConsumedFrame=-1;lastReturn[0]=lastReturn[1]=ReturnOutcome.Unknown;}
    void Awake()
    {
        dialogue=GetComponent<NPCDialogue>();dialogue.SetNPCName(isSideWorldB?GuideBName:GuideAName);
        dialogue.SetRepeatable(true);dialogue.SetInteractionPrompt("[E] "+dialogue.NPCName);
        dialogue.ConfigureDialogue("[E] "+dialogue.NPCName,isSideWorldB?new[]{
            "TraversalArchive holds the team's platforming trial. Reach the final portal to complete it.",
            "After this conversation, choose Enter Trial or Not Now. You can return to this archive and replay."}:new[]{
            "CombatArchive holds the team's combat trials. Choose a trial inside and outlast your rival.",
            "After this conversation, choose Enter Trial or Not Now. The first combat trial grants the saved Trial A reward."});
        string label=isSideWorldB?"Label_SIDE WORLD B":"Label_SIDE WORLD A";
        foreach(var root in gameObject.scene.GetRootGameObjects())foreach(var sign in root.GetComponentsInChildren<TextMesh>(true))
            if(sign.gameObject.name==label||(sign.gameObject.name=="Entrance label"&&sign.transform.IsChildOf(transform)))sign.text=dialogue.NPCName;
    }
    void OnEnable(){if(!dialogue)dialogue=GetComponent<NPCDialogue>();dialogue.DialogueCompleted+=OpenConfirmation;}
    void OnDisable(){if(dialogue)dialogue.DialogueCompleted-=OpenConfirmation;StopAllCoroutines();CloseImmediately();}
    void OnDestroy()=>CloseImmediately();
    public void OpenConfirmation(DialogueUI ui)
    {
        if(owner||closing||!isActiveAndEnabled)return;
        var travel=SideWorldTravel.Instance;
        if(!travel||travel.Busy||travel.IsAway)return;
        flow=FindFirstObjectByType<BaselineFlow>();var world=flow?flow.GetComponent<BaselineWorld>():null;
        if(!flow||!flow.Started||flow.BlockWorldInput||!world||!world.player||world.JournalOpen)return;
        movement=world.player.GetComponent<PlayerMovement>();look=FindFirstObjectByType<ThirdPersonCamera>();
        if(!movement||(ui&&(ui.IsDialogueActive||ui.IsRewardPresentationActive)))return;
        // DialogueUI invokes completion AFTER CloseDialogue unlocks its movement/camera.
        // Capture those resulting host states, not the temporary dialogue locks.
        oldMovementLock=movement.IsGameplayInputLocked;oldLookLock=look&&look.IsLookInputLocked;
        oldCursor=Cursor.lockState;oldCursorVisible=Cursor.visible;captured=true;
        owner=this;selectedEnter=true;openedFrame=Time.frameCount;InputConsumedFrame=Time.frameCount;
        movement.SetGameplayInputLocked(true);if(look)look.SetLookInputLocked(true);
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    void Update()
    {
        // No P shortcut: all entry comes from the dialogue completion confirmation.
        if(owner!=this||closing||Time.frameCount<=openedFrame)return;
        var travel=SideWorldTravel.Instance;
        if(!travel||travel.Busy||travel.IsAway){CloseImmediately();return;}
        var k=Keyboard.current;if(k==null)return;
        if(k.escapeKey.wasPressedThisFrame){Choose(false);return;}
        if(k.tabKey.wasPressedThisFrame||k.leftArrowKey.wasPressedThisFrame||k.rightArrowKey.wasPressedThisFrame){selectedEnter=!selectedEnter;InputConsumedFrame=Time.frameCount;}
        if(k.enterKey.wasPressedThisFrame||k.numpadEnterKey.wasPressedThisFrame||WorldInputBindings.Pressed("Interact"))Choose(selectedEnter);
    }
    public void ConfirmEntry()=>Choose(true);
    public void CancelConfirmation()=>Choose(false);
    void Choose(bool enter)
    {
        if(owner!=this||closing||Time.frameCount<=openedFrame)return;
        InputConsumedFrame=Time.frameCount;owner=null;closing=true;
        StartCoroutine(FinishChoice(enter));
    }
    IEnumerator FinishChoice(bool enter)
    {
        // Keep physical input locked for all Update/OnGUI passes of the closing frame.
        yield return new WaitForEndOfFrame();
        if(!enter){Restore();closing=false;yield break;}
        // Enter must observe ConfirmationOpen=false. Defer one frame so consumed-frame guards
        // cannot reject travel and the confirmation click cannot become a main-world action.
        yield return null;
        Restore();closing=false;
        var travel=SideWorldTravel.Instance;if(travel&&!travel.Busy&&!travel.IsAway)travel.Enter(isSideWorldB);
    }
    void Restore()
    {
        if(!captured)return;captured=false;
        if(movement)movement.SetGameplayInputLocked(oldMovementLock);
        if(look)look.SetLookInputLocked(oldLookLock);
        Cursor.lockState=oldCursor;Cursor.visible=oldCursorVisible;
    }
    void CloseImmediately()
    {
        if(owner==this){owner=null;InputConsumedFrame=Time.frameCount;}
        Restore();closing=false;
    }
    string StatusText()
    {
        int i=isSideWorldB?1:0;bool completed=flow&&(i==0?flow.Progress.trialACompleted:flow.Progress.trialBCompleted);
        bool claimed=flow&&flow.Progress.trialRewardClaimed!=null&&flow.Progress.trialRewardClaimed.Length>i&&flow.Progress.trialRewardClaimed[i];
        string saved=claimed?"Saved status: reward received. Replays do not grant it again.":completed?"Saved status: completed, reward unclaimed. Claim it in the journey menu.":"Saved status: not completed. Returning early does not count as a clear.";
        string last=lastReturn[i]==ReturnOutcome.Failed?"Last visit: defeat. Your existing saved rewards remain unchanged.":lastReturn[i]==ReturnOutcome.LeftEarly?"Last visit: returned before completion.":lastReturn[i]==ReturnOutcome.Completed?"Last visit: completed.":lastReturn[i]==ReturnOutcome.PracticeCompleted?"Last visit: practice stage completed; no saved reward assigned to that stage.":"";
        return saved+(last.Length>0?"\n"+last:"");
    }
    void OnGUI()
    {
        if(owner!=this||closing)return;
        var oldMatrix=GUI.matrix;int depth=GUI.depth;var oldSkin=GUI.skin;
        GUI.matrix=Matrix4x4.identity;GUI.depth=-500;WayfarerUITheme.Apply();
        float scale=Mathf.Min(Screen.width/960f,Screen.height/540f);
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        var shade=GUI.color;GUI.color=new Color(0,0,0,.6f);GUI.DrawTexture(new Rect(0,0,960,540),Texture2D.whiteTexture);GUI.color=shade;
        GUI.Box(new Rect(175,130,610,285),GUIContent.none);
        var title=new GUIStyle(GUI.skin.label){fontSize=26,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        var text=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true,alignment=TextAnchor.UpperLeft};
        GUI.Label(new Rect(195,146,570,40),isSideWorldB?GuideBName:GuideAName,title);
        GUI.Label(new Rect(200,195,560,100),StatusText(),text);
        GUI.Label(new Rect(200,299,560,28),"Enter / E confirms selection · Tab switches · RMB / Esc cancels",new GUIStyle(text){fontSize=14});
        bool enabled=GUI.enabled;GUI.enabled=Time.frameCount>openedFrame;
        if(GUI.Button(new Rect(200,345,267,48),(selectedEnter?"▶ ":"")+"ENTER TRIAL"))Choose(true);
        if(GUI.Button(new Rect(493,345,267,48),(!selectedEnter?"▶ ":"")+"NOT NOW"))Choose(false);
        GUI.enabled=enabled;GUI.matrix=oldMatrix;GUI.depth=depth;GUI.skin=oldSkin;
    }
    // Root calls this BEFORE unloading the actual selected trial scene on ReturnToMain.
    // Persistent completion is deliberately NOT used to infer this visit's outcome.
    public static void CaptureReturnOutcome(bool sideB,Scene scene)
    {
        ReturnOutcome result=ReturnOutcome.LeftEarly;
        if(!scene.IsValid()||!scene.isLoaded){lastReturn[sideB?1:0]=ReturnOutcome.Unknown;return;}
        foreach(var root in scene.GetRootGameObjects()){
            if(!sideB){var battle=root.GetComponentInChildren<BattleManager>(true);if(battle&&battle.IsGameOver)result=battle.PlayerHasWon?(battle.CurrentTrialIndex==0?ReturnOutcome.Completed:ReturnOutcome.PracticeCompleted):ReturnOutcome.Failed;}
            else{
                foreach(var portal in root.GetComponentsInChildren<VictoryPortalTrigger2D>(true))if(portal.HasWon)result=ReturnOutcome.Completed;
                foreach(var health in root.GetComponentsInChildren<PlayerHealth>(true))if(health.IsDead&&result!=ReturnOutcome.Completed)result=ReturnOutcome.Failed;
            }
        }
        lastReturn[sideB?1:0]=result;
    }
}
