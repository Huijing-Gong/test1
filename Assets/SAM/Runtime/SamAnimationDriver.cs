using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SamProduction
{
    [RequireComponent(typeof(Animator))]
    public sealed class SamAnimationDriver : MonoBehaviour
    {
        public AnimationClip idle;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable actionPlayable;
        private float weight;
        private float targetWeight;
        private double idleTime;
        public Animator Animator { get; private set; }

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("SAM independent action graph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "SAM skeleton", Animator);
            output.SetSourcePlayable(mixer);
            if (idle != null)
            {
                idlePlayable = AnimationClipPlayable.Create(graph, idle);
                graph.Connect(idlePlayable, 0, mixer, 0);
                mixer.SetInputWeight(0, 1f);
            }
            graph.Play();
        }

        public void Play(AnimationClip clip)
        {
            if (clip == null) return;
            if (actionPlayable.IsValid())
            {
                mixer.DisconnectInput(1);
                graph.DestroyPlayable(actionPlayable);
            }
            actionPlayable = AnimationClipPlayable.Create(graph, clip);
            actionPlayable.SetApplyFootIK(false);
            graph.Connect(actionPlayable, 0, mixer, 1);
            targetWeight = 1;
            actionPlayable.SetTime(0);
        }

        public void Sample(double actionTime, float delta, bool exact = false)
        {
            idleTime += delta;
            if (idlePlayable.IsValid() && idle != null) idlePlayable.SetTime(idleTime % idle.length);
            if (actionPlayable.IsValid()) actionPlayable.SetTime(actionTime);
            weight = exact ? targetWeight : Mathf.MoveTowards(weight, targetWeight, delta / .08f);
            mixer.SetInputWeight(0, 1 - weight);
            mixer.SetInputWeight(1, weight);
            graph.Evaluate(0);
        }

        public void Recover() { targetWeight = 0; }
        private void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
