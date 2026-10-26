using UnityEngine;

namespace SamProduction
{
    [CreateAssetMenu(menuName = "SAM/Skill Definition")]
    public sealed class SamSkillDefinition : ScriptableObject
    {
        public string skillId;
        public string displayName;
        public AnimationClip animationClip;
        public double duration = 3.01;
        public float recoverTime = 2.7f;
        public float damage = 100f;
        [Min(.01f)] public float hitRadius = .35f;
        [Min(0)] public float hitReach;
        public string hitDirectionBone;
        public bool startsOverheated;
        public string heatChestSocket = "socket_heat_chest";
        public string heatLeftShoulderSocket = "socket_heat_shoulder.L";
        public string heatRightShoulderSocket = "socket_heat_shoulder.R";
        public SamTimedEvent[] events = System.Array.Empty<SamTimedEvent>();
        public bool useAudioTimeline;
        public SamImpactAudioMode impactAudioMode = SamImpactAudioMode.Split;
        public SamAudioCue[] audioCues = System.Array.Empty<SamAudioCue>();
        public AnimationCurve forward = AnimationCurve.Linear(0, 0, 3.01f, 0);
        public AnimationCurve lateral = AnimationCurve.Linear(0, 0, 3.01f, 0);
        public AnimationCurve height = AnimationCurve.Linear(0, 0, 3.01f, 0);
        public AnimationCurve yaw = AnimationCurve.Linear(0, 0, 3.01f, 0);
        public AnimationCurve heat = AnimationCurve.Linear(0, 0, 3.01f, 0);
        public bool productionApproved;

        public Vector3 MotionAt(double time) => new Vector3(lateral.Evaluate((float)time), height.Evaluate((float)time), forward.Evaluate((float)time));
    }
}
