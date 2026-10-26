using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Starts only after the authored collapse; world-grid cells drive both clipping and debris release.
public sealed class MountedKnightPixelDeath : MonoBehaviour {
 const float Cell=.085f, DissolveDuration=2.6f, DebrisLife=1.65f;
 Health health;float age;bool dead,started;Renderer[] renderers;Material[][] originals;readonly List<Material> copies=new();
 struct Block { public Vector3 p,velocity;public float delay,size,spin;public int color; }
 readonly List<Block> blocks=new();readonly List<Matrix4x4>[] batches={new(),new(),new()};
 Material[] blockMaterials;Mesh cube;
 public bool Complete {get;private set;}public int BlockCount=>blocks.Count;public float DissolveProgress {get;private set;}
 void Awake(){health=GetComponent<Health>();}
 void OnEnable(){health.Died+=Died;health.HealthChanged+=Changed;}
 void Died(Health h,DamageInfo d){ResetVisuals();dead=true;age=0;}
 void Changed(Health h,int previous,int current){if(current>0&&dead)ResetVisuals();}
 static float Hash(Vector3 c){float x=Mathf.Sin(Vector3.Dot(c,new Vector3(12.9898f,78.233f,37.719f)))*43758.5453f;return x-Mathf.Floor(x);}
 void Begin(){started=true;var shader=Shader.Find("Combat/WardenPixelDissolve");renderers=GetComponentsInChildren<Renderer>();originals=new Material[renderers.Length][];var cells=new HashSet<Vector3Int>();
  for(int r=0;r<renderers.Length;r++){
   var renderer=renderers[r];if((!(renderer is SkinnedMeshRenderer)&&!(renderer is MeshRenderer))||(!renderer.GetComponentInParent<Animator>()&&renderer.name!="Warden_SoftSaddleBlanket"))continue;
   originals[r]=renderer.sharedMaterials;var mats=new Material[originals[r].Length];
   for(int j=0;j<mats.Length;j++){var source=originals[r][j];var m=new Material(shader);if(source){m.SetTexture("_BaseMap",source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.HasProperty("_MainTex")?source.GetTexture("_MainTex"):null);m.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white);}m.SetFloat("_Cell",Cell);m.SetFloat("_Progress",-.01f);mats[j]=m;copies.Add(m);}renderer.sharedMaterials=mats;
   // Clear residual hit property blocks before the new shader takes ownership.
   renderer.SetPropertyBlock(null);for(int j=0;j<mats.Length;j++)renderer.SetPropertyBlock(null,j);
   if(renderer is SkinnedMeshRenderer skin){var mesh=skin.sharedMesh;var vs=mesh.vertices;var weights=mesh.boneWeights;var binds=mesh.bindposes;var bones=skin.bones;var matrices=new Matrix4x4[bones.Length];for(int j=0;j<bones.Length;j++)matrices[j]=bones[j].localToWorldMatrix*binds[j];
    for(int j=0;j<vs.Length;j+=31){var w=weights[j];Vector3 p=matrices[w.boneIndex0].MultiplyPoint3x4(vs[j])*w.weight0;if(w.weight1>0)p+=matrices[w.boneIndex1].MultiplyPoint3x4(vs[j])*w.weight1;if(w.weight2>0)p+=matrices[w.boneIndex2].MultiplyPoint3x4(vs[j])*w.weight2;if(w.weight3>0)p+=matrices[w.boneIndex3].MultiplyPoint3x4(vs[j])*w.weight3;Add(p,cells);}
   }else{var filter=renderer.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh){var vs=filter.sharedMesh.vertices;for(int j=0;j<vs.Length;j+=13)Add(renderer.transform.TransformPoint(vs[j]),cells);}}
  }
  var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=primitive.GetComponent<MeshFilter>().sharedMesh;Destroy(primitive);
  blockMaterials=new Material[3];var colors=new[]{new Color(.08f,.8f,.95f),new Color(.75f,.91f,.94f),new Color(.08f,.2f,.27f)};
  for(int i=0;i<3;i++){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.enableInstancing=true;m.SetColor("_BaseColor",colors[i]);m.SetFloat("_Smoothness",.25f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",colors[i]*(i==2?.15f:1.3f));blockMaterials[i]=m;}
 }
 void Add(Vector3 p,HashSet<Vector3Int> cells){var c=Vector3Int.FloorToInt(p/Cell);if(!cells.Add(c)||blocks.Count>=1600)return;float h=Hash(c);float b=Hash((Vector3)c+new Vector3(17,9,4));blocks.Add(new Block{p=p,delay=h*DissolveDuration,size=Cell*Mathf.Lerp(.42f,.9f,b),velocity=new Vector3(Mathf.Lerp(-.8f,1.1f,b),.45f+b*.9f,Mathf.Lerp(-.5f,.5f,h)),spin=b*350+80,color=b<.6f?0:b<.84f?1:2});}
 void Update(){if(!dead||Complete)return;age+=Time.deltaTime;if(!started&&age>=4.85f)Begin();if(!started)return;float t=age-4.85f;DissolveProgress=Mathf.Clamp01(t/DissolveDuration);foreach(var m in copies)m.SetFloat("_Progress",DissolveProgress*1.02f);
  foreach(var batch in batches)batch.Clear();
  foreach(var b in blocks){float a=t-b.delay;if(a<0||a>DebrisLife)continue;float k=a/DebrisLife;Vector3 p=b.p+b.velocity*a+Vector3.up*a*a*.22f;p.x+=Mathf.Sin(Mathf.Floor(a*16)+b.spin)*.045f*(1-k);float size=b.size*(1-Mathf.SmoothStep(.45f,1,k));if(Mathf.Floor(a*23+b.spin)%11==0)size*=.35f;batches[b.color].Add(Matrix4x4.TRS(p,Quaternion.Euler(b.spin*a,b.spin*.6f*a,0),Vector3.one*size));}
  for(int c=0;c<3;c++)for(int j=0;j<batches[c].Count;j+=1023){int count=Mathf.Min(1023,batches[c].Count-j);Graphics.DrawMeshInstanced(cube,0,blockMaterials[c],batches[c].GetRange(j,count),null,ShadowCastingMode.Off,false,gameObject.layer);}
  if(t>DissolveDuration+DebrisLife){foreach(var r in renderers)if(r)r.enabled=false;Complete=true;}
 }
 void ResetVisuals(){if(renderers!=null)for(int i=0;i<renderers.Length;i++)if(renderers[i]&&originals[i]!=null){renderers[i].sharedMaterials=originals[i];renderers[i].enabled=true;}foreach(var m in copies)if(m)Destroy(m);copies.Clear();if(blockMaterials!=null)foreach(var m in blockMaterials)if(m)Destroy(m);blocks.Clear();dead=started=Complete=false;DissolveProgress=0;}
 void OnDisable(){if(health){health.Died-=Died;health.HealthChanged-=Changed;}ResetVisuals();}
}
