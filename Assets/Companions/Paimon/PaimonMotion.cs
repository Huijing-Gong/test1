using System;
using System.Collections.Generic;
using UnityEngine;

// Deterministic, independently previewable motion. Poses are rebuilt from bind pose every sample.
[DefaultExecutionOrder(100)]
public sealed class PaimonMotion : MonoBehaviour
{
    public enum Action { Float, Wave, Curious, Happy, Fly, Summon, Recall, Drowsy, Sleepy, Crown, Orbit, Airplane, Celebrate, Sway, LookBack, Twirl, JaizyOi, LookAround, SmileSway, DrowsyRecover, ArcSweep, RiseDip, PlayfulTurn, PartnerPeek, ShoulderPeek, LeadAndReturn, Surprise, Joy, Concern, StarFigureEight, MoonSwoop, HeartBounce, CometSpin }
    public Transform visual;
    [SerializeField] Transform[] joints;
    [SerializeField] Quaternion[] rotations;
    [SerializeField] Vector3[] positions;
    [SerializeField] Vector3 visualScale=Vector3.one;
    [SerializeField] Vector3 visualPosition;
    readonly Dictionary<string,Transform> map=new Dictionary<string,Transform>();
    SkinnedMeshRenderer[] skins;
    Quaternion[] blendRotations;Vector3[] blendPositions,blendScales;float[][] blendFaces;
    bool hairReady;
    Vector3 lastHairPosition,lastHairVelocity,hairLag,hairSpringVelocity;
    float lastHairYaw;
    Quaternion haloRestRotation=Quaternion.identity;
    Vector3 headFaceAxis;
    public Vector3 FaceForward => Bone("Head")?(Bone("Head").rotation*headFaceAxis).normalized:transform.forward;
    public Vector3 FacePosition => Bone("Head")?Bone("Head").position:transform.position;
    public void LookAtPartner(Vector3 point,float weight)
    {
        var head=Bone("Head");if(!head)return;Vector3 local=visual.InverseTransformDirection(point-head.position);float yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-42,42),pitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-22,22);
        Vector3 goal=visual.TransformDirection(Quaternion.Euler(pitch,yaw,0)*Vector3.forward);
        Quaternion desired=Quaternion.FromToRotation(head.rotation*headFaceAxis,goal)*head.rotation;head.rotation=Quaternion.Slerp(head.rotation,desired,Mathf.Clamp01(weight)*.85f);
    }

    AnimationClip importedIdle,importedOi,lookAround,smileSway,drowsyRecover,arcSweep,riseDip,playfulTurn,partnerPeek,shoulderPeek,leadAndReturn,surprise,joy,concern,starFigureEight,moonSwoop,heartBounce,cometSpin;
    public int NativeGestureSamples {get;private set;}
    public AnimationClip GestureClip(Action action)
    {
        if(action==Action.JaizyOi)return importedOi;
        if(action==Action.LookAround){if(!lookAround)lookAround=Resources.Load<AnimationClip>("Companions/PaimonLookAround");return lookAround;}
        if(action==Action.SmileSway){if(!smileSway)smileSway=Resources.Load<AnimationClip>("Companions/PaimonSmileSway");return smileSway;}
        if(action==Action.DrowsyRecover){if(!drowsyRecover)drowsyRecover=Resources.Load<AnimationClip>("Companions/PaimonDrowsyRecover");return drowsyRecover;}
        if(action==Action.ArcSweep){if(!arcSweep)arcSweep=Resources.Load<AnimationClip>("Companions/PaimonArcSweep");return arcSweep;}
        if(action==Action.RiseDip){if(!riseDip)riseDip=Resources.Load<AnimationClip>("Companions/PaimonRiseDip");return riseDip;}
        if(action==Action.PlayfulTurn){if(!playfulTurn)playfulTurn=Resources.Load<AnimationClip>("Companions/PaimonPlayfulTurn");return playfulTurn;}
        if(action==Action.PartnerPeek){if(!partnerPeek)partnerPeek=Resources.Load<AnimationClip>("Companions/PaimonPartnerPeek");return partnerPeek;}
        if(action==Action.ShoulderPeek){if(!shoulderPeek)shoulderPeek=Resources.Load<AnimationClip>("Companions/PaimonShoulderPeek");return shoulderPeek;}
        if(action==Action.LeadAndReturn){if(!leadAndReturn)leadAndReturn=Resources.Load<AnimationClip>("Companions/PaimonLeadAndReturn");return leadAndReturn;}
        if(action==Action.Surprise){if(!surprise)surprise=Resources.Load<AnimationClip>("Companions/PaimonSurprise");return surprise;}
        if(action==Action.Joy){if(!joy)joy=Resources.Load<AnimationClip>("Companions/PaimonJoy");return joy;}
        if(action==Action.Concern){if(!concern)concern=Resources.Load<AnimationClip>("Companions/PaimonConcern");return concern;}
        if(action==Action.StarFigureEight){if(!starFigureEight)starFigureEight=Resources.Load<AnimationClip>("Companions/PaimonStarFigureEight");return starFigureEight;}
        if(action==Action.MoonSwoop){if(!moonSwoop)moonSwoop=Resources.Load<AnimationClip>("Companions/PaimonMoonSwoop");return moonSwoop;}
        if(action==Action.HeartBounce){if(!heartBounce)heartBounce=Resources.Load<AnimationClip>("Companions/PaimonHeartBounce");return heartBounce;}
        if(action==Action.CometSpin){if(!cometSpin)cometSpin=Resources.Load<AnimationClip>("Companions/PaimonCometSpin");return cometSpin;}
        return null;
    }
    static readonly string[] SecondaryNames={"+HairS L A01","+HairS L A02","+HairS R A01","+HairS R A02","+EarB CF AF01"};
    readonly Quaternion[] secondaryRotations=new Quaternion[5];
    readonly Vector3[] secondaryPositions=new Vector3[5];
    public int NativeIdleSamples {get;private set;}
    public int NativeOiSamples {get;private set;}
    public bool ImportedReady => importedIdle&&importedOi;
    public void SampleJaizyBase(float time,float flight)
    {
        if(!importedIdle)importedIdle=Resources.Load<AnimationClip>("Companions/PaimonJaizyIdle");
        if(!importedOi)importedOi=Resources.Load<AnimationClip>("Companions/PaimonJaizyOi");
        Sample(time,Action.Float,0,0);
        if(!importedIdle)return;
        visual.localPosition=visualPosition;visual.localRotation=Quaternion.identity;
        for(int i=0;i<SecondaryNames.Length;i++){var bone=Bone(SecondaryNames[i]);if(bone){secondaryRotations[i]=bone.localRotation;secondaryPositions[i]=bone.localPosition;}}
        importedIdle.SampleAnimation(visual.gameObject,Mathf.Repeat(time,importedIdle.length));NativeIdleSamples++;
        for(int i=0;i<SecondaryNames.Length;i++){var bone=Bone(SecondaryNames[i]);if(bone){bone.localRotation=secondaryRotations[i];bone.localPosition=secondaryPositions[i];}}
    }
    public void CaptureRest()
    {
        joints=visual.GetComponentsInChildren<Transform>(true);
        rotations=new Quaternion[joints.Length]; positions=new Vector3[joints.Length]; visualScale=visual.localScale;
        for(int i=0;i<joints.Length;i++){rotations[i]=joints[i].localRotation;positions[i]=joints[i].localPosition;}
        visualPosition=visual.localPosition; Cache();
    }
    void Cache()
    {
        map.Clear();if(joints==null)return;
        foreach(var j in joints)if(j&&!map.ContainsKey(j.name))map.Add(j.name,j);
        skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        RefreshHairMesh();
        var head=Bone("Head");if(head){var chain=new System.Collections.Generic.Stack<Transform>();for(var t=head;t&&t!=visual;t=t.parent)chain.Push(t);Quaternion neutral=visual.rotation;foreach(var t in chain){int i=Array.IndexOf(joints,t);neutral*=i>=0?rotations[i]:t.localRotation;}headFaceAxis=Quaternion.Inverse(neutral)*visual.forward;}
        var halo=Bone("+EarB CF AF01");haloRestRotation=Quaternion.identity;
        for(var t=halo;t&&t!=visual;t=t.parent)
        {
            int index=Array.IndexOf(joints,t);
            haloRestRotation=(index>=0?rotations[index]:t.localRotation)*haloRestRotation;
        }
    }
    public void RefreshHairMesh()
    {
        var mesh=Resources.Load<Mesh>("Companions/PaimonHairMesh");
        if(!mesh||!visual)return;
        foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if(skin.sharedMesh&&skin.sharedMesh.vertexCount==mesh.vertexCount&&skin.bones.Length==mesh.bindposes.Length)skin.sharedMesh=mesh;
    }
    void OnEnable(){hairReady=false;capeReady=false;poseReady=false;if(joints==null||joints.Length==0){if(visual)CaptureRest();}else Cache();}
    Transform Bone(string n){map.TryGetValue(n,out var t);return t;}
    // Aim a deform segment using its real child axis, rather than assuming MMD bone axes.
    void Aim(string bone,string child,Vector3 direction,float weight=1)
    {
        var b=Bone(bone);var e=Bone(child);if(!b||!e)return;
        var delta=Quaternion.FromToRotation(e.position-b.position,transform.TransformDirection(direction));
        b.rotation=Quaternion.Slerp(b.rotation,delta*b.rotation,weight);
    }
    void Reach(string side,Vector3 goal,float weight)
    {
        var a=Bone("Arm_"+side);var b=Bone("Elbow_"+side);var c=Bone("Wrist_"+side);if(!a||!b||!c)return;
        float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position);
        Vector3 delta=goal-a.position;float distance=Mathf.Clamp(delta.magnitude,.001f,(upper+lower)*.98f);Vector3 axis=delta.normalized;
        Vector3 pole=Vector3.ProjectOnPlane(transform.right*(side=="R"?1:-1)-transform.forward*.2f,axis).normalized;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        Vector3 elbow=a.position+axis*along+pole*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        a.rotation=Quaternion.Slerp(a.rotation,Quaternion.FromToRotation(b.position-a.position,elbow-a.position)*a.rotation,weight);
        b.rotation=Quaternion.Slerp(b.rotation,Quaternion.FromToRotation(c.position-b.position,goal-b.position)*b.rotation,weight);
    }
    void Rotate(string bone,Vector3 euler)
    {var b=Bone(bone);if(b)b.localRotation*=Quaternion.Euler(euler);}
    void HairFloat(float time,float flight)
    {
        for(int side=0;side<2;side++)for(int segment=1;segment<=2;segment++)
        {
            float lag=segment*.36f+side*.45f;
            float amplitude=Mathf.Lerp(7.2f,10f,flight)*(segment==1?.65f:1);
            Rotate("+HairS "+(side==0?"L":"R")+" A0"+segment,
                new Vector3((Mathf.Sin(time*2.5f-lag)+flight*.7f)*amplitude,
                Mathf.Sin(time*1.45f-lag-.4f)*amplitude*.3f,
                Mathf.Sin(time*2.15f-lag-.65f)*amplitude*.65f));
        }
    }
    // Run after the companion's pose: world acceleration and turns gently drag the hair tips.
    void LateUpdate(){if(Application.isPlaying&&Time.deltaTime>0){AdvanceHair(Time.deltaTime);AdvanceCape(Time.unscaledDeltaTime);}}
    public void AdvanceHair(float dt)
    {
        if(!visual||dt<=0)return;
        Vector3 position=transform.position;float yaw=transform.eulerAngles.y;
        if(!hairReady||Vector3.Distance(position,lastHairPosition)>1)
        {hairReady=true;lastHairPosition=position;lastHairYaw=yaw;lastHairVelocity=hairLag=hairSpringVelocity=Vector3.zero;return;}
        Vector3 velocity=(position-lastHairPosition)/Mathf.Max(.001f,dt);
        Vector3 acceleration=visual.InverseTransformDirection((velocity-lastHairVelocity)/Mathf.Max(.001f,dt));
        Vector3 localVelocity=visual.InverseTransformDirection(velocity);
        float turnRate=Mathf.DeltaAngle(lastHairYaw,yaw)/Mathf.Max(.001f,dt);
        Vector3 target=new Vector3(Mathf.Clamp(-acceleration.z*.22f-localVelocity.z*.65f,-3.5f,3.5f),
            Mathf.Clamp(-turnRate*.03f,-3.5f,3.5f),Mathf.Clamp(acceleration.x*.22f,-3,3));
        hairLag=Vector3.SmoothDamp(hairLag,target,ref hairSpringVelocity,.20f,15,Mathf.Min(dt,.05f));
        foreach(string side in new[]{"L","R"})for(int segment=1;segment<=2;segment++)
            Rotate("+HairS "+side+" A0"+segment,hairLag*(segment==1?.55f:1));
        lastHairPosition=position;lastHairVelocity=velocity;lastHairYaw=yaw;
    }
    public float ArmClothingAdjustment {get;private set;}
    public void ApplyClothingClearance()
    {
        var left=Bone("Arm_L");var right=Bone("Arm_R");var chest=Bone("UpperBody2");var hips=Bone("LowerBody");if(!left||!right||!chest||!hips)return;
        Vector3 center=(left.position+right.position)*.5f,side=(right.position-left.position).normalized;
        Vector3 up=Vector3.ProjectOnPlane(chest.position-hips.position,side).normalized;if(up.sqrMagnitude<.1f)up=visual.up;
        Vector3 front=Vector3.Cross(side,up).normalized;if(Vector3.Dot(front,FaceForward)<0)front=-front;
        float half=Vector3.Distance(left.position,right.position)*.5f;ArmClothingAdjustment=0;
        // A small anatomical clearance envelope keeps the elbows ahead of the cape and wrists outside the tunic.
        foreach(string name in new[]{"L","R"}){
            var upper=Bone("Arm_"+name);var elbow=Bone("Elbow_"+name);var wrist=Bone("Wrist_"+name);if(!upper||!elbow||!wrist)continue;
            float sign=name=="L"?-1:1;
            KeepOutside(upper,elbow,center,side*sign,front,half+.014f,.025f);
            KeepOutside(elbow,wrist,center,side*sign,front,half+.028f,.052f);
        }
        // The next transition starts from the pose actually displayed after clearance.
        if(poseReady&&previousPose!=null)for(int i=0;i<joints.Length;i++)if(joints[i].name.StartsWith("Arm_")||joints[i].name.StartsWith("Elbow_"))previousPose[i]=joints[i].localRotation;
    }
    void KeepOutside(Transform joint,Transform endpoint,Vector3 center,Vector3 outward,Vector3 front,float width,float depth)
    {
        Vector3 offset=endpoint.position-center;
        Vector3 correction=outward*Mathf.Max(0,width-Vector3.Dot(offset,outward))+front*Mathf.Max(0,depth-Vector3.Dot(offset,front));
        correction=Vector3.ClampMagnitude(correction,.035f);if(correction.sqrMagnitude<.0000001f)return;
        Quaternion goal=Quaternion.FromToRotation(endpoint.position-joint.position,endpoint.position+correction-joint.position)*joint.rotation;
        ArmClothingAdjustment=Mathf.Max(ArmClothingAdjustment,Quaternion.Angle(joint.rotation,goal));joint.rotation=goal;
    }
    bool capeReady;Vector3 capeLastPosition,capeLastVelocity,capeDrag,capeDragVelocity;float capeLastYaw,capeTime;
    public float CapeMotionDegrees {get;private set;}
    public void AdvanceCape(float dt)
    {
        if(!visual||dt<=0)return;
        ApplyClothingClearance();
        float yaw=transform.eulerAngles.y;Vector3 position=transform.position;
        if(!capeReady||Vector3.Distance(position,capeLastPosition)>2)
        {capeReady=true;capeLastPosition=position;capeLastVelocity=Vector3.zero;capeLastYaw=yaw;capeDrag=capeDragVelocity=Vector3.zero;}
        var velocity=(position-capeLastPosition)/Mathf.Max(.001f,dt);
        var localVelocity=visual.InverseTransformDirection(velocity);
        var acceleration=visual.InverseTransformDirection((velocity-capeLastVelocity)/Mathf.Max(.001f,dt));
        float turn=Mathf.DeltaAngle(capeLastYaw,yaw)/Mathf.Max(.001f,dt);
        var goal=new Vector3(Mathf.Clamp(-localVelocity.z*1.5f-acceleration.z*.10f,-7,7),Mathf.Clamp(-turn*.035f,-6,6),Mathf.Clamp(localVelocity.x*1.4f+acceleration.x*.12f,-6,6));
        capeDrag=Vector3.SmoothDamp(capeDrag,goal,ref capeDragVelocity,.28f,35,Mathf.Min(dt,.05f));
        capeTime+=dt;CapeMotionDegrees=0;
        for(int row=0;row<10;row++)for(int col=0;col<6;col++)
        {
            string name="PF_"+row+"_"+col;var bone=Bone(name);if(!bone)continue;
            float tip=Mathf.Pow(row/9f,1.55f),edge=Mathf.Abs(col-2.5f)/2.5f;
            float wave=capeTime*3.4f-row*.58f-col*.19f;
            Vector3 bend=new Vector3((Mathf.Sin(wave)*(2.8f+edge)+2+Mathf.Clamp(localVelocity.magnitude,0,5)*.35f)*tip,
                Mathf.Sin(wave*.65f-.6f)*1.7f*tip,Mathf.Sin(wave-.8f)*(1.7f+edge)*tip)+capeDrag*tip*.6f;
            // Keep the shoulder attachment quiet; leave the lower hem free to flutter.
            float shoulderEdge=1-.4f*edge*(1-row/9f);bend*=shoulderEdge;
            bend=Vector3.ClampMagnitude(bend,row<4?2.2f:row<6?4.2f:8f);bone.localRotation*=Quaternion.Euler(bend);
            CapeMotionDegrees=Mathf.Max(CapeMotionDegrees,bend.magnitude);
        }
        capeLastPosition=position;capeLastVelocity=velocity;capeLastYaw=yaw;
    }
    void RelaxPalm(string side,float time)
    {
        var wrist=Bone("Wrist_"+side);var middle=Bone("MiddleFinger1_"+side);
        var index=Bone("IndexFinger1_"+side);var little=Bone("LittleFinger1_"+side);
        if(!wrist||!middle||!index||!little)return;
        float sign=side=="L"?-1:1;
        Vector3 length=(middle.position-wrist.position).normalized;
        Vector3 across=Vector3.ProjectOnPlane(index.position-little.position,length).normalized;
        // Explicit palm frame removes the inherited supination from the MMD bind pose.
        Vector3 down=visual.TransformDirection(new Vector3(sign*.08f,-1,.025f+.025f*Mathf.Sin(time*1.3f+(side=="L"?0:1.2f)))).normalized;
        Vector3 thumbSide=Vector3.ProjectOnPlane(visual.forward,down).normalized;
        Quaternion frame=Quaternion.LookRotation(length,across);
        Quaternion target=Quaternion.LookRotation(down,thumbSide);
        wrist.rotation=target*Quaternion.Inverse(frame)*wrist.rotation;
    }
    void RelaxHand(string side,float time,float strength)
    {
        var wrist=Bone("Wrist_"+side);var middle=Bone("MiddleFinger1_"+side);
        if(!wrist||!middle)return;
        float sign=side=="L"?-1:1;
        Vector3 length=(middle.position-wrist.position).normalized;
        Vector3 inward=Vector3.ProjectOnPlane(visual.TransformDirection(new Vector3(-sign,0,.18f)),length).normalized;
        Vector3 curlAxis=Vector3.Cross(length,inward).normalized;
        string[] fingers={"IndexFinger","MiddleFinger","RingFinger","LittleFinger"};
        for(int f=0;f<fingers.Length;f++)
        {
            float pulse=Mathf.Sin(time*1.3f-f*.38f+(side=="L"?0:1.3f));
            float curl=(12+f*2+1.5f*pulse)*strength;
            for(int joint=1;joint<=3;joint++)
            {
                var bone=Bone(fingers[f]+joint+"_"+side);
                if(bone)bone.rotation=Quaternion.AngleAxis(curl*(joint==2?1.1f:joint==3?.55f:1),curlAxis)*bone.rotation;
            }
        }
        var thumb=Bone("Thumb0_"+side);
        if(thumb)thumb.rotation=Quaternion.AngleAxis(-sign*6,length)*thumb.rotation;
        for(int joint=1;joint<=2;joint++)
        {
            thumb=Bone("Thumb"+joint+"_"+side);
            if(thumb)thumb.rotation=Quaternion.AngleAxis(joint==1?10:6,curlAxis)*thumb.rotation;
        }
    }
    static float BlinkPulse(float elapsed)
    {
        if(elapsed<0||elapsed>.23f)return 0;
        if(elapsed<.065f)return Ease(elapsed/.065f);
        return 1-Ease((elapsed-.09f)/.14f);
    }
    public static float BlinkWeight(float time)
    {
        float t=Mathf.Repeat(time,13.7f);
        return Mathf.Max(BlinkPulse(t-1.8f),Mathf.Max(BlinkPulse(t-5.6f),Mathf.Max(BlinkPulse(t-9.9f),BlinkPulse(t-10.24f))));
    }
    static float Ease(float x){x=Mathf.Clamp01(x);return x*x*x*(x*(x*6-15)+10);}
    static float Envelope(float t,float length,float fade){return Ease(t/fade)*Ease((length-t)/fade);}
    public static float Duration(Action action){return action==Action.LookAround||action==Action.SmileSway||action==Action.DrowsyRecover||action==Action.ArcSweep||action==Action.RiseDip||action==Action.PlayfulTurn||action==Action.PartnerPeek||action==Action.ShoulderPeek||action==Action.LeadAndReturn||action==Action.Surprise||action==Action.Joy||action==Action.Concern||action==Action.StarFigureEight||action==Action.MoonSwoop||action==Action.HeartBounce||action==Action.CometSpin?160f/30:action==Action.JaizyOi?2.2f:action==Action.Twirl?6.2f:action>=Action.Airplane?4.6f:action==Action.Orbit?5.6f:action==Action.Summon||action==Action.Recall?2.4f:action==Action.Wave||action==Action.Happy?4:action==Action.Crown||action==Action.Sleepy?7.2f:6;}
    static readonly float[] OrbitTimes={0,.12f,.28f,.46f,.66f,.84f,1};
    // Nonuniform Hermite keys: continuous velocity through the flight beats, resting endpoints.
    static float FlightKey(float p,params float[] values)
    {
        p=Mathf.Clamp01(p);int k=0;while(k<OrbitTimes.Length-2 && p>OrbitTimes[k+1])k++;
        float span=OrbitTimes[k+1]-OrbitTimes[k],u=(p-OrbitTimes[k])/span;
        float m0=k==0?0:(values[k+1]-values[k-1])/(OrbitTimes[k+1]-OrbitTimes[k-1]);
        float m1=k+1==values.Length-1?0:(values[k+2]-values[k])/(OrbitTimes[k+2]-OrbitTimes[k]);
        return (2*u*u*u-3*u*u+1)*values[k]+(u*u*u-2*u*u+u)*span*m0+(-2*u*u*u+3*u*u)*values[k+1]+(u*u*u-u*u)*span*m1;
    }
    public void SampleOrbit(float time,float progress)
    {
        Sample(time,Action.Float,0);
        float p=Mathf.Clamp01(progress),flight=Ease(p/.16f)*Ease((1-p)/.18f);
        float turn=Ease((p-.40f)/.20f);
        float sway=p*Mathf.PI*2;
        float bank=flight*(Mathf.Lerp(-5,5,turn)+Mathf.Sin(sway)*2);
        // Mostly upright, with a small forward drift instead of a rigid horizontal pose.
        visual.localRotation=Quaternion.Slerp(visual.localRotation,Quaternion.Euler(12+Mathf.Sin(sway-.3f)*2,0,bank),flight);
        visual.localPosition+=new Vector3(Mathf.Sin(sway)*.008f,Mathf.Sin(sway-.4f)*.018f,0)*flight;
        Rotate("UpperBody",new Vector3(-1.5f*flight,Mathf.Sin(sway-.2f)*2*flight,Mathf.Sin(sway)*1.5f*flight));
        Rotate("UpperBody2",new Vector3(Mathf.Sin(sway-.4f)*1.8f*flight,Mathf.Sin(sway-.5f)*2*flight,-bank*.18f));
        Rotate("LowerBody",new Vector3(2*flight,Mathf.Sin(sway-.7f)*2*flight,bank*.2f));
        // Eyes and head lead the turn, with the shoulders following softly.
        Rotate("Neck",new Vector3(-2*flight,-Mathf.Sin(Mathf.PI*turn)*4,bank*.12f));
        Rotate("Head",new Vector3(-3*flight,-Mathf.Sin(Mathf.PI*turn)*7,Mathf.Sin(sway-.6f)*2*flight));
        for(int side=0;side<2;side++)
        {
            string name=side==0?"L":"R";float sign=side==0?-1:1;
            float a=sway+side*Mathf.PI;
            float upperBeat=Mathf.Sin(a),foreBeat=Mathf.Sin(a-.45f),handBeat=Mathf.Sin(a-.85f);
            Rotate("Shoulder_"+name,new Vector3(upperBeat*1.5f*flight,foreBeat*2*flight,sign*upperBeat*2*flight));
            AimBody("Arm_"+name,"Elbow_"+name,new Vector3(sign*(.66f+.02f*upperBeat),-.75f,.17f+.05f*upperBeat),flight);
            AimBody("Elbow_"+name,"Wrist_"+name,new Vector3(sign*(.46f+.02f*foreBeat),-.70f+.05f*foreBeat,.62f+.16f*foreBeat),flight);
            Rotate("Wrist_"+name,new Vector3(4*handBeat*flight,sign*2*foreBeat*flight,sign*3*handBeat*flight));
            RelaxPalm(name,time);
            // An asymmetric, suspended resting pose; tiny changes in the knees never become running kicks.
            float hip=(8+Mathf.Sin(a-.25f)*4)*Mathf.Deg2Rad;
            float knee=((side==0?46:35)+Mathf.Sin(a-.75f)*7+Mathf.Sin(Mathf.PI*turn)*4)*Mathf.Deg2Rad;
            foreach(string suffix in new[]{"","D"})
            {
                AimBody("Leg"+suffix+"_"+name,"Knee"+suffix+"_"+name,new Vector3(sign*.055f,-Mathf.Cos(hip),Mathf.Sin(hip)),flight);
                AimBody("Knee"+suffix+"_"+name,"Ankle"+suffix+"_"+name,new Vector3(sign*.025f,-Mathf.Cos(hip-knee),Mathf.Sin(hip-knee)),flight);
                Rotate("Ankle"+suffix+"_"+name,new Vector3(Mathf.Sin(a-1.2f)*5*flight,Mathf.Sin(a-.9f)*2*flight,sign*2*flight));
            }
        }
        for(int row=0;row<10;row++)for(int col=0;col<6;col++)Rotate("PF_"+row+"_"+col,new Vector3((.6f+Mathf.Sin(sway*1.5f-row*.35f-col*.12f)*.65f)*flight,0,Mathf.Sin(sway-row*.22f)*.4f*flight));
        foreach(string side in new[]{"L","R"})for(int i=1;i<=2;i++)
            Rotate("+HairS "+side+" A0"+i,new Vector3((1.8f+Mathf.Sin(sway-i*.4f)*1.8f)*flight,
                -Mathf.Sin(Mathf.PI*turn)*3*flight,Mathf.Sin(sway-i*.5f)*1.5f*flight)*(i==1?.4f:1));
    }
    void AimBody(string bone,string child,Vector3 direction,float weight)
    {
        Aim(bone,child,transform.InverseTransformDirection(visual.TransformDirection(direction)),weight);
    }
    public void ApplyFollowFlight(float time,float weight,float bank,float brake,Vector3? variation=null,bool preserveImportedArms=false)
    {
        weight=Mathf.Clamp01(weight);if(weight<.001f)return;
        float beat=time*(2*Mathf.PI/2.25f),cycle=Mathf.Repeat(time,10.8f);
        float paddle=Envelope(cycle-2.4f,4.2f,.65f),look=Envelope(cycle-7.1f,3.3f,.65f);
        if(variation.HasValue){paddle=variation.Value.x;look=variation.Value.y;}
        float lift=Mathf.Sin(beat-.4f);
        visual.localRotation=Quaternion.Slerp(visual.localRotation,Quaternion.Euler(18+lift*3+paddle*3-brake*8,
            look*Mathf.Sin(time*1.4f)*5,bank*9+Mathf.Sin(beat*.5f)*4),weight);
        visual.localPosition=Vector3.Lerp(visual.localPosition,visualPosition+new Vector3(Mathf.Sin(beat*.5f)*.018f,lift*.055f,Mathf.Sin(beat-.8f)*.018f),weight);
        Rotate("UpperBody",new Vector3(-3*weight,Mathf.Sin(beat*.5f)*3*weight,0));
        Rotate("UpperBody2",new Vector3(-3*weight,bank*3*weight,Mathf.Sin(beat-.5f)*2*weight));
        Rotate("Head",new Vector3((-5+Mathf.Sin(beat-.7f)*2)*weight,(look*Mathf.Sin(time*1.4f-.3f)*18-bank*6)*weight,Mathf.Sin(beat*.5f-.4f)*3*weight));
        for(int side=0;side<2;side++)
        {
            string name=side==0?"L":"R";float sign=side==0?-1:1;
            float t=beat+side*.85f,stroke=Mathf.Sin(t-.35f),fore=Mathf.Sin(t-.85f);
            // Airborne balance: lift and sweep the arms, then let the elbows and hands trail.
            if(!preserveImportedArms){
            Rotate("Shoulder_"+name,new Vector3(stroke*3*weight,0,sign*stroke*3*weight));
            AimBody("Arm_"+name,"Elbow_"+name,new Vector3(sign*(.82f+.14f*stroke),-.46f+.18f*stroke+paddle*.12f,-.24f+.17f*stroke),weight);
            AimBody("Elbow_"+name,"Wrist_"+name,new Vector3(sign*(.48f+.14f*fore),-.62f+.17f*fore,.33f+.20f*fore+paddle*.12f),weight);
            RelaxPalm(name,time);}
            // Both legs float behind the body, with a small stagger rather than a walking cadence.
            float hip=(18+Mathf.Sin(t-.5f)*9+brake*8)*Mathf.Deg2Rad;
            float knee=((side==0?66:54)+Mathf.Sin(t-1)*14+paddle*8+brake*8)*Mathf.Deg2Rad;
            foreach(string suffix in new[]{"","D"})
            {
                AimBody("Leg"+suffix+"_"+name,"Knee"+suffix+"_"+name,new Vector3(sign*(.10f+.025f*fore),-Mathf.Cos(hip),Mathf.Sin(hip)),weight);
                AimBody("Knee"+suffix+"_"+name,"Ankle"+suffix+"_"+name,new Vector3(sign*.04f,-Mathf.Cos(hip-knee),Mathf.Sin(hip-knee)),weight);
                Rotate("Ankle"+suffix+"_"+name,new Vector3((5+Mathf.Sin(t-1.4f)*9)*weight,Mathf.Sin(t-1)*3*weight,sign*3*weight));
            }
        }
        foreach(var skin in skins)
        for(int i=0;i<skin.sharedMesh.blendShapeCount;i++)
        {
            string n=skin.sharedMesh.GetBlendShapeName(i);n=n.Substring(n.LastIndexOf('.')+1);
            float cheer=variation.HasValue?variation.Value.z:Envelope(cycle-3,2.8f,.55f),curious=variation.HasValue?variation.Value.y:Envelope(cycle-7.5f,2.2f,.5f);
            if(n=="口角上げ")skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),22+cheer*18,weight));
            if(n=="にっこり")skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),cheer*25,weight));
            if(n=="にこり"){
                float closure=0;for(int k=0;k<skin.sharedMesh.blendShapeCount;k++){string name=skin.sharedMesh.GetBlendShapeName(k);if(name.EndsWith("ウィンク")||name.EndsWith("ウィンク右"))closure=Mathf.Max(closure,skin.GetBlendShapeWeight(k));}
                skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),(6+cheer*22)*(1-Mathf.Clamp01(closure/50)),weight));
            }
            if(n=="上")skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),cheer*15+curious*12,weight));
            if(n=="お")skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),curious*16,weight));
            if(n=="照れ")skin.SetBlendShapeWeight(i,Mathf.Lerp(skin.GetBlendShapeWeight(i),cheer*16,weight));
        }
    }
    Quaternion[] previousPose;Vector3[] previousPositions;float[][] previousFaces;
    bool poseReady;
    Quaternion[] transitionPose;Vector3[] transitionPositions;float[][] transitionFaces;float transitionTime,transitionDuration;
    public void BeginActionTransition(float duration)
    {
        if(joints==null||skins==null)return;
        if(transitionPose==null||transitionPose.Length!=joints.Length){transitionPose=new Quaternion[joints.Length];transitionPositions=new Vector3[joints.Length];transitionFaces=new float[skins.Length][];for(int k=0;k<skins.Length;k++)transitionFaces[k]=new float[skins[k].sharedMesh.blendShapeCount];}
        for(int i=0;i<joints.Length;i++){transitionPose[i]=joints[i].localRotation;transitionPositions[i]=joints[i].localPosition;}
        for(int k=0;k<skins.Length;k++)for(int i=0;i<transitionFaces[k].Length;i++)transitionFaces[k][i]=skins[k].GetBlendShapeWeight(i);
        transitionTime=0;transitionDuration=Mathf.Max(.01f,duration);
    }
    public void SmoothCompanionPose(float dt)
    {
        if(joints==null||dt<=0)return;
        if(previousPose==null||previousPose.Length!=joints.Length)
        {previousPose=new Quaternion[joints.Length];previousPositions=new Vector3[joints.Length];previousFaces=new float[skins.Length][];for(int k=0;k<skins.Length;k++)previousFaces[k]=new float[skins[k].sharedMesh.blendShapeCount];poseReady=false;}
        if(transitionTime<transitionDuration&&transitionPose!=null){transitionTime=Mathf.Min(transitionDuration,transitionTime+dt);float weight=PaimonCompanion.GestureEase(transitionTime/transitionDuration);
         for(int i=0;i<joints.Length;i++){joints[i].localRotation=Quaternion.Slerp(transitionPose[i],joints[i].localRotation,weight);joints[i].localPosition=Vector3.Lerp(transitionPositions[i],joints[i].localPosition,weight);}
         for(int k=0;k<skins.Length;k++)for(int i=0;i<transitionFaces[k].Length;i++)skins[k].SetBlendShapeWeight(i,Mathf.Lerp(transitionFaces[k][i],skins[k].GetBlendShapeWeight(i),weight));
        }
        float blend=1-Mathf.Exp(-dt/.16f);
        for(int i=0;i<joints.Length;i++)
        {
            if(poseReady){var goal=Quaternion.Slerp(previousPose[i],joints[i].localRotation,blend);joints[i].localRotation=Quaternion.RotateTowards(previousPose[i],goal,210*dt);joints[i].localPosition=Vector3.Lerp(previousPositions[i],joints[i].localPosition,blend);}
            previousPose[i]=joints[i].localRotation;previousPositions[i]=joints[i].localPosition;
        }
        for(int k=0;k<skins.Length;k++)for(int i=0;i<previousFaces[k].Length;i++)
        {float value=skins[k].GetBlendShapeWeight(i);if(poseReady)value=Mathf.Lerp(previousFaces[k][i],value,1-Mathf.Exp(-dt/.12f));skins[k].SetBlendShapeWeight(i,value);previousFaces[k][i]=value;}
        poseReady=true;
    }
    // Keep the flight trajectory and lower body, and layer the independent gesture above them.
    public void ApplyIndependentAction(float time,Action action,float actionTime,float weight)
    {
        if(action==Action.Float||weight<=.001f||joints==null)return;
        if(blendRotations==null||blendRotations.Length!=joints.Length)
        {blendRotations=new Quaternion[joints.Length];blendPositions=new Vector3[joints.Length];blendScales=new Vector3[joints.Length];blendFaces=new float[skins.Length][];for(int k=0;k<skins.Length;k++)blendFaces[k]=new float[skins[k].sharedMesh.blendShapeCount];}
        for(int i=0;i<joints.Length;i++){blendRotations[i]=joints[i].localRotation;blendPositions[i]=joints[i].localPosition;blendScales[i]=joints[i].localScale;}
        for(int k=0;k<skins.Length;k++)for(int i=0;i<blendFaces[k].Length;i++)blendFaces[k][i]=skins[k].GetBlendShapeWeight(i);
        var rootPosition=visual.localPosition;var rootRotation=visual.localRotation;var rootScale=visual.localScale;
        var nativeClip=GestureClip(action);bool native=nativeClip;
        if(native){nativeClip.SampleAnimation(visual.gameObject,Mathf.Clamp(actionTime-(action==Action.JaizyOi?.2f:0),0,nativeClip.length));if(action==Action.JaizyOi)NativeOiSamples++;else NativeGestureSamples++;}
        else Sample(time,action,actionTime);
        for(int i=0;i<joints.Length;i++)
        {
            string n=joints[i].name;
            bool secondary=n.StartsWith("+HairS ")||n=="+EarB CF AF01";
            bool upper=native&&!secondary||n=="Head"||n=="Neck"||n.StartsWith("UpperBody")||n.StartsWith("Shoulder_")||n.StartsWith("Arm_")||n.StartsWith("Elbow_")||n.StartsWith("Wrist_")||n.Contains("Finger")||n.StartsWith("Thumb");
            joints[i].localRotation=upper?Quaternion.Slerp(blendRotations[i],joints[i].localRotation,Mathf.Clamp01(weight)):blendRotations[i];
            joints[i].localPosition=native&&!secondary?Vector3.Lerp(blendPositions[i],joints[i].localPosition,weight):blendPositions[i];joints[i].localScale=blendScales[i];
        }
        visual.localPosition=rootPosition;visual.localRotation=rootRotation;visual.localScale=rootScale;
        for(int k=0;k<skins.Length;k++)for(int i=0;i<blendFaces[k].Length;i++)skins[k].SetBlendShapeWeight(i,Mathf.Lerp(blendFaces[k][i],skins[k].GetBlendShapeWeight(i),weight));
    }
    public void SampleBlended(float time,Action action,float actionTime,float flyWeight,float weight)
    {
        weight=Mathf.Clamp01(weight);
        if(action==Action.Float||weight<=.001f){Sample(time,Action.Float,0,flyWeight);return;}
        if(weight>=.999f){Sample(time,action,actionTime,flyWeight);return;}
        Sample(time,Action.Float,0,flyWeight);
        if(blendRotations==null||blendRotations.Length!=joints.Length)
        {blendRotations=new Quaternion[joints.Length];blendPositions=new Vector3[joints.Length];blendScales=new Vector3[joints.Length];blendFaces=new float[skins.Length][];for(int k=0;k<skins.Length;k++)blendFaces[k]=new float[skins[k].sharedMesh.blendShapeCount];}
        for(int i=0;i<joints.Length;i++){blendRotations[i]=joints[i].localRotation;blendPositions[i]=joints[i].localPosition;blendScales[i]=joints[i].localScale;}
        for(int k=0;k<skins.Length;k++)for(int i=0;i<blendFaces[k].Length;i++)blendFaces[k][i]=skins[k].GetBlendShapeWeight(i);
        Sample(time,action,actionTime,flyWeight);
        for(int i=0;i<joints.Length;i++){joints[i].localRotation=Quaternion.Slerp(blendRotations[i],joints[i].localRotation,weight);joints[i].localPosition=Vector3.Lerp(blendPositions[i],joints[i].localPosition,weight);joints[i].localScale=Vector3.Lerp(blendScales[i],joints[i].localScale,weight);}
        for(int k=0;k<skins.Length;k++)for(int i=0;i<blendFaces[k].Length;i++)skins[k].SetBlendShapeWeight(i,Mathf.Lerp(blendFaces[k][i],skins[k].GetBlendShapeWeight(i),weight));
    }
    public void Sample(float time,Action action,float actionTime,float flyWeight=0)
    {
        if(action==Action.Orbit){SampleOrbit(time,actionTime/Duration(Action.Orbit));return;}
        if(map.Count==0)Cache();if(joints==null)return;
        for(int i=0;i<joints.Length;i++)if(joints[i]){joints[i].localRotation=rotations[i];if(positions!=null&&positions.Length==joints.Length)joints[i].localPosition=positions[i];}
        float airplane=action==Action.Airplane?Envelope(actionTime,4.6f,.7f):0;
        float celebrate=action==Action.Celebrate?Envelope(actionTime,4.6f,.7f):0;
        float sway=action==Action.Sway?Envelope(actionTime,4.6f,.7f):0;
        float lookBack=action==Action.LookBack?Envelope(actionTime,4.6f,.7f):0;
        float twirl=action==Action.Twirl?Envelope(actionTime,6.2f,.9f):0;
        float turnBeat=Mathf.Sin((actionTime-.7f)/4.6f*Mathf.PI*2);
        float gestureBeat=Mathf.Sin(actionTime*3.5f);
        float breath=Mathf.Sin(time*2*Mathf.PI/3f),slow=Mathf.Sin(time*2*Mathf.PI/6f);
        float wave=action==Action.Wave?Envelope(actionTime,4,.65f):0;
        float curious=action==Action.Curious?Envelope(actionTime,6,1):0;
        float happy=action==Action.Happy?Envelope(actionTime,4,.7f):0;
        float sleepy=action==Action.Drowsy?Envelope(actionTime,6,.9f):action==Action.Sleepy?Envelope(actionTime,7.2f,1):0;
        float nod=sleepy*(.45f+.55f*Mathf.Sin(actionTime*2.5f-.8f));
        float startle=action==Action.Sleepy?Envelope(actionTime-4.5f,1.1f,.24f):0;
        float crown=action==Action.Crown?Envelope(actionTime,7.2f,1):0;
        float crownLift=action==Action.Crown?Envelope(actionTime-1.1f,4.6f,.9f):0;
        float stretch=action==Action.Drowsy?Envelope(actionTime-2.6f,2.4f,.7f):0;
        float look=curious*Mathf.Sin(actionTime*1.65f-.5f);
        float fly=action==Action.Fly?1:Mathf.Clamp01(flyWeight);
        float flutter=Mathf.Sin(time*3.8f),cheer=Mathf.Sin(actionTime*7);
        visual.localPosition=visualPosition+new Vector3(.015f*slow,.055f*breath+happy*.035f*Mathf.Sin(actionTime*7),0);
        float appear=action==Action.Summon?Ease(actionTime/.85f):action==Action.Recall?1-Ease((actionTime-.5f)/1.2f):1;
        visual.localScale=visualScale*appear;
        if(action==Action.Summon||action==Action.Recall)visual.localPosition+=new Vector3(0,(1-appear)*.12f,-(1-appear)*.16f);
        visual.localPosition+=Vector3.down*(sleepy*.035f+nod*.025f)+Vector3.up*startle*.05f;
        visual.localRotation=Quaternion.Euler(3*breath+fly*23+sleepy*6-startle*9,2*slow+look*7,2.5f*Mathf.Sin(time*1.3f)+curious*3+happy*cheer*4+sleepy*Mathf.Sin(actionTime*1.8f)*5);
        // Torso leads; head, wrists and feet respond with a slight phase delay.
        Rotate("UpperBody",new Vector3(1.5f*breath,slow*2,slow));
        Rotate("UpperBody2",new Vector3(-2*breath-curious*4,look*-11,curious*4+happy*cheer*2));
        Rotate("Neck",new Vector3(Mathf.Sin(time*1.6f-.25f)*1.5f,slow*2,-curious*4));
        Rotate("Head",new Vector3(Mathf.Sin(time*1.2f-.4f)*2-curious*7,look*28+slow*4,-curious*5+Mathf.Sin(time*1.1f)*2));
        Rotate("Head",new Vector3(nod*19-startle*24,0,sleepy*7-crown*8));
        Rotate("UpperBody2",new Vector3(sleepy*5-startle*8,0,0));
        Rotate("UpperBody2",new Vector3(-celebrate*8,lookBack*-18,sway*gestureBeat*12+airplane*Mathf.Sin(actionTime*2)*7));
        Rotate("Head",new Vector3(-celebrate*9,lookBack*52,sway*-gestureBeat*10+airplane*Mathf.Sin(actionTime*2-.4f)*5));
        Rotate("UpperBody2",new Vector3(twirl*-3,twirl*turnBeat*-9,twirl*turnBeat*6));
        Rotate("Head",new Vector3(twirl*-3,twirl*Mathf.Sin((actionTime-.4f)/4.6f*Mathf.PI*2)*16,twirl*turnBeat*-4));
        Rotate("LowerBody",new Vector3(0,Mathf.Sin(time*1.4f-.5f)*3,Mathf.Sin(time*1.4f)*4));
        for(int side=0;side<2;side++)
        {
            string s=side==0?"L":"R";float sign=side==0?-1:1;
            float limbPhase=time*(2*Mathf.PI/6f)+side*Mathf.PI;
            float lag=Mathf.Sin(limbPhase-.65f);
            float shoulderSwing=Mathf.Sin(limbPhase);
            float elbowSwing=Mathf.Sin(limbPhase-.45f);
            float wristSwing=Mathf.Sin(limbPhase-.9f);
            Rotate("Shoulder_"+s,new Vector3(shoulderSwing*3,shoulderSwing*3,sign*(2+shoulderSwing*2)));
            Vector3 arm=new Vector3(sign*(.66f+.02f*shoulderSwing),-.75f,.17f+.05f*shoulderSwing);
            Vector3 fore=new Vector3(sign*(.46f+.02f*elbowSwing),-.70f+.05f*elbowSwing,.62f+.16f*elbowSwing);
            arm=Vector3.Lerp(arm,new Vector3(sign*(.66f+.02f*shoulderSwing),-.75f,.12f+shoulderSwing*.05f),fly);
            fore=Vector3.Lerp(fore,new Vector3(sign*.46f,-.70f+.05f*elbowSwing,.59f+elbowSwing*.16f),fly);
            float w=side==1?wave:0;
            arm=Vector3.Lerp(arm,new Vector3(sign*(.60f+.08f*Mathf.Sin(actionTime*6)),.05f+.10f*Mathf.Sin(actionTime*6-.3f),.15f),w);
            fore=Vector3.Lerp(fore,new Vector3(sign*(.10f+.28f*Mathf.Sin(actionTime*6-.45f)),.85f,.24f+.08f*Mathf.Sin(actionTime*6-.7f)),w);
            arm=Vector3.Lerp(arm,new Vector3(sign*.7f,.25f,.15f),happy);
            fore=Vector3.Lerp(fore,new Vector3(sign*.45f,.6f,.2f),happy);
            if(side==0){arm=Vector3.Lerp(arm,new Vector3(-.3f,-.3f,.4f),curious);fore=Vector3.Lerp(fore,new Vector3(-.24f,-.25f,.3f),curious);}
            if(sleepy>0){arm=Vector3.Lerp(arm,new Vector3(sign*.25f,-.85f,.03f+lag*.06f),sleepy);fore=Vector3.Lerp(fore,new Vector3(sign*.1f,-.65f,.32f+wristSwing*.08f),sleepy);}
            if(stretch>0){arm=Vector3.Lerp(arm,new Vector3(sign*.8f,.4f,.05f),stretch);fore=Vector3.Lerp(fore,new Vector3(sign*.12f,.8f,.25f),stretch);}
            if(startle>0){arm=Vector3.Lerp(arm,new Vector3(sign*.8f,-.15f,.1f),startle);fore=Vector3.Lerp(fore,new Vector3(sign*.35f,-.5f,.4f),startle);}
            if(side==1){arm=Vector3.Lerp(arm,new Vector3(.58f,.55f,.06f),crown);fore=Vector3.Lerp(fore,new Vector3(-.35f,.9f,.06f),crown);}
            // Distinct silhouettes: outstretched glide, two raised arms, alternating sway, looking back.
            arm=Vector3.Lerp(arm,new Vector3(sign*.85f,-.38f+Mathf.Sin(actionTime*2+side)*.08f,-.12f),airplane);
            fore=Vector3.Lerp(fore,new Vector3(sign*.6f,-.55f,.18f),airplane);
            arm=Vector3.Lerp(arm,new Vector3(sign*.72f,.55f+gestureBeat*.12f,.10f),celebrate);
            fore=Vector3.Lerp(fore,new Vector3(sign*.18f,.9f,.25f),celebrate);
            arm=Vector3.Lerp(arm,new Vector3(sign*.7f,-.60f+sign*gestureBeat*.12f,.18f),sway);
            fore=Vector3.Lerp(fore,new Vector3(sign*.4f,-.45f-sign*gestureBeat*.10f,.35f),sway);
            arm=Vector3.Lerp(arm,new Vector3(sign*.65f,side==0?-.45f:-.65f,.25f),lookBack);
            fore=Vector3.Lerp(fore,new Vector3(sign*.35f,side==0?-.25f:-.55f,.35f),lookBack);
            arm=Vector3.Lerp(arm,new Vector3(sign*(.65f+turnBeat*.08f),-.65f,-.12f+sign*turnBeat*.14f),twirl);
            fore=Vector3.Lerp(fore,new Vector3(sign*.38f,-.60f,.25f+sign*turnBeat*.12f),twirl);
            AimBody("Arm_"+s,"Elbow_"+s,arm,1);
            AimBody("Elbow_"+s,"Wrist_"+s,fore,1);
            Rotate("Wrist_"+s,new Vector3(8*wristSwing,w*15*Mathf.Sin(actionTime*6-.9f),sign*(4+4*wristSwing+w*18*Mathf.Sin(actionTime*6-.9f))));
            // Relaxed flight pose: the hip leads, knee follows, then the loose ankle.
            // Opposite phases make one leg tuck while the other gently releases.
            float hip=(12+10*Mathf.Sin(limbPhase-.2f)+fly*13+happy*5)*Mathf.Deg2Rad;
            float knee=(43+15*Mathf.Sin(limbPhase-.75f)+fly*20+sleepy*5)*Mathf.Deg2Rad;
            float spread=.08f+.035f*Mathf.Sin(limbPhase-.35f);
            foreach(string suffix in new[]{"", "D"})
            {
                Aim("Leg"+suffix+"_"+s,"Knee"+suffix+"_"+s,new Vector3(sign*spread,-Mathf.Cos(hip),Mathf.Sin(hip)));
                Aim("Knee"+suffix+"_"+s,"Ankle"+suffix+"_"+s,new Vector3(sign*.04f,-Mathf.Cos(hip-knee),Mathf.Sin(hip-knee)));
                Rotate("Ankle"+suffix+"_"+s,new Vector3(13+Mathf.Sin(limbPhase-1.25f)*9,Mathf.Sin(limbPhase-1)*4,sign*(5+Mathf.Sin(limbPhase-1.1f)*4)));
            }
            RelaxPalm(s,time);
            RelaxHand(s,time,1-wave*.65f);
        }
        // Subtle secondary motion down the cape chains, no rigid full-body bobbing.
        for(int row=0;row<10;row++)for(int col=0;col<6;col++)
            Rotate("PF_"+row+"_"+col,new Vector3(Mathf.Sin(time*2-row*.3f-col*.35f)*(1.2f+row*.18f)+fly*2,0,Mathf.Sin(time*1.4f-row*.22f)*.7f));
        HairFloat(time,fly);
        var halo=Bone("+EarB CF AF01");
        if(halo)
        {
            Vector3 drift=new Vector3(Mathf.Sin(time*1.45f-.6f)*.006f,
                .015f+Mathf.Sin(time*1.9f-.8f)*.012f,Mathf.Sin(time*1.3f-1)*.004f);
            halo.position+=visual.TransformDirection(drift);
            halo.rotation=Quaternion.Slerp(halo.rotation,visual.rotation*haloRestRotation,.7f)
                *Quaternion.Euler(Mathf.Sin(time*1.65f-.8f)*3,Mathf.Sin(time*.85f)*6,Mathf.Sin(time*1.35f-.5f)*4);
        }
        if(halo && crown>0)
        {
            Vector3 home=halo.position;
            halo.position=Vector3.Lerp(home,transform.TransformPoint(new Vector3(.13f,.14f,.18f)),crownLift);
            halo.rotation=Quaternion.AngleAxis(crownLift*32,transform.right)*halo.rotation;
            Reach("R",halo.position+transform.right*.055f-transform.up*.025f,crown);
        }
        if(skins!=null)foreach(var skin in skins)
        {
            var mesh=skin.sharedMesh;if(!mesh)continue;
            float blink=BlinkWeight(time);
            for(int i=0;i<mesh.blendShapeCount;i++)
            {
                string n=mesh.GetBlendShapeName(i).ToLowerInvariant();
                n=n.Substring(n.LastIndexOf('.')+1);
                if(n.Contains("まばたき")||n.Contains("blink")||n=="ウィンク"||n=="ウィンク右")skin.SetBlendShapeWeight(i,Mathf.Max(blink,sleepy*(1-startle)) * 100);
                if(n=="にこり")skin.SetBlendShapeWeight(i,(wave*.35f+happy*.7f+celebrate*.65f+sway*.35f)*100);
                if(n=="お")skin.SetBlendShapeWeight(i,startle*45+lookBack*22);
                if(n=="口角上げ")skin.SetBlendShapeWeight(i,(airplane*.45f+celebrate*.55f+sway*.4f)*100);
                if(n=="にっこり")skin.SetBlendShapeWeight(i,celebrate*35);
                if(n=="上")skin.SetBlendShapeWeight(i,(lookBack*.25f+celebrate*.2f)*100);
                if(n=="照れ")skin.SetBlendShapeWeight(i,sway*20);
            }
        }
    }
}
