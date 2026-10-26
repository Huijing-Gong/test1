using UnityEngine;
using UnityEngine.InputSystem;

// Shared input arbiter; challenge prefabs contain no serialized player/scene references.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class MinigamePlayer : MonoBehaviour
{
    public static MinigamePlayer Existing { get; private set; }
    public System.Func<bool> HostGate;
    PlayerMovement movement;
    PlayerInteraction interaction;
    DialogueUI dialogue;
    BaselineFlow flow;
    Health health;
    ConstellationGame carriedGame;
    int carriedLamp = -1;
    Vector3 carriedOriginalScale, carriedVisualCenter;
    Transform carryHead;
    string notice;
    float noticeUntil;
    readonly RaycastHit[] hits = new RaycastHit[32];
    public bool Carrying => carriedGame != null;
    public bool CanAct => isActiveAndEnabled && gameObject.activeInHierarchy && (HostGate == null || HostGate()) && movement && movement.CanStartInteraction && Time.timeScale > 0 &&
        (!health || !health.IsDead) && (!flow || (flow.Started && !flow.BlockWorldInput && !flow.MenuOpen && !flow.MapOpen && (!flow.GetComponent<BaselineWorld>() || (!flow.GetComponent<BaselineWorld>().JournalOpen && flow.GetComponent<BaselineWorld>().LastJournalClosedFrame!=Time.frameCount)))) &&
        !(SideWorldTravel.Instance && (SideWorldTravel.Instance.IsAway || SideWorldTravel.Instance.Busy)) &&
        (!dialogue || (!dialogue.IsDialogueActive && !dialogue.IsRewardPresentationActive && dialogue.LastDialogueClosedFrame != Time.frameCount));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Clear() => Existing = null;
    static T SceneObject<T>(UnityEngine.SceneManagement.Scene scene) where T:Component
    {foreach(var root in scene.GetRootGameObjects()){var found=root.GetComponentInChildren<T>(true);if(found)return found;}return null;}
    public static MinigamePlayer Find()=>Find(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    public static MinigamePlayer Find(UnityEngine.SceneManagement.Scene scene)
    {
        if(!scene.IsValid()||!scene.isLoaded)return null;
        if(Existing&&Existing.gameObject.scene==scene&&Existing.isActiveAndEnabled&&Existing.gameObject.activeInHierarchy)return Existing;
        var movement=SceneObject<PlayerMovement>(scene);
        if(!movement||!movement.gameObject.activeInHierarchy)return null;
        var player=movement.GetComponent<MinigamePlayer>()??movement.gameObject.AddComponent<MinigamePlayer>();
        if(player.isActiveAndEnabled)Existing=player;return player;
    }
    void Awake(){movement=GetComponent<PlayerMovement>();interaction=GetComponent<PlayerInteraction>();health=GetComponent<Health>();BindScene();}
    void BindScene(){Existing=this;dialogue=SceneObject<DialogueUI>(gameObject.scene);flow=SceneObject<BaselineFlow>(gameObject.scene);}
    void OnEnable(){BindScene();}
    public bool CanSee(Transform target)
    {
        Vector3 origin = transform.position + Vector3.up * .9f;
        Vector3 offset = target.position + Vector3.up * .9f - origin;
        int count = Physics.RaycastNonAlloc(origin, offset.normalized, hits, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++)
            if (!hits[i].transform.IsChildOf(transform) && !hits[i].transform.IsChildOf(target)) return false;
        return true;
    }
    void Update()
    {
        // Carrying stays inside its puzzle area and cannot strand a lamp after travel/death.
        if (Carrying && (!carriedGame.isActiveAndEnabled || Vector3.Distance(transform.position, carriedGame.transform.position) > 24 || (health && health.IsDead))) Release(carriedGame);
        if (!CanAct || (interaction && interaction.SelectedNPC)) return;
        var keyboard = Keyboard.current;
        if (Carrying)
        {
            Offer("[E] Place star orb");
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Drop();
            return;
        }
        MinigameTarget best = null;
        float distance = float.PositiveInfinity;
        foreach (var target in MinigameTarget.Active)
        {
            if (!target || target.gameObject.scene!=gameObject.scene || !target.Available) continue;
            Vector3 delta = target.transform.position - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            float d = flat.sqrMagnitude;
            if (Mathf.Abs(delta.y) > 2 || d > target.distance * target.distance || d >= distance) continue;
            if (flat.sqrMagnitude > .001f && Vector3.Dot(transform.forward, flat.normalized) < .25f) continue;
            if (!CanSee(target.transform)) continue;
            best = target; distance = d;
        }
        if (!best) return;
        Offer(best.Prompt);
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame) best.Interact(this);
    }
    void LateUpdate()
    {
        if (!Carrying) return;
        var orb = carriedGame.movableLamps[carriedLamp];
        orb.rotation = transform.rotation;
        Vector3 head = carryHead ? carryHead.position : transform.position + Vector3.up * 1.57f;
        Vector3 center = head - transform.right * .32f + Vector3.up * .06f;
        orb.position = center - orb.TransformVector(carriedVisualCenter);
    }
    void Offer(string text) { if (dialogue) dialogue.OfferPrompt(text, 20); }
    public void PickUp(ConstellationGame game, int index)
    {
        if(!game||game.gameObject.scene!=gameObject.scene||index<0||index>=game.movableLamps.Length)return;
        if (Carrying || !CanAct || game.State.IsPlaced(index)) return;
        carriedGame = game; carriedLamp = index;
        var orb = game.movableLamps[index];
        carriedOriginalScale = orb.localScale;
        var renderers = orb.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            carriedVisualCenter = orb.InverseTransformPoint(bounds.center);
            float diameter = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            orb.localScale *= Mathf.Min(1, .16f / Mathf.Max(.001f, diameter));
        }
        var animator = GetComponentInChildren<Animator>();
        carryHead = animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        LateUpdate();
        Notify("Carry the orb to an empty marked position. Press E to place it.", 5);
    }
    public bool Drop()
    {
        if (!Carrying || !CanAct) return false;
        Vector3 desired = transform.position + transform.forward * 1.2f;
        int slot = carriedGame.FindSlot(desired);
        if (slot >= 0 && CanSee(carriedGame.targets[slot]) && carriedGame.Place(carriedLamp, slot))
        { RestoreCarriedSize(); carriedGame = null; carriedLamp = -1; return true; }
        // A safe ordinary placement is allowed, so observation remains the player's task.
        if (Physics.Raycast(desired + Vector3.up * 1.5f, Vector3.down, out var ground, 3, ~0, QueryTriggerInteraction.Ignore) &&
            ground.normal.y > .8f && !ground.transform.IsChildOf(transform) &&
            !Physics.CheckCapsule(ground.point + Vector3.up * .35f, ground.point + Vector3.up * 1.1f, .25f, ~0, QueryTriggerInteraction.Ignore) &&
            !Physics.Linecast(transform.position + Vector3.up, ground.point + Vector3.up, ~0, QueryTriggerInteraction.Ignore))
        {
            carriedGame.movableLamps[carriedLamp].position = ground.point;
            RestoreCarriedSize();
            carriedGame = null; carriedLamp = -1;
            Notify("The orb is not aligned with an empty marked position.");
            return true;
        }
        Notify("Find clear ground to place the orb.");
        return false;
    }
    public void Release(ConstellationGame game)
    {
        if (!Carrying || carriedGame != game) return;
        RestoreCarriedSize();
        carriedGame.ReturnLamp(carriedLamp);
        carriedGame = null; carriedLamp = -1;
    }
    void RestoreCarriedSize()
    {
        if (Carrying) carriedGame.movableLamps[carriedLamp].localScale = carriedOriginalScale;
        carryHead = null;
    }
    void OnDisable() { if (Carrying) Release(carriedGame); if (Existing == this) Existing = null; }
    public void Notify(string text, float duration = 4) { notice = text; noticeUntil = Time.unscaledTime + duration; }
    void OnGUI()
    {
        if (Time.unscaledTime > noticeUntil || string.IsNullOrEmpty(notice) || (flow && (flow.BlockWorldInput || flow.MapOpen || flow.GetComponent<BaselineWorld>().JournalOpen))) return;
        var old = GUI.skin;
        WayfarerUITheme.Apply();
        float scale = Mathf.Max(.6f, Screen.height / 720f);
        float width = Mathf.Min(660 * scale, Screen.width - 40 * scale);
        var textStyle = new GUIStyle(WayfarerUITheme.NoticeTextStyle)
        {
            fontSize = Mathf.RoundToInt(20 * scale),
            padding = new RectOffset(0, 0, 0, 0)
        };
        float height = Mathf.Max(54 * scale, textStyle.CalcHeight(new GUIContent(notice), width - 36 * scale) + 24 * scale);
        var panel = new Rect((Screen.width - width) * .5f, 24 * scale, width, height);
        GUI.Box(panel, GUIContent.none, WayfarerUITheme.NoticePanelStyle);
        GUI.Label(new Rect(panel.x + 18 * scale, panel.y + 12 * scale, width - 36 * scale, height - 24 * scale), notice, textStyle);
        GUI.skin = old;
    }
}
