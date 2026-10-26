using UnityEngine;

// Observes real body movement. Explicit jump/drop/ledge cues are emitted only
// after the controller accepts the action. Never writes physics or input.
[DefaultExecutionOrder(180), DisallowMultipleComponent]
public sealed class BattleMotionPresentation:MonoBehaviour
{
    public int CueCount {get;private set;}
    public string LastCue {get;private set;}
    static readonly Color Cream=new Color32(247,236,205,255),Mint=new Color32(132,205,196,255),Ink=new Color32(19,55,49,255);
    Rigidbody body;CapsuleCollider shape;BattlePlayerController player;BattleAttack attack;
    BattleSpecialMoves special;BattleDamageReceiver receiver;BattleManager manager;BattleHumanoidRig rig;
    Material material;BattleJPresentation.InkMesh mesh;
    readonly Cue[] cues=new Cue[8];readonly RaycastHit[] groundHits=new RaycastHit[12];
    bool ready,grounded;Vector3 previousVelocity,previousPosition;float stepTimer,landingPose,brakePose,trailTime,trailTimer;
    int cursor;float direction=1;
    struct Cue {public Vector3 position;public float age,life,power,facing;public int kind;public bool live;}
    public int ActiveCount {get {int n=0;foreach(var c in cues)if(c.live)n++;return n;}}
    void Awake()
    {
        body=GetComponent<Rigidbody>();shape=GetComponent<CapsuleCollider>();player=GetComponent<BattlePlayerController>();
        attack=GetComponent<BattleAttack>();special=GetComponent<BattleSpecialMoves>();receiver=GetComponent<BattleDamageReceiver>();
        manager=FindFirstObjectByType<BattleManager>();rig=BattleHumanoidRig.Create(GetComponentInChildren<Animator>());
        material=new Material(Resources.Load<Shader>("VFX/BattleJInk")){hideFlags=HideFlags.DontSave,name="Motion ink"};
        mesh=new BattleJPresentation.InkMesh("Movement cue pool",transform,material);
        if(special && !GetComponent<BattleAfterimages>())gameObject.AddComponent<BattleAfterimages>();
    }
    public void Jumped(bool air)
    {Emit(air?2:0,transform.position+Vector3.down*.92f,air?1.20f:.85f,direction,air?"double jump":"jump");if(air)GetComponent<BattleAfterimages>()?.DoubleJump();}
    public void Dropped(Vector3 contact)=>Emit(3,contact,.90f,direction,"drop through");
    public void Grabbed(Vector3 contact)=>Emit(4,contact,.27f,direction,"ledge grab");
    public void Launched(float strength){if(strength>=8)trailTime=Mathf.Lerp(.25f,.6f,Mathf.InverseLerp(8,34,strength));}
    public void Emit(int kind,Vector3 point,float power,float facing,string label)
    {
        point.z=-.90f;
        cues[cursor++%cues.Length]=new Cue{position=point,power=power,facing=facing,life=kind==4?.15f:kind==5?.48f:kind==2?.48f:kind==3?.55f:.42f,kind=kind,live=true};
        LastCue=label;CueCount++;
    }
    bool Grounded()
    {
        if(player)return player.IsOnGround;
        if(!shape)return false;
        var origin=new Vector3(shape.bounds.center.x,shape.bounds.min.y+.13f,shape.bounds.center.z);
        int count=Physics.RaycastNonAlloc(origin,Vector3.down,groundHits,.24f,1,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)if(!groundHits[i].collider.transform.IsChildOf(transform) && groundHits[i].normal.y>.5f)return true;
        return false;
    }
    void Update()
    {
        rig?.RestorePose();
        if(Time.timeScale<=0)return;
        float dt=Time.deltaTime;
        landingPose=Mathf.Max(0,landingPose-dt);brakePose=Mathf.Max(0,brakePose-dt);
        for(int i=0;i<cues.Length;i++)if(cues[i].live){cues[i].age+=dt;if(cues[i].age>=cues[i].life)cues[i].live=false;}
        if(!body || !manager || !manager.IsBattleActive || manager.IsGameOver || body.isKinematic)
        {ready=false;trailTime=0;return;}
        var velocity=body.linearVelocity;bool onGround=Grounded();
        if(Mathf.Abs(velocity.x)>.25f)direction=Mathf.Sign(velocity.x);
        bool teleported=(transform.position-previousPosition).sqrMagnitude>16;
        if(!ready || teleported){previousVelocity=velocity;grounded=onGround;previousPosition=transform.position;ready=true;return;}
        bool acting=attack && attack.IsMovementLocked || special && special.IsBusy || receiver && receiver.IsInHitstun || player && (player.IsLedgeHanging||player.IsLedgeClimbing);
        Vector3 feet=transform.position+Vector3.down*.96f;
        if(onGround && !grounded && previousVelocity.y<-.5f)
        {landingPose=.18f;Emit(0,feet,Mathf.Lerp(.65f,1.10f,Mathf.InverseLerp(2,18,-previousVelocity.y)),direction,"land");}
        if(onGround && !acting)
        {
            float speed=Mathf.Abs(velocity.x),before=Mathf.Abs(previousVelocity.x);
            if(speed>.8f && before<.3f)Emit(0,feet,.65f,-direction,"start");
            if(before>1.3f && (speed<.3f || velocity.x*previousVelocity.x<0))
            {brakePose=.14f;Emit(1,feet,.48f,-Mathf.Sign(previousVelocity.x),"brake turn");}
            stepTimer-=dt;
            if(speed>1 && stepTimer<=0){Emit(0,feet,.45f,-direction,"footstep");stepTimer=.24f;}
        }
        else stepTimer=.12f;
        if(trailTime>0)
        {
            trailTime-=dt;trailTimer-=dt;
            if(velocity.magnitude>6 && trailTimer<=0)
            {Emit(5,transform.position-velocity.normalized*.4f,Mathf.Clamp(velocity.magnitude/28,.25f,.65f),-direction,"knockback");trailTimer=.075f;}
            if(velocity.magnitude<3 || onGround && Mathf.Abs(velocity.x)<2)trailTime=0;
        }
        previousPosition=transform.position;previousVelocity=velocity;grounded=onGround;
    }
    void LateUpdate()
    {
        if(Time.timeScale<=0)return;
        mesh.Begin(Vector3.zero);
        foreach(var cue in cues)if(cue.live)Draw(cue);
        mesh.End();
        if(!ready || rig==null || body.isKinematic || receiver && receiver.IsInHitstun || attack && attack.IsMovementLocked ||
            special && special.IsBusy || player && (player.IsLedgeHanging||player.IsLedgeClimbing))return;
        rig.CapturePose();
        float facing=player?player.FacingDirection:direction;
        rig.LeanTorso(facing,grounded?(brakePose>0?-6:Mathf.Abs(body.linearVelocity.x)>1?5:0):body.linearVelocity.y<0?4:-3,.8f);
        if(landingPose>0 && grounded)
        {
            // A landing accent must not pin both feet while the body runs.
            // Running keeps only a tiny hip compression; the gait owns the legs.
            float plant = 1-Mathf.InverseLerp(.2f,1.5f,Mathf.Abs(body.linearVelocity.x));
            float weight = Mathf.Lerp(.2f,1,plant);
            rig.ShiftHips(Vector3.down*(.08f*Mathf.Sin(landingPose/.18f*Mathf.PI)*weight));
            if(plant>0)rig.PlantFeet(transform.position+Vector3.right*facing*.35f,plant);
        }
        if(!grounded && body.linearVelocity.y<-.5f)
        {
            Vector3 root=transform.position;
            rig.AimLeg(true,root+new Vector3(.08f*facing,-.77f,-.10f),root+new Vector3(.3f*facing,-.3f,-.1f),.35f);
            rig.AimArm(false,root+new Vector3(.25f*facing,.26f,.18f),root+new Vector3(-.1f*facing,.1f,.3f),.35f);
        }
    }
    void Draw(Cue c)
    {
        float t=c.age/c.life,fade=1-Mathf.SmoothStep(.35f,1,t);
        Vector2 origin=new Vector2(c.position.x,c.position.y);
        if(c.kind==2)
        {
            // Two tapered, layered crescents rather than a thin ellipse.
            for(int ribbon=0;ribbon<2;ribbon++)for(int layer=0;layer<3;layer++)for(int i=0;i<28;i++)
            {
                float u=i/28f,v=(i+1)/28f;
                float a=Mathf.Lerp(3.1f,6.1f,u),b=Mathf.Lerp(3.1f,6.1f,v);
                float r=c.power*(.58f+t*.35f)*(ribbon==0?1:.76f);
                Vector2 p=origin+new Vector2(Mathf.Cos(a)*r,Mathf.Sin(a)*r*.72f+.25f);
                Vector2 q=origin+new Vector2(Mathf.Cos(b)*r,Mathf.Sin(b)*r*.72f+.25f);
                float width=.22f*Mathf.Sin((u+.018f)*Mathf.PI)*(layer==0?1.18f:layer==1?1:.40f);
                mesh.Line(p,q,width,Alpha(layer==0?Ink:layer==1?Mint:Cream,fade*(layer==0?.45f:1)),c.position.z-layer*.008f);
            }
            for(int i=0;i<5;i++)mesh.Diamond(origin+new Vector2((i-2)*.32f,.12f+Mathf.Abs(i-2)*.18f+t*.25f),new Vector2(.045f,.085f)*(1-t*.5f),Alpha(i%2==0?Mint:new Color32(234,190,85,255),fade),c.position.z-.04f);
        }
        else if(c.kind==3)
        {
            float r=c.power*(.65f+t*.65f);
            for(int i=0;i<22;i++)
            {
                float a=i/22f*Mathf.PI*2,b=(i+1)/22f*Mathf.PI*2;
                var p=origin+new Vector2(Mathf.Cos(a)*r,Mathf.Sin(a)*r*.28f);
                var q=origin+new Vector2(Mathf.Cos(b)*r,Mathf.Sin(b)*r*.28f);
                mesh.Line(p,q,.10f,Alpha(Mint,fade),c.position.z);
                mesh.Line(p,q,.035f,Alpha(Cream,fade),c.position.z-.012f);
            }
            for(int i=0;i<3;i++)
            {
                Vector2 p=origin+new Vector2((i-1)*.38f,0);
                mesh.Line(p+Vector2.up*.55f,p+Vector2.down*(.25f+t*.6f),.016f,Alpha(Mint,fade*.75f),c.position.z-.02f);
                mesh.Diamond(p+Vector2.up*.12f,new Vector2(.035f,.07f),Alpha(Cream,fade),c.position.z-.03f);
            }
        }
        else if(c.kind==4)
        {
            mesh.Diamond(origin,new Vector2(.07f,.18f)*(1+t),Alpha(Cream,fade),c.position.z);
            mesh.Diamond(origin,new Vector2(.16f,.04f)*(1+t),Alpha(Mint,fade),c.position.z);
        }
        else
        {
            for(int i=0;i<6;i++)
            {
                float side=i%2==0?-1:1;
                float r=c.power*(.28f+t*.15f)*(i%3==0?1.05f:.7f);
                Vector2 center=origin+new Vector2((c.kind==1?c.facing:side)*(.10f+t*c.power*(.7f+i*.14f)),r*.65f+(i%3)*r*.42f+t*.12f);
                for(int layer=0;layer<3;layer++)for(int j=0;j<14;j++)
                {
                    float a=j*Mathf.PI*2/14,b=(j+1)*Mathf.PI*2/14;
                    float scale=layer==0?1.05f:layer==1?1:.65f;
                    Vector2 extent=new Vector2(r,r*.82f)*scale;
                    Vector2 offset=layer==2?new Vector2(-r*.13f,r*.20f):Vector2.zero;
                    Color shade=c.kind==5?(player?new Color(.64f,.73f,.66f):new Color(.83f,.49f,.46f)):new Color(.78f,.79f,.62f);
                    Color color=Alpha(layer==0?new Color(.38f,.44f,.31f):layer==1?shade:Cream,fade*(c.kind==5?.90f:.95f));
                    mesh.Triangle(center+offset,center+offset+Vector2.Scale(new Vector2(Mathf.Cos(a),Mathf.Sin(a)),extent),
                        center+offset+Vector2.Scale(new Vector2(Mathf.Cos(b),Mathf.Sin(b)),extent),color,c.position.z-layer*.008f);
                }
            }
            if(c.kind!=5)for(int i=0;i<3;i++)mesh.Diamond(origin+new Vector2((i-1)*c.power*.7f,.20f+c.power*.30f+t*.25f),new Vector2(.035f,.065f)*(1-t*.6f),Alpha(new Color32(234,190,85,255),fade),c.position.z-.03f);
        }
    }
    static Color Alpha(Color c,float a){c.a=a;return c;}
    void OnDisable(){rig?.RestorePose();ready=false;trailTime=0;System.Array.Clear(cues,0,cues.Length);mesh?.Hide();}
    void OnDestroy(){rig?.RestorePose();mesh?.Dispose();if(material)Destroy(material);}
}
