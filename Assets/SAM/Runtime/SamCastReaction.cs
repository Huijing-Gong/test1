using System;
using UnityEngine;

namespace SamProduction
{
    /// <summary>Visual response only. Samples body and ornament without moving the gameplay owner.</summary>
    public sealed class SamCastReaction : MonoBehaviour
    {
        public SamSkillRunner runner;
        public AnimationClip response;
        public string skillId = "DashPunch";
        public double SampledTime { get; private set; }
        public bool HasResponse { get; private set; }
        Transform[] nodes;
        Vector3[] positions, scales;
        Quaternion[] rotations;
        bool bound;

        void Awake() { RememberRest(); }
        void OnEnable()
        {
            RememberRest();
            if (runner == null || bound) return;
            runner.PoseSampled += Sample;
            runner.PresentationReset += ResetPose;
            bound = true;
            if (runner.IsCasting) Sample(runner.Current, runner.Clock.Time);
            else ResetPose();
        }
        void RememberRest()
        {
            if (nodes != null) return;
            nodes = GetComponentsInChildren<Transform>(true);
            positions = new Vector3[nodes.Length];
            rotations = new Quaternion[nodes.Length];
            scales = new Vector3[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            { positions[i] = nodes[i].localPosition; rotations[i] = nodes[i].localRotation; scales[i] = nodes[i].localScale; }
        }
        void Sample(SamSkillDefinition skill, double time)
        {
            if (skill == null || skill.skillId != skillId) { ResetPose(); return; }
            SampleAt(time);
        }
        public void SampleAt(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
            if (response == null || !response.legacy) throw new InvalidOperationException("Cast response requires a bound legacy transform clip.");
            response.SampleAnimation(gameObject, (float)Math.Min(time, response.length));
            SampledTime = Math.Min(time, response.length);
            HasResponse = true;
        }
        public void ResetPose()
        {
            RememberRest();
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] != null)
                { nodes[i].localPosition = positions[i]; nodes[i].localRotation = rotations[i]; nodes[i].localScale = scales[i]; }
            SampledTime = 0;
            HasResponse = false;
        }
        void OnDisable()
        {
            if (bound && runner != null)
            { runner.PoseSampled -= Sample; runner.PresentationReset -= ResetPose; }
            bound = false;
            ResetPose();
        }
    }
}
