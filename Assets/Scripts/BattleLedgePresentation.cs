using UnityEngine;

// Human-only ledge pose. Physics climb and interruption remain controller-owned.
[DefaultExecutionOrder(220),DisallowMultipleComponent]
public sealed class BattleLedgePresentation:MonoBehaviour
{
    BattlePlayerController player;BattleHumanoidRig rig;float age;bool wasHanging;
    void Awake(){player=GetComponent<BattlePlayerController>();rig=BattleHumanoidRig.Create(GetComponentInChildren<Animator>());}
    void Update(){rig?.RestorePose();}
    void LateUpdate()
    {
        if(!player || rig==null)return;
        bool hanging=player.IsLedgeHanging,climbing=player.IsLedgeClimbing;
        if(!hanging&&!climbing){age=0;wasHanging=false;return;}
        if(hanging&&!wasHanging)age=0;
        wasHanging=hanging;
        if(Time.timeScale>0)age+=Time.deltaTime;
        rig.CapturePose();
        float facing=-player.HangingLedgeSide;
        Vector3 root=transform.position;
        Vector3 lip=new Vector3(player.HangingLedgeX,player.HangingLedgeTopY+.025f,root.z);
        if(hanging)
        {
            float reach=Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.10f));
            rig.ShiftHips(new Vector3(facing*.32f,.08f,0));
            rig.LeanTorso(facing,-5,1);
            rig.AimArm(true,lip+new Vector3(facing*.035f,0,-.15f),root+new Vector3(facing*.22f,.35f,-.4f),reach);
            rig.AimArm(false,lip+new Vector3(facing*.025f,0,.15f),root+new Vector3(facing*.2f,.35f,.4f),reach);
            // One knee raised, other leg hanging: never a symmetric squat.
            rig.AimLeg(true,root+new Vector3(facing*.22f,-.48f,-.10f),root+new Vector3(facing*.58f,-.10f,-.14f),1);
            rig.AimLeg(false,root+new Vector3(-facing*.20f,-.90f,.10f),root+new Vector3(facing*.06f,-.45f,.14f),1);
        }
        else
        {
            float pull=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(player.HangingLedgeTopY-.4f,player.HangingLedgeTopY+.75f,root.y));
            rig.LeanTorso(facing,25,pull);
            rig.AimArm(true,lip+new Vector3(facing*.10f,0,-.15f),root+new Vector3(-facing*.15f,.3f,-.4f),pull);
            rig.AimArm(false,lip+new Vector3(facing*.10f,0,.15f),root+new Vector3(-facing*.15f,.3f,.4f),pull);
            rig.AimLeg(true,root+new Vector3(facing*.35f,-.4f,-.10f),root+new Vector3(facing*.6f,-.05f,-.1f),pull);
        }
    }
    void OnDisable(){rig?.RestorePose();age=0;wasHanging=false;}
    void OnDestroy()=>rig?.RestorePose();
}
