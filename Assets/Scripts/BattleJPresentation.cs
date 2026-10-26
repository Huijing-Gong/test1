using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// J-only presentation. One reusable swing mesh and eight reusable contact meshes.
// No colliders, animation events, damage, motion, or gameplay random state.
[DisallowMultipleComponent]
public sealed class BattleJPresentation : MonoBehaviour
{
    public const int ContactCapacity=8;
    static readonly Color Ink=new Color32(19,55,49,255), Cream=new Color32(247,236,205,255),
        Mint=new Color32(132,205,196,255), Gold=new Color32(234,190,85,255);
    BattleAttack attack;
    BattleJMove move;
    int step, nextContact;
    float direction;
    BattleJMoveData data;
    bool swinging;
    Material material;
    InkMesh swing;
    readonly Burst[] contacts=new Burst[ContactCapacity];
    AudioClip air, punch, guard, body;
    readonly AudioSource[] voices=new AudioSource[6];
    readonly bool[] pausedVoice=new bool[6];
    readonly System.Random audioVariation=new System.Random(739391);
    int voiceIndex;
    bool paused;
    public int HitCount {get;private set;}
    public int BlockCount {get;private set;}
    public int ImpactAudioCount {get;private set;}
    public int BodyAudioCount {get;private set;}
    public int GuardAudioCount {get;private set;}
    public Vector3 LastContactPosition {get;private set;}
    public bool LastContactBlocked {get;private set;}
    public bool SwingVisible => swing!=null && swing.renderer.enabled;
    public int ActiveContacts {get {int n=0;foreach(var b in contacts)if(b!=null && b.live)n++;return n;}}

    void Awake()
    {
        attack=GetComponent<BattleAttack>();
        material=new Material(Resources.Load<Shader>("VFX/BattleJInk")){name="Shared J ink",hideFlags=HideFlags.DontSave};
        swing=new InkMesh("J strike ribbon",transform,material);
        for(int i=0;i<contacts.Length;i++) contacts[i]=new Burst{mesh=new InkMesh("J contact pool "+i,transform,material)};
        air=MakeSound(0);punch=MakeSound(1);guard=MakeSound(2);body=MakeSound(3);
        for(int i=0;i<voices.Length;i++)
        {
            voices[i]=gameObject.AddComponent<AudioSource>();
            voices[i].playOnAwake=false;voices[i].spatialBlend=0;voices[i].priority=110;
        }
    }
    public void Begin(BattleJMove kind,int comboStep,float facing,BattleJMoveData timing)
    {
        move=kind;step=comboStep;direction=facing;data=timing;swinging=true;
        swing.Hide();
    }
    public void Swing()
    {
        float variation=Variation();
        if(!SideAAudio.Swing(move,step,variation)) Sound(air,.10f,(move==BattleJMove.Low?.88f:step==2?.92f:1.08f)*variation);
        if(move==BattleJMove.Low)GetComponent<BattleMotionPresentation>()?.Emit(1,transform.position+new Vector3(.65f*direction,-.92f,0),.62f,direction,"low sweep dust");
    }
    public void CancelSwing() {swinging=false;swing?.Hide();}

    public void Contact(BattleDamageReceiver target,Vector3 position,bool blocked)
    {
        if(blocked) BlockCount++; else HitCount++;
        BattleJHitReaction reaction=null;
        if(target && !blocked)
        {
            reaction=target.GetComponent<BattleJHitReaction>();
            if(!reaction)reaction=target.gameObject.AddComponent<BattleJHitReaction>();
            position=reaction.VisualContactPoint(position,move,direction);
        }
        var b=contacts[nextContact++%contacts.Length];
        b.age=0;b.bornFrame=Time.frameCount;b.live=true;b.blocked=blocked;b.move=move;b.step=step;b.direction=direction;
        b.life=blocked?.12f:step==3?.25f:step==2?.16f:move==BattleJMove.Side || move==BattleJMove.Upper?.17f:.13f;
        b.position=position;b.position.z=-.72f;
        LastContactPosition=position;LastContactBlocked=blocked;
        DrawBurst(b);
        float variation=Variation();
        if(blocked)
        {
            // TakeHit has already emitted the native Parry once. This quiet
            // high-frequency accent is not a second hurt/impact cue.
            Sound(guard,SideAAudio.Available?.08f:.20f,1.10f*variation);GuardAudioCount++;
            GetComponent<BattleJAttackPose>()?.GuardRecoil(direction);
            return;
        }
        if(!SideAAudio.MeleeImpact(step,variation)) Sound(punch,step==3?.28f:.17f,(step==3?.78f:1.08f-step*.035f)*variation);
        Sound(body,step==3?.17f:step==2?.105f:.075f,(step==3?.78f:1.07f-step*.045f)*variation);
        ImpactAudioCount++;BodyAudioCount++;
        BattleCameraController.RequestJShake(position,move,step,direction);
        if(reaction)reaction.Play(move,direction,step);
    }
    void Update()
    {
        bool nowPaused=Time.timeScale<=0;
        if(nowPaused!=paused)
        {
            paused=nowPaused;
            for(int i=0;i<voices.Length;i++)
                if(paused){pausedVoice[i]=voices[i].isPlaying;if(pausedVoice[i])voices[i].Pause();}
                else if(pausedVoice[i]){voices[i].UnPause();pausedVoice[i]=false;}
        }
        if(paused)return;
        // Bursts finish through hit-stop, but freeze in the actual pause menu.
        foreach(var b in contacts)
        {
            if(!b.live || b.bornFrame==Time.frameCount)continue;
            // A shader/import hitch must not erase a newly spawned burst before
            // the camera has rendered it. Discard pre-spawn frame time.
            b.age+=Mathf.Min(Time.unscaledDeltaTime,1f/30f);
            if(b.age>=b.life){b.live=false;b.mesh.Hide();}else DrawBurst(b);
        }
    }
    void LateUpdate()
    {
        if(Time.timeScale<=0)return;
        if(!swinging || !attack || !attack.IsJAttack){swing.Hide();return;}
        float t=attack.JElapsed, begin=data.Startup*.72f, end=data.Startup+data.Active;
        if(t<begin || t>end+.065f){swing.Hide();return;}
        float progress=Mathf.Clamp01((t-begin)/Mathf.Max(.02f,data.Startup+.018f-begin));
        float alpha=t<=end?Mathf.Clamp01((t-begin)/.022f):.38f*(1-(t-end)/.065f);
        swing.Begin(transform.position+new Vector3(0,0,-.70f));
        float start=Mathf.Max(0,progress-.90f);
        for(int layer=0;layer<3;layer++)
        {
            Color color=layer==0?Ink:layer==1?Mint:Cream;
            color.a=alpha*(layer==0?.68f:layer==1?.85f:1f);
            float width=(move==BattleJMove.Upper?.17f:move==BattleJMove.Low?.23f:step==2?.16f:.12f)*(layer==0?1.25f:layer==1?1f:.34f);
            for(int i=0;i<18;i++)
            {
                float u=Mathf.Lerp(start,progress,i/18f),v=Mathf.Lerp(start,progress,(i+1)/18f);
                float taper=Mathf.Sin((i+.5f)/18f*Mathf.PI);
                swing.Line(Path(u),Path(v),width*taper,color,-layer*.006f);
            }
        }
        if(move==BattleJMove.Low)
            for(int i=0;i<3;i++)
                swing.Diamond(new Vector2((.25f+i*.24f+progress*.18f)*direction,-.88f+Mathf.Sin(progress*Mathf.PI)*.07f),
                    new Vector2(.09f,.027f)*(1-progress*.4f),WithAlpha(Cream,alpha*.5f),-.025f);
        swing.End();
    }
    Vector2 Path(float t)
    {
        if(move==BattleJMove.Upper) return new Vector2((.32f+Mathf.Sin(t*Mathf.PI)*.43f)*direction,Mathf.Lerp(-.12f,1.3f,t));
        if(move==BattleJMove.Low) return new Vector2(Mathf.Lerp(-.22f,1.35f,t)*direction,-.52f-Mathf.Sin(t*Mathf.PI)*.34f);
        if(move==BattleJMove.Side) return new Vector2(Mathf.Lerp(.25f,1.48f,t)*direction,.13f+Mathf.Sin(t*Mathf.PI)*.12f);
        if(step==2) return new Vector2((.36f+Mathf.Sin(t*Mathf.PI*.72f)*.75f)*direction,.33f-Mathf.Sin(t*Mathf.PI*.8f)*.35f);
        return new Vector2(Mathf.Lerp(.25f,step==1?1.13f:1f,t)*direction,.2f+Mathf.Sin(t*Mathf.PI)* (step==1?-.14f:.12f));
    }
    void DrawBurst(Burst b)
    {
        float t=Mathf.Clamp01(b.age/b.life), fade=1-t*t, size=Mathf.Lerp(.65f,1.15f,Mathf.Sqrt(t));
        float extent=b.blocked?.22f:b.step==3?.49f:b.step==2?.28f:b.move==BattleJMove.Side?.36f:.22f;
        b.mesh.Begin(b.position);
        if(b.blocked)
        {
            // A compact mint shield crescent and three metallic splinters,
            // never the gold successful-hit explosion.
            for(int layer=0;layer<2;layer++) for(int i=0;i<16;i++)
            {
                float a=Mathf.Lerp(-1.2f,1.2f,i/16f), c=Mathf.Lerp(-1.2f,1.2f,(i+1)/16f);
                Vector2 p=new Vector2(-b.direction*Mathf.Cos(a),Mathf.Sin(a))*extent*size;
                Vector2 q=new Vector2(-b.direction*Mathf.Cos(c),Mathf.Sin(c))*extent*size;
                b.mesh.Line(p,q,layer==0?.045f:.014f,WithAlpha(layer==0?Mint:Cream,fade),-layer*.007f);
            }
            for(int i=0;i<3;i++)
            {
                Vector2 axis=new Vector2(-b.direction*(.5f+i*.2f),(i-1)*.8f).normalized;
                b.mesh.Line(axis*(.10f+t*.16f),axis*(.20f+t*.20f),.015f*(1-t),WithAlpha(Cream,fade),-.02f);
            }
            b.mesh.End();return;
        }
        for(int layer=0;layer<3;layer++)
        {
            float scale=extent*size*(layer==0?1.14f:layer==1?1f:.66f);
            Color color=WithAlpha(layer==0?Ink:layer==1?Gold:Cream,fade);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4f, c=(i+1)*Mathf.PI/4f;
                Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(i%2==0?scale:scale*.24f);
                Vector2 q=new Vector2(Mathf.Cos(c),Mathf.Sin(c))*((i+1)%2==0?scale:scale*.24f);
                if(b.move==BattleJMove.Side || b.step==3){p.x*=1.35f;q.x*=1.35f;p.y*=.70f;q.y*=.70f;}
                if(b.move==BattleJMove.Low){p.y*=.65f;q.y*=.65f;}
                b.mesh.Triangle(Vector2.zero,p,q,color,-layer*.007f);
            }
        }
        if(!b.blocked)
        {
            int count=b.step==3?8:b.step==2?5:3;
            for(int i=0;i<count;i++)
            {
                float angle=Mathf.Lerp(-.7f,.85f,i/(float)Mathf.Max(1,count-1));
                Vector2 axis=b.move==BattleJMove.Upper ? new Vector2(Mathf.Sin(angle)*.7f,Mathf.Cos(angle)) :
                    new Vector2(Mathf.Cos(angle)*b.direction,Mathf.Sin(angle));
                if(b.move==BattleJMove.Low)axis.y=Mathf.Abs(axis.y)*.25f;
                Vector2 p=axis*(extent*.7f+t*.62f);
                b.mesh.Diamond(p,new Vector2(.033f,.065f)*(1-t),WithAlpha(i==0?Cream:Gold,fade),-.025f);
            }
        }
        b.mesh.End();
    }
    static Color WithAlpha(Color c,float alpha){c.a=alpha;return c;}
    float Variation() => .965f+(float)audioVariation.NextDouble()*.07f;
    void Sound(AudioClip clip,float volume,float pitch)
    {
        if(Time.timeScale<=0 || !isActiveAndEnabled)return;
        var v=voices[voiceIndex++%voices.Length];v.Stop();v.clip=clip;v.pitch=pitch;v.volume=volume;v.Play();
    }
    static AudioClip MakeSound(int kind)
    {
        const int rate=22050;
        float duration=kind==0?.12f:kind==1?.11f:kind==3?.15f:.09f;
        var samples=new float[Mathf.CeilToInt(rate*duration)];uint seed=71237;float filtered=0,phase=0;
        for(int i=0;i<samples.Length;i++)
        {
            float t=i/(float)rate,u=t/duration;
            seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;
            float noise=(seed&65535)/32767.5f-1;
            filtered=Mathf.Lerp(filtered,noise,kind==0?.16f:kind==3?.08f:.46f);
            phase+=Mathf.Lerp(kind==2?1500:kind==3?105:260,kind==2?800:kind==3?42:100,u)*Mathf.PI*2/rate;
            float envelope=kind==0?Mathf.Sin(u*Mathf.PI)*Mathf.Pow(1-u,.7f):Mathf.Min(1,t/.002f)*Mathf.Exp(-u*6);
            samples[i]=(kind==0?filtered:kind==3?filtered*.10f+Mathf.Sin(phase)*.65f+Mathf.Sin(phase*2)*.12f:filtered*.6f+Mathf.Sin(phase)*.32f)*envelope*.75f;
        }
        var clip=AudioClip.Create("J "+(kind==0?"air":kind==1?"contact":kind==3?"body":"guard"),samples.Length,1,rate,false);
        clip.hideFlags=HideFlags.DontSave;clip.SetData(samples,0);return clip;
    }
    void OnDisable()
    {
        CancelSwing();foreach(var b in contacts)if(b!=null){b.live=false;b.mesh.Hide();}
        foreach(var v in voices)if(v)v.Stop();
        System.Array.Clear(pausedVoice,0,pausedVoice.Length);paused=false;
    }
    void OnDestroy()
    {
        swing?.Dispose();foreach(var b in contacts)b?.mesh.Dispose();
        foreach(var v in voices)if(v)Destroy(v);
        if(material)Destroy(material);if(air)Destroy(air);if(punch)Destroy(punch);if(guard)Destroy(guard);if(body)Destroy(body);
    }
    sealed class Burst
    {
        public InkMesh mesh; public Vector3 position; public BattleJMove move;
        public int step,bornFrame;public float age,life,direction;public bool live,blocked;
    }
    internal sealed class InkMesh
    {
        readonly GameObject node;readonly Mesh mesh;
        public readonly MeshRenderer renderer;
        readonly List<Vector3> vertices=new List<Vector3>(1024);
        readonly List<Color> colors=new List<Color>(1024);
        readonly List<int> triangles=new List<int>(1536);
        public InkMesh(string name,Transform parent,Material material)
        {
            node=new GameObject(name);node.transform.SetParent(parent,false);
            mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave};mesh.MarkDynamic();
            node.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortingOrder=110;Hide();
        }
        public void Begin(Vector3 position)
        {
            node.transform.SetPositionAndRotation(position,Quaternion.identity);
            vertices.Clear();colors.Clear();triangles.Clear();
        }
        public void Triangle(Vector2 a,Vector2 b,Vector2 c,Color color,float z)
        {
            int n=vertices.Count;
            vertices.Add(new Vector3(a.x,a.y,z));vertices.Add(new Vector3(b.x,b.y,z));vertices.Add(new Vector3(c.x,c.y,z));
            for(int i=0;i<3;i++){colors.Add(color);triangles.Add(n+i);}
        }
        public void Line(Vector2 a,Vector2 b,float width,Color color,float z)
        {
            var d=b-a;var normal=new Vector2(-d.y,d.x).normalized*width*.5f;
            Triangle(a-normal,a+normal,b+normal,color,z);Triangle(a-normal,b+normal,b-normal,color,z);
        }
        public void Diamond(Vector2 p,Vector2 extent,Color color,float z)
        {
            Triangle(p+Vector2.up*extent.y,p+Vector2.right*extent.x,p-Vector2.up*extent.y,color,z);
            Triangle(p+Vector2.up*extent.y,p-Vector2.up*extent.y,p-Vector2.right*extent.x,color,z);
        }
        public void End(){mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();renderer.enabled=true;}
        public void Hide(){if(renderer)renderer.enabled=false;}
        public void Dispose(){if(mesh)Object.Destroy(mesh);if(node)Object.Destroy(node);}
    }
}
