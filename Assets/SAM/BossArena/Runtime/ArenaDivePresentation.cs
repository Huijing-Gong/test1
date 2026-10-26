using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace SamProduction.BossArena
{
 public sealed class ArenaDivePresentation:MonoBehaviour
 {
  public ArenaStompPresentation groundWave;public AudioClip ascent,kneeCharge,kickRush,landing;
  public float VisibleRadius=>groundWave.VisibleRadius;public bool WarningVisible=>groundWave.WarningVisible;
  private ArenaBossBrain owner;private ParticleSystem fire;private Material material;private AudioSource voice;private GameObject root;private uint seed=18401;private float accumulator;private bool ascended,knee,rush;private Vector3 impactCentre;
  private void Prepare()
  {
   if(root!=null)return;
   if(groundWave==null||ascent==null||kneeCharge==null||kickRush==null||landing==null)throw new InvalidOperationException("Dive VFX/audio assets missing");
   root=new GameObject("Dive18 owned jet fire");voice=root.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=.85f;voice.minDistance=4;voice.maxDistance=45;
   var source=owner.GetComponent<ArenaDashPresentation>().sourceFire;material=new Material(source.flameShader);
   material.SetColor("_BaseColor",new Color(1,.025f,.001f));material.SetColor("_HotColor",new Color(1,.58f,.07f));material.SetFloat("_EmissionPower",3);material.SetFloat("_Opacity",.67f);material.SetFloat("_Dissolve",.16f);
   material.SetFloat("_ShapeMode",0);material.SetFloat("_UseDepthFade",1);material.SetFloat("_SoftDistance",.08f);if(source.erosionTexture!=null)material.SetTexture("_MainTex",source.erosionTexture);
   fire=root.AddComponent<ParticleSystem>();fire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   var main=fire.main;main.playOnAwake=false;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=800;main.startSize3D=true;main.startSpeed=0;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
   var emission=fire.emission;emission.enabled=false;var shape=fire.shape;shape.enabled=false;
   var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.25f,.01f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.55f,.65f),new GradientAlphaKey(0,1)});
   var color=fire.colorOverLifetime;color.enabled=true;color.color=g;var size=fire.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.5f),new Keyframe(.15f,1),new Keyframe(1,.05f)));
   var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   fire.useAutoRandomSeed=false;fire.randomSeed=18401;fire.Pause();
  }
  public void Begin(ArenaBossBrain brain,Vector3 centre,float radius)
  {owner=brain;Prepare();Clear();ascended=knee=rush=false;accumulator=0;seed=18401;impactCentre=centre;groundWave.Begin(owner.Socket("socket_foot.R"),centre,radius);}
  public void SetCentre(Vector3 point){impactCentre=point;groundWave.SetCentre(point);}
  public void Release()
  {
   groundWave.Release();float radius=owner.DiveMotor.Settings.impactRadius;
   for(int i=0;i<64;i++)
   {
    float a=Random01()*Mathf.PI*2,r=Mathf.Sqrt(Random01())*radius;var direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
    fire.Emit(new ParticleSystem.EmitParams{position=impactCentre+direction*r+Vector3.up*.12f,velocity=direction*.5f+Vector3.up*(1+Random01()*2),startLifetime=.25f+Random01()*.2f,startSize3D=new Vector3(.38f,.8f+Random01()*.5f,.38f),rotation=Random01()*50-25,startColor=Color.white},1);
   }
  }
  public void Land(){if(voice!=null){voice.transform.position=owner.transform.position;voice.PlayOneShot(landing,.62f);}}
  private float Random01(){seed=seed*1664525+1013904223;return(seed&0x00ffffff)/16777216f;}
  public void Tick(float dt,float t,ArenaDiveMotor.Contract c,float waveRadius)
  {
   groundWave.Tick(dt,t,c.impactTime,c.waveSeconds,waveRadius);material.SetFloat("_LocalTime",t);fire.Simulate(dt,false,false,true);fire.Pause();voice.transform.position=owner.Socket("chest").position;
   if(!ascended&&t>=2){ascended=true;voice.PlayOneShot(ascent,.6f);}
   if(!knee&&t>=2.65f){knee=true;voice.PlayOneShot(kneeCharge,.52f);}
   if(!rush&&t>=3.13f){rush=true;voice.PlayOneShot(kickRush,.7f);}
   if(t<2||t>3.7f)return;
   accumulator+=dt*(t<2.65f?280:t<3.13f?120:390);
   while(accumulator>=1)
   {
    accumulator--;Vector3 p,v;float height;
    if(t<2.65f){p=owner.Socket(Random01()<.5f?"socket_foot.L":"socket_foot.R").position;v=Vector3.down*(2+Random01()*2);height=1.0f;}
    else if(t<3.13f){p=owner.Socket("shin.L").position;v=Vector3.up*.4f-owner.transform.forward*.3f;height=.4f;}
    else{p=owner.FootPosition;v=-owner.transform.forward*(1+Random01()*2)+Vector3.up*.5f;height=.85f;}
    float a=Random01()*Mathf.PI*2;p+=new Vector3(Mathf.Cos(a)*.13f,0,Mathf.Sin(a)*.13f);
    fire.Emit(new ParticleSystem.EmitParams{position=p,velocity=v,startLifetime=.15f+Random01()*.22f,startSize3D=new Vector3(.18f+Random01()*.16f,height,.25f),rotation=Random01()*40-20,startColor=Color.white},1);
   }
  }
  public void Clear(){groundWave?.Clear();if(fire!=null)fire.Clear();if(voice!=null)voice.Stop();}
  private void OnDestroy(){if(root!=null)Destroy(root);if(material!=null)Destroy(material);}
 }
}
