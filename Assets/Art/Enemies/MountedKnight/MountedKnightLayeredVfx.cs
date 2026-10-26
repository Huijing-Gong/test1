using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>Pooled slash surfaces, directional thrusts and ground dust. Cosmetic only.</summary>
public sealed class MountedKnightLayeredVfx : MonoBehaviour
{
    sealed class Burst { public GameObject go; public Mesh mesh; public MeshRenderer renderer; public MaterialPropertyBlock block=new MaterialPropertyBlock(); public float age,duration; public Vector3 scale; public bool ring; public Vector3 worldPosition; public Quaternion worldRotation; }
    readonly List<Burst> pool=new List<Burst>();
    Material surface,dustMaterial,ribbonMaterial; ParticleSystem dust;
    public Material RibbonMaterial => ribbonMaterial;
    public int SlashCount {get;private set;}
    public int GroundCount {get;private set;}
    public void Initialize(){
        var shader=Shader.Find("Warden/Layered Slash");if(!shader){enabled=false;return;}
        surface=new Material(shader){name="Warden layered blade light"};surface.SetColor("_Tint",new Color(.10f,.82f,1.1f,.95f));
        ribbonMaterial=new Material(surface);ribbonMaterial.SetFloat("_Mode",1);
        dustMaterial=new Material(shader);dustMaterial.SetFloat("_Mode",2);dustMaterial.SetColor("_Tint",new Color(.31f,.28f,.20f,.37f));
        for(int i=0;i<12;i++){
            var b=new Burst();b.go=new GameObject("Pooled crafted slash "+i);b.go.transform.SetParent(transform,false);b.mesh=new Mesh{name="Warden crescent"};b.mesh.MarkDynamic();b.go.AddComponent<MeshFilter>().sharedMesh=b.mesh;b.renderer=b.go.AddComponent<MeshRenderer>();b.renderer.sharedMaterial=surface;b.renderer.shadowCastingMode=ShadowCastingMode.Off;b.renderer.receiveShadows=false;b.go.SetActive(false);pool.Add(b);
        }
        var o=new GameObject("Soft ground dust");o.transform.SetParent(transform,false);dust=o.AddComponent<ParticleSystem>();dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var m=dust.main;m.playOnAwake=false;m.loop=false;m.maxParticles=140;m.simulationSpace=ParticleSystemSimulationSpace.World;m.startLifetime=.8f;
        var e=dust.emission;e.enabled=false;var sh=dust.shape;sh.enabled=false;
        var c=dust.colorOverLifetime;c.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.7f,.12f),new GradientAlphaKey(0,1)});c.color=g;
        var size=dust.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.35f,1,1));
        var r=o.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=dustMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
    }
    void Surface(Vector3 at,Quaternion rotation,float radius,float width,float arc,float seconds,bool ring,Color tint){
        if(!surface)return;Burst b=null;foreach(var item in pool)if(!item.go.activeSelf){b=item;break;}if(b==null)return;
        const int steps=64;var vs=new Vector3[(steps+1)*2];var uv=new Vector2[vs.Length];var cols=new Color[vs.Length];var ids=new int[steps*6];
        for(int i=0;i<=steps;i++){
            float t=(float)i/steps,a=(-arc*.5f+arc*t)*Mathf.Deg2Rad;float taper=ring?1:Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),.6f);
            Vector3 dir=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));vs[i*2]=dir*(radius-width*taper);vs[i*2+1]=dir*radius;if(ring){vs[i*2]+=Vector3.up*.12f;vs[i*2+1]+=Vector3.up*.025f;}
            uv[i*2]=new Vector2(t,0);uv[i*2+1]=new Vector2(t,1);cols[i*2]=cols[i*2+1]=Color.white;
            if(i<steps){int k=i*6,j=i*2;ids[k]=j;ids[k+1]=j+1;ids[k+2]=j+2;ids[k+3]=j+1;ids[k+4]=j+3;ids[k+5]=j+2;}
        }
        b.mesh.Clear();b.mesh.vertices=vs;b.mesh.uv=uv;b.mesh.colors=cols;b.mesh.triangles=ids;b.mesh.RecalculateBounds();
        b.go.transform.SetPositionAndRotation(at,rotation);b.go.transform.localScale=Vector3.one;
        // Cancel inherited character scaling; all radii are measured in world metres.
        var lossy=b.go.transform.lossyScale;b.go.transform.localScale=new Vector3(1/lossy.x,1/lossy.y,1/lossy.z);b.scale=b.go.transform.localScale;
        b.worldPosition=at;b.worldRotation=rotation;b.duration=seconds;b.age=0;b.ring=ring;b.block.SetColor("_Tint",tint);b.block.SetFloat("_Life",0);b.renderer.SetPropertyBlock(b.block);b.go.SetActive(true);
    }
    public void Strike(Vector3 at,Quaternion facing,int attack,bool radial,float radius){
        // Blade effects come only from the sampled blade surface, never a detached crescent.
        if(!radial){SlashCount++;return;}
        GroundCount++;
        bool heavy=attack==2;
        Surface(at+Vector3.up*.05f,facing,heavy?1.05f:.62f,heavy?.19f:.1f,360,heavy?.34f:.22f,true,heavy?new Color(1,.68f,.25f,.8f):new Color(.12f,.75f,1,.7f));
        Dust(at,heavy?14:7,heavy?1.1f:.65f);
    }
    public void Dust(Vector3 at,int count,float speed){
        if(!dust)return;dust.Play();for(int i=0;i<count;i++){float a=Random.value*Mathf.PI*2;Vector3 d=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));var e=new ParticleSystem.EmitParams{position=at+d*Random.Range(.1f,.7f)+Vector3.up*.08f,velocity=d*speed+Vector3.up*Random.Range(.1f,.4f),startSize=Random.Range(.35f,.8f),startLifetime=Random.Range(.45f,1f),startColor=Color.white,rotation=Random.value*360};dust.Emit(e,1);}
    }
    void Update(){foreach(var b in pool){if(!b.go.activeSelf)continue;b.go.transform.SetPositionAndRotation(b.worldPosition,b.worldRotation);b.age+=Time.deltaTime;float t=b.age/b.duration;if(t>=1){b.go.SetActive(false);continue;}b.block.SetFloat("_Life",t);b.renderer.SetPropertyBlock(b.block);b.go.transform.localScale=b.scale*(b.ring?Mathf.Lerp(.25f,1,1-Mathf.Pow(1-t,3)):Mathf.Lerp(.96f,1.05f,t));}}
    public void Clear(){foreach(var b in pool)b.go.SetActive(false);if(dust)dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void OnDisable(){Clear();}
    void OnDestroy(){foreach(var b in pool)if(b.mesh)Destroy(b.mesh);if(surface)Destroy(surface);if(ribbonMaterial)Destroy(ribbonMaterial);if(dustMaterial)Destroy(dustMaterial);}
}
