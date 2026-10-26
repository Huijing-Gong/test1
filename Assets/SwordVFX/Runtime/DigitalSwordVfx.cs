using UnityEngine;
using UnityEngine.Rendering;
namespace KingOfGraphic.SwordVFX
{
 public sealed class DigitalSwordVfx : MonoBehaviour
 {
  public Transform blade;
  public Material surfaceMaterial,cyanFragmentMaterial,goldFragmentMaterial;
  public Mesh cubeMesh;
  public Mesh hologramMesh,hiltMesh;
  public Mesh bodyMesh;
  public Material bodyMaterial;
  public Shader metalTransitionShader;
  readonly SwordActivationState activation=new SwordActivationState();
  public float Reveal => activation.Reveal;
  public float HoldRemaining => activation.HoldRemaining;
  Material[] originalMaterials,transitionMaterials;
  Renderer[] hologramRenderers;
  MaterialPropertyBlock revealBlock;
  MaterialPropertyBlock originalBlock;
  public bool automaticTime=true;
  const int Capacity=40;
  struct Fragment {public Transform transform;public Renderer renderer;public Vector3 position,velocity;public float age,life,size;}
  Fragment[] fragments;
  GameObject surface;
  GameObject hilt;
  Renderer original;
  bool originalEnabled;
  int cursor,sequence;
  float emissionTime;
  bool swinging;
  public int LiveCount {get {int n=0;if(fragments!=null)foreach(var f in fragments)if(f.renderer && f.renderer.enabled)n++;return n;}}
  public void Initialize()
  {
   if(fragments!=null)return;
   if(!blade)blade=transform.parent;
   if(blade && surfaceMaterial && hologramMesh && hiltMesh)
   {
    var source=blade.GetComponent<MeshFilter>();
    if(source)
    {
     original=blade.GetComponent<Renderer>();originalEnabled=original && original.enabled;
     if(original){
      originalMaterials=original.sharedMaterials;originalBlock=new MaterialPropertyBlock();original.GetPropertyBlock(originalBlock);
      if(metalTransitionShader){
       transitionMaterials=new Material[originalMaterials.Length];
       for(int i=0;i<originalMaterials.Length;i++){transitionMaterials[i]=new Material(originalMaterials[i]);transitionMaterials[i].shader=metalTransitionShader;}
      }
     }
     surface=new GameObject("Holographic blade points"){hideFlags=HideFlags.DontSave};surface.transform.SetParent(blade,false);
     surface.AddComponent<MeshFilter>().sharedMesh=hologramMesh;
     var r=surface.AddComponent<MeshRenderer>();r.sharedMaterial=surfaceMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
     if(bodyMesh && bodyMaterial){
      var body=new GameObject("Continuous translucent blade");body.transform.SetParent(surface.transform,false);
      body.AddComponent<MeshFilter>().sharedMesh=bodyMesh;
      var br=body.AddComponent<MeshRenderer>();br.sharedMaterial=bodyMaterial;br.shadowCastingMode=ShadowCastingMode.Off;br.receiveShadows=false;
     }
     hilt=new GameObject("Original sword hilt"){hideFlags=HideFlags.DontSave};hilt.transform.SetParent(blade,false);
     hilt.AddComponent<MeshFilter>().sharedMesh=hiltMesh;
     var hr=hilt.AddComponent<MeshRenderer>();if(original)hr.sharedMaterials=original.sharedMaterials;
     if(original)original.enabled=false;
     hologramRenderers=surface.GetComponentsInChildren<Renderer>();revealBlock=new MaterialPropertyBlock();
    }
   }
   fragments=new Fragment[Capacity];
   for(int i=0;i<Capacity;i++)
   {
    var go=new GameObject("Digital fragment "+i){hideFlags=HideFlags.DontSave};go.transform.SetParent(transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=cubeMesh;
    var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=i%4==0?goldFragmentMaterial:cyanFragmentMaterial;
    r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.enabled=false;
    fragments[i]=new Fragment{transform=go.transform,renderer=r};
   }
   ApplyActivation();
  }
  public void BeginAttack(){Initialize();activation.BeginAttack();}
  public void EndAttack(){activation.EndAttack();}
  void ApplyActivation(){
   float amount=activation.Reveal;
   if(surface)surface.SetActive(amount>0);
   if(hilt)hilt.SetActive(amount>=1);
   if(hologramRenderers!=null){revealBlock.SetFloat("_Reveal",amount);foreach(var r in hologramRenderers)if(r)r.SetPropertyBlock(revealBlock);}
   if(!original)return;
   if(amount<=0){original.sharedMaterials=originalMaterials;original.SetPropertyBlock(originalBlock);original.enabled=originalEnabled;}
   else if(amount<1 && transitionMaterials!=null){
    foreach(var mat in transitionMaterials)mat.SetFloat("_Reveal",amount);
    original.sharedMaterials=transitionMaterials;original.enabled=originalEnabled;
   }else{original.enabled=false;}
  }
  void Awake(){Initialize();}
  public void BeginSwing(){Initialize();swinging=true;emissionTime=0;}
  public void EndSwing(){swinging=false;}
  static float Noise(int i){float f=Mathf.Sin(i*12.9898f+4.1414f)*43758.5453f;return f-Mathf.Floor(f);}
  void Spawn(Vector3 position,Vector3 velocity,float size,float life)
  {
   ref var f=ref fragments[cursor];f.position=position;f.velocity=velocity;f.age=0;f.life=life;f.size=size;
   f.transform.position=position;f.transform.rotation=Quaternion.Euler(sequence*31,sequence*57,sequence*13);
   f.transform.localScale=Vector3.one*size;f.renderer.enabled=true;cursor=(cursor+1)%Capacity;sequence++;
  }
  public void Advance(float dt)
  {
   Initialize();if(dt<=0)return;activation.Tick(dt);ApplyActivation();
   for(int i=0;i<Capacity;i++)
   {
    ref var f=ref fragments[i];if(!f.renderer.enabled)continue;
    f.age+=dt;if(f.age>=f.life){f.renderer.enabled=false;continue;}
    f.position+=f.velocity*dt;f.velocity+=Vector3.down*(.7f*dt);
    f.transform.position=f.position;f.transform.Rotate(new Vector3(30,60,20)*dt,Space.World);
    f.transform.localScale=Vector3.one*f.size*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,1,f.age/f.life)));
   }
   if(!swinging || !blade)return;
   emissionTime+=dt;int budget=6;
   while(emissionTime>=.025f && budget-->0)
   {
    emissionTime-=.025f;float t=Noise(sequence);float side=sequence%2==0?1:-1;
    Vector3 local=new Vector3((t-.5f)*.07f,Mathf.Lerp(.24f,.98f,t),side*.025f);
    Spawn(blade.TransformPoint(local),blade.TransformDirection(new Vector3(side*.08f,.04f,side*.08f)),.003f+t*.003f,.22f+t*.12f);
   }
  }
  public void PlayImpact(Vector3 point)
  {
   Initialize();
   for(int i=0;i<12;i++)
   {
    float a=i*2.39996f;float t=Noise(sequence);
    var velocity=new Vector3(Mathf.Cos(a),.2f+t,Mathf.Sin(a))*(.45f+t*.45f);
    Spawn(point,velocity,.004f+t*.004f,.22f+t*.13f);
   }
  }
  public void Clear(){swinging=false;emissionTime=0;activation.Reset();ApplyActivation();if(fragments!=null)foreach(var f in fragments)if(f.renderer)f.renderer.enabled=false;}
  void Update(){if(automaticTime)Advance(Time.deltaTime);}
  void OnEnable(){ApplyActivation();}
  void OnDisable(){Clear();if(surface)surface.SetActive(false);if(hilt)hilt.SetActive(false);if(original)original.enabled=originalEnabled;}
  void OnDestroy(){if(original){original.enabled=originalEnabled;original.sharedMaterials=originalMaterials;original.SetPropertyBlock(originalBlock);}if(surface)Release(surface);if(hilt)Release(hilt);if(fragments!=null)foreach(var f in fragments)if(f.transform)Release(f.transform.gameObject);if(transitionMaterials!=null)foreach(var m in transitionMaterials)if(m)Release(m);}
  static void Release(Object o){if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
 }
}
