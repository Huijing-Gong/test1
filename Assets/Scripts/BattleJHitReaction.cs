using UnityEngine;

// Short directional accent on the existing hit animation. Never extends stun
// or moves the actor root. Both restored Generic and Humanoid rigs are supported.
[DefaultExecutionOrder(40), DisallowMultipleComponent]
public sealed class BattleJHitReaction : MonoBehaviour
{
    BattleHumanoidRig rig;
    BattleDamageReceiver receiver;
    BattleJMove move;
    float direction,remaining,duration;
    int step;
    Transform genericTorso,genericHips;
    Transform contactAnchor;
    CapsuleCollider contactCapsule;
    Quaternion torsoBefore;
    Vector3 hipsBefore;
    bool genericApplied;
    public bool Active => remaining>0 && receiver && receiver.IsInHitstun;
    public bool HasCompatibleRig => rig!=null || genericTorso;
    void Awake()
    {
        var animator=GetComponentInChildren<Animator>();
        rig=BattleHumanoidRig.Create(animator);receiver=GetComponent<BattleDamageReceiver>();
        contactCapsule=GetComponent<CapsuleCollider>();
        if(animator && animator.isHuman)
            contactAnchor=animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        if(rig!=null || !animator)return;
        foreach(var joint in animator.GetComponentsInChildren<Transform>(true))
        {
            if(joint.name=="Hips")genericHips=joint;
            if(joint.name=="Spine" || joint.name=="Chest")genericTorso=joint;
        }
        if(!genericTorso)genericTorso=genericHips;
        contactAnchor=genericTorso;
    }
    public Vector3 VisualContactPoint(Vector3 requested,BattleJMove kind,float facing)
    {
        if(kind!=BattleJMove.Neutral || !contactAnchor || Vector3.Distance(contactAnchor.position,transform.position)>2.5f)return requested;
        // Capsule width is a generous gameplay envelope, not the visible
        // chest surface. Align only the mark, never the hit test or target.
        float inset=contactCapsule?Mathf.Min(.20f,contactCapsule.bounds.extents.x*.40f):.18f;
        requested.x=contactAnchor.position.x-Mathf.Sign(facing)*inset;
        requested.y=Mathf.Lerp(requested.y,contactAnchor.position.y,.80f);
        return contactCapsule?contactCapsule.ClosestPoint(requested):requested;
    }
    public void Play(BattleJMove kind,float facing,int comboStep)
    {
        Restore();move=kind;direction=facing;step=comboStep;
        remaining=duration=kind==BattleJMove.Low?.11f:comboStep==3?.24f:comboStep==2?.15f:.11f;
    }
    void Update()
    {
        if(Time.timeScale<=0)return;
        Restore();
        // The visual recoil settles through hit-stop, not only after it ends.
        remaining-=Mathf.Min(Time.unscaledDeltaTime,.033f);
    }
    void LateUpdate()
    {
        if(!Active || !HasCompatibleRig || Time.timeScale<=0)return;
        float weight=Mathf.Pow(Mathf.Clamp01(remaining/duration),2);
        if(BattlePresentationSettings.ReducedMotion)weight*=.45f;
        float lean=move==BattleJMove.Low?7:move==BattleJMove.Upper?17:step==3?23:step==2?12:7;
        float twist=(step%2==0?-1:1)*(step==3?17:step==2?12:6)*direction;
        Vector3 offset=new Vector3(direction*(step==3?.045f:.014f),move==BattleJMove.Low?-.02f:0,0)*weight;
        if(rig!=null)
        {
            rig.CapturePose();rig.LeanTorso(direction,lean,weight);rig.TwistTorso(twist,weight);
            if(!BattlePresentationSettings.ReducedMotion)rig.ShiftHips(offset);
        }
        else
        {
            torsoBefore=genericTorso.localRotation;
            if(genericHips)hipsBefore=genericHips.localPosition;
            genericApplied=true;
            genericTorso.rotation=Quaternion.AngleAxis(-direction*lean*weight,Vector3.forward)*genericTorso.rotation;
            genericTorso.rotation=Quaternion.AngleAxis(twist*weight,Vector3.up)*genericTorso.rotation;
            if(genericHips && !BattlePresentationSettings.ReducedMotion)genericHips.position+=offset;
        }
    }
    void Restore()
    {
        rig?.RestorePose();
        if(!genericApplied)return;
        if(genericTorso)genericTorso.localRotation=torsoBefore;
        if(genericHips)genericHips.localPosition=hipsBefore;
        genericApplied=false;
    }
    void OnDisable(){remaining=0;Restore();}
    void OnDestroy()=>Restore();
}
