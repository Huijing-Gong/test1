using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>One frame of Side A combat intent, shared by normal attacks and specials.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class BattleCombatInput : MonoBehaviour
{
    public bool NormalPressed { get; private set; }
    public bool SpecialPressed { get; private set; }
    public bool SpecialPressFromMouse { get; private set; }
    public bool CanStartActions { get; private set; }

    BattleManager manager;
    BattlePlayerController controller;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>(12);
    PointerEventData pointer;
    EventSystem pointerEventSystem;
    bool hasFocus = true;
    bool armed;

    void Awake()
    {
        controller = GetComponent<BattlePlayerController>();
        manager = FindFirstObjectByType<BattleManager>();
    }

    void Update()
    {
        NormalPressed = SpecialPressed = SpecialPressFromMouse = false;
        if (!manager) manager = FindFirstObjectByType<BattleManager>();
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        SideWorldTravel travel = SideWorldTravel.Instance;
        // Modal/travel hotkeys are intents on this frame, not only states set later
        // by Manager/Travel Update. They must win before specials run at -80.
        bool menuIntent = manager && keyboard != null &&
            (keyboard.hKey.wasPressedThisFrame || manager.trialMenu && keyboard.escapeKey.wasPressedThisFrame ||
             manager.HelpOpen && keyboard.enterKey.wasPressedThisFrame);
        bool leavingSideA = travel && travel.IsAway && !travel.IsSideWorldB &&
            (travel.Busy || WorldInputBindings.Pressed("ReturnWorld"));
        // Capture before BattleManager/EventSystem/IMGUI can close a menu this frame.
        bool playable = !WorldInputBindings.ConsumedInterfaceInput && hasFocus && !menuIntent && !leavingSideA && controller && controller.enabled && Time.timeScale > 0f &&
            (!manager || manager.IsBattleActive && !manager.IsGameOver && !manager.IsPaused &&
                !manager.HelpOpen && !manager.IsAwaitingIntroduction);
        CanStartActions = false;
        if (!playable)
        {
            armed = false;
            // Run before specials (-80): even H + release on the same frame
            // must not fire a charge behind a modal or just after resuming it.
            GetComponent<BattleSpecialMoves>()?.CancelUnreleasedCharge();
            return;
        }

        if (!armed)
        {
            // No buffered menu clicks, held keys, or focus-regain attacks.
            if (CombatButtonHeld(keyboard, mouse)) return;
            armed = true;
        }
        CanStartActions = true;

        bool left = mouse != null && mouse.leftButton.wasPressedThisFrame;
        bool right = mouse != null && mouse.rightButton.wasPressedThisFrame;
        if ((left || right) && PointerOverInteractable(mouse))
        {
            // A UI click consumes this frame, including simultaneous J/K backups.
            // Releasing menu-originated holds is required before another action.
            armed = false;
            CanStartActions = false;
            GetComponent<BattleSpecialMoves>()?.CancelUnreleasedCharge();
            return;
        }

        bool specialKey = keyboard != null && keyboard.kKey.wasPressedThisFrame;
        SpecialPressed = right || specialKey;
        SpecialPressFromMouse = right;
        // A simultaneous normal/special request creates only one special.
        NormalPressed = !SpecialPressed && (left || keyboard != null && keyboard.jKey.wasPressedThisFrame);
    }

    /// <summary>Ongoing charge ownership is independent of the new-action/menu gate.</summary>
    public bool ChargeHeld(bool fromMouse)
    {
        if (!hasFocus) return false;
        return fromMouse ? Mouse.current != null && Mouse.current.rightButton.isPressed :
            Keyboard.current != null && Keyboard.current.kKey.isPressed;
    }

    static bool CombatButtonHeld(Keyboard keyboard, Mouse mouse) =>
        keyboard != null && (keyboard.jKey.isPressed || keyboard.kKey.isPressed) ||
        mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);

    bool PointerOverInteractable(Mouse mouse)
    {
        EventSystem eventSystem = EventSystem.current;
        if (!eventSystem || mouse == null) return false;
        if (pointer == null || pointerEventSystem != eventSystem)
        {
            pointerEventSystem = eventSystem;
            pointer = new PointerEventData(eventSystem);
        }
        pointer.position = mouse.position.ReadValue();
        uiHits.Clear();
        eventSystem.RaycastAll(pointer, uiHits);
        for (int i = 0; i < uiHits.Count; i++)
        {
            Selectable control = uiHits[i].gameObject.GetComponentInParent<Selectable>();
            if (control && control.IsActive() && control.IsInteractable()) return true;
        }
        return false;
    }

    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        armed = false;
        NormalPressed = SpecialPressed = SpecialPressFromMouse = CanStartActions = false;
        if (focused) return;
        GetComponent<BattleAttack>()?.CancelForInputFocusLoss();
        GetComponent<BattleSpecialMoves>()?.Cancel();
    }

    void OnDisable()
    {
        armed = false;
        NormalPressed = SpecialPressed = SpecialPressFromMouse = CanStartActions = false;
    }
}
