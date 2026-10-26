using System;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    // Authoring/setup data captured from a neutral, correctly scaled instance.
    // Capture is explicit and never occurs in Sample or a historical blade query.
    public sealed class HeroGroundTurnReference
    {
        internal readonly Animator Animator;
        internal readonly Vector3 Hip;
        internal readonly Quaternion HipRotation;
        internal readonly Vector3[] Feet=new Vector3[2],Poles=new Vector3[2];
        internal readonly Quaternion[] Rotations=new Quaternion[2];
        internal readonly float[] Clearance=new float[2], ShoeRadius=new float[2];

        public HeroGroundTurnReference(HeroPoseDriver pose,Transform host,float floor)
        {
            if(!pose||!pose.Ready||!host)throw new ArgumentException("A ready neutral hero and its host are required");
            Animator=pose.animator;
            var hips=Animator.GetBoneTransform(HumanBodyBones.Hips);
            Hip=host.InverseTransformPoint(hips.position);
            HipRotation=Quaternion.Inverse(host.rotation)*hips.rotation;
            var skin=pose.GetComponentInChildren<SkinnedMeshRenderer>();
            if(!skin||!skin.sharedMesh)throw new ArgumentException("Neutral hero skin required");
            var mesh=new Mesh();
            try
            {
                skin.BakeMesh(mesh,true);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
                var sourceVertices=skin.sharedMesh.vertices;var bind=skin.sharedMesh.bindposes;var bones=skin.bones;
                for(int k=0;k<2;k++)
                {
                    var f=Bone(k,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot);
                    Feet[k]=host.InverseTransformPoint(f.position);
                    Rotations[k]=Quaternion.Inverse(host.rotation)*f.rotation;
                    Poles[k]=host.InverseTransformDirection(Bone(k,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg).position-Bone(k,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg).position);
                    float low=float.MaxValue;
                    for(int i=0;i<weights.Length;i++)
                    {
                        var w=weights[i];
                        float influence=Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3);
                        
                        var world=skin.transform.TransformPoint(vertices[i]);
                        if(influence>.8f)low=Mathf.Min(low,world.y);
                        if(influence>.8f||(influence>.0001f&&world.y<=f.position.y+.025f&&Vector3.Distance(world,f.position)<=Vector3.Distance(world,Bone(1-k,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot).position))){
                            // Bound every skin influence, including lower-leg blends. Bone-chain
                            // lengths plus bind-space radius cover arbitrary rotations/toe bends
                            // for this fixed-length, fixed-scale turn rig.
                            float bound=Bound(w.boneIndex0,w.weight0)+Bound(w.boneIndex1,w.weight1)+Bound(w.boneIndex2,w.weight2)+Bound(w.boneIndex3,w.weight3);
                            ShoeRadius[k]=Mathf.Max(ShoeRadius[k],bound);
                        }
                        float Bound(int bi,float weight){
                            if(weight<=0)return 0;
                            var bone=bones[bi];
                            if(bone==f.parent){
                                var sc=bone.lossyScale;float m=Mathf.Max(Mathf.Abs(sc.x),Mathf.Abs(sc.y),Mathf.Abs(sc.z));
                                return weight*((bind[bi].MultiplyPoint3x4(sourceVertices[i])-f.localPosition).magnitude*m);
                            }
                            Transform common=f;
                            while(common&&bone!=common&&!bone.IsChildOf(common))common=common.parent;
                            if(!common)throw new ArgumentException("Foot skin outside rig");
                            float length=0;for(var t=f;t!=common;t=t.parent)length+=Vector3.Distance(t.position,t.parent.position);
                            for(var t=bone;t!=common;t=t.parent)length+=Vector3.Distance(t.position,t.parent.position);
                            var scale=bone.lossyScale;float max=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                            return weight*(length+bind[bi].MultiplyPoint3x4(sourceVertices[i]).magnitude*max);
                        }
                    }
                    if(low==float.MaxValue)throw new ArgumentException("Neutral foot skin not found");
                    Clearance[k]=floor+.004f-low;
                    float Weight(int b,float amount)=>skin.bones[b]==f||skin.bones[b].IsChildOf(f)?amount:0;
                }
            }
            finally{if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);}
        }
        internal Transform Bone(int k,HumanBodyBones left,HumanBodyBones right)=>Animator.GetBoneTransform(k==0?left:right);
    }

    // One immutable entry snapshot. Replaying it does not advance clocks, capture a
    // new contact, bake geometry, commit the visible pose, or change diagnostics.
    public sealed class HeroGroundTurnPose
    {
        public const float Duration=.85f;
        readonly HeroGroundTurnReference calibration;
        readonly HeroActionDefinition definition;
        readonly AnimationClip clip;
        readonly Vector3 pivot,entryHip;
        readonly Quaternion reference,entryHipRotation;
        readonly Vector3[] entryFeet=new Vector3[2],entryPoles=new Vector3[2];
        readonly Quaternion[] entryRotations=new Quaternion[2];
        readonly float angle;
        public string ActionId=>angle>0?"LOC_TURN_R":"LOC_TURN_L";
        public float SupportRadius {get;private set;}

        public HeroGroundTurnPose(HeroGroundTurnReference neutral,HeroActionDefinition action,float degrees,Vector3 worldPivot,Quaternion heading)
        {
            if(neutral==null||action==null||!action.clip||float.IsNaN(degrees)||float.IsInfinity(degrees)||Mathf.Abs(degrees)<.001f||Mathf.Abs(degrees)>90.001f)throw new ArgumentException("Measured turn and valid heading required");
            if(!Finite(worldPivot.x)||!Finite(worldPivot.y)||!Finite(worldPivot.z)||!Finite(heading.x)||!Finite(heading.y)||!Finite(heading.z)||!Finite(heading.w)||Mathf.Abs(Quaternion.Dot(heading,heading)-1)>.001f)throw new ArgumentException("Finite pivot and unit heading required");
            calibration=neutral;definition=action;clip=action.clip;angle=degrees;pivot=worldPivot;reference=heading;
            if(action.id!=ActionId||Mathf.Abs(action.duration-Duration)>.0001f)throw new ArgumentException("Turn clip and duration must match");
            var hips=neutral.Animator.GetBoneTransform(HumanBodyBones.Hips);
            entryHip=hips.position;entryHipRotation=hips.rotation;
            for(int k=0;k<2;k++)
            {
                var foot=neutral.Bone(k,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot);
                
                entryFeet[k]=foot.position;entryRotations[k]=foot.rotation;
                var nominal=worldPivot+heading*(neutral.Feet[k]+Vector3.up*neutral.Clearance[k]);
                var error=Vector3.ProjectOnPlane(entryFeet[k]-nominal,Vector3.up).magnitude;
                var ankle=Vector3.ProjectOnPlane(neutral.Feet[k],Vector3.up).magnitude;
                SupportRadius=Mathf.Max(SupportRadius,ankle+error+neutral.ShoeRadius[k]+.02f);
                entryPoles[k]=neutral.Bone(k,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg).position-neutral.Bone(k,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg).position;
            }
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        internal bool Matches(Animator animator,HeroActionDefinition action)=>calibration.Animator==animator&&ReferenceEquals(definition,action)&&action.clip==clip&&Mathf.Abs(action.duration-Duration)<.0001f;
        static float Ease(float t,float a,float b)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
        static Quaternion Rotation(Vector3 from,Vector3 to)
        {
            from.Normalize();to.Normalize();float w=1+Vector3.Dot(from,to);
            if(w<.000001f)return Quaternion.FromToRotation(from,to);
            var c=Vector3.Cross(from,to);return new Quaternion(c.x,c.y,c.z,w).normalized;
        }
        static float Solve(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 pole)
        {
            var delta=target-upper.position;
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,end.position);
            float length=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.00001f,a+b-.00001f);
            var axis=delta.normalized;var side=Vector3.ProjectOnPlane(pole,axis).normalized;
            float along=(a*a-b*b+length*length)/(2*length);
            var middle=upper.position+axis*along+side*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Rotation(lower.position-upper.position,middle-upper.position)*upper.rotation;
            lower.rotation=Rotation(end.position-lower.position,upper.position+axis*length-lower.position)*lower.rotation;
            return Vector3.Distance(end.position,target);
        }
        internal float Apply(float seconds)
        {
            float u=Mathf.Clamp01(seconds/Duration);bool right=angle>0;
            var body=Quaternion.AngleAxis(angle*Ease(u,.10f,.90f),Vector3.up);
            float first=Ease(u,0,.10f)*(1-Ease(u,.36f,.47f)),second=Ease(u,.35f,.49f)*(1-Ease(u,.80f,1));
            var planted=right?Vector3.left:Vector3.right;var next=Quaternion.AngleAxis(angle,Vector3.up)*(-planted);
            float amount=Mathf.Lerp(.55f,1,Mathf.InverseLerp(0,90,Mathf.Abs(angle)));
            var shift=planted*.045f*first+next*.04f*second-Vector3.up*(.012f*Mathf.Sin(Mathf.PI*u)*Mathf.Sin(Mathf.PI*u));
            float entry=Ease(seconds,0,.10f);
            var hips=calibration.Animator.GetBoneTransform(HumanBodyBones.Hips);
            hips.position=Vector3.Lerp(entryHip,pivot+reference*(body*calibration.Hip+shift*amount),entry);
            hips.rotation=Quaternion.Slerp(entryHipRotation,reference*body*calibration.HipRotation,entry);
            float error=0;
            for(int k=0;k<2;k++)
            {
                bool leading=right?k==1:k==0;float a=leading?.12f:.50f,b=leading?.40f:.80f;
                float swing=Ease(u,a,b);var q=Quaternion.AngleAxis(angle*swing,Vector3.up);
                float lift=.085f*Mathf.Sin(Mathf.PI*swing)*Mathf.Lerp(.65f,1,Mathf.InverseLerp(0,90,Mathf.Abs(angle)));
                var nominal=pivot+reference*(q*calibration.Feet[k]+Vector3.up*calibration.Clearance[k]);
                var initial=pivot+reference*(calibration.Feet[k]+Vector3.up*calibration.Clearance[k]);
                var target=nominal+(entryFeet[k]-initial)*(1-swing)+Vector3.up*lift;
                var pole=Vector3.Slerp(entryPoles[k],reference*q*calibration.Poles[k],entry);
                var foot=calibration.Bone(k,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot);
                error=Mathf.Max(error,Solve(calibration.Bone(k,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg),calibration.Bone(k,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg),foot,target,pole));
                foot.rotation=Quaternion.Slerp(entryRotations[k],reference*Quaternion.AngleAxis(angle,Vector3.up)*calibration.Rotations[k],swing);
            }
            return error;
        }
    }

    public readonly struct HeroGroundTurnFrame
    {
        public bool IsActive=>Pose!=null;
        public float StartTime=>Start;
        internal readonly HeroGroundTurnPose Pose;
        internal readonly float Start,End;
        public HeroGroundTurnFrame(HeroGroundTurnPose pose,float start,float end)
        {
            if(pose==null||float.IsNaN(start)||float.IsNaN(end)||float.IsInfinity(start)||float.IsInfinity(end)||start<0||end<start||end>HeroGroundTurnPose.Duration+.00001f)throw new ArgumentException("Monotonic finite turn frame required");
            Pose=pose;Start=start;End=end;
        }
        internal bool Matches(Animator animator,HeroActionDefinition action)=>Pose!=null&&Pose.Matches(animator,action);
        internal void Validate(Animator animator,HeroActionDefinition action,float seconds,float expectedStart,float delta,float rate)
        {
            if(Pose==null)return;
            if(!Matches(animator,action)||float.IsNaN(seconds)||float.IsNaN(expectedStart)||float.IsNaN(delta)||float.IsNaN(rate)||float.IsInfinity(seconds)||float.IsInfinity(expectedStart)||float.IsInfinity(delta)||float.IsInfinity(rate)||Mathf.Abs(End-seconds)>.0001f||Mathf.Abs(Start-expectedStart)>.0001f||Mathf.Abs((End-Start)-delta*rate)>.0001f)throw new ArgumentException("Turn pose and animation clocks must match");
        }
        internal float Apply(float fraction)=>Pose==null?0:Pose.Apply(Mathf.Lerp(Start,End,Mathf.Clamp01(fraction)));
    }
}
