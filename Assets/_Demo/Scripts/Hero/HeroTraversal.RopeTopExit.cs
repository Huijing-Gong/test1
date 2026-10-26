using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
namespace KingOfGraphic.Hero
{
    public sealed partial class HeroTraversal
    {
        HeroRopeTopExitMotion ropeTopExit;
        HeroActionDefinition ropeTopExitSource,ropeTopExitDefinition;
        float ropeTopFrameStart,ropeTopFrameRate;
        string ropeTopMetadataBlocked;bool ropeTopDropRequested;
        public bool TopRopeExitActive=>phase==-6&&ropeTopExit!=null&&ActionId=="CLIMB_TOP";
        public float TopRopeExitStartTime=>ropeTopFrameStart;
        public float TopRopeExitRate=>ropeTopFrameRate;
        public string TopRopeExitStage=>ropeTopExit==null?"":ropeContact!=null?"Align":ropeTopExit.Stage;
        public string TopRopeExitBlocked=>ropeTopMetadataBlocked??ropeTopExit?.Blocked;
        void ResetRopeTopExit(){ropeTopExit?.Dispose();ropeTopExit=null;ropeTopExitSource=ropeTopExitDefinition=null;ropeTopMetadataBlocked=null;ropeTopDropRequested=false;ropeTopFrameStart=ropeTopFrameRate=0;}
        bool TryBeginRopeTopExit()
        {
            var d=hero.Library.ropeTopExit;
            if(d==null||!d.canonicalRopeTopExit)return false;
            // A configured but invalid pair retains contact; it never falls back to the legacy pull.
            if(ropeTopExit!=null)return true;
            var idle=hero.Library.Find("LOC_IDLE");
            if(ropeContact==null||!ropeContact.Current.Valid||d.id!="CLIMB_TOP"||!d.clip||d.clip!=d.ropeTopExitClip||
                d.ropeTopExitHoldClip!=pairedRopeHoldClip||d.loop||d.blendDuration!=0||Mathf.Abs(d.duration-5.4f)>.0001f||
                Mathf.Abs(d.clip.length-d.duration)>.002f||!hero.pose.PoseCorrectionReady(d)||idle==null||!IdlePairMatches(idle,d)||!hero.pose.PoseCorrectionReady(idle)||
                !HeroRopeTopExitMotion.TryPrepare(capsule,surface,ropeEntry,hero.pose,d,out ropeTopExit))return true;
            ropeTopExitSource=d;
            ropeTopExitDefinition=new HeroActionDefinition{id=d.id,title=d.title,group=d.group,clip=d.clip,duration=d.duration,loop=false,blendDuration=0,poseCorrectionClip=d.poseCorrectionClip,poseCorrections=d.poseCorrections,ropeTopExitHoldClip=d.ropeTopExitHoldClip,ropeTopExitIdleClip=d.ropeTopExitIdleClip,ropeTopExitIdleCorrections=d.ropeTopExitIdleCorrections};
            phase=-6;phaseTime=0;return true;
        }
        static bool IdlePairMatches(HeroActionDefinition idle,HeroActionDefinition d)=>idle!=null&&idle.clip==d.ropeTopExitIdleClip&&idle.poseCorrections==d.ropeTopExitIdleCorrections&&idle.poseCorrectionClip==idle.clip&&idle.loop&&Mathf.Abs(idle.duration-idle.clip.length)<.002f;
        void UpdateRopeTopExit(float dt,bool cancel)
        {
            ropeTopFrameStart=ActionTime;ropeTopFrameRate=0;
            if(cancel||(vitals&&!vitals.CanExert)){if(cancel)movement.ConsumeJumpInputForFrame();ropeTopDropRequested=true;}
            // Drop only once the physical capsule clears the rope; do not use Cancel's teleport/depenetration path.
            if(ropeTopDropRequested&&ropeContact==null&&ropeTopExit!=null&&ropeTopExit.CanRelease){FinishRopeTopOwnership();return;}
            if(ropeTopExit==null||!capsule||!capsule.enabled||ropeEntry==null||!ropeEntry.ContactGeometryUnchanged)
            {ropeTopMetadataBlocked="geometry-changed";if(ropeContact!=null){ropeContactFrame=ropeContact.AlignAtEndpoint(4*HeroRopeContactMotion.Rise,dt,false);ActionTime=ropeContactFrame.EndTime;}return;}
            var source=hero.Library.ropeTopExit;var frozen=ropeTopExitDefinition;
            bool metadata=source==ropeTopExitSource&&source!=null&&source.canonicalRopeTopExit&&source.id==frozen.id&&source.clip==frozen.clip&&
                source.ropeTopExitClip==frozen.clip&&source.ropeTopExitHoldClip==frozen.ropeTopExitHoldClip&&source.ropeTopExitHoldClip==pairedRopeHoldClip&&
                source.poseCorrectionClip==frozen.poseCorrectionClip&&source.poseCorrections==frozen.poseCorrections&&source.ropeTopExitRootPath==ropeTopExit.PathAsset&&
                !source.loop&&source.duration==frozen.duration&&source.blendDuration==0&&source.ropeTopExitIdleClip==frozen.ropeTopExitIdleClip&&source.ropeTopExitIdleCorrections==frozen.ropeTopExitIdleCorrections&&IdlePairMatches(hero.Library.Find("LOC_IDLE"),frozen);
            ropeTopMetadataBlocked=metadata?null:"top-exit-metadata-changed";
            if(ropeContact!=null)
            {
                if(!ropeContact.Current.EndpointAligned||!metadata)
                {
                    ropeContactFrame=ropeContact.AlignAtEndpoint(surface.Top.y-surface.Bottom.y,dt,metadata);
                    if(!ropeContactFrame.IsActive){ropeTopMetadataBlocked="invalid-contact-history";return;}
                    ActionTime=ropeContactFrame.EndTime;return;
                }
                ResetRopeContact();Action("CLIMB_TOP");ActionTime=ropeTopFrameStart=ropeTopFrameRate=0;return;
            }
            if(!metadata)return;
            ropeTopExit.Tick(dt);ActionTime=ropeTopExit.Clock;ropeTopFrameRate=dt>0?(ActionTime-ropeTopFrameStart)/dt:0;
            if(ropeTopExit.Complete&&ropeTopExit.SafeToFinish)FinishRopeTopOwnership();
        }
        void FinishRopeTopOwnership()
        {
                foreach(var c in ignored)if(c&&capsule)Physics.IgnoreCollision(capsule,c,false);
                ignored.Clear();hero.SynchronizeAfterPairedRopeTop(ropeTopExit.Complete);ResetRopeTopExit();ropeEntry=null;canonicalRopeSession=false;ResetRopeUpPair();
                surface=null;Candidate=null;ActionId=null;ledgeTopDefinition=null;phase=0;if(vitals)vitals.ResetTraversal();
                if(ownsLock&&movement){movement.SetActionInputLocked(this,false);ownsLock=false;}
        }
    }

    // Opt-in physical owner for the measured side transfer. The parent capsule owns all travel;
    // paired local poses subtract that same path, and observed shoe support gates each release.
    public sealed class HeroRopeTopExitMotion:IDisposable
    {
        const float Duration=5.4f;
        CharacterController body;HeroTraversalSurface facility;HeroRopeEntryMotion geometry;BoxCollider platform;
        Vector3 origin,platformCenter,platformSize,platformScale;Quaternion heading,platformRotation;
        SkinnedMeshRenderer skin;Mesh baked;int[][] feet;float[] times;Vector3[] points;
        readonly List<Vector3> vertices=new List<Vector3>();readonly Collider[] overlaps=new Collider[64];readonly RaycastHit[] hits=new RaycastHit[64];
        readonly float[] gates={1.85f,3f,4.4f,5.08f,Duration};int gate;float gateAge,finishAge;
        public TextAsset PathAsset {get;private set;}
        public float Clock {get;private set;}
        public bool Complete {get;private set;}
        public string Blocked {get;private set;}
        public string Stage=>Complete?"Complete":Clock<1.85f?"FirstFoot":Clock<3f?"SecondFoot":Clock<3.8f?"ReleaseHands":Clock<Duration?"TurnSteps":"Settle";
        public bool CanRelease=>GeometryValid()&&Clear(body.transform.position,null);
        public bool SafeToFinish=>Complete&&GeometryValid()&&geometry.FloorMatches(body.transform.position,.001f)&&Clear(body.transform.position,null)&&Vector3.Distance(body.transform.position,origin+heading*Point(Duration))<.001f;
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool ReadPath(TextAsset asset,out float[] times,out Vector3[] points)
        {
            times=null;points=null;if(!asset||asset.text.Length>250000)return false;
            try
            {
                var t=new List<float>();var p=new List<Vector3>();using(var reader=new StringReader(asset.text))
                {
                    if(reader.ReadLine()!="time,x,y,z")return false;string line;
                    while((line=reader.ReadLine())!=null){if(string.IsNullOrWhiteSpace(line))continue;var s=line.Split(',');if(s.Length!=4||t.Count>=4096)return false;
                        var v=new float[4];for(int i=0;i<4;i++)if(!float.TryParse(s[i],NumberStyles.Float,CultureInfo.InvariantCulture,out v[i])||!Finite(v[i]))return false;
                        if(v[0]<0||v[0]>Duration+.00001f||(t.Count>0&&(v[0]<=t[t.Count-1]||v[0]-t[t.Count-1]>.01f)))return false;
                        var q=new Vector3(v[1],v[2],v[3]);if(q.magnitude>1.5f||q.y<-.04f||q.y>.005f)return false;t.Add(v[0]);p.Add(q);
                    }
                }
                if(t.Count<2||t[0]!=0||Mathf.Abs(t[t.Count-1]-Duration)>.00001f||p[0].magnitude>.0001f||Vector3.Distance(p[p.Count-1],new Vector3(-.285f,-.026f,.709f))>.0001f)return false;
                times=t.ToArray();points=p.ToArray();return true;
            }
            catch(ArgumentException){return false;}
        }
        public static bool TryPrepare(CharacterController body,HeroTraversalSurface facility,HeroRopeEntryMotion geometry,HeroPoseDriver pose,HeroActionDefinition definition,out HeroRopeTopExitMotion motion)
        {
            motion=null;
            if(!body||!facility||!pose||!pose.Ready||geometry==null||!geometry.ContactGeometryUnchanged||!definition.ropeTopExitRootPath||
                Vector3.Distance(body.transform.position,facility.Top)>.0001f||Quaternion.Angle(body.transform.rotation,facility.transform.rotation)>.1f||
                Mathf.Abs(facility.Top.y-facility.Bottom.y-4*HeroRopeContactMotion.Rise)>.0001f)return false;
            var m=new HeroRopeTopExitMotion{body=body,facility=facility,geometry=geometry,origin=facility.Top,heading=facility.transform.rotation,PathAsset=definition.ropeTopExitRootPath};
            if(!ReadPath(m.PathAsset,out m.times,out m.points))return false;
            foreach(var c in facility.GetComponentsInChildren<BoxCollider>())if(c.enabled&&!c.isTrigger)
            {
                var center=Quaternion.Inverse(m.heading)*(c.transform.TransformPoint(c.center)-m.origin);var size=Vector3.Scale(c.size,c.transform.lossyScale);
                if(Vector3.Distance(center,new Vector3(0,-.156f,1.35f))<.003f&&Vector3.Distance(size,new Vector3(1.9f,.16f,1.8f))<.003f&&Quaternion.Angle(c.transform.rotation,m.heading)<.1f){if(m.platform)return false;m.platform=c;}
            }
            if(!m.platform)return false;m.platformCenter=m.platform.transform.TransformPoint(m.platform.center);m.platformSize=m.platform.size;m.platformScale=m.platform.transform.lossyScale;m.platformRotation=m.platform.transform.rotation;
            // Validate the full physical path before committing the displayed hold to the transfer.
            for(int i=0;i<m.points.Length;i++)if(!m.Clear(m.origin+m.heading*m.points[i],geometry.Rope))return false;
            if(!geometry.FloorMatches(m.origin+m.heading*m.Point(Duration),.001f))return false;
            m.skin=pose.GetComponentInChildren<SkinnedMeshRenderer>();if(!m.skin||!m.skin.sharedMesh)return false;var weights=m.skin.sharedMesh.boneWeights;m.feet=new int[2][];
            for(int j=0;j<2;j++){var foot=pose.animator.GetBoneTransform(j==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);if(!foot)return false;var belongs=new bool[m.skin.bones.Length];
                for(int i=0;i<belongs.Length;i++)belongs[i]=m.skin.bones[i]&&(m.skin.bones[i]==foot||m.skin.bones[i].IsChildOf(foot));var selected=new List<int>();
                for(int i=0;i<weights.Length;i++){var w=weights[i];float weight=(belongs[w.boneIndex0]?w.weight0:0)+(belongs[w.boneIndex1]?w.weight1:0)+(belongs[w.boneIndex2]?w.weight2:0)+(belongs[w.boneIndex3]?w.weight3:0);if(weight>.8f)selected.Add(i);}if(selected.Count<3)return false;m.feet[j]=selected.ToArray();}
            m.baked=new Mesh{name="Rope top support probe"};motion=m;return true;
        }
        Vector3 Point(float t)
        {
            int lo=1,hi=times.Length-1;while(lo<hi){int mid=(lo+hi)/2;if(t<=times[mid])hi=mid;else lo=mid+1;}
            return Vector3.Lerp(points[lo-1],points[lo],Mathf.InverseLerp(times[lo-1],times[lo],t));
        }
        bool GeometryValid()=>body&&body.enabled&&geometry!=null&&geometry.ContactGeometryUnchanged&&facility&&platform&&platform.enabled&&!platform.isTrigger&&
            Vector3.Distance(facility.Top,origin)<.0001f&&Quaternion.Angle(body.transform.rotation,heading)<.1f&&
            Vector3.Distance(platform.transform.TransformPoint(platform.center),platformCenter)<.0001f&&Vector3.Distance(platform.size,platformSize)<.0001f&&
            Vector3.Distance(platform.transform.lossyScale,platformScale)<.0001f&&Quaternion.Angle(platform.transform.rotation,platformRotation)<.1f;
        bool Clear(Vector3 p,Collider ignore)
        {
            var center=p+heading*body.center;float half=body.height*.5f-body.radius;
            int n=Physics.OverlapCapsuleNonAlloc(center+Vector3.up*half,center-Vector3.up*half,body.radius+.005f,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(n>=overlaps.Length)return false;
            for(int i=0;i<n;i++)if(overlaps[i]&&overlaps[i]!=ignore&&!overlaps[i].transform.IsChildOf(body.transform))return false;return true;
        }
        bool Sweep(Vector3 target)
        {
            if(!Clear(target,geometry.Rope))return false;var delta=target-body.transform.position;float length=delta.magnitude;if(length<.000001f)return true;
            var center=body.transform.position+heading*body.center;float half=body.height*.5f-body.radius;
            int n=Physics.CapsuleCastNonAlloc(center+Vector3.up*half,center-Vector3.up*half,body.radius+.005f,delta/length,hits,length,~0,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length)return false;for(int i=0;i<n;i++)if(hits[i].collider&&hits[i].collider!=geometry.Rope&&!hits[i].collider.transform.IsChildOf(body.transform))return false;return true;
        }
        bool FootSupported(int mask)
        {
            skin.BakeMesh(baked,true);baked.GetVertices(vertices);
            for(int j=0;j<2;j++){if((mask&(1<<j))==0)continue;float min=float.PositiveInfinity;foreach(int i in feet[j])min=Mathf.Min(min,skin.transform.TransformPoint(vertices[i]).y);
                int supported=0;Vector2 low=new Vector2(float.PositiveInfinity,float.PositiveInfinity),high=new Vector2(float.NegativeInfinity,float.NegativeInfinity);foreach(int i in feet[j]){var p=skin.transform.TransformPoint(vertices[i]);if(p.y>min+.006f)continue;
                    if(platform.Raycast(new Ray(p+Vector3.up*.08f,Vector3.down),out var hit,.12f)&&hit.normal.y>.99f&&p.y-hit.point.y>=-.003f&&p.y-hit.point.y<=.018f){supported++;var q=Quaternion.Inverse(heading)*(hit.point-origin);low=Vector2.Min(low,new Vector2(q.x,q.z));high=Vector2.Max(high,new Vector2(q.x,q.z));}}
                if(supported<3||high.x-low.x<.008f||high.y-low.y<.008f)return false;}return true;
        }
        int SupportMask()=>Clock<1.85f?0:Clock<3f?1:Clock<4f?3:Clock<4.4f?2:Clock<4.68f?3:Clock<5.08f?1:3;
        public void Tick(float dt)
        {
            if(dt==0||Complete)return;Blocked=null;
            if(!Finite(dt)||dt<0||dt>.1f){Blocked="invalid-step";return;}
            if(!GeometryValid()){Blocked="geometry-changed";return;}
            if(Vector3.Distance(body.transform.position,origin+heading*Point(Clock))>.001f){Blocked="root-left-committed-path";return;}
            int mask=SupportMask();if(mask!=0&&!FootSupported(mask)){Blocked="lost-planted-foot";gateAge=0;return;}
            if(gate<gates.Length&&Clock>=gates[gate]-.000001f){if(!FootSupported(gate==0?1:3)){Blocked="awaiting-foot-patch";gateAge=0;return;}gateAge+=dt;if(gateAge<.05f)return;gate++;gateAge=0;}
            if(Clock>=Duration)
            {
                if(!geometry.FloorMatches(body.transform.position,.001f)||!Clear(body.transform.position,null)||!FootSupported(3)){Blocked="awaiting-finish-support";finishAge=0;return;}
                var before=body.transform.position;var flags=body.Move(Vector3.down*.003f);
                if((flags&CollisionFlags.Below)==0||Vector3.Distance(before,body.transform.position)>.001f){Blocked="unconfirmed-physical-ground";finishAge=0;return;}
                finishAge+=dt;if(finishAge>=.12f)Complete=true;return;
            }
            float next=Mathf.Min(Duration,Clock+dt);if(gate<gates.Length)next=Mathf.Min(next,gates[gate]);
            // Subdivide the authored curve, including stationary-body animation intervals.
            int n=Mathf.Max(1,Mathf.CeilToInt((next-Clock)*240));float begin=Clock;
            for(int i=1;i<=n;i++){float sample=Mathf.Lerp(begin,next,i/(float)n);var target=origin+heading*Point(sample);
                if(!Sweep(target)){Blocked="path-obstacle";return;}body.Move(target-body.transform.position);
                if(Vector3.Distance(body.transform.position,target)>.001f){Blocked="partial-physical-move";return;}Clock=sample;}
        }
        public void Dispose(){if(baked){if(Application.isPlaying)UnityEngine.Object.Destroy(baked);else UnityEngine.Object.DestroyImmediate(baked);baked=null;}}
    }
}
