using UnityEngine;
using System.Collections.Generic;
// Deterministic timeline: scrubbing and looping do not leave old particles behind.
public class FireDragonVFX : MonoBehaviour {
 public float releaseTime=1.50f;
 public float peakTime=1.57f;
 public float trailDuration=.11f;
 public float trajectoryRate=120f;
 public Vector3[] fistTrajectory;
 public Vector3 HeadWorldPosition => head ? head.position : Vector3.zero;
 public bool HeadVisible => head && head.gameObject.activeInHierarchy;
 public bool visible=true;
 public Transform fist;
 public Material flameMaterial;
 Transform root,head; Material fire,core,ember,rock,shell;
 List<LineRenderer> ribbons=new List<LineRenderer>(),rings=new List<LineRenderer>(),sparks=new List<LineRenderer>(),cracks=new List<LineRenderer>();
 List<Transform> debris=new List<Transform>(); List<Mesh> meshes=new List<Mesh>();
 public Vector3 releaseOrigin=new Vector3(.16f,.70f,.30f); Vector3 origin; bool built;
 readonly Color orange=new Color(1,.19f,.012f,1), gold=new Color(1,.63f,.12f,1);
 public void Build(){if(built)return;built=true;
 root=new GameObject("Fire Dragon Geometry").transform;root.SetParent(transform,false); root.gameObject.hideFlags=HideFlags.DontSave;
 shell=new Material(Shader.Find("Boss/DragonSurface"));
 var shader=Shader.Find("Boss/DragonFlame"); fire=new Material(shader);fire.SetColor("_Color",orange);core=new Material(shader);core.SetColor("_Color",gold);core.SetFloat("_Soft",0);ember=new Material(shader);ember.SetColor("_Color",new Color(1,.38f,.04f));ember.SetFloat("_Soft",0);
 rock=new Material(Shader.Find("Universal Render Pipeline/Lit"));rock.color=new Color(.055f,.045f,.04f);rock.SetFloat("_Smoothness",.2f);
 for(int i=0;i<15;i++)ribbons.Add(Line("Flame ribbon",fire,root));
 for(int i=0;i<3;i++)rings.Add(Line("Pressure ring",core,root));
 for(int i=0;i<100;i++)sparks.Add(Line("Ember",i%4==0?core:ember,root));
 

 head=new GameObject("Sculpted flame dragon head").transform;head.SetParent(root,false);
 // Long snout, separate jaw, backward horns, cheek fins and whiskers produce an identifiable dragon silhouette.
 Tube("Skull",new[]{new Vector3(0,0,-.45f),new Vector3(0,.03f,-.15f),new Vector3(0,0,.20f),new Vector3(0,-.04f,.55f)},new[]{.24f,.30f,.23f,.12f},shell);
 Tube("Upper muzzle",new[]{new Vector3(0,-.02f,.20f),new Vector3(0,-.08f,.62f),new Vector3(0,-.06f,.78f)},new[]{.19f,.15f,.07f},shell);
 Tube("Open lower jaw",new[]{new Vector3(0,-.16f,-.05f),new Vector3(0,-.31f,.36f),new Vector3(0,-.23f,.64f)},new[]{.14f,.10f,.035f},shell);
 for(int s=-1;s<=1;s+=2){
 Tube("Swept horn",new[]{new Vector3(s*.18f,.17f,-.03f),new Vector3(s*.31f,.40f,-.34f),new Vector3(s*.25f,.51f,-.77f)},new[]{.09f,.055f,0f},shell);
 Tube("Cheek fin",new[]{new Vector3(s*.17f,-.02f,.10f),new Vector3(s*.38f,.06f,-.24f),new Vector3(s*.43f,.12f,-.65f)},new[]{.11f,.075f,0f},shell);
 Tube("Whisker",new[]{new Vector3(s*.11f,-.05f,.64f),new Vector3(s*.42f,-.14f,.32f),new Vector3(s*.51f,.06f,-.25f),new Vector3(s*.60f,.13f,-.7f)},new[]{.018f,.015f,.01f,0f},core);
 Tube("Eye",new[]{new Vector3(s*.24f,.11f,.12f),new Vector3(s*.21f,.10f,.29f)},new[]{.038f,.012f},core);
 for(int j=0;j<3;j++)Tube("Fang",new[]{new Vector3(s*.12f,-.10f,.30f+j*.12f),new Vector3(s*.10f,-.22f,.35f+j*.12f)},new[]{.026f,0f},core);
 }
 for(int i=0;i<6;i++)Tube("Flame crest",new[]{new Vector3(0,.17f,-.1f-i*.13f),new Vector3(0,.42f,-.30f-i*.16f)},new[]{.08f,0f},fire);
 foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=gameObject.layer;
 }
 LineRenderer Line(string name,Material mat,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);var l=g.AddComponent<LineRenderer>();l.sharedMaterial=mat;l.useWorldSpace=false;l.numCapVertices=3;l.widthMultiplier=.02f;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return l;}
 void Tube(string name,Vector3[] points,float[] radii,Material mat){int sides=24;var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
 for(int i=0;i<points.Length;i++){var tangent=(points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(0,i-1)]).normalized;var side=Vector3.Cross(tangent,Vector3.up).normalized;if(side.sqrMagnitude<.1f)side=Vector3.right;var up=Vector3.Cross(side,tangent);
 for(int j=0;j<=sides;j++){float a=j*2*Mathf.PI/sides;verts.Add(points[i]+radii[i]*(Mathf.Cos(a)*side+Mathf.Sin(a)*up));uv.Add(new Vector2((float)i/(points.Length-1),(float)j/sides));if(i>0&&j>0){int k=i*(sides+1)+j;tris.AddRange(new[]{k,k-1,k-sides-1,k-1,k-sides-2,k-sides-1});}}}
 var mesh=new Mesh();mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();meshes.Add(mesh);var g=new GameObject(name);g.transform.SetParent(head,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=mat;}
 float Hash(int i){return Mathf.Repeat(Mathf.Sin(i*127.1f+31.7f)*43758.5453f,1);}
 public System.Func<float,Vector3> LiveTrack;
 public Vector3 Track(float time){if(LiveTrack!=null)return LiveTrack(time);if(fistTrajectory==null||fistTrajectory.Length<2)return fist?transform.InverseTransformPoint(fist.position):releaseOrigin;float f=Mathf.Clamp(time*trajectoryRate,0,fistTrajectory.Length-1);int i=Mathf.FloorToInt(f);return Vector3.Lerp(fistTrajectory[i],fistTrajectory[Mathf.Min(i+1,fistTrajectory.Length-1)],f-i);}
 public void Sample(float time,bool enabledEffect=true){Build();root.gameObject.SetActive(visible&&enabledEffect);if(!visible||!enabledEffect)return;
 Vector3 hand=fist?transform.InverseTransformPoint(fist.position):Track(time);
 Vector3 velocity=(Track(time+.0125f)-Track(time-.0125f))/.025f;
 Vector3 strikeVector=Track(peakTime+.025f)-Track(peakTime-.025f);
 Vector3 direction=strikeVector.sqrMagnitude>.00001f?strikeVector.normalized:Vector3.forward;
 if(time<peakTime+.075f&&velocity.magnitude>.7f)direction=velocity.normalized;
 Quaternion aim=Quaternion.LookRotation(direction,Vector3.up);
 float age=time-releaseTime;
 float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(peakTime+.075f,peakTime+.24f,time));
 float burst=Mathf.SmoothStep(0,1,Mathf.InverseLerp(releaseTime,peakTime,time))*fade;
 float charge=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,releaseTime,time));
 bool striking=time>=releaseTime&&time<peakTime+.24f;
 shell.SetFloat("_Phase",time);shell.SetColor("_Color",new Color(1,.2f,.01f,burst*.85f));fire.SetFloat("_Phase",time);core.SetFloat("_Phase",time);
 fire.SetColor("_Color",new Color(1,.24f,.012f,age<0?charge*.55f:fade));core.SetColor("_Color",new Color(1,.66f,.18f,age<0?charge*.6f:fade));
 // The dragon is a brief flaming glove: its centre stays on the animated hand, never an independent projectile.
 head.gameObject.SetActive(striking&&burst>.01f);head.localPosition=hand+direction*.025f;head.localRotation=aim;head.localScale=Vector3.one*(.12f+.23f*burst);
 for(int i=0;i<ribbons.Count;i++){var l=ribbons[i];l.enabled=(time>.65f&&age<0)||striking;l.positionCount=36;float phase=i*2.39996f;
 l.widthMultiplier=age<0?.008f*charge:(.012f+(i%3)*.009f)*fade;l.startColor=Color.white;l.endColor=new Color(1,.3f,.01f,0);
 for(int j=0;j<36;j++){float u=j/35f;Vector3 p;if(age<0){float a=u*5+time*9+phase;float r=(.025f+u*.03f)*charge;p=hand+aim*new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,-u*.06f);}else{float sampleTime=Mathf.Max(releaseTime-.04f,time-u*trailDuration);p=Track(sampleTime)+(hand-Track(time));float a=u*7-time*15+phase;float r=(.025f+.045f*Mathf.Sin(u*Mathf.PI))*burst;p+=aim*new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);}l.SetPosition(j,p);}
 }
 for(int i=0;i<rings.Count;i++){var l=rings[i];float a=time-peakTime-i*.02f;l.enabled=age<0?(time>.8f&&i==0):(a>=0&&a<.10f);l.positionCount=49;float r=age<0?.04f+charge*.025f:.065f+a*.8f;l.widthMultiplier=age<0?.006f*charge:.012f*(1-a/.1f);for(int j=0;j<49;j++){float theta=j*Mathf.PI*2/48;l.SetPosition(j,hand+aim*new Vector3(Mathf.Cos(theta)*r,Mathf.Sin(theta)*r,.025f));}}
 for(int i=0;i<sparks.Count;i++){var l=sparks[i];float seed=Hash(i+1);float spawnTime=peakTime-.025f+seed*.055f;float a=time-spawnTime;bool charging=time>.8f&&age<0&&i<16;l.enabled=charging||(a>=0&&a<.24f);l.positionCount=2;Vector3 p;
 if(charging){float phase=time*4+i*2.4f;p=hand+aim*new Vector3(Mathf.Sin(phase)*.08f,Mathf.Cos(phase)*.08f,-seed*.08f);}else{p=Track(spawnTime)+aim*new Vector3(Mathf.Sin(i*4.1f)*a*.45f,Mathf.Cos(i*1.3f)*a*.45f-a*a*.25f,a*(.3f+seed*.8f));}
 l.widthMultiplier=.0025f+(i%3)*.001f;float alpha=charging?charge*.4f:Mathf.Clamp01(1-a/.24f);l.startColor=new Color(1,1,1,alpha);l.endColor=new Color(1,.3f,0,0);l.SetPosition(0,p);l.SetPosition(1,p-direction*(.012f+seed*.015f));}
 }
 void OnDestroy(){foreach(var m in meshes)if(m)Destroy(m);foreach(var m in new[]{fire,core,ember,rock,shell})if(m)Destroy(m);if(root)Destroy(root.gameObject);}
}
