using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction
{
    public sealed class SamPreviewControls : MonoBehaviour
    {
        public SamSkillRunner runner;
        public SamCharacterMotor motor;
        public Camera view;
        public bool allowTranslation;
        public bool showPanel = true;
        public bool showTechnicalTargets;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private void Start()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            SetTargetVisibility();
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) showPanel = !showPanel;
            if (keyboard.f2Key.wasPressedThisFrame) { showTechnicalTargets = !showTechnicalTargets; SetTargetVisibility(); }
            if (keyboard.digit1Key.wasPressedThisFrame) runner.TryStart(0);
            if (keyboard.escapeKey.wasPressedThisFrame) runner.Cancel();
            if (keyboard.pKey.wasPressedThisFrame) runner.Clock.Paused = !runner.Clock.Paused;
            if (keyboard.rKey.wasPressedThisFrame)
            {
                runner.Cancel(true);
                motor.Teleport(startPosition, startRotation);
                foreach (var target in FindObjectsByType<SamDamageable>(FindObjectsSortMode.None)) target.ResetTarget();
            }
            if (allowTranslation && !runner.IsCasting && !runner.Clock.Paused)
            {
                var input = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                motor.MoveInput(input, keyboard.leftShiftKey.isPressed, keyboard.spaceKey.wasPressedThisFrame, view.transform, Time.deltaTime);
            }
        }

        private void SetTargetVisibility()
        {
            foreach (var target in FindObjectsByType<SamDamageable>(FindObjectsSortMode.None))
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = showTechnicalTargets;
            }
        }

        private void OnGUI()
        {
            if (!showPanel) return;
            GUI.color = new Color(.06f, .07f, .1f, .92f);
            GUI.Box(new Rect(20, 20, 340, 195), GUIContent.none);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(36, 32, 306, 170));
            GUILayout.Label("SAM / Production preview");
            GUILayout.Label(runner.Current != null ? runner.Current.displayName : "Animation & contact review");
            GUILayout.Label("1: Dash punch    R: Reset    Esc: Cancel");
            GUILayout.Label("P: Pause    Right mouse: Orbit    F1: Hide");
            GUILayout.Label("F2: Show/hide technical collision target");
            GUILayout.Label($"Time {runner.Clock.Time:F3}s    Hits {runner.HitCount}");
            runner.playbackSpeed = GUILayout.HorizontalSlider(runner.playbackSpeed, .5f, 1.5f);
            GUILayout.EndArea();
        }
    }
}
