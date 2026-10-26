using System;
using System.Collections.Generic;
using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        HeroRopeGroundExitMotion ropeGroundExit;
        HeroActionDefinition ropeGroundExitDefinition,ropeGroundExitSource;
        string ropeExitMetadataBlocked;
        float ropeExitFrameStart,ropeExitFrameRate;
        public bool GroundRopeExitActive=>phase==-5&&ropeGroundExit!=null&&ActionId=="ROPE_EXIT_GROUND";
        public float GroundRopeExitStartTime=>ropeExitFrameStart;
        public float GroundRopeExitRate=>ropeExitFrameRate;
        public string GroundRopeExitStage=>ropeGroundExit==null?"":ropeContact!=null?"Align":ropeGroundExit.StageName;
        public string GroundRopeExitBlocked=>!string.IsNullOrEmpty(ropeExitMetadataBlocked)?ropeExitMetadataBlocked:ropeGroundExit==null?"":ropeGroundExit.Blocked;
        public float GroundRopeExitFootGap=>ropeGroundExit==null?0:ropeGroundExit.MaximumFootGap;
        void ResetRopeGroundExit(){ropeGroundExit?.Dispose();ropeGroundExit=null;ropeGroundExitDefinition=ropeGroundExitSource=null;ropeExitMetadataBlocked=null;ropeExitFrameStart=ropeExitFrameRate=0;}
        bool TryBeginRopeGroundExit()
        {
            var d=hero.Library.Find("ROPE_EXIT_GROUND");
            if(d==null||!d.canonicalRopeGroundExit)return false;
            // Invalid optional metadata holds the existing contacts; never fall through to legacy interpolation.
            if(ropeGroundExit!=null)return true;
            if(!d.clip||d.clip!=d.ropeGroundExitClip||d.ropeGroundExitHoldClip!=pairedRopeHoldClip||
                d.loop||d.blendDuration!=0||Mathf.Abs(d.duration-1.2f)>.001f||Mathf.Abs(d.clip.length-1.2f)>.002f||
                !hero.pose.PoseCorrectionReady(d)||ropeContact==null||!ropeContact.Current.Valid||Mathf.Abs(ropeContact.Current.Phase)>.005f||
                !HeroRopeGroundExitMotion.TryPrepare(capsule,surface,ropeEntry,hero.pose,out ropeGroundExit))return true;
            ropeGroundExitSource=d;
            ropeGroundExitDefinition=new HeroActionDefinition{id=d.id,title=d.title,group=d.group,clip=d.clip,duration=d.duration,loop=false,blendDuration=0,poseCorrectionClip=d.poseCorrectionClip,poseCorrections=d.poseCorrections,canonicalRopeGroundExit=true,ropeGroundExitClip=d.ropeGroundExitClip,ropeGroundExitHoldClip=d.ropeGroundExitHoldClip};phase=-5;phaseTime=0;return true;
        }
        void UpdateRopeGroundExit(float dt,bool cancel)
        {
            if(ropeGroundExit==null||!capsule||!capsule.enabled||ropeEntry==null||!ropeEntry.ContactGeometryUnchanged){Cancel(false);return;}
            var source=hero.Library.Find("ROPE_EXIT_GROUND");var frozen=ropeGroundExitDefinition;
            bool metadata=source==ropeGroundExitSource&&source.id==frozen.id&&source.canonicalRopeGroundExit&&source.clip==frozen.clip&&source.ropeGroundExitClip==frozen.clip&&source.ropeGroundExitHoldClip==frozen.ropeGroundExitHoldClip&&source.poseCorrectionClip==frozen.poseCorrectionClip&&source.poseCorrections==frozen.poseCorrections&&!source.loop&&source.duration==frozen.duration&&source.blendDuration==0&&(!GroundRopeExitActive||hero.pose.PoseCorrectionReady(frozen));
            ropeExitMetadataBlocked=metadata?null:"exit-metadata-changed";
            if(cancel||(vitals&&!vitals.CanExert)){if(cancel)movement.ConsumeJumpInputForFrame();Cancel();return;}
            if(ropeContact!=null)
            {
                if(!ropeContact.Current.BottomAligned||!metadata)
                {
                    ropeContactFrame=ropeContact.AlignAtBottom(dt,metadata);
                    if(!ropeContactFrame.IsActive){Cancel(false);return;}
                    ActionTime=ropeContactFrame.EndTime;return;
                }
                // The exact aligned pose was already displayed. EXIT(0) shares that stance.
                ResetRopeContact();Action("ROPE_EXIT_GROUND");ActionTime=ropeExitFrameStart=ropeExitFrameRate=0;return;
            }
            ropeExitFrameStart=ActionTime;ropeExitFrameRate=0;
            if(!metadata)return;
            ropeGroundExit.Tick(dt);
            ActionTime=ropeGroundExit.Clock;
            ropeExitFrameRate=dt>0?(ActionTime-ropeExitFrameStart)/dt:0;
            if(ropeGroundExit.Complete)FinishRopeGroundExit();
        }
        void FinishRopeGroundExit()
        {
            // A normal exit must already be supported and outside the rope.
            // Cancel's penetration correction/entry-safe teleport is not part of this path.
            if(!ropeGroundExit.Complete||!ropeGroundExit.SafeToFinish)return;
            foreach(var c in ignored)if(c&&capsule)Physics.IgnoreCollision(capsule,c,false);
            ignored.Clear();hero.SynchronizeAfterTraversal(true);ResetRopeGroundExit();
            ropeEntry=null;canonicalRopeSession=false;ResetRopeUpPair();
            surface=null;Candidate=null;ActionId=null;ledgeTopDefinition=null;phase=0;
            if(vitals)vitals.ResetTraversal();
            if(ownsLock&&movement){movement.SetActionInputLocked(this,false);ownsLock=false;}
        }
    }
    public sealed class HeroRopeGroundExitMotion:IDisposable
    {
        const float Duration=1.2f,LandTime=.504f,RetreatTime=.78f;
        CharacterController body;HeroTraversalSurface facility;HeroRopeEntryMotion geometry;
        Vector3 origin,back;Quaternion heading;
        SkinnedMeshRenderer skin;Mesh baked;int[][] feet;
        readonly List<Vector3> supportVertices=new List<Vector3>();
        readonly RaycastHit[] hits=new RaycastHit[32];
        float supportAge,finishAge;bool landConfirmed,firstStepConfirmed;
        public float Clock {get;private set;}
        public bool Complete {get;private set;}
        public string Blocked {get;private set;}
        public float MaximumFootGap {get;private set;}
        public string StageName=>Complete?"Complete":Clock<LandTime?"Land":!landConfirmed?"ConfirmSupport":Clock<RetreatTime?"Release":Clock<Duration?"Retreat":"Settle";
        public bool SafeToFinish=>Complete&&body&&body.enabled&&geometry!=null&&geometry.ContactGeometryUnchanged&&geometry.FloorMatches(body.transform.position)&&
            geometry.Clear(body.transform.position,null)&&Vector3.Distance(body.transform.position,origin+back*.4f)<.002f;
        public static bool TryPrepare(CharacterController body,HeroTraversalSurface facility,HeroRopeEntryMotion geometry,HeroPoseDriver pose,out HeroRopeGroundExitMotion motion)
        {
            motion=null;
            if(!body||!facility||geometry==null||!geometry.ContactGeometryUnchanged||!pose||!pose.Ready||
                Vector3.Distance(body.transform.position,facility.Bottom)>.003f||Quaternion.Angle(body.transform.rotation,facility.transform.rotation)>.1f||
                !geometry.FloorMatches(body.transform.position,.0005f))return false;
            var m=new HeroRopeGroundExitMotion{body=body,facility=facility,geometry=geometry,origin=body.transform.position,back=-facility.transform.forward,heading=body.transform.rotation};
            for(int i=0;i<=16;i++){var p=m.origin+m.back*(.4f*i/16);if(!geometry.FloorMatches(p)||!geometry.Clear(p,geometry.Rope))return false;}
            m.skin=pose.GetComponentInChildren<SkinnedMeshRenderer>();if(!m.skin||!m.skin.sharedMesh)return false;
            var weights=m.skin.sharedMesh.boneWeights;m.feet=new int[2][];
            for(int j=0;j<2;j++)
            {
                var foot=pose.animator.GetBoneTransform(j==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(!foot)return false;var belongs=new bool[m.skin.bones.Length];
                for(int i=0;i<belongs.Length;i++)belongs[i]=m.skin.bones[i]&&(m.skin.bones[i]==foot||m.skin.bones[i].IsChildOf(foot));
                var selected=new List<int>();
                for(int i=0;i<weights.Length;i++){var w=weights[i];float weight=(belongs[w.boneIndex0]?w.weight0:0)+(belongs[w.boneIndex1]?w.weight1:0)+(belongs[w.boneIndex2]?w.weight2:0)+(belongs[w.boneIndex3]?w.weight3:0);if(weight>.8f)selected.Add(i);}
                if(selected.Count<3)return false;m.feet[j]=selected.ToArray();
            }
            m.baked=new Mesh{name="Rope exit support probe"};motion=m;return true;
        }
        static float Ease(float u){u=Mathf.Clamp01(u);return u*u*u*(10+u*(-15+6*u));}
        public static float RetreatDistance(float time)=>.4f*Ease((time/Duration-.65f)/.35f);
        static float TimeForDistance(float distance)
        {
            float low=RetreatTime,high=Duration;
            for(int i=0;i<24;i++){float mid=(low+high)*.5f;if(RetreatDistance(mid)<distance)low=mid;else high=mid;}
            return (low+high)*.5f;
        }
        bool FloorPoint(Vector3 p,out float height)
        {
            height=float.NegativeInfinity;
            int n=Physics.RaycastNonAlloc(p+Vector3.up*.12f,Vector3.down,hits,.3f,~0,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length)return false;
            for(int i=0;i<n;i++){var h=hits[i];if(h.collider&&h.collider!=geometry.Rope&&!h.collider.transform.IsChildOf(body.transform)&&h.normal.y>.99f)height=Mathf.Max(height,h.point.y);}
            return !float.IsNegativeInfinity(height);
        }
        bool FeetSupported(int mask=3)
        {
            skin.BakeMesh(baked,true);baked.GetVertices(supportVertices);var vertices=supportVertices;float maximum=0;
            for(int j=0;j<2;j++)
            {
                if((mask&(1<<j))==0)continue;
                float minimum=float.PositiveInfinity;Vector3 lowest=Vector3.zero;
                foreach(int index in feet[j]){var p=skin.transform.TransformPoint(vertices[index]);if(p.y<minimum){minimum=p.y;lowest=p;}}
                if(!FloorPoint(lowest,out float floor))return false;
                float gap=minimum-floor;maximum=Mathf.Max(maximum,gap);
                if(gap<-.003f||gap>.012f){MaximumFootGap=gap;return false;}
                int supported=0;
                foreach(int index in feet[j])
                {
                    var p=skin.transform.TransformPoint(vertices[index]);if(p.y>minimum+.006f)continue;
                    if(FloorPoint(p,out float h)&&Mathf.Abs(h-floor)<.003f&&p.y-h>=-.003f&&p.y-h<=.018f)supported++;
                }
                if(supported<3)return false;
            }
            MaximumFootGap=maximum;return true;
        }
        bool ClearMove(Vector3 target)
        {
            var current=body.transform.position;
            if(!geometry.Clear(target,geometry.Rope)||!geometry.FloorMatches(target))return false;
            var delta=target-current;float distance=delta.magnitude;if(distance<.000001f)return true;
            var center=current+heading*body.center;float half=body.height*.5f-body.radius;
            int n=Physics.CapsuleCastNonAlloc(center+Vector3.up*half,center-Vector3.up*half,body.radius+.005f,delta/distance,hits,distance,~0,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length)return false;
            for(int i=0;i<n;i++)if(hits[i].collider&&hits[i].collider!=geometry.Rope&&!hits[i].collider.transform.IsChildOf(body.transform))return false;
            return true;
        }
        public void Tick(float dt)
        {
            if(dt<=0||Complete)return;
            Blocked="";
            if(float.IsNaN(dt)||float.IsInfinity(dt)||!body||!body.enabled||!geometry.ContactGeometryUnchanged||
                Quaternion.Angle(body.transform.rotation,heading)>.1f||!geometry.FloorMatches(body.transform.position,.001f))
            {Blocked="geometry-or-floor";return;}
            if(dt>.1f){Blocked="unsupported-large-step";return;}
            if(Vector3.Distance(body.transform.position,origin+back*RetreatDistance(Clock))>.001f){Blocked="root-left-committed-path";return;}
            if(Clock>=LandTime&&!landConfirmed)
            {
                var prior=body.transform.position;var flags=body.Move(Vector3.down*.003f);
                if((flags&CollisionFlags.Below)==0||Vector3.Distance(prior,body.transform.position)>.001f||!FeetSupported())
                {supportAge=0;Blocked="awaiting-two-foot-support";return;}
                supportAge+=dt;if(supportAge>=.05f)landConfirmed=true;return;
            }
            // Keep checking the actually planted shoe during both backward steps.
            // A time boundary alone never proves that the next support was acquired.
            if(landConfirmed&&Clock<Duration)
            {
                int mask=Clock<RetreatTime?3:Clock<.972f?2:Clock<.984f?3:1;
                if(!FeetSupported(mask)){Blocked="lost-stance-foot";return;}
                if(Clock>=.972f&&!firstStepConfirmed){if(!FeetSupported(1)){Blocked="awaiting-back-step-support";return;}firstStepConfirmed=true;return;}
            }
            if(Clock>=Duration)
            {
                if(!geometry.Clear(body.transform.position,null)||!FeetSupported()||Vector3.Distance(body.transform.position,origin+back*.4f)>.002f)
                {finishAge=0;Blocked="awaiting-stable-exit";return;}
                finishAge+=dt;if(finishAge>=.12f)Complete=true;return;
            }
            float next=Mathf.Min(Duration,Clock+dt);
            if(!landConfirmed)next=Mathf.Min(LandTime,next);
            if(!firstStepConfirmed)next=Mathf.Min(.972f,next);
            var target=origin+back*RetreatDistance(next);
            if(next>RetreatTime)
            {
                if(!ClearMove(target)){Blocked="retreat-obstacle";return;}
                var prior=body.transform.position;body.Move(target-prior);
                var after=body.transform.position;float actual=Vector3.Dot(after-origin,back);
                if(Vector3.ProjectOnPlane(after-origin,back).magnitude>.001f||actual<RetreatDistance(Clock)-.0001f)
                {Blocked="unexpected-physical-displacement";return;}
                // A real collision can shorten the step after preflight; clock follows actual travel.
                if(Vector3.Distance(after,target)>.0001f){Clock=Mathf.Max(Clock,TimeForDistance(actual));Blocked="partial-retreat";return;}
            }
            Clock=next;
        }
        public void Dispose(){if(baked){if(Application.isPlaying)UnityEngine.Object.Destroy(baked);else UnityEngine.Object.DestroyImmediate(baked);baked=null;}}
    }
}
