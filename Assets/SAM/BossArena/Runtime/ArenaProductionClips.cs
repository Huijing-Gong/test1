using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // The export JSON is the single source for metre trajectories and durations.
    public sealed class ArenaProductionClips : MonoBehaviour
    {
        public string[] ids;
        public AnimationClip[] clips;
        public TextAsset motionData;
        private ArenaMotionData data;
        public ArenaMotionClip Motion(string id)
        {
            if (data == null) data = ArenaMotionData.Read(motionData);
            return data.Get(id);
        }
        public AnimationClip Clip(string id)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return clips[i];
            throw new InvalidOperationException("Missing Production18 clip: " + id);
        }
    }
}
