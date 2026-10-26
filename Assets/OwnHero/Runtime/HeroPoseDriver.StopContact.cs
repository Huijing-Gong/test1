using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        bool stopContactActive,stopGeometryReady;
        float stopContactDuration,stopLift;
        int stopSupport;
        Vector3 stopRootPosition;
        Quaternion stopRootRotation;
        Transform stopHips;
        readonly Transform[] stopThigh=new Transform[2],stopKnee=new Transform[2],stopFoot=new Transform[2];
        readonly Vector3[] stopSoleOffset=new Vector3[2],stopInitial=new Vector3[2],stopLanding=new Vector3[2],stopFinal=new Vector3[2],stopPoleInitial=new Vector3[2],stopPoleFinal=new Vector3[2];
        readonly Quaternion[] stopRotationInitial=new Quaternion[2],stopRotationFinal=new Quaternion[2];
        readonly RaycastHit[] stopGroundHits=new RaycastHit[16];
        public bool StopContactActive=>stopContactActive;
        public string StopContactFailure {get;private set;}
        public int StopSupportIndex=>stopSupport;
        public Vector3 StopMarkerPosition(int side)=>stopFoot[side]?stopFoot[side].position+stopFoot[side].rotation*stopSoleOffset[side]:transform.position;
        public Vector3 StopFinalMarker(int side)=>stopFinal[side];
        static float StopEase(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        void RestoreCommittedForStop()
        {for(int i=0;i<poseBones.Length;i++)if(poseBones[i]){poseBones[i].localPosition=posePositions[i];poseBones[i].localRotation=poseRotations[i];poseBones[i].localScale=poseScales[i];}}
        bool StopGround(Vector3 point,out Vector3 floor)
        {
            floor=point;int n=gameObject.scene.GetPhysicsScene().Raycast(point+Vector3.up*.55f,Vector3.down,stopGroundHits,2.5f,~0,QueryTriggerInteraction.Ignore);
            if(n==stopGroundHits.Length)return false;
            float best=float.MaxValue;var capsule=GetComponentInParent<CharacterController>();
            for(int i=0;i<n;i++)
            {
                var hit=stopGroundHits[i];if(capsule&&hit.collider.transform.IsChildOf(capsule.transform))continue;
                if(hit.collider is CharacterController||hit.rigidbody||hit.normal.y<.99863f||hit.distance>=best)continue;best=hit.distance;floor=hit.point;
            }
            return best<float.MaxValue;
        }
        bool CacheStopSoleGeometry()
        {
            if(stopGeometryReady)return true;
            var skin=GetComponentInChildren<SkinnedMeshRenderer>();if(!skin||!skin.sharedMesh)return false;
            var mesh=new Mesh();
            try
            {
                skin.BakeMesh(mesh,true);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
                for(int side=0;side<2;side++)
                {
                    var toe=animator.GetBoneTransform(side==0?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
                    int a=System.Array.IndexOf(skin.bones,stopFoot[side]),b=System.Array.IndexOf(skin.bones,toe);int chosen=-1;float y=float.MaxValue;
                    for(int j=0;j<weights.Length;j++)
                    {
                        var w=weights[j];float sum=((w.boneIndex0==a||w.boneIndex0==b)?w.weight0:0)+((w.boneIndex1==a||w.boneIndex1==b)?w.weight1:0)+((w.boneIndex2==a||w.boneIndex2==b)?w.weight2:0)+((w.boneIndex3==a||w.boneIndex3==b)?w.weight3:0);
                        if(sum<.75f)continue;float candidate=skin.transform.TransformPoint(vertices[j]).y;if(candidate<y){y=candidate;chosen=j;}
                    }
                    if(chosen<0)return false;
                    Vector3 local=stopFoot[side].InverseTransformPoint(skin.transform.TransformPoint(vertices[chosen]));stopSoleOffset[side]=Vector3.Scale(local,stopFoot[side].lossyScale);
                }
                CacheStopSkinGeometry(skin);stopGeometryReady=true;return true;
            }
            finally{if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        }
        void CaptureStopContact(HeroActionDefinition definition)
        {
            stopContactActive=false;StopContactFailure=null;if(!committed||poseBones==null)return;
            stopHips=animator.GetBoneTransform(HumanBodyBones.Hips);
            for(int side=0;side<2;side++)
            {
                stopThigh[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                stopKnee[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                stopFoot[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(!stopThigh[side]||!stopKnee[side]||!stopFoot[side]||!stopHips)return;
            }
            RestoreCommittedForStop();var initialFeet=new Vector3[2];
            for(int side=0;side<2;side++){initialFeet[side]=stopFoot[side].position;stopRotationInitial[side]=stopFoot[side].rotation;stopPoleInitial[side]=(stopKnee[side].position-stopThigh[side].position).normalized;}
            stopRootPosition=transform.position;stopRootRotation=transform.rotation;
            double savedTime=current.GetTime();float w0=mixer.GetInputWeight(0),w1=mixer.GetInputWeight(1);
            Vector3 savedPosition=transform.localPosition;Quaternion savedRotation=transform.localRotation;
            try
            {
                current.SetTime(Mathf.Max(0,definition.duration-.0001f));mixer.SetInputWeight(0,0);mixer.SetInputWeight(1,1);PreparePoseCorrectionEvaluation();graph.Evaluate(0);ApplyPoseCorrection(definition,Mathf.Max(0,definition.duration-.0001f));transform.localPosition=basePosition;transform.localRotation=baseRotation;ApplySupport(RawSupport(definition,definition.duration));
                if(!CacheStopSoleGeometry())return;RefreshStopSkinMatrices();
                var floors=new Vector3[2];
                for(int side=0;side<2;side++)
                {
                    stopSkinFinal[side]=StopSkinMinimum(side);stopInitial[side]=initialFeet[side]+stopRotationInitial[side]*stopSoleOffset[side];stopRotationFinal[side]=stopFoot[side].rotation;stopFinal[side]=StopMarkerPosition(side);stopPoleFinal[side]=(stopKnee[side].position-stopThigh[side].position).normalized;
                    if(!StopGround(stopFinal[side],out floors[side])||!StopGround(stopInitial[side],out var initialFloor)){StopContactFailure="No stable flat ground";return;}
                    if(Mathf.Abs(initialFloor.y-floors[side].y)>.035f){StopContactFailure="Step height";return;}
                    // Keep the measured idle support plane. Some authored floor overlays
                    // sit above their collider; forcing the marker onto the collider would
                    // bury the foot and make the next idle frame jump back up.
                    stopSkinLanding[side]=stopSkinFinal[side]+initialFloor.y-floors[side].y;
                    stopLanding[side]=new Vector3(stopInitial[side].x,stopFinal[side].y+initialFloor.y-floors[side].y,stopInitial[side].z);
                }
                if(Mathf.Abs(floors[0].y-floors[1].y)>.035f)return;
                float left=stopInitial[0].y-stopLanding[0].y,right=stopInitial[1].y-stopLanding[1].y;
                stopSupport=Mathf.Abs(left-right)>.015f?(left<right?0:1):(Vector3.Dot(stopInitial[0]-stopInitial[1],transform.forward)>0?0:1);
                // During a running flight phase neither foot is planted. Land the foot
                // closest to its idle support area first instead of locking a trailing leg.
                if(Mathf.Min(left,right)>.03f)stopSupport=Vector2.Distance(new Vector2(stopInitial[0].x,stopInitial[0].z),new Vector2(stopFinal[0].x,stopFinal[0].z))<Vector2.Distance(new Vector2(stopInitial[1].x,stopInitial[1].z),new Vector2(stopFinal[1].x,stopFinal[1].z))?0:1;
                stopContactDuration=Mathf.Max(.01f,definition.duration);stopLift=definition.id=="LOC_RUN_STOP"?.065f:.045f;stopContactActive=true;
            }
            finally{current.SetTime(savedTime);mixer.SetInputWeight(0,w0);mixer.SetInputWeight(1,w1);transform.localPosition=savedPosition;transform.localRotation=savedRotation;RestoreCommittedForStop();if(stopGeometryReady){RefreshStopSkinMatrices();for(int side=0;side<2;side++)stopSkinInitial[side]=StopSkinMinimum(side);}}
        }
        void EvaluateStopContact(float seconds,bool mayCancel=false)
        {
            if(!stopContactActive||currentDefinition==null||!currentDefinition.groundStop)return;
            if(Vector3.Distance(transform.position,stopRootPosition)>.12f||Quaternion.Angle(transform.rotation,stopRootRotation)>12f){if(mayCancel){stopContactActive=false;StopContactFailure="Root moved or turned";}return;}
            float u=Mathf.Clamp01(seconds/stopContactDuration);var targets=new Vector3[2];var rotations=new Quaternion[2];
            for(int side=0;side<2;side++)
            {
                float step=side==stopSupport?Mathf.Clamp01((u-.56f)/.44f):Mathf.Clamp01(u/.56f);
                Vector3 point;
                if(side==stopSupport&&u<=.56f)point=Vector3.Lerp(stopInitial[side],stopLanding[side],StopEase(u/.18f));
                else point=Vector3.Lerp(side==stopSupport?stopLanding[side]:stopInitial[side],stopFinal[side],StopEase(step))+Vector3.up*(Mathf.Sin(Mathf.PI*step)*stopLift);
                float rotation=side==stopSupport?StopEase(u/.18f):StopEase(step);rotations[side]=Quaternion.Slerp(stopRotationInitial[side],stopRotationFinal[side],rotation);targets[side]=point-rotations[side]*stopSoleOffset[side];
            }
            // A small pelvis drop permits extension without changing the capsule or stretching bones.
            float drop=0;
            for(int side=0;side<2;side++)
            {
                var a=stopThigh[side].position;float length=Vector3.Distance(a,stopKnee[side].position)+Vector3.Distance(stopKnee[side].position,stopFoot[side].position)-.003f;
                Vector3 delta=a-targets[side];float horizontal=delta.x*delta.x+delta.z*delta.z;
                if(horizontal<length*length&&delta.y>0)drop=Mathf.Max(drop,delta.y-Mathf.Sqrt(length*length-horizontal));
            }
            drop=Mathf.Clamp(drop,0,.065f);
            for(int side=0;side<2;side++)
            {
                float upper=Vector3.Distance(stopThigh[side].position,stopKnee[side].position),lower=Vector3.Distance(stopKnee[side].position,stopFoot[side].position);
                float reach=Vector3.Distance(stopThigh[side].position-Vector3.up*drop,targets[side]);
                if(reach>upper+lower-.00001f||reach<Mathf.Abs(upper-lower)+.00001f){if(mayCancel){stopContactActive=false;StopContactFailure="Unreachable contact";}return;}
            }
            stopHips.position-=Vector3.up*drop;
            for(int side=0;side<2;side++)SolveStopLeg(side,targets[side],rotations[side],Vector3.Slerp(stopPoleInitial[side],stopPoleFinal[side],StopEase(u)));
            CorrectStopShoeFloor(u,targets,rotations,mayCancel);
        }
        void SolveStopLeg(int side,Vector3 target,Quaternion rotation,Vector3 pole)
        {
            var thigh=stopThigh[side];var knee=stopKnee[side];var foot=stopFoot[side];Vector3 a=thigh.position,b=knee.position,c=foot.position,axis=target-a;
            float upper=Vector3.Distance(a,b),lower=Vector3.Distance(b,c);float distance=Mathf.Clamp(axis.magnitude,Mathf.Abs(upper-lower)+.00001f,upper+lower-.00001f);axis=axis.sqrMagnitude>1e-8f?axis.normalized:Vector3.down;
            Vector3 bend=Vector3.ProjectOnPlane(pole,axis);if(bend.sqrMagnitude<1e-6f)bend=Vector3.ProjectOnPlane(transform.forward,axis);bend.Normalize();float along=(upper*upper-lower*lower+distance*distance)/(2*distance);Vector3 elbow=a+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            thigh.rotation=Quaternion.FromToRotation(b-a,elbow-a)*thigh.rotation;knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,target-knee.position)*knee.rotation;foot.rotation=rotation;
        }
    }
}
