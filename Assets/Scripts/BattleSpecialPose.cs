using UnityEngine;

// Additive accents on the existing rig, not root-motion or Rigidbody changes.
// Update restores before Animator evaluation; LateUpdate adds the current pose.
[DefaultExecutionOrder(250)]
public sealed class BattleSpecialPose : MonoBehaviour
{
    BattleSpecialMoves moves;
    BattleAttack attack;
    Transform[] bones;
    Vector3[] positions;
    Quaternion[] rotations;
    bool applied;
    int weaponArm=1;
    Vector3 carrotTipLocal;
    BattleHumanoidRig humanoidRig;
    Transform rightHand, leftHand, rightFoot, leftFoot;
    public Vector3 CarrotTipWorldDirection=>bones!=null && bones.Length>9 && bones[9]
        ? bones[9].TransformDirection(carrotTipLocal).normalized : Vector3.zero;
    public Vector3 EmitterWorldDirection=>humanoidRig!=null
        ? humanoidRig.EmitterWorldDirection : CarrotTipWorldDirection;
    public bool IsApplied=>applied;

    void Start()
    {
        moves=GetComponent<BattleSpecialMoves>();attack=GetComponent<BattleAttack>();
        var animator=GetComponentInChildren<Animator>();
        humanoidRig=BattleHumanoidRig.Create(animator);
        if(humanoidRig!=null)return;
        string[] names={"Hips","UpperArm.R","LowerArm.R","UpperLeg.R","LowerLeg.R","UpperArm.L","LowerArm.L","UpperLeg.L","LowerLeg.L","Middle1.L"};
        bones=new Transform[names.Length];positions=new Vector3[names.Length];rotations=new Quaternion[names.Length];
        if(!animator)return;
        rightHand=BattleHumanoidRig.FindGenericEndpoint(animator,"Hand.R","Middle1.R");
        leftHand=BattleHumanoidRig.FindGenericEndpoint(animator,"Hand.L","Middle1.L");
        rightFoot=BattleHumanoidRig.FindGenericEndpoint(animator,"Foot.R");
        leftFoot=BattleHumanoidRig.FindGenericEndpoint(animator,"Foot.L");
        foreach(var bone in animator.GetComponentsInChildren<Transform>(true))
        {
            for(int i=0;i<names.Length;i++)if(bone.name==names[i])bones[i]=bone;
            if(bone.name=="Carrot")
                for(var parent=bone.parent;parent && parent!=transform;parent=parent.parent)
                {
                    if(parent.name=="UpperArm.L" || parent.name=="LowerArm.L")weaponArm=5;
                    if(parent.name=="UpperArm.R" || parent.name=="LowerArm.R")weaponArm=1;
                }
        }
        // The shipped Bunny's Carrot is skinned: all its vertices are attached
        // to Middle1.L, not parented beneath an arm. Rotating its object would
        // not reliably rotate its skinned vertices. Aim the small carrier bone
        // instead, with the bind-pose conversion keeping FBX axis/scale safe.
        foreach(var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(renderer.name!="Carrot" || !renderer.sharedMesh || !bones[9])continue;
            var mesh=renderer.sharedMesh;Vector3 size=mesh.bounds.size;
            // Native stage comparison established the orange point is on the
            // positive imported long axis; negative points towards the leaves.
            // A derived-axis alignment test alone cannot establish that sign.
            Vector3 tip=size.x>size.y && size.x>size.z ? Vector3.right :
                size.y>size.z ? Vector3.up : Vector3.forward;
            var bindposes=mesh.bindposes;
            for(int i=0;i<renderer.bones.Length && i<bindposes.Length;i++)
                if(renderer.bones[i]==bones[9])
                {
                    carrotTipLocal=bindposes[i].MultiplyVector(tip).normalized;
                    weaponArm=5;
                    break;
                }
        }
    }

    void Update()=>Restore();
    void LateUpdate()
    {
        if(!moves || !moves.IsBusy || (bones==null && humanoidRig==null) || (attack && attack.IsJAttack))return;
        if(humanoidRig!=null)humanoidRig.CapturePose();
        else for(int i=0;i<bones.Length;i++)if(bones[i]){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;}
        applied=true;
        BattleSpecialPhase phase=moves.Phase;
        float weight=phase==BattleSpecialPhase.Startup ? Mathf.SmoothStep(0,1,moves.Phase01) :
            phase==BattleSpecialPhase.Recovery ? 1f-Mathf.SmoothStep(0,1,moves.Phase01) : .96f;
        float facing=moves.FacingDirection;
        if(humanoidRig!=null)
        {
            ApplyHumanoidPose(phase,facing,weight);
            return;
        }
        switch(moves.Kind)
        {
            case BattleSpecialKind.Charge:
                bool releasing=phase==BattleSpecialPhase.Active;
                LowerHips((releasing?.065f:.045f)*weight);
                Rotate(0,new Vector3(releasing?-13:-6,0,0)*weight);
                // Charge gathers between the hands; release pushes both hands
                // towards the actual emitter, with a short backward torso recoil.
                AimArm(weaponArm,weaponArm+1,new Vector3((releasing?.81f:.55f)*facing,.22f,-.05f),weight);
                int freeArm=weaponArm==1?5:1;
                AimArm(freeArm,freeArm+1,new Vector3((releasing?.77f:.57f)*facing,.19f,-.02f),weight);
                if(bones[9] && carrotTipLocal.sqrMagnitude>.1f)
                    AimBone(bones[9],bones[9].TransformDirection(carrotTipLocal),Vector3.right*facing,weight);
                if(phase==BattleSpecialPhase.Recovery)Rotate(0,new Vector3(-10,0,0)*weight);
                break;
            case BattleSpecialKind.Dash:
                LowerHips(.07f*weight);Rotate(0,new Vector3(12,0,0)*weight);
                if(phase==BattleSpecialPhase.Startup)
                {
                    Rotate(3,new Vector3(-35,0,0)*weight);Rotate(4,new Vector3(42,0,0)*weight);
                    Rotate(7,new Vector3(-28,0,0)*weight);Rotate(8,new Vector3(34,0,0)*weight);
                }
                else
                {
                    // The kick accents the runtime contact at root Y-.12,
                    // rather than drawing a foot-level sweep far below it.
                    AimLeg(3,4,new Vector3(.90f*facing,-.20f,0),weight);
                    Rotate(7,new Vector3(25,0,0)*weight);Rotate(8,new Vector3(45,0,0)*weight);
                }
                AimArm(weaponArm,weaponArm+1,new Vector3(-.18f*facing,.12f,0),weight*.7f);
                AimArm(weaponArm==1?5:1,weaponArm==1?6:2,new Vector3(.20f*facing,.40f,0),weight*.8f);
                break;
            case BattleSpecialKind.Rise:
                if(phase==BattleSpecialPhase.Startup)
                {
                    LowerHips(.12f*weight);
                    Rotate(3,new Vector3(-42,0,0)*weight);Rotate(4,new Vector3(48,0,0)*weight);
                    Rotate(7,new Vector3(-42,0,0)*weight);Rotate(8,new Vector3(48,0,0)*weight);
                }
                else
                {
                    Rotate(3,new Vector3(14,0,0)*weight);Rotate(4,new Vector3(25,0,0)*weight);
                    Rotate(7,new Vector3(-16,0,0)*weight);Rotate(8,new Vector3(38,0,0)*weight);
                    float height=phase==BattleSpecialPhase.Helpless ? .38f : 1.12f;
                    AimArm(weaponArm,weaponArm+1,new Vector3(.23f*facing,height,0),weight);
                }
                break;
            case BattleSpecialKind.Counter:
                LowerHips(.09f*weight);Rotate(0,new Vector3(9,0,0)*weight);
                AimArm(weaponArm,weaponArm+1,new Vector3(.50f*facing,.24f,0),weight);
                AimArm(weaponArm==1?5:1,weaponArm==1?6:2,new Vector3(.37f*facing,.10f,0),weight);
                Rotate(3,new Vector3(-20,0,0)*weight);Rotate(4,new Vector3(24,0,0)*weight);
                Rotate(7,new Vector3(-18,0,0)*weight);Rotate(8,new Vector3(21,0,0)*weight);
                if(moves.CounterSucceeded && phase==BattleSpecialPhase.Active)
                    AimArm(weaponArm,weaponArm+1,new Vector3(1.10f*facing,.20f,0),weight);
                break;
        }
    }

    void ApplyHumanoidPose(BattleSpecialPhase phase,float facing,float weight)
    {
        Vector3 root=transform.position;
        Vector3 direction=Vector3.right*facing;
        Vector3 rightPole=root+new Vector3(-.10f*facing,.10f,-.32f*facing);
        Vector3 leftPole=root+new Vector3(-.10f*facing,.10f,.32f*facing);
        Vector3 kneePole=root+direction*.35f+Vector3.down*.35f;
        switch(moves.Kind)
        {
            case BattleSpecialKind.Charge:
                bool releasing=phase==BattleSpecialPhase.Active;
                // Match the unchanged ResolveBeam origin during release.
                // Startup/charging gather at the existing compact charge cue.
                float push=releasing?Mathf.Lerp(.75f,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(moves.Phase01/.24f))):0;
                float reach=releasing?Mathf.Lerp(.55f,.85f,push):.55f;
                float emitterHeight=releasing?.22f:.19f;
                humanoidRig.ShiftHips((direction*(releasing?.10f:.02f)
                    +Vector3.down*(releasing?.065f:.045f))*weight);
                humanoidRig.LeanTorso(facing,releasing?Mathf.Lerp(-5,18,push):7f+moves.Charge01*3,weight);
                humanoidRig.PlantFeet(kneePole,weight);
                humanoidRig.AimArm(true,root+new Vector3(reach*facing,emitterHeight,-.04f*facing),rightPole,weight);
                humanoidRig.AimArm(false,root+new Vector3(reach*facing,emitterHeight,.04f*facing),leftPole,weight);
                humanoidRig.AimPalm(true,direction,weight);
                humanoidRig.AimPalm(false,direction,weight);
                break;
            case BattleSpecialKind.Dash:
                humanoidRig.ShiftHips(Vector3.down*(.07f*weight));
                humanoidRig.LeanTorso(facing,15f,weight);
                if(phase==BattleSpecialPhase.Startup)
                    humanoidRig.PlantFeet(kneePole,weight);
                else
                {
                    // The kick is extended as movement/contact become active;
                    // it must not stay tucked while the body passes a close target.
                    humanoidRig.AimLeg(true,root+new Vector3(.90f*facing,-.12f,-.04f*facing),kneePole,weight);
                    humanoidRig.AimLeg(false,root+new Vector3(-.30f*facing,-.65f,.10f*facing),kneePole,weight);
                }
                humanoidRig.AimArm(true,root+new Vector3(-.15f*facing,.14f,-.08f*facing),rightPole,weight*.7f);
                humanoidRig.AimArm(false,root+new Vector3(.20f*facing,.40f,.08f*facing),leftPole,weight*.8f);
                break;
            case BattleSpecialKind.Rise:
                if(phase==BattleSpecialPhase.Startup)
                {
                    humanoidRig.ShiftHips(Vector3.down*(.12f*weight));
                    humanoidRig.LeanTorso(facing,8f,weight);
                    humanoidRig.PlantFeet(kneePole,weight);
                    humanoidRig.AimArm(true,root+new Vector3(.22f*facing,.16f,-.08f*facing),rightPole,weight);
                }
                else
                {
                    humanoidRig.LeanTorso(facing,-5f,weight);
                    humanoidRig.AimLeg(true,root+new Vector3(.08f*facing,-.75f,-.12f*facing),kneePole,weight);
                    humanoidRig.AimLeg(false,root+new Vector3(-.10f*facing,-.62f,.12f*facing),kneePole,weight);
                    float height=phase==BattleSpecialPhase.Helpless?.38f:1.04f;
                    humanoidRig.AimArm(true,root+new Vector3(.23f*facing,height,-.04f*facing),rightPole,weight);
                }
                humanoidRig.AimArm(false,root+new Vector3(.12f*facing,.34f,.08f*facing),leftPole,weight*.8f);
                break;
            case BattleSpecialKind.Counter:
                humanoidRig.ShiftHips(Vector3.down*(.09f*weight));
                humanoidRig.LeanTorso(facing,9f,weight);
                humanoidRig.PlantFeet(kneePole,weight);
                bool striking=moves.CounterSucceeded && phase==BattleSpecialPhase.Active;
                if(moves.CounterSucceeded)humanoidRig.TwistTorso(striking?20:-12,weight);
                humanoidRig.AimArm(true,root+new Vector3((striking?1.10f:.45f)*facing,.24f,-.06f*facing),rightPole,weight);
                humanoidRig.AimArm(false,root+new Vector3(.36f*facing,.26f,.06f*facing),leftPole,weight);
                humanoidRig.AimPalm(true,direction,weight);
                humanoidRig.AimPalm(false,direction,weight);
                break;
        }
    }

    void AimArm(int upper,int lower,Vector3 offset,float weight)
    {
        if(!bones[upper] || !bones[lower])return;
        Vector3 target=transform.position+offset;
        AimBone(bones[upper],bones[lower].position-bones[upper].position,target-bones[upper].position,weight);
        Transform hand=lower==2?rightHand:leftHand;
        if(hand)
            AimBone(bones[lower],hand.position-bones[lower].position,target-bones[lower].position,weight*.85f);
    }
    void AimLeg(int upper,int lower,Vector3 offset,float weight)
    {
        if(!bones[upper] || !bones[lower])return;
        Vector3 target=transform.position+offset;
        AimBone(bones[upper],bones[lower].position-bones[upper].position,target-bones[upper].position,weight);
        Transform foot=lower==4?rightFoot:leftFoot;
        if(foot)
            AimBone(bones[lower],foot.position-bones[lower].position,target-bones[lower].position,weight*.92f);
    }
    static void AimBone(Transform bone,Vector3 from,Vector3 to,float weight)
    {
        if(from.sqrMagnitude<.0001f || to.sqrMagnitude<.0001f)return;
        Quaternion rotation=Quaternion.FromToRotation(from,to)*bone.rotation;
        bone.rotation=Quaternion.Slerp(bone.rotation,rotation,weight);
    }
    void LowerHips(float metres) {if(bones[0])bones[0].position+=Vector3.down*metres;}
    void Rotate(int index,Vector3 euler) {if(bones[index])bones[index].localRotation*=Quaternion.Euler(euler);}
    public void Restore()
    {
        if(!applied)return;
        if(humanoidRig!=null)humanoidRig.RestorePose();
        else if(bones!=null)
            for(int i=0;i<bones.Length;i++)if(bones[i]){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];}
        applied=false;
    }
    void OnDisable()=>Restore();
    void OnDestroy()=>Restore();
}
