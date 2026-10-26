using System;
using System.IO;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        internal sealed class CorrectionTrack
        {
            public Transform bone;
            public float[] times;
            public Quaternion[] rotations;
            // HBP1 optionally stores absolute authored local positions after retargeting.
            // Assign instead of adding: unanimated auxiliary bones must not accumulate offsets.
            // HBC1 tracks keep this null and retain their original rotation-only behavior.
            public Vector3[] localPositions;
            public Vector3 PositionAt(float t)
            {
                if(localPositions==null)return Vector3.zero;
                int i=Array.BinarySearch(times,t);if(i>=0)return localPositions[i];
                i=~i;if(i==0)return localPositions[0];if(i==times.Length)return localPositions[i-1];
                return Vector3.Lerp(localPositions[i-1],localPositions[i],(t-times[i-1])/(times[i]-times[i-1]));
            }
            public Quaternion At(float t)
            {
                int i=Array.BinarySearch(times,t);
                if(i>=0)return rotations[i];
                i=~i;if(i==0)return rotations[0];if(i==times.Length)return rotations[i-1];
                return Quaternion.Slerp(rotations[i-1],rotations[i],(t-times[i-1])/(times[i]-times[i-1]));
            }
        }


        // The source cylinder solver over-extends fingers near its release windows.
        // Keep its verified closed grip, but use a relaxed, monotone transfer pose.
        internal sealed class RopeFingerPose
        {
            readonly CorrectionTrack[] tracks;readonly bool reversed;
            readonly Quaternion[] closed=new Quaternion[30],opened=new Quaternion[30];
            readonly float[] spans=new float[2];
            internal RopeFingerPose(CorrectionTrack[] tracks,bool reversed)
            {
                this.tracks=tracks;this.reversed=reversed;
                for(int side=0;side<2;side++)for(int j=0;j<15;j++)
                {
                    int i=side*15+j;closed[i]=tracks[i].At(0);
                    float peak=(HeroRopeContactMotion.Release(side)+HeroRopeContactMotion.End(side))*.5f*HeroRopeContactMotion.Cycle;
                    if(reversed)peak=HeroRopeContactMotion.Cycle-peak;
                    opened[i]=Quaternion.Slerp(closed[i],tracks[i].At(peak),.55f);
                    spans[side]=Mathf.Max(spans[side],Quaternion.Angle(closed[i],opened[i]));
                }
            }
            internal float Span(int side)=>spans[side];
            internal bool Valid {get{foreach(var track in tracks)if(!track.bone)return false;return true;}}
            internal void Apply(float phase,float leftOpening,float rightOpening)
            {
                float clock=Mathf.Repeat(reversed?-phase:phase,1)*HeroRopeContactMotion.Cycle;
                for(int side=0;side<2;side++)for(int j=0;j<15;j++)
                {
                    int i=side*15+j;var track=tracks[i];
                    // Opening itself has eased endpoints; adding another sharp easing
                    // band here would speed up the middle and reintroduce finger flicks.
                    var desired=Quaternion.Slerp(closed[i],opened[i],side==0?leftOpening:rightOpening);
                    track.bone.localRotation=desired*Quaternion.Inverse(track.At(clock))*track.bone.localRotation;
                }
            }
        }
        internal RopeFingerPose BindRopeFingers(HeroActionDefinition definition,bool reversed=false)
        {
            if(!PoseCorrectionReady(definition))throw new ArgumentException("Missing rope finger correction");
            var selected=new CorrectionTrack[30];
            for(int side=0;side<2;side++)for(int j=0;j<15;j++)
            {
                var bone=animator.GetBoneTransform((HumanBodyBones)((int)(side==0?HumanBodyBones.LeftThumbProximal:HumanBodyBones.RightThumbProximal)+j));
                foreach(var track in correctionTracks)if(track.bone==bone){selected[side*15+j]=track;break;}
                if(!bone||selected[side*15+j]==null)throw new ArgumentException("Missing paired finger bone");
            }
            return new RopeFingerPose(selected,reversed);
        }

        sealed class CorrectionBasis
        {
            public UnityEngine.Vector3 position;
            public UnityEngine.Quaternion rotation;
        }
        readonly System.Collections.Generic.Dictionary<Transform,CorrectionBasis> correctionBases=new System.Collections.Generic.Dictionary<Transform,CorrectionBasis>();
        readonly System.Collections.Generic.Dictionary<Transform,CorrectionBasis> correctionDefaults=new System.Collections.Generic.Dictionary<Transform,CorrectionBasis>();
        void CapturePoseCorrectionDefaults()
        {
            foreach(var bone in animator.GetComponentsInChildren<Transform>(true))
                if(!correctionDefaults.ContainsKey(bone))correctionDefaults.Add(bone,new CorrectionBasis{position=bone.localPosition,rotation=bone.localRotation});
        }
        // Full-pose tracks also cover bones without animation curves. Restore their
        // pre-correction base before graph evaluation and when leaving this driver.
        // Ordinary HBC1-only playback leaves this collection empty.
        void PreparePoseCorrectionEvaluation()
        {
            foreach(var entry in correctionBases)if(entry.Key)
            {entry.Key.localPosition=entry.Value.position;entry.Key.localRotation=entry.Value.rotation;}
        }

        TextAsset boundCorrection;
        AnimationClip boundCorrectionClip;
        float boundCorrectionDuration;
        CorrectionTrack[] correctionTracks;
        public static bool HasPairedCorrection(HeroActionDefinition d)=>d!=null&&d.clip&&d.clip==d.poseCorrectionClip&&d.poseCorrections;
        public bool PoseCorrectionReady(HeroActionDefinition d)
        {
            if(!HasPairedCorrection(d)||!animator)return false;
            if(boundCorrection!=d.poseCorrections||boundCorrectionClip!=d.clip||boundCorrectionDuration!=d.duration)
            {
                boundCorrection=d.poseCorrections;boundCorrectionClip=d.clip;boundCorrectionDuration=d.duration;correctionTracks=null;
                try
                {
                    var text=boundCorrection.text;
                    bool fullPose=text.StartsWith("HBP1:",StringComparison.Ordinal);
                    if(fullPose&&!graph.IsValid())CapturePoseCorrectionDefaults();
                    if(text.Length>4000000||(!fullPose&&!text.StartsWith("HBC1:",StringComparison.Ordinal)))return false;
                    using(var stream=new MemoryStream(Convert.FromBase64String(text.Substring(5))))
                    using(var reader=new BinaryReader(stream))
                    {
                        if(reader.ReadInt32()!=(fullPose?0x31504248:0x31434248))return false;
                        float duration=reader.ReadSingle();int count=reader.ReadInt32();
                        if(!Finite(duration)||Mathf.Abs(duration-d.duration)>.001f||duration<=0||count<1||count>200)return false;
                        var tracks=new CorrectionTrack[count];var unique=new System.Collections.Generic.HashSet<Transform>();int total=0;
                        for(int k=0;k<count;k++)
                        {
                            var path=reader.ReadString();if(path.Length==0||path.Length>1024)return false;
                            var bone=animator.transform.Find(path);if(!bone||!unique.Add(bone)||(fullPose&&!correctionDefaults.ContainsKey(bone)))return false;
                            int n=reader.ReadInt32();total+=n;if(n<2||n>20000||total>150000)return false;
                            var track=new CorrectionTrack{bone=bone,times=new float[n],rotations=new Quaternion[n],localPositions=fullPose?new Vector3[n]:null};
                            for(int j=0;j<n;j++)
                            {
                                float t=reader.ReadSingle();var q=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                                float norm=Quaternion.Dot(q,q);
                                if(!Finite(t)||!Finite(norm)||norm<.5f||norm>1.5f||t<0||t>duration+.001f||(j>0&&t<=track.times[j-1]))return false;
                                track.times[j]=t;track.rotations[j]=q.normalized;
                                if(fullPose){var p=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))return false;track.localPositions[j]=p;}
                            }
                            if(track.times[0]>.0001f||Mathf.Abs(track.times[n-1]-duration)>.001f)return false;
                            tracks[k]=track;
                        }
                        if(stream.Position!=stream.Length)return false;
                        correctionTracks=tracks;
                    }
                }
                catch(IOException){return false;}
                catch(FormatException){return false;}
                catch(ArgumentException){return false;}
            }
            return correctionTracks!=null;
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        void ApplyPoseCorrection(HeroActionDefinition d,float seconds)
        {
            if(!PoseCorrectionReady(d))return;
            float t=ClipTime(d,seconds);
            foreach(var track in correctionTracks)if(!track.bone){correctionTracks=null;return;}
            foreach(var track in correctionTracks)
            {
                if(track.localPositions!=null)
                {
                    if(!correctionBases.ContainsKey(track.bone))
                    {
                        if(!correctionDefaults.TryGetValue(track.bone,out var basis))return;
                        correctionBases.Add(track.bone,basis);
                    }
                    track.bone.localPosition=track.PositionAt(t);
                }
                track.bone.localRotation=track.At(t)*track.bone.localRotation;
            }
        }
        void ResetPoseCorrection(){PreparePoseCorrectionEvaluation();correctionBases.Clear();boundCorrection=null;boundCorrectionClip=null;boundCorrectionDuration=0;correctionTracks=null;}
    }
}


