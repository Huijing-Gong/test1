using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SamProduction.BossArena
{
    [RequireComponent(typeof(Animator))]
    public sealed class ArenaAnimation : MonoBehaviour
    {
        public AnimationClip authoredRun;
        public bool authoredFootIK;
        public AnimationClip guard, walk, jab, stomp, startShort, stopShort, comboTwo, comboThree;
        public event System.Action<AnimationClip,float> Evaluated;
        [Min(.01f)] public float walkStride = 1.96086f;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationLayerMixerPlayable layers;
        private AnimationClipPlayable upperBody;
        private AnimationClip upperClip;
        private AvatarMask upperMask;
        private AnimationClipPlayable current, previous;
        private AnimationClip currentClip, previousClip;
        private double lastTime, previousTime;
        private float blendTime, blendLength;
        public string ClipName => currentClip != null ? currentClip.name : "none";

        private void Awake()
        {
            var animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("SAM Boss Arena animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, layers, 0); layers.SetInputWeight(0, 1);
            upperMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) upperMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }) upperMask.SetHumanoidBodyPartActive(part, true);
            layers.SetLayerMaskFromAvatarMask(1, upperMask);
            AnimationPlayableOutput.Create(graph, "Boss skeleton and corrective keys", animator).SetSourcePlayable(layers);
            graph.Play();
        }

        // The collision controller owns translation. Walk time comes from actual distance travelled.
        public void Pose(AnimationClip clip, double time, float delta, bool loop = false, float blend = .16f)
        {
            if (clip == null || !graph.IsValid()) return;
            if (currentClip != clip)
            {
                if (previous.IsValid()) { mixer.DisconnectInput(0); graph.DestroyPlayable(previous); }
                previous = current;
                previousClip = currentClip;
                previousTime = lastTime;
                if (previous.IsValid()) { mixer.DisconnectInput(1); graph.Connect(previous, 0, mixer, 0); }
                current = AnimationClipPlayable.Create(graph, clip);
                current.SetApplyFootIK(authoredFootIK&&(clip==guard||clip==walk||clip==authoredRun));
                current.SetApplyPlayableIK(false);
                graph.Connect(current, 0, mixer, 1);
                currentClip = clip;
                // Contact-led bridges have identical endpoint poses. A frozen old
                // walking pose mixed over that bridge would destroy its planted foot.
                blendLength = previous.IsValid() ? Mathf.Max(0, blend) : 0;
                blendTime = 0;
            }
            lastTime = loop && clip.length > 0 ? time % clip.length : Mathf.Clamp((float)time, 0, clip.length);
            current.SetTime(lastTime);
            if (previous.IsValid())
            {
                // Distance-driven locomotion cannot continue striding after the collision motor stops.
                if (previousClip != walk) previousTime += delta;
                previous.SetTime(previousClip == guard || previousClip == walk || previousClip == authoredRun
                    ? previousTime % previousClip.length : System.Math.Min(previousTime, previousClip.length));
            }
            blendTime += delta;
            float amount = blendLength <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(blendTime / blendLength));
            mixer.SetInputWeight(0, 1 - amount);
            mixer.SetInputWeight(1, amount);
            graph.Evaluate(0);
            Evaluated?.Invoke(clip,(float)lastTime);
        }

        public void PoseContactClip(AnimationClip clip, double time, float delta, bool loop = false)
            => Pose(clip, time, delta, loop, 0);

        public void ResetPose()
        {
            ClearUpperBody();
            if (previous.IsValid()) { mixer.DisconnectInput(0); graph.DestroyPlayable(previous); }
            if (current.IsValid()) { mixer.DisconnectInput(1); graph.DestroyPlayable(current); }
            previous = default;
            current = default;
            previousClip = null;
            currentClip = null;
            Pose(guard, 0, 0, true, 0);
        }

        public void PoseUpperBody(AnimationClip clip, float time, float weight)
        {
            if (!clip || !graph.IsValid()) return;
            if (upperClip != clip)
            {
                ClearUpperBody();upperClip=clip;upperBody=AnimationClipPlayable.Create(graph,clip);
                upperBody.SetApplyFootIK(false);upperBody.SetApplyPlayableIK(false);
                graph.Connect(upperBody,0,layers,1);
            }
            upperBody.SetTime(Mathf.Clamp(time,0,clip.length));layers.SetInputWeight(1,Mathf.Clamp01(weight));graph.Evaluate(0);
        }
        public void ClearUpperBody()
        {
            if (!graph.IsValid()) return;
            layers.SetInputWeight(1,0);
            if(upperBody.IsValid()){layers.DisconnectInput(1);graph.DestroyPlayable(upperBody);}
            upperBody=default;upperClip=null;
        }
        private void OnDestroy() { if (graph.IsValid()) graph.Destroy(); if(upperMask)Destroy(upperMask); }
    }
}
