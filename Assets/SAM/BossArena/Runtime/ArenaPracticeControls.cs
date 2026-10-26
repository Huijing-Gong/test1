using UnityEngine;
using UnityEngine.InputSystem;

namespace SamProduction.BossArena
{
    // Optional practice tools. Normal encounters never enable protection or
    // choose a skill on the player's behalf; the HUD labels every practice aid.
    public sealed class ArenaPracticeControls : MonoBehaviour
    {
        public ArenaSession arena;
        private bool practice, protection, slow;
        public bool Active=>practice;
        private string feedback = "Approach the Boss, then choose an attack.";
        private GUIStyle heading, body;

        private void Update()
        {
            if(arena == null || !arena.automaticTick || !Application.isFocused) return;
            var keys = Keyboard.current;
            if(keys == null) return;
            if(keys.f1Key.wasPressedThisFrame)
            {
                practice = !practice;
                arena.boss.decisionEnabled = !practice;
                if(!practice) { protection = slow = false; Time.timeScale = 1; }
            }
            if(practice)
            {
                if(keys.gKey.wasPressedThisFrame) protection = !protection;
                if(keys.f2Key.wasPressedThisFrame) { slow = !slow; Time.timeScale = slow ? .5f : 1; }
                ArenaAttack? chosen = keys.digit1Key.wasPressedThisFrame ? ArenaAttack.BoxJab
                    : keys.digit2Key.wasPressedThisFrame ? ArenaAttack.DiveKick
                    : keys.digit3Key.wasPressedThisFrame ? ArenaAttack.Stomp
                    : keys.digit4Key.wasPressedThisFrame ? ArenaAttack.BoxHeavy
                    : keys.digit5Key.wasPressedThisFrame ? ArenaAttack.Meteor : (ArenaAttack?)null;
                if(keys.f3Key.wasPressedThisFrame)chosen=ArenaAttack.FireWave;
                if(keys.f4Key.wasPressedThisFrame)chosen=ArenaAttack.FireBird;
                if(keys.f5Key.wasPressedThisFrame)chosen=ArenaAttack.LightningCall;
                if(keys.f6Key.wasPressedThisFrame)chosen=ArenaAttack.GroundRift;
                if(keys.f7Key.wasPressedThisFrame)chosen=ArenaAttack.SwordFall;
                if(keys.f8Key.wasPressedThisFrame)chosen=ArenaAttack.ThunderOrb;
                if(keys.f9Key.wasPressedThisFrame)chosen=ArenaAttack.DelayedBlast;
                if(keys.f10Key.wasPressedThisFrame)
                    feedback=arena.boss.v13Motor&&arena.boss.v13Motor.defense&&arena.boss.v13Motor.defense.BeginGuard(arena)
                        ?"Boss guard raised. Attack from the front or rear.":"Wait for recovery and the guard cooldown.";
                if(chosen.HasValue)
                    feedback = arena.boss.TrySkill(chosen.Value) ? "Attack started. Move or dodge freely."
                        : "Start the encounter and wait for the current action to finish.";
            }
            arena.player.fighter.TrainingInvincible = practice && protection;
        }

        private void OnGUI()
        {
            if(!practice || arena == null || !arena.automaticTick) return;
            if(heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            }
            GUI.Box(new Rect(20, 150, 650, 270), GUIContent.none);
            GUI.Label(new Rect(34, 160, 440, 26), "PRACTICE  |  F1 TO RETURN TO BATTLE", heading);
            GUI.Label(new Rect(34, 190, 505, 24), "1  JAB    2  DIVE KICK    3  GROUND FIRE RING", body);
            GUI.Label(new Rect(34, 216, 505, 24), "4  HEAVY PUNCH    5  METEOR", body);
            GUI.Label(new Rect(34, 243, 505, 24), "G  DAMAGE PROTECTION: " + (protection ? "ON" : "OFF"), body);
            GUI.Label(new Rect(34, 270, 505, 24), "F2  SPEED: " + (slow ? "0.5x" : "1x") + "    R RECENTER / ENTER RETRY", body);
            GUI.Label(new Rect(34, 301, 505, 38), feedback, body);
            GUI.Label(new Rect(34, 345, 625, 24), "F3 FIRE WAVE   F4 FIRE BIRD   F5 LIGHTNING   F6 MOBILE RIFT", body);
            GUI.Label(new Rect(34, 373, 625, 24), "F7 SWORD   F8 ORB   F9 BLAST   F10 BOSS GUARD", body);
        }

        private void OnDisable()
        {
            if(slow) Time.timeScale = 1;
            if(arena != null && arena.player != null) arena.player.fighter.TrainingInvincible = false;
        }
    }
}
