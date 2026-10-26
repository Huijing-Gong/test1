using UnityEngine;using System.Collections.Generic;
public class LandingImpactVFX:MonoBehaviour {
 public Transform RuntimeRoot=>root?root.transform:null;

 public float waveRadius=-1;
 public float impactTime=2.09f,lifetime=1.25f;public Vector3 landingPoint=new Vector3(-.0098f,-.083f,.307f);public bool IsVisible=>root&&root.activeSelf;
 GameObject root;Mesh mesh;Material material;readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<Color> colors=new List<Color>();readonly List<int> triangles=new List<int>();
 public void Sample(float time,bool enabled,Camera camera){if(!root){root=new GameObject("J056 landing burst"){hideFlags=HideFlags.DontSave};root.layer=gameObject.layer;mesh=new Mesh(){name="Landing shockwave",hideFlags=HideFlags.HideAndDontSave};root.AddComponent<MeshFilter>().sharedMesh=mesh;material=new Material(Shader.Find("Boss/LandingImpact")){hideFlags=HideFlags.HideAndDontSave};root.AddComponent<MeshRenderer>().sharedMaterial=material;}float t=time-impactTime;root.SetActive(enabled&&t>=0&&t<lifetime);if(!root.activeSelf)return;root.transform.position=landingPoint;vertices.Clear();uv.Clear();colors.Clear();triangles.Clear();
 // A single ground-locked impact: fast bright ring, broken flame crown, sparks, fading radial traces.
 float fade=Mathf.Pow(Mathf.Clamp01(1-t/.68f),1.5f),radius=.12f+1.18f*(1-Mathf.Exp(-t*6));
 if(waveRadius>=0)radius=waveRadius;
 Ring(radius,.07f+.10f*(1-t/.68f),.015f,new Color(5,1.65f,.12f,fade));
 if(t<.4f)Ring(.08f+t*2.2f,.12f,.022f,new Color(5,2.9f,.55f,Mathf.Pow(1-t/.4f,2)));
 for(int i=0;i<20;i++){float a=i*Mathf.PI*2/20;Vector3 dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));float h=(.28f+Hash(i)*.45f)*Mathf.Sin(Mathf.Clamp01(t/.42f)*Mathf.PI);float r=.12f+t*(.7f+Hash(i+5));Vector3 b=dir*r,tip=b+dir*(.1f+t*.35f)+Vector3.up*h;float alpha=Mathf.Clamp01(1-t/.5f);Ribbon(b,tip,Vector3.Cross(dir,Vector3.up)*(.035f+.045f*alpha),new Color(5,1.3f,.09f,alpha));}
 for(int i=0;i<42;i++){float a=Hash(i+78)*Mathf.PI*2,speed=.7f+Hash(i+132)*1.4f;Vector3 dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));float age=t,life=.4f+Hash(i+14)*.6f;if(age>life)continue;Vector3 pos=dir*(.06f+age*speed)+Vector3.up*Mathf.Max(.025f,age*(.8f+Hash(i+33)*1.3f)-1.8f*age*age);Vector3 tail=pos-dir*.06f-Vector3.up*.025f;Ribbon(tail,pos,camera.transform.right*.009f,new Color(4,2.0f,.35f,1-age/life));}
 for(int i=0;i<15;i++){float a=i*2.39996f;Vector3 dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),side=Vector3.Cross(dir,Vector3.up);float length=.4f+Hash(i+91)*.5f;float alpha=Mathf.Clamp01(t/.035f)*Mathf.Pow(Mathf.Clamp01(1-t/1.05f),2);Vector3 previous=dir*.12f+Vector3.up*.012f;for(int j=1;j<=4;j++){Vector3 next=dir*(.12f+length*j/4)+side*(Hash(i*4+j)-.5f)*.1f+Vector3.up*.012f;Ribbon(previous,next,side*.012f,new Color(2.5f,.55f,.04f,alpha));previous=next;}}
 mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();}
 void Ring(float radius,float width,float y,Color color){for(int i=0;i<128;i++){float a=i*Mathf.PI*2/128,b=(i+1)*Mathf.PI*2/128;float r1=radius*(1+.018f*Mathf.Sin(a*13)),r2=radius*(1+.018f*Mathf.Sin(b*13));Vector3 d1=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),d2=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));Quad(d1*(r1-width)+Vector3.up*y,d1*(r1+width)+Vector3.up*y,d2*(r2+width)+Vector3.up*y,d2*(r2-width)+Vector3.up*y,color);}}
 void Ribbon(Vector3 a,Vector3 b,Vector3 width,Color color){Quad(a-width,a+width,b+width*.15f,b-width*.15f,color);}
 void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color){int k=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)});for(int j=0;j<4;j++)colors.Add(color);triangles.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}
 static float Hash(int i)=>Mathf.Repeat(Mathf.Sin(i*127.1f)*43758.5453f,1);
 void OnDestroy(){if(root)Destroy(root);if(mesh)Destroy(mesh);if(material)Destroy(material);}
}
