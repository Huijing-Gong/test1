using UnityEngine;
using System.Collections.Generic;
// Samples bone trajectories, not accumulated particle simulation: repeat/seek is deterministic.
public class ChargeFlameVFX:MonoBehaviour {
 public float startTime=1.52f,peakTime=1.69f,endTime=1.88f,sampleRate=120,groundY=-.09f;
 public string[] boneNames={"Hips","Chest","Head","Left_Shoulder","Right_Shoulder","Left_LowerArm","Right_LowerArm","Left_Hand","Right_Hand","Left_LowerLeg","Right_LowerLeg","Left_Foot","Right_Foot"};
 public Vector3[] trajectory;public float[] stepTimes;public Vector3[] stepPositions;public Transform[] anchors;
 public Camera targetCamera;
 Transform root;Material flame,ground,aura;Mesh flameMesh,groundMesh;MeshRenderer flameRenderer,groundRenderer;MeshFilter auraFilter;GameObject auraObject;
 readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uvs=new List<Vector2>();readonly List<Color> colors=new List<Color>();readonly List<int> triangles=new List<int>();
 public bool IsVisible=>root&&root.gameObject.activeSelf;
 public float Strength{get;private set;}
 public System.Func<int,float,Vector3> LiveTrack;
 public Vector3 Track(int bone,float time){if(LiveTrack!=null)return LiveTrack(bone,time);if(trajectory==null||trajectory.Length<boneNames.Length*2)return anchors!=null&&bone<anchors.Length?transform.InverseTransformPoint(anchors[bone].position):Vector3.zero;int frames=trajectory.Length/boneNames.Length;float f=Mathf.Clamp(time*sampleRate,0,frames-1);int i=Mathf.FloorToInt(f);return Vector3.Lerp(trajectory[i*boneNames.Length+bone],trajectory[Mathf.Min(i+1,frames-1)*boneNames.Length+bone],f-i);}
 void Build(){if(root)return;root=new GameObject("J065 Burning motion").transform;root.SetParent(transform,false);root.gameObject.hideFlags=HideFlags.DontSave;root.gameObject.layer=gameObject.layer;
 flame=new Material(Shader.Find("Boss/ChargeFire"));ground=new Material(Shader.Find("Boss/ChargeFire"));ground.SetFloat("_Flat",1);aura=new Material(Shader.Find("Boss/ChargeAura"));
 flameMesh=NewMesh("Body flames and wake",flame,out flameRenderer);groundMesh=NewMesh("Footstep embers",ground,out groundRenderer);
 }
 Mesh NewMesh(string name,Material material,out MeshRenderer renderer){var go=new GameObject(name);go.transform.SetParent(root,false);go.layer=gameObject.layer;var mesh=new Mesh(){name=name};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return mesh;}
 public void BindPoseMesh(Mesh mesh,Transform source){Build();if(!auraObject){auraObject=new GameObject("Burning body outline"){hideFlags=HideFlags.DontSave};auraObject.transform.SetParent(source,false);auraObject.layer=source.gameObject.layer;auraFilter=auraObject.AddComponent<MeshFilter>();auraObject.AddComponent<MeshRenderer>().sharedMaterial=aura;}auraFilter.sharedMesh=mesh;}
 void Clear(){vertices.Clear();uvs.Clear();colors.Clear();triangles.Clear();}
 void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float opacity){int k=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uvs.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)});for(int j=0;j<4;j++)colors.Add(new Color(1,1,1,opacity));triangles.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});}
 void Commit(Mesh mesh){mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();}
 float Hash(int i){return Mathf.Repeat(Mathf.Sin(i*127.1f+31.7f)*43758.5453f,1);}
 void Tongue(Vector3 origin,Vector3 bend,float height,float width,float phase,float opacity,Vector3 side){int k=vertices.Count;const int count=8;for(int j=0;j<count;j++){float u=j/(float)(count-1);Vector3 p=origin+Vector3.up*height*u+bend*u*u+side*Mathf.Sin(u*8+phase)*width*.35f*u;float w=width*(1-u)*(1-u)*(.7f+.3f*Mathf.Sin(u*5+phase));vertices.Add(p-side*w);vertices.Add(p+side*w);uvs.Add(new Vector2(0,u));uvs.Add(new Vector2(1,u));colors.Add(new Color(1,1,1,opacity));colors.Add(new Color(1,1,1,opacity));if(j>0){int m=k+j*2;triangles.AddRange(new[]{m-2,m,m-1,m-1,m,m+1});}}}
 public void Sample(float time,bool enabledEffect){Build();bool active=enabledEffect&&time>=startTime&&time<endTime+.65f;root.gameObject.SetActive(active);if(auraObject)auraObject.SetActive(active);if(!active){Strength=0;return;}
 float onset=Mathf.SmoothStep(0,1,Mathf.InverseLerp(startTime,startTime+.09f,time));float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(endTime+.03f,endTime+.30f,time));Strength=onset*fade;
 flame.SetFloat("_Phase",time);ground.SetFloat("_Phase",time);aura.SetFloat("_Phase",time);aura.SetFloat("_Strength",Strength*.85f);
 Vector3 delta=Track(0,peakTime+.03f)-Track(0,peakTime-.03f);delta.y=0;Vector3 direction=delta.sqrMagnitude>.00001f?delta.normalized:Vector3.forward;
 Vector3 side=targetCamera?transform.InverseTransformDirection(targetCamera.transform.right):Vector3.right;
 Clear();
 // Flames cling to the live bones. The body remains visible between tongues.
 for(int i=0;i<78;i++){int bone=i%boneNames.Length;float seed=Hash(i+1),phase=time*16+seed*30;Vector3 center=anchors!=null&&anchors[bone]?transform.InverseTransformPoint(anchors[bone].position):Track(bone,time);Vector3 radial=new Vector3(Mathf.Sin(seed*6.28f),0,Mathf.Cos(seed*6.28f))*(bone==0||bone==1?.09f:.035f);float height=(.10f+seed*.16f)*Strength;float bend=.04f+Mathf.Clamp01((Track(0,time+.015f)-Track(0,time-.015f)).magnitude/.12f)*.20f;
 Tongue(center+radial,-direction*bend,height,.030f+seed*.025f,phase,Strength*(.55f+seed*.3f),side);}
 // World-space wake stays at previous body positions; no independent projectile.
 for(int i=0;i<34;i++){float seed=Hash(i+100),age=.03f+seed*.24f;float born=time-age;if(born<startTime||born>endTime)continue;int bone=i%11;Vector3 p=Track(bone,born);float life=1-age/.30f;Tongue(p+Vector3.up*age*.12f,-direction*.06f,.12f+seed*.15f,.025f+seed*.022f,time*11+i,life*.5f,side);}
 // Lifting sparks follow the same historical positions and die quickly.
 for(int i=0;i<45;i++){float seed=Hash(i+400),age=Mathf.Repeat(time*1.7f+seed,1)*.28f;float born=time-age;if(born<startTime||born>endTime+.06f)continue;Vector3 p=Track(i%13,born)+new Vector3(Mathf.Sin(i*5)*age*.22f,age*.8f,-age*.3f);Quad(p-side*.003f,p+side*.003f,p+Vector3.up*.018f-side*.001f,p+Vector3.up*.018f+side*.001f,(1-age/.28f)*.7f);}
 if(stepTimes!=null)for(int i=0;i<stepTimes.Length;i++){float age=time-stepTimes[i];if(age<0||age>.55f)continue;float life=1-age/.55f;for(int j=0;j<4;j++)Tongue(stepPositions[i]+new Vector3(Mathf.Sin(j*2.4f)*.03f,0,Mathf.Cos(j*2.4f)*.04f),-direction*.03f,.13f*life,.035f*life,time*12+j,life*.7f,side);}
 Commit(flameMesh);Clear();
 if(stepTimes!=null)for(int i=0;i<stepTimes.Length;i++){float age=time-stepTimes[i];if(age<0||age>.65f)continue;float life=1-age/.65f;Vector3 p=stepPositions[i],right=Vector3.Cross(Vector3.up,direction)*(.08f+age*.03f),forward=direction*(.13f+age*.035f);Quad(p-right-forward,p+right-forward,p-right+forward,p+right+forward,life*.9f);}
 Commit(groundMesh);
 }
 void OnDestroy(){if(root)Destroy(root.gameObject);if(auraObject)Destroy(auraObject);foreach(var m in new[]{flame,ground,aura})if(m)Destroy(m);foreach(var mesh in new[]{flameMesh,groundMesh})if(mesh)Destroy(mesh);}
}
