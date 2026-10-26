using UnityEngine;

// Additive battle poses on Humanoid rigs, with the original Generic fallback.
// Only visual bones change; the physical root and attack data remain authored.
public sealed class BattleJAttackPose : MonoBehaviour
{
    BattleAttack attack;
    Transform[] bones;
    Vector3[] positions;
    Quaternion[] rotations;
    bool applied;
    int weaponArm=1;
    BattleHumanoidRig humanoidRig;
    Transform rightHand, leftHand;
    float recoilRemaining,recoilDirection;
    public bool GuardRecoilActive => recoilRemaining>0;
    public void GuardRecoil(float direction){recoilDirection=direction;recoilRemaining=.11f;}
    void Start()
    {
        attack=GetComponent<BattleAttack>();
        var animator=GetComponentInChildren<Animator>();
        humanoidRig=BattleHumanoidRig.Create(animator);
        if(humanoidRig!=null)return;
        string[] names={"Hips","UpperArm.R","LowerArm.R","UpperLeg.R","LowerLeg.R","UpperArm.L","LowerArm.L"};
        bones=new Transform[names.Length]; positions=new Vector3[names.Length]; rotations=new Quaternion[names.Length];
        if (!animator) return;
        rightHand=BattleHumanoidRig.FindGenericEndpoint(animator,"Hand.R","Middle1.R");
        leftHand=BattleHumanoidRig.FindGenericEndpoint(animator,"Hand.L","Middle1.L");
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
        {
            for(int i=0;i<names.Length;i++) if(bone.name==names[i]) bones[i]=bone;
            if(bone.name=="Carrot")
            {
                for(var parent=bone.parent;parent && parent!=transform;parent=parent.parent)
                {
                    if(parent.name=="UpperArm.L" || parent.name=="LowerArm.L")weaponArm=5;
                    if(parent.name=="UpperArm.R" || parent.name=="LowerArm.R")weaponArm=1;
                }
                // This restored FBX skins the carrot to a finger bone instead
                // of parenting the mesh under its hand. Follow the weighted
                // bone's ancestors, so J weapon swings use the actual prop arm.
                var skin=bone.GetComponent<SkinnedMeshRenderer>();
                if(skin && skin.sharedMesh && skin.sharedMesh.boneWeights.Length>0)
                {
                    int index=skin.sharedMesh.boneWeights[0].boneIndex0;
                    if(index>=0 && index<skin.bones.Length)
                        for(var parent=skin.bones[index];parent && parent!=transform;parent=parent.parent)
                        {
                            if(parent.name=="UpperArm.L" || parent.name=="LowerArm.L")weaponArm=5;
                            if(parent.name=="UpperArm.R" || parent.name=="LowerArm.R")weaponArm=1;
                        }
                }
            }
        }
    }
    void Update()
    {
        Restore(); // Before this frame's Animator evaluation.
        if(Time.timeScale>0)recoilRemaining=Mathf.Max(0,recoilRemaining-Mathf.Min(Time.unscaledDeltaTime,.033f));
    }
    void LateUpdate()
    {
        if (!attack || !attack.IsJAttack || (bones==null && humanoidRig==null)) return;
        if(humanoidRig!=null)humanoidRig.CapturePose();
        else for(int i=0;i<bones.Length;i++) if(bones[i]) {positions[i]=bones[i].localPosition; rotations[i]=bones[i].localRotation;}
        applied=true;
        float t=attack.JPoseProgress, contact=attack.JContactProgress;
        float wind=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/Mathf.Max(.01f,contact)));
        float fade=humanoidRig!=null
            ? 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(contact+.14f,1f,t))
            : 1f-Mathf.SmoothStep(contact+.14f,1f,t);
        float weight=wind*fade;
        if(humanoidRig!=null)
        {
            ApplyHumanoidPose(weight);
            if(recoilRemaining>0)
            {
                float recoil=Mathf.Pow(recoilRemaining/.11f,2)*(BattlePresentationSettings.ReducedMotion?.4f:1f);
                humanoidRig.LeanTorso(-recoilDirection,7,recoil);
                if(!BattlePresentationSettings.ReducedMotion)humanoidRig.ShiftHips(Vector3.left*(recoilDirection*.025f*recoil));
            }
            return;
        }
        if(recoilRemaining>0)
        {
            float recoil=Mathf.Pow(recoilRemaining/.11f,2)*(BattlePresentationSettings.ReducedMotion?.4f:1f);
            if(bones[0])
            {
                bones[0].rotation=Quaternion.AngleAxis(recoilDirection*7*recoil,Vector3.forward)*bones[0].rotation;
                if(!BattlePresentationSettings.ReducedMotion)bones[0].position+=Vector3.left*(recoilDirection*.025f*recoil);
            }
        }
        switch(attack.CurrentJMove)
        {
            case BattleJMove.Upper:
                Rotate(0,new Vector3(-9,0,0)*weight);
                int freeArm=weaponArm==1 ? 5 : 1;
                AimArm(freeArm,freeArm+1,new Vector3(.30f*attack.CommittedDirection,1.45f,0),weight);
                break;
            case BattleJMove.Low:
                // FBX rig parents can have unit-conversion scale: use world
                // metres, never a raw local offset that stretches below stage.
                if(bones[0]) bones[0].position+=Vector3.down*.10f*weight;
                Rotate(0,new Vector3(15,0,0)*weight);
                Rotate(3,new Vector3(-42,0,0)*weight);
                Rotate(4,new Vector3(42,0,0)*weight);
                AimArm(weaponArm,weaponArm+1,new Vector3(1.12f*attack.CommittedDirection,-.42f,0),weight);
                break;
            case BattleJMove.Side:
                Rotate(0,new Vector3(13,0,0)*weight);
                AimArm(weaponArm,weaponArm+1,new Vector3(1.48f*attack.CommittedDirection,.12f,0),weight);
                break;
            default:
                bool heavy=attack.CurrentJStep==3, hook=attack.CurrentJStep==2;
                Rotate(0,new Vector3(heavy?17:hook?11:5,
                    (attack.CurrentJStep%2==0?-1:1)*(heavy?22:8),0)*weight);
                int arm=attack.CurrentJStep%2==1 ? weaponArm : weaponArm==1 ? 5 : 1;
                float reach=heavy?1.18f:hook?.94f:1.08f;
                float arc=hook?Mathf.Sin(wind*Mathf.PI)*.18f:0;
                if(heavy && bones[0])bones[0].position+=Vector3.right*(attack.CommittedDirection*.055f*weight);
                AimArm(arm,arm+1,new Vector3(reach*attack.CommittedDirection,.20f,arc),weight);
                break;
        }
    }
    void ApplyHumanoidPose(float weight)
    {
        var data=attack.JData;
        float elapsed=attack.JElapsed;
        float prepare=Mathf.Clamp01(elapsed/Mathf.Max(.01f,data.Startup));
        float drive=Mathf.SmoothStep(0,1,Mathf.InverseLerp(data.Startup*.68f,data.Startup+.018f,elapsed));
        float recover=Mathf.InverseLerp(data.Startup+data.Active,data.Startup+data.Active+data.Recovery,elapsed);
        // Keep a visibly spent posture through the long hook/whiff opening.
        float release=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,.94f,recover));
        weight=Mathf.SmoothStep(0,1,prepare*3)*release;
        float facing=attack.CommittedDirection;
        Vector3 root=transform.position;
        Vector3 rightPole=root+new Vector3(-.12f*facing,.12f,-.32f*facing);
        Vector3 leftPole=root+new Vector3(-.12f*facing,.12f,.32f*facing);
        float wind=(1-drive)*prepare;
        humanoidRig.LeanTorso(facing,-9,wind*weight);
        switch(attack.CurrentJMove)
        {
            case BattleJMove.Upper:
                humanoidRig.ShiftHips(Vector3.down*(.10f*wind*weight));
                humanoidRig.LeanTorso(facing,-7f,drive*weight);
                humanoidRig.PlantFeet(root+Vector3.right*(.32f*facing),weight);
                humanoidRig.AimArm(false,root+Vector3.Lerp(new Vector3(.20f*facing,-.12f,.12f*facing),
                    new Vector3(.38f*facing,1.22f,.04f*facing),drive),leftPole,weight);
                humanoidRig.AimArm(true,root+new Vector3(.22f*facing,.34f,-.08f*facing),rightPole,weight*.8f);
                break;
            case BattleJMove.Low:
                humanoidRig.ShiftHips(Vector3.down*(.21f*weight));
                humanoidRig.LeanTorso(facing,8f,weight);
                // A normal-height fighter can sweep the same low J volume
                // with a foot while the hands retain a compact guard.
                humanoidRig.PlantFeet(root+Vector3.right*(.32f*facing),weight,false);
                humanoidRig.AimLeg(true,root+Vector3.Lerp(new Vector3(-.12f*facing,-.62f,-.12f*facing),new Vector3(.95f*facing,-.58f,-.04f*facing),drive),
                    root+new Vector3(.35f*facing,-.25f,-.12f*facing),weight);
                humanoidRig.AimArm(true,root+new Vector3(.35f*facing,.12f,-.08f*facing),rightPole,weight);
                humanoidRig.AimArm(false,root+new Vector3(.25f*facing,.20f,.08f*facing),leftPole,weight);
                break;
            case BattleJMove.Side:
                humanoidRig.ShiftHips(Vector3.right*(Mathf.Lerp(-.055f,.15f,drive)*facing*weight));
                humanoidRig.LeanTorso(facing,16f,drive*weight);
                humanoidRig.PlantFeet(root+Vector3.right*(.32f*facing),weight);
                humanoidRig.AimArm(true,root+Vector3.Lerp(new Vector3(.08f*facing,.34f,-.16f*facing),new Vector3(1.12f*facing,.24f,-.04f*facing),drive),rightPole,weight);
                humanoidRig.AimArm(false,root+new Vector3(.25f*facing,.30f,.08f*facing),leftPole,weight);
                break;
            default:
                bool hook=attack.CurrentJStep==2;
                bool heavy=attack.CurrentJStep==3;
                humanoidRig.LeanTorso(facing,heavy?20f:hook?13f:6f,drive*weight);
                humanoidRig.TwistTorso(Mathf.Lerp(heavy?-30:-12,heavy?38:hook?30:12,drive)*facing,weight);
                if(heavy){humanoidRig.ShiftHips(Vector3.right*(Mathf.Lerp(-.09f,.17f,drive)*facing*weight));humanoidRig.PlantFeet(root+Vector3.right*(.32f*facing),weight);}
                bool right=attack.CurrentJStep==1 || heavy;
                Vector3 fist=Vector3.Lerp(new Vector3(.10f*facing,.30f,(right?-.22f:.22f)*facing),
                    new Vector3((heavy?1.16f:hook?.78f:.98f)*facing,.26f,(right?-.04f:.04f)*facing),drive);
                if(hook) fist.z+=Mathf.Sin(drive*Mathf.PI)*.28f*facing;
                humanoidRig.AimArm(right,root+fist,
                    right?rightPole:leftPole,weight);
                humanoidRig.AimArm(!right,root+new Vector3(.24f*facing,.30f,(right?.08f:-.08f)*facing),
                    right?leftPole:rightPole,weight*.8f);
                break;
        }
    }
    void AimArm(int upper,int lower,Vector3 localContact,float weight)
    {
        if(!bones[upper] || !bones[lower])return;
        Vector3 from=bones[lower].position-bones[upper].position;
        Vector3 to=transform.position+localContact-bones[upper].position;
        if(from.sqrMagnitude<.0001f || to.sqrMagnitude<.0001f)return;
        Quaternion aim=Quaternion.FromToRotation(from,to)*bones[upper].rotation;
        bones[upper].rotation=Quaternion.Slerp(bones[upper].rotation,aim,weight);
        Transform hand=lower==2?rightHand:leftHand;
        if(hand)
        {
            Vector3 wrist=hand.position-bones[lower].position;
            Vector3 reach=transform.position+localContact-bones[lower].position;
            if(wrist.sqrMagnitude>.0001f && reach.sqrMagnitude>.0001f)
            {
                Quaternion forearm=Quaternion.FromToRotation(wrist,reach)*bones[lower].rotation;
                bones[lower].rotation=Quaternion.Slerp(bones[lower].rotation,forearm,weight);
            }
        }
    }
    void Rotate(int index,Vector3 euler) {if(bones[index]) bones[index].localRotation*=Quaternion.Euler(euler);}
    public void Restore()
    {
        if(!applied) return;
        if(humanoidRig!=null)humanoidRig.RestorePose();
        else if(bones!=null)
            for(int i=0;i<bones.Length;i++) if(bones[i]) {bones[i].localPosition=positions[i]; bones[i].localRotation=rotations[i];}
        applied=false;
    }
    void OnDisable(){recoilRemaining=0;Restore();}
    void OnDestroy()=>Restore();
}
