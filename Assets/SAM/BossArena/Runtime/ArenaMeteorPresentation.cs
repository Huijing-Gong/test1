using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace SamProduction.BossArena
{
 public sealed class ArenaMeteorPresentation:MonoBehaviour
 {
  public ArenaStompPresentation groundWave;public AudioClip gather,mainFall,companionFall,punchRelease,groundImpact;
  public float VisibleRadius=>groundWave.VisibleRadius;public bool WarningVisible=>groundWave.WarningVisible;
  public bool MainMeteorActive=>mainRenderer!=null&&mainRenderer.enabled&&mainRenderer.gameObject.activeInHierarchy;
  public Bounds MainMeteorBounds=>mainRenderer!=null?mainRenderer.bounds:new Bounds();
  private Renderer mainRenderer;
  private ArenaBossBrain owner;private GameObject root;private ParticleSystem fire;private Material material;private AudioSource voice;private Vector3 centre;private uint seed=18601;private float accumulator;private bool mainCue,companionsCue,punchCue;
  private readonly Vector3[] offsets=new Vector3[12];
  private readonly Transform[] cores=new Transform[12];private readonly LineRenderer[] trails=new LineRenderer[12];
  private readonly LineRenderer[] pressureRings=new LineRenderer[3];private Material coreMaterial,trailMaterial;private Mesh volumeProxy;
  private readonly Vector3[] trailPoints=new Vector3[9],ringPoints=new Vector3[48];
  private void Prepare()
  {
   if(root!=null)return;if(groundWave==null||gather==null||mainFall==null||companionFall==null||punchRelease==null||groundImpact==null)throw new InvalidOperationException("Meteor VFX/audio missing");
   root=new GameObject("Meteor18 owned fire field");voice=root.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=.7f;voice.minDistance=6;voice.maxDistance=65;
   var source=owner.GetComponent<ArenaDashPresentation>().sourceFire;material=new Material(source.flameShader);
   material.SetColor("_BaseColor",new Color(1,.035f,.002f));material.SetColor("_HotColor",new Color(1,.7f,.16f));material.SetFloat("_EmissionPower",3.5f);material.SetFloat("_Opacity",.75f);material.SetFloat("_Dissolve",.12f);
   material.SetFloat("_ShapeMode",0);material.SetFloat("_UseDepthFade",1);material.SetFloat("_SoftDistance",.12f);if(source.erosionTexture!=null)material.SetTexture("_MainTex",source.erosionTexture);
   material.SetFloat("_Opacity",.32f);
   coreMaterial=new Material(source.flameShader);coreMaterial.SetFloat("_ShapeMode",4);coreMaterial.SetFloat("_VolumeSteps",20);coreMaterial.SetFloat("_VolumeDensity",2.5f);coreMaterial.SetFloat("_VolumeCoreBreakup",.8f);coreMaterial.SetFloat("_Opacity",.7f);coreMaterial.SetFloat("_EmissionPower",3);coreMaterial.SetColor("_BaseColor",new Color(1,.025f,.001f));coreMaterial.SetColor("_HotColor",new Color(1,.62f,.13f));
   trailMaterial=new Material(source.flameShader);trailMaterial.SetFloat("_ShapeMode",1);trailMaterial.SetFloat("_Opacity",.26f);trailMaterial.SetFloat("_EmissionPower",2.3f);trailMaterial.SetFloat("_Dissolve",.32f);trailMaterial.SetColor("_BaseColor",new Color(1,.06f,.005f));trailMaterial.SetColor("_HotColor",new Color(1,.55f,.08f));
   for(int i=0;i<12;i++)
   {
    var core=GameObject.CreatePrimitive(PrimitiveType.Cube);core.name=i==0?"Main turbulent meteor core":"Companion meteor core "+i;core.transform.SetParent(root.transform,false);var collider=core.GetComponent<Collider>();collider.enabled=false;Destroy(collider);cores[i]=core.transform;
    if(volumeProxy==null){volumeProxy=Instantiate(core.GetComponent<MeshFilter>().sharedMesh);volumeProxy.name="Meteor volume bounds minus-one to one";var vertices=volumeProxy.vertices;for(int j=0;j<vertices.Length;j++)vertices[j]*=2;volumeProxy.vertices=vertices;volumeProxy.RecalculateBounds();}
    core.GetComponent<MeshFilter>().sharedMesh=volumeProxy;
    var coreRenderer=core.GetComponent<MeshRenderer>();coreRenderer.sharedMaterial=coreMaterial;coreRenderer.shadowCastingMode=ShadowCastingMode.Off;coreRenderer.receiveShadows=false;core.SetActive(false);
    if(i==0)mainRenderer=coreRenderer;
    var tail=new GameObject("Meteor trailing wake "+i);tail.transform.SetParent(root.transform,false);var line=tail.AddComponent<LineRenderer>();line.sharedMaterial=trailMaterial;line.useWorldSpace=true;line.positionCount=9;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;trails[i]=line;
   }
   for(int i=0;i<3;i++){var ring=new GameObject("Meteor pressure ring "+i);ring.transform.SetParent(root.transform,false);var line=ring.AddComponent<LineRenderer>();line.sharedMaterial=trailMaterial;line.positionCount=48;line.loop=true;line.useWorldSpace=true;line.widthMultiplier=.065f;line.enabled=false;pressureRings[i]=line;}
   fire=root.AddComponent<ParticleSystem>();fire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=fire.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=2600;main.startSize3D=true;main.startSpeed=0;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
   var emission=fire.emission;emission.enabled=false;var shape=fire.shape;shape.enabled=false;
   var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.2f,.015f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.1f),new GradientAlphaKey(.6f,.7f),new GradientAlphaKey(0,1)});var color=fire.colorOverLifetime;color.enabled=true;color.color=gradient;
   var size=fire.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.6f),new Keyframe(.15f,1),new Keyframe(1,.03f)));
   var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   fire.useAutoRandomSeed=false;fire.randomSeed=18601;fire.Pause();
   for(int i=1;i<offsets.Length;i++){float angle=i*2.399963f;float radius=2.5f+((i*7)%9)*.35f;offsets[i]=new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);}
  }
  public void Begin(ArenaBossBrain brain,Vector3 point,float radius){owner=brain;Prepare();Clear();seed=18601;accumulator=0;mainCue=companionsCue=punchCue=false;centre=point;groundWave.Begin(owner.Socket("socket_foot.R"),point,radius);voice.PlayOneShot(gather,.66f);}
  public void SetCentre(Vector3 point){centre=point;groundWave.SetCentre(point);}
  public void Release()
  {
   HideMeteors();
   groundWave.Release();voice.transform.position=centre;voice.PlayOneShot(groundImpact,.85f);
   for(int i=0;i<150;i++){float a=Rand()*Mathf.PI*2;var direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));Emit(centre+direction*(Mathf.Sqrt(Rand())*2)+Vector3.up*.2f,direction*(1+Rand()*3)+Vector3.up*(2+Rand()*4),.7f,1.6f,.35f+Rand()*.35f);}
  }
  private float Rand(){seed=seed*1664525+1013904223;return(seed&0xffffff)/16777216f;}
  private void Emit(Vector3 position,Vector3 velocity,float width,float height,float lifetime)
  {fire.Emit(new ParticleSystem.EmitParams{position=position,velocity=velocity,startLifetime=lifetime,startSize3D=new Vector3(width,height,width),rotation=Rand()*70-35,startColor=Color.white},1);}
  public void Tick(float dt,float t,ArenaMeteorMotor.Contract c,float waveRadius)
  {
   groundWave.Tick(dt,t,c.impactTime,c.waveSeconds,waveRadius);material.SetFloat("_LocalTime",t);coreMaterial.SetFloat("_LocalTime",t);trailMaterial.SetFloat("_LocalTime",t);fire.Simulate(dt,false,false,true);fire.Pause();voice.transform.position=owner.Socket("chest").position;
   if(!mainCue&&t>=c.mainTime){mainCue=true;voice.PlayOneShot(mainFall,.68f);}
   if(!companionsCue&&t>=c.companionsTime){companionsCue=true;voice.PlayOneShot(companionFall,.6f);}
   if(!punchCue&&t>=c.punchTime){punchCue=true;voice.PlayOneShot(punchRelease,.7f);}
   if(t<c.chargeSeconds)
   {
    accumulator+=dt*(70+80*t/c.chargeSeconds);while(accumulator>=1){accumulator--;var chest=owner.Socket("chest").position;float a=Rand()*Mathf.PI*2;Emit(chest+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*(.5f+Rand()*.3f),Vector3.up*.25f,.25f,.55f,.2f+Rand()*.15f);}return;
   }
   if(t<c.mainTime||t>=c.impactTime)return;
   for(int i=0;i<12;i++)
   {
    bool visible=i==0||t>=c.companionsTime;cores[i].gameObject.SetActive(visible);trails[i].enabled=visible;if(!visible)continue;
    float start=i==0?c.mainTime:c.companionsTime;float u=Mathf.InverseLerp(start,c.impactTime,t);float fall=u*u;
    var end=centre+offsets[i];var from=end+owner.transform.TransformDirection(new Vector3(-4,17,-8));var point=Vector3.Lerp(from,end,fall);var reverse=(from-end).normalized;
    float size=i==0?1.65f:.65f+(i%3)*.10f;cores[i].SetPositionAndRotation(point,Quaternion.LookRotation(reverse));cores[i].localScale=Vector3.one*size;
    var line=trails[i];line.startWidth=size*.68f;line.endWidth=.02f;line.startColor=new Color(1,.65f,.08f,.6f);line.endColor=new Color(1,.08f,.002f,0);
    for(int j=0;j<9;j++){float v=j/8f;trailPoints[j]=point+reverse*(size*(.3f+v*5))+owner.transform.right*(Mathf.Sin(v*8+t*12+i)*v*size*.12f);}line.SetPositions(trailPoints);
    if(i==0)
    {
     var x=Vector3.Cross(reverse,Vector3.up).normalized;var y=Vector3.Cross(reverse,x);
     for(int k=0;k<3;k++){float phase=Mathf.Repeat((t-c.mainTime)*2.3f+k/3f,1);var ring=pressureRings[k];ring.enabled=true;ring.startColor=ring.endColor=new Color(1,.55f,.12f,(1-phase)*.32f);float radius=size*(1+phase*.8f);for(int j=0;j<48;j++){float a=j*Mathf.PI*2/48;ringPoints[j]=point+reverse*(phase*size*4)+(x*Mathf.Cos(a)+y*Mathf.Sin(a))*radius;}ring.SetPositions(ringPoints);}
    }
   }
   accumulator+=dt*20;while(accumulator>=1)
   {
    accumulator--;int count=t<c.companionsTime?1:offsets.Length;
    for(int i=0;i<count;i++)
    {
     float start=i==0?c.mainTime:c.companionsTime;float u=Mathf.InverseLerp(start,c.impactTime,t);float fall=u*u;
     var end=centre+offsets[i];var from=end+owner.transform.TransformDirection(new Vector3(-4,17,-8));var point=Vector3.Lerp(from,end,fall);
     float size=i==0?1.65f:.65f+(i%3)*.10f;var reverse=(from-end).normalized;
     float a=Rand()*Mathf.PI*2;Emit(point+new Vector3(Mathf.Cos(a),Mathf.Sin(a),Rand()-.5f)*size*.35f,reverse*(2+Rand()*3),size*.25f,size*.5f,.15f+Rand()*.15f);
    }
   }
  }
  private void HideMeteors(){foreach(var core in cores)if(core!=null)core.gameObject.SetActive(false);foreach(var trail in trails)if(trail!=null)trail.enabled=false;foreach(var ring in pressureRings)if(ring!=null)ring.enabled=false;}
  public void Clear(){HideMeteors();groundWave?.Clear();if(fire!=null)fire.Clear();if(voice!=null)voice.Stop();}
  private void OnDestroy(){if(root!=null)Destroy(root);if(material!=null)Destroy(material);if(coreMaterial!=null)Destroy(coreMaterial);if(trailMaterial!=null)Destroy(trailMaterial);if(volumeProxy!=null)Destroy(volumeProxy);}
 }
}
