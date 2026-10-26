using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    [Serializable]
    public sealed class ArenaMotionSample
    {
        public int frame;
        public float time, yaw;
        public float[] position;
        public Vector3 Position => new Vector3(position[0], position[1], position[2]);
    }

    [Serializable]
    public sealed class ArenaMotionClip
    {
        public string id, clip;
        public float fps, duration, stride, speed;
        public int frame_count;
        public bool loop;
        public ArenaMotionSample[] trajectory;

        public void Sample(float time, out Vector3 position, out float yaw)
        {
            float sample = Mathf.Clamp(time * fps, 0, trajectory.Length - 1);
            int a = Mathf.FloorToInt(sample), b = Mathf.Min(a + 1, trajectory.Length - 1);
            float t = sample - a;
            position = Vector3.LerpUnclamped(trajectory[a].Position, trajectory[b].Position, t);
            // Exported yaw is already unwrapped. Do not wrap a legitimate turn at 180 degrees.
            yaw = Mathf.LerpUnclamped(trajectory[a].yaw, trajectory[b].yaw, t);
        }

        public Vector3 EndPosition => trajectory[trajectory.Length - 1].Position;
    }

    [Serializable]
    public sealed class ArenaMotionData
    {
        public int schema;
        public string coordinates;
        public ArenaMotionClip[] clips;

        public static ArenaMotionData Read(TextAsset asset)
        {
            if (asset == null) throw new InvalidOperationException("Boss Arena motion trajectory asset is required.");
            var data = JsonUtility.FromJson<ArenaMotionData>(ArenaMotionEncoding.Text(asset));
            if (data == null || data.schema != 1 || data.clips == null)
                throw new InvalidOperationException("Invalid Boss Arena trajectory schema.");
            foreach (var clip in data.clips)
            {
                if (clip.fps <= 0 || clip.trajectory == null || clip.trajectory.Length != clip.frame_count || clip.frame_count < 2)
                    throw new InvalidOperationException("Missing complete trajectory for " + clip.id);
                if (Mathf.Abs((clip.frame_count - 1) / clip.fps - clip.duration) > .0001f)
                    throw new InvalidOperationException("Trajectory duration mismatch for " + clip.id);
                for (int i = 0; i < clip.trajectory.Length; i++)
                {
                    var sample = clip.trajectory[i];
                    if (sample.frame != i || Mathf.Abs(sample.time - i / clip.fps) > .0001f || sample.position == null || sample.position.Length != 3)
                        throw new InvalidOperationException("Trajectory samples are incomplete or unordered for " + clip.id);
                    foreach (float value in sample.position)
                        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Non-finite motion position.");
                    if (float.IsNaN(sample.yaw) || float.IsInfinity(sample.yaw)) throw new InvalidOperationException("Non-finite motion yaw.");
                }
                if (clip.trajectory[0].Position.sqrMagnitude > .000001f || Mathf.Abs(clip.trajectory[0].yaw) > .001f)
                    throw new InvalidOperationException("Trajectory must start relative to the current actor transform: " + clip.id);
            }
            return data;
        }

        public ArenaMotionClip Get(string id)
        {
            foreach (var clip in clips) if (clip.id == id) return clip;
            throw new InvalidOperationException("Missing Boss Arena motion clip: " + id);
        }
    }
}
