using System.Collections.Generic;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroPoseDriver
    {
        struct StopSkinVertex
        {
            public BoneWeight weights;
            public Vector3 p0,p1,p2,p3;
        }
        readonly List<StopSkinVertex>[] stopShoePoints={new List<StopSkinVertex>(),new List<StopSkinVertex>()};
        Transform[] stopSkinBones;
        Matrix4x4[] stopSkinMatrices;
        bool[] stopSkinUsed;
        readonly float[] stopSkinInitial=new float[2],stopSkinFinal=new float[2],stopSkinLanding=new float[2];

        void CacheStopSkinGeometry(SkinnedMeshRenderer skin)
        {
            stopSkinBones=skin.bones;stopSkinMatrices=new Matrix4x4[stopSkinBones.Length];stopSkinUsed=new bool[stopSkinBones.Length];
            var vertices=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;var bind=skin.sharedMesh.bindposes;
            for(int side=0;side<2;side++){
                var points=stopShoePoints[side];points.Clear();var foot=stopFoot[side];
                for(int i=0;i<vertices.Length;i++){
                    var w=weights[i];float influence=Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3);
                    if(influence<.75f)continue;
                    points.Add(new StopSkinVertex{weights=w,p0=bind[w.boneIndex0].MultiplyPoint3x4(vertices[i]),p1=bind[w.boneIndex1].MultiplyPoint3x4(vertices[i]),p2=bind[w.boneIndex2].MultiplyPoint3x4(vertices[i]),p3=bind[w.boneIndex3].MultiplyPoint3x4(vertices[i])});
                    if(w.weight0>0)stopSkinUsed[w.boneIndex0]=true;if(w.weight1>0)stopSkinUsed[w.boneIndex1]=true;if(w.weight2>0)stopSkinUsed[w.boneIndex2]=true;if(w.weight3>0)stopSkinUsed[w.boneIndex3]=true;
                }
                float Weight(int bone,float amount)=>stopSkinBones[bone]==foot||stopSkinBones[bone].IsChildOf(foot)?amount:0;
            }
        }
        void RefreshStopSkinMatrices()
        {
            if(stopSkinBones==null)return;
            for(int i=0;i<stopSkinBones.Length;i++)if(stopSkinUsed[i])stopSkinMatrices[i]=stopSkinBones[i].localToWorldMatrix;
        }
        float StopSkinMinimum(int side)
        {
            float minimum=float.PositiveInfinity;
            foreach(var p in stopShoePoints[side]){
                var w=p.weights;
                float y=Y(w.boneIndex0,p.p0,w.weight0)+Y(w.boneIndex1,p.p1,w.weight1)+Y(w.boneIndex2,p.p2,w.weight2)+Y(w.boneIndex3,p.p3,w.weight3);
                minimum=Mathf.Min(minimum,y);
            }
            return minimum;
            float Y(int bone,Vector3 p,float weight)=>weight>0?stopSkinMatrices[bone].MultiplyPoint3x4(p).y*weight:0;
        }
        void CorrectStopShoeFloor(float u,Vector3[] targets,Quaternion[] rotations,bool mayCancel)
        {
            // The recorded final visual support plane is authoritative here. This
            // does not move the capsule or force feet down onto map overlays.
            var thigh0=stopThigh[0].localRotation;var thigh1=stopThigh[1].localRotation;
            var knee0=stopKnee[0].localRotation;var knee1=stopKnee[1].localRotation;
            var foot0=stopFoot[0].localRotation;var foot1=stopFoot[1].localRotation;
            void RestoreBaseContact(string reason){
                stopThigh[0].localRotation=thigh0;stopThigh[1].localRotation=thigh1;
                stopKnee[0].localRotation=knee0;stopKnee[1].localRotation=knee1;
                stopFoot[0].localRotation=foot0;stopFoot[1].localRotation=foot1;
                // Keep the original stop contact active: query and display must both
                // retain the same base IK when only the optional shoe refinement fails.
                if(mayCancel)StopContactFailure=reason;
            }
            for(int pass=0;pass<8;pass++){
                RefreshStopSkinMatrices();bool changed=false;
                for(int side=0;side<2;side++){
                    float step=side==stopSupport?Mathf.Clamp01((u-.56f)/.44f):Mathf.Clamp01(u/.56f);
                    float floor=Mathf.Lerp(stopSkinLanding[side],stopSkinFinal[side],StopEase(step));
                    // Preserve an already-low entry exactly, then remove its deficit
                    // during the same settling window used by the original stop.
                    float required=Mathf.Lerp(Mathf.Min(stopSkinInitial[side],floor),floor,StopEase(u/.18f));
                    float lift=required-StopSkinMinimum(side);
                    if(lift>.00001f){targets[side]+=Vector3.up*(lift+.000005f);changed=true;}
                }
                if(!changed)return;
                bool reachable=true;
                for(int side=0;side<2;side++){
                    float upper=Vector3.Distance(stopThigh[side].position,stopKnee[side].position),lower=Vector3.Distance(stopKnee[side].position,stopFoot[side].position);
                    float distance=Vector3.Distance(stopThigh[side].position,targets[side]);
                    if(distance>upper+lower-.00001f||distance<Mathf.Abs(upper-lower)+.00001f)reachable=false;
                }
                if(!reachable){RestoreBaseContact("Whole shoe correction unreachable");return;}
                for(int side=0;side<2;side++)SolveStopLeg(side,targets[side],rotations[side],Vector3.Slerp(stopPoleInitial[side],stopPoleFinal[side],StopEase(u)));
            }
            // Mixed knee/foot weights do not move exactly one-for-one with the ankle.
            // Check the last solve too, and never leave a partly converged correction.
            RefreshStopSkinMatrices();
            for(int side=0;side<2;side++){
                float step=side==stopSupport?Mathf.Clamp01((u-.56f)/.44f):Mathf.Clamp01(u/.56f);
                float floor=Mathf.Lerp(stopSkinLanding[side],stopSkinFinal[side],StopEase(step));
                float required=Mathf.Lerp(Mathf.Min(stopSkinInitial[side],floor),floor,StopEase(u/.18f));
                if(required-StopSkinMinimum(side)>.00002f){
                    RestoreBaseContact("Whole shoe correction did not converge");return;
                }
            }
        }
    }
}
