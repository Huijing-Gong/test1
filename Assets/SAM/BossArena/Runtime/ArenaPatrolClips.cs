using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace SamProduction.BossArena
{
    [Serializable] public sealed class ArenaPatrolLegData
    {
        public string side = "", hip = "", knee = "", foot = "";
        public float upperLength, lowerLength;
        public float[] guardSoleActor = Array.Empty<float>();
    }
    [Serializable] public sealed class ArenaPatrolSoleLegGeometry
    {
        public string side = "", foot = "";
        public float[] guardVerticesActor = Array.Empty<float>(), guardSoleActor = Array.Empty<float>();
    }
    [Serializable] public sealed class ArenaPatrolSoleGeometry
    {
        public string sourceContractSha256 = "";
        public ArenaPatrolSoleLegGeometry[] legs = Array.Empty<ArenaPatrolSoleLegGeometry>();
    }
    [Serializable] public sealed class ArenaPatrolStepData
    {
        public int index;
        public string side = "";
        public float swingStart, land, plantRootProgress;
    }
    [Serializable] public sealed class ArenaPatrolSourceSample
    {
        public float time, rootProgress, leftLift, rightLift;
        public float[] leftHipActor = Array.Empty<float>(), rightHipActor = Array.Empty<float>();
    }
    [Serializable] public sealed class ArenaPatrolSourceContract
    {
        public int schema, stepCount;
        public string clipId = "";
        public float duration, fps, canonicalDistance;
        public ArenaPatrolLegData[] legs = Array.Empty<ArenaPatrolLegData>();
        public ArenaPatrolStepData[] steps = Array.Empty<ArenaPatrolStepData>();
        public ArenaPatrolSourceSample[] samples = Array.Empty<ArenaPatrolSourceSample>();
        public void Sample(float time, out float progress, out Vector3 leftHip, out Vector3 rightHip, out float leftLift, out float rightLift)
        {
            float frame = Mathf.Clamp(time * fps, 0, samples.Length - 1);
            int i = Mathf.FloorToInt(frame), j = Mathf.Min(i + 1, samples.Length - 1); float f = frame - i;
            var a = samples[i]; var b = samples[j];
            progress = Mathf.Lerp(a.rootProgress, b.rootProgress, f);
            leftHip = Vector3.Lerp(V(a.leftHipActor), V(b.leftHipActor), f); rightHip = Vector3.Lerp(V(a.rightHipActor), V(b.rightHipActor), f);
            leftLift = Mathf.Lerp(a.leftLift, b.leftLift, f); rightLift = Mathf.Lerp(a.rightLift, b.rightLift, f);
        }
        public static Vector3 V(float[] values)
        {
            if (values == null || values.Length != 3) throw new InvalidOperationException("Patrol contract requires Unity actor-space XYZ vectors.");
            return new Vector3(values[0], values[1], values[2]);
        }
    }
    [Serializable] public sealed class ArenaPatrolVariantAssets
    {
        public string id = "";
        public TextAsset motionData, contractData, soleGeometryData;
    }
    // Independent library: E14 remains unchanged. Contract vectors use actor
    // coordinates, never an assumed Blender-to-FBX bone-local axis mapping.
    public sealed class ArenaPatrolClips : MonoBehaviour
    {
        public const string DirectId = "patrol_six_step", ReturnId = "patrol_return_eight_step", LeftId = "patrol_left_eight_step", FourId = "patrol_four_step", FourShortId = "patrol_four_step_short";
        public string[] ids = Array.Empty<string>();
        public AnimationClip[] clips = Array.Empty<AnimationClip>();
        public TextAsset motionData, contractData, soleGeometryData;
        public ArenaPatrolVariantAssets[] variants = Array.Empty<ArenaPatrolVariantAssets>();
        private sealed class LoadedVariant
        {
            public ArenaMotionData motion;
            public ArenaPatrolSourceContract contract;
            public ArenaPatrolSoleGeometry geometry;
            public TextAsset contractAsset;
        }
        private readonly Dictionary<string,LoadedVariant> loaded = new Dictionary<string,LoadedVariant>();
        public bool Configured => motionData != null && contractData != null && ids != null && clips != null && ids.Length == clips.Length && ids.Length > 0;
        public ArenaPatrolSourceContract Contract => SourceContract(DirectId);
        public ArenaPatrolSourceContract SourceContract(string id = DirectId) => Load(id).contract;
        public bool HasVariant(string id)
        {
            if(id==DirectId)return Configured;
            if(variants==null)return false;
            foreach(var item in variants)if(item!=null&&item.id==id)
                return item.motionData!=null&&item.contractData!=null&&item.soleGeometryData!=null;
            return false;
        }
        public ArenaPatrolSoleLegGeometry SoleGeometry(string side,string id = DirectId)
        {
            var soleGeometry=Load(id).geometry;
            if (soleGeometry == null) throw new InvalidOperationException("The original patrol sole geometry calibration asset is required: "+id);
            foreach (var leg in soleGeometry.legs) if (leg.side == side) return leg;
            throw new InvalidOperationException("Missing original sole geometry for " + side);
        }
        public ArenaMotionClip Motion(string id = DirectId) => Load(id).motion.Get(id);
        public AnimationClip Clip(string id = "patrol_six_step")
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id && clips[i] != null) return clips[i];
            throw new InvalidOperationException("Missing actual patrol animation " + id);
        }
        public void Validate()
        {
            ValidateVariant(DirectId);
            var seen=new HashSet<string>{DirectId};
            if(variants!=null)foreach(var item in variants)
            {
                if(item==null||!seen.Add(item.id))throw new InvalidOperationException("Duplicate or empty patrol variant binding.");
                ValidateVariant(item.id);
                var other=SourceContract(item.id);
                foreach(var leg in Contract.legs)
                {
                    ArenaPatrolLegData same=null;foreach(var candidate in other.legs)if(candidate.side==leg.side)same=candidate;
                    if(same==null||same.hip!=leg.hip||same.knee!=leg.knee||same.foot!=leg.foot
                        ||Mathf.Abs(same.upperLength-leg.upperLength)>.00001f||Mathf.Abs(same.lowerLength-leg.lowerLength)>.00001f
                        ||Vector3.Distance(ArenaPatrolSourceContract.V(same.guardSoleActor),ArenaPatrolSourceContract.V(leg.guardSoleActor))>.000001f)
                        throw new InvalidOperationException("Complete patrol variants do not share the original actual guard/rig contract.");
                }
            }
        }
        private void ValidateVariant(string id)
        {
            var entry=Load(id);var contract=entry.contract;var soleGeometry=entry.geometry;var source=Motion(id);
            int expectedSteps=id==DirectId?6:(id==ReturnId||id==LeftId)?8:(id==FourId||id==FourShortId)?4:0;
            float expectedDuration=id==DirectId?4.8f:(id==ReturnId||id==LeftId)?5.6f:id==FourId?2.8f:id==FourShortId?2.4f:0;
            if(expectedSteps==0)throw new InvalidOperationException("Unknown complete patrol source: "+id);
            if (soleGeometry == null || soleGeometry.legs.Length != 2)
                throw new InvalidOperationException("Bind the separate original sole geometry evidence before using the patrol motor.");
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(entry.contractAsset.bytes)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, soleGeometry.sourceContractSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Sole geometry was not derived from this exact original patrol contract.");
            }
            foreach (var leg in contract.legs)
            {
                var geometry = SoleGeometry(leg.side,id);
                if (geometry.foot != leg.foot || geometry.guardVerticesActor.Length < 96 || geometry.guardVerticesActor.Length % 3 != 0
                    || Vector3.Distance(ArenaPatrolSourceContract.V(geometry.guardSoleActor), ArenaPatrolSourceContract.V(leg.guardSoleActor)) > .000001f)
                    throw new InvalidOperationException("Original geometric calibration does not preserve the authored foot anchor.");
                foreach (float coordinate in geometry.guardVerticesActor)
                    if (float.IsNaN(coordinate) || float.IsInfinity(coordinate)) throw new InvalidOperationException("Non-finite original sole geometry.");
            }
            if (Mathf.Abs(source.duration - expectedDuration) > .001f || Mathf.Abs(Clip(id).length - source.duration) > .002f || Mathf.Abs(contract.duration - source.duration) > .001f)
                throw new InvalidOperationException("The complete patrol must retain its independently authored duration: "+id);
            if(id!=DirectId&&(contract.schema!=2||contract.clipId!=id||contract.stepCount!=expectedSteps))
                throw new InvalidOperationException("B8 requires its exact schema 2 source identity and eight complete steps.");
            if (contract.steps.Length != expectedSteps || contract.legs.Length != 2 || contract.samples.Length != Mathf.RoundToInt(contract.duration * contract.fps) + 1)
                throw new InvalidOperationException("Incomplete patrol contacts, deform chains or source samples.");
            if (contract.canonicalDistance <= 0 || Mathf.Abs(source.EndPosition.z - contract.canonicalDistance) > .001f)
                throw new InvalidOperationException("Patrol canonical root distance disagrees with its contract.");
            for (int i = 0; i < expectedSteps; i++)
            {
                var step = contract.steps[i];
                if (step.index != i || step.side != (i % 2 == 0 ? (id==LeftId?"L":"R") : (id==LeftId?"R":"L")) || step.swingStart < 0 || step.land <= step.swingStart || step.land >= contract.duration
                    || step.plantRootProgress <= 0 || step.plantRootProgress > 1.00001f || (i > 0 && step.swingStart <= contract.steps[i-1].land))
                    throw new InvalidOperationException("Invalid full-step contact contract at step " + i);
            }
            if (Mathf.Abs(contract.steps[expectedSteps-2].plantRootProgress - 1) > .00001f || Mathf.Abs(contract.steps[expectedSteps-1].plantRootProgress - 1) > .00001f)
                throw new InvalidOperationException("Both final feet must reach the actual final guard endpoint.");
            for (int i = 0; i < contract.samples.Length; i++)
            {
                var sample = contract.samples[i];
                if (Mathf.Abs(sample.time - i / contract.fps) > .00001f || sample.rootProgress < -.00001f || sample.rootProgress > 1.00001f
                    || (i > 0 && sample.rootProgress < contract.samples[i-1].rootProgress - .00001f) || sample.leftLift < -.001f || sample.rightLift < -.001f)
                    throw new InvalidOperationException("Patrol sampled timing, signed progress or lift is invalid.");
                ArenaPatrolSourceContract.V(sample.leftHipActor); ArenaPatrolSourceContract.V(sample.rightHipActor);
            }
            foreach (var point in source.trajectory)
                if (Mathf.Abs(point.Position.x) > .00001f || Mathf.Abs(point.Position.y) > .00001f || Mathf.Abs(point.yaw) > .00001f)
                    throw new InvalidOperationException("Canonical patrol root must be grounded +Z; runtime owns the explicitly planned world path.");
        }
        private LoadedVariant Load(string id)
        {
            if(loaded.TryGetValue(id,out var existing))return existing;
            if (!Configured) throw new InvalidOperationException("Patrol18 source is unavailable; no older gait substitutes for its continuous endpoint coverage.");
            TextAsset selectedMotion=motionData,selectedContract=contractData,selectedGeometry=soleGeometryData;
            if(id!=DirectId)
            {
                ArenaPatrolVariantAssets selected=null;
                if(variants!=null)foreach(var item in variants)if(item!=null&&item.id==id)selected=item;
                if(selected==null||selected.motionData==null||selected.contractData==null||selected.soleGeometryData==null)
                    throw new InvalidOperationException("Complete return source is unavailable; bind "+id+" before integrated patrol.");
                selectedMotion=selected.motionData;selectedContract=selected.contractData;selectedGeometry=selected.soleGeometryData;
            }
            var loadedMotion = ArenaMotionData.Read(selectedMotion);
            var loadedContract = JsonUtility.FromJson<ArenaPatrolSourceContract>(selectedContract.text);
            if (loadedContract == null) throw new InvalidOperationException("Patrol contract JSON is empty.");
            var result=new LoadedVariant{motion=loadedMotion,contract=loadedContract,contractAsset=selectedContract,
                geometry=selectedGeometry==null?null:JsonUtility.FromJson<ArenaPatrolSoleGeometry>(selectedGeometry.text)};
            loaded.Add(id,result);return result;
        }
    }
}
