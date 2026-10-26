using UnityEngine;
using System.Collections.Generic;
public class DeathDissolveVFX:MonoBehaviour {
 public Transform RuntimeRoot=>dustObject?dustObject.transform:null;

 public float startTime=3.5f,duration=1.65f;public float Progress{get;private set;}public bool IsActive{get;private set;}
 Renderer body;Material[] original, dissolve;Mesh dust;Material dustMaterial;GameObject dustObject;
 public void Bind(Renderer renderer){if(body==renderer)return;Restore();body=renderer;original=body.sharedMaterials;dissolve=new Material[original.Length];for(int i=0;i<original.Length;i++){dissolve[i]=new Material(Shader.Find("Boss/DeathDissolve")){hideFlags=HideFlags.HideAndDontSave};dissolve[i].mainTexture=original[i].mainTexture;}}
 public void Sample(float time,bool active,Mesh pose,Transform source,Camera camera){IsActive=active;Progress=active?Mathf.Clamp01((time-startTime)/duration):0;if(!body)return;body.sharedMaterials=active?dissolve:original;foreach(var m in dissolve)m.SetFloat("_Progress",Progress);
 if(!dustObject){dustObject=new GameObject("J026 rising gold dust"){hideFlags=HideFlags.DontSave};dustObject.layer=gameObject.layer;dust=new Mesh(){name="Death dust",hideFlags=HideFlags.HideAndDontSave};dustObject.AddComponent<MeshFilter>().sharedMesh=dust;dustMaterial=new Material(Shader.Find("Boss/DeathDust")){hideFlags=HideFlags.HideAndDontSave};dustObject.AddComponent<MeshRenderer>().sharedMaterial=dustMaterial;}
 dustObject.SetActive(active&&time>startTime&&time<startTime+duration+1.1f);if(!dustObject.activeSelf || !pose || !source || !camera || pose.vertexCount==0)return;
 var vs=new List<Vector3>();var uv=new List<Vector2>();var cs=new List<Color>();var ts=new List<int>();var points=pose.vertices;Vector3 right=camera.transform.right,up=camera.transform.up;
 for(int i=0;i<480;i++){float seed=Hash(i+1),born=startTime+seed*duration*.95f,age=time-born,life=.55f+Hash(i+133)*.55f;if(age<0||age>life)continue;Vector3 p=source.TransformPoint(points[(i*7919)%points.Length]);p+=new Vector3(Mathf.Sin(i*2.4f+age*3)*.065f,age*(.25f+seed*.3f),Mathf.Cos(i*1.3f+age*2)*.055f)*age;float size=(.007f+Hash(i+53)*.014f)*(1-age/life);int k=vs.Count;vs.Add(p-right*size-up*size);vs.Add(p+right*size-up*size);vs.Add(p+right*size+up*size);vs.Add(p-right*size+up*size);uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(1,1));uv.Add(new Vector2(0,1));Color c=new Color(4,2.1f,.45f,Mathf.Sin(age/life*Mathf.PI));for(int j=0;j<4;j++)cs.Add(c);ts.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}dust.Clear();dust.SetVertices(vs);dust.SetUVs(0,uv);dust.SetColors(cs);dust.SetTriangles(ts,0);dust.RecalculateBounds();}
 static float Hash(int n){return Mathf.Repeat(Mathf.Sin(n*127.1f)*43758.5453f,1);}
 void Restore(){if(body&&original!=null)body.sharedMaterials=original;if(dissolve!=null)foreach(var m in dissolve)if(m)Destroy(m);}
 void OnDestroy(){Restore();if(dustObject)Destroy(dustObject);if(dust)Destroy(dust);if(dustMaterial)Destroy(dustMaterial);}
}
