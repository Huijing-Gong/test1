using System.Collections.Generic;
using UnityEngine;

// Small world-space comic accents, timed to the expression rather than combat time scale.
[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class PaimonEmotionEffects : MonoBehaviour
{
 PaimonCompanion companion;PaimonMotion motion;GameObject surface;Mesh mesh;Material material;
 readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();readonly List<Color> colors=new List<Color>();
 GameObject fadingSurface;Mesh fadingMesh;Color[] fadingColors;float fadingStart=-100;PaimonMotion.Action renderedAction;
 PaimonMotion.Action previous;float time;
 public int VisibleSymbols {get;private set;}
 void Awake(){companion=GetComponent<PaimonCompanion>();motion=GetComponent<PaimonMotion>();Ensure();}
 void Ensure(){if(surface)return;surface=new GameObject("Paimon expression accents");surface.transform.SetParent(transform,false);mesh=new Mesh{name="Paimon comic symbols"};mesh.MarkDynamic();surface.AddComponent<MeshFilter>().sharedMesh=mesh;material=new Material(Shader.Find("Companions/Paimon Emotion"));surface.AddComponent<MeshRenderer>().sharedMaterial=material;}
 void LateUpdate(){var action=companion.IdleAction;if(action!=previous){time=0;previous=action;}else time+=Time.unscaledDeltaTime;RenderPose(action,time,companion.Equipped&&companion.Visible&&companion.isActiveAndEnabled?companion.IdleActionBlend:0);}
 void FadePrevious(PaimonMotion.Action action)
 {
  if(action!=renderedAction){
   if(surface.activeSelf&&mesh.vertexCount>0){
    if(!fadingSurface){fadingSurface=new GameObject("Paimon fading accents");fadingSurface.transform.SetParent(transform,false);fadingMesh=new Mesh{name="Paimon accent crossfade"};fadingSurface.AddComponent<MeshFilter>().sharedMesh=fadingMesh;fadingSurface.AddComponent<MeshRenderer>().sharedMaterial=material;}
    fadingSurface.transform.position=surface.transform.position;fadingSurface.transform.rotation=surface.transform.rotation;fadingSurface.transform.localScale=surface.transform.localScale;
    fadingMesh.Clear();fadingMesh.vertices=mesh.vertices;fadingMesh.triangles=mesh.triangles;fadingColors=mesh.colors;fadingMesh.colors=fadingColors;fadingMesh.RecalculateBounds();fadingStart=Time.unscaledTime;fadingSurface.SetActive(true);
   }
   renderedAction=action;
  }
  if(fadingSurface&&fadingSurface.activeSelf){float p=(Time.unscaledTime-fadingStart)/.35f;if(p>=1)fadingSurface.SetActive(false);else{var faded=new Color[fadingColors.Length];float alpha=1-PaimonCompanion.GestureEase(p);for(int i=0;i<faded.Length;i++){faded[i]=fadingColors[i];faded[i].a*=alpha;}fadingMesh.colors=faded;}}
 }
 public void Orient(Camera camera){if(surface&&camera)surface.transform.rotation=camera.transform.rotation;}
 public void RenderPose(PaimonMotion.Action action,float t,float blend)
 {
  Ensure();FadePrevious(action);vertices.Clear();triangles.Clear();colors.Clear();VisibleSymbols=0;
  bool flourish=action==PaimonMotion.Action.StarFigureEight||action==PaimonMotion.Action.MoonSwoop||action==PaimonMotion.Action.HeartBounce||action==PaimonMotion.Action.CometSpin;
  bool concern=action==PaimonMotion.Action.Concern;bool shock=action==PaimonMotion.Action.Surprise,joy=action==PaimonMotion.Action.Joy||action==PaimonMotion.Action.SmileSway||action==PaimonMotion.Action.ShoulderPeek,question=action==PaimonMotion.Action.LookAround;
  float appear=Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.35f)/.35f));float alpha=blend*appear;
  surface.SetActive(alpha>.01f&&(shock||joy||question||concern||flourish));if(!surface.activeSelf)return;
  surface.transform.position=motion.FacePosition+Vector3.up*(flourish?-.035f:.22f);surface.transform.localScale=Vector3.one;Orient(Camera.main);
  if(flourish){
   if(action==PaimonMotion.Action.StarFigureEight){for(int i=0;i<7;i++){float angle=t*2.1f+i*Mathf.PI*2/7;Vector2 center=new Vector2(.31f*Mathf.Sin(angle),.17f*Mathf.Sin(angle*2)+.06f);Color color=i%2==0?new Color(.48f,.92f,1,alpha):new Color(.85f,.66f,1,alpha);Star(center,.033f+.009f*Mathf.Sin(t*3+i),color,angle*.5f);VisibleSymbols++;}}
   else if(action==PaimonMotion.Action.MoonSwoop){Color moon=new Color(.74f,.88f,1,alpha);Vector2 center=new Vector2(.23f,.16f+.025f*Mathf.Sin(t*2));Vector2 last=center+new Vector2(.07f,.07f);for(int i=1;i<=18;i++){float p=i/18f,a=Mathf.Lerp(45,315,p)*Mathf.Deg2Rad;Vector2 next=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.10f;Line(last,next,.008f+.025f*Mathf.Sin(p*Mathf.PI),moon);last=next;}for(int i=0;i<4;i++){float a=t*.9f+i*1.25f;Star(new Vector2(-.19f+.07f*Mathf.Sin(a),-.10f+i*.10f),.027f,new Color(.52f,.88f,1,alpha),a);}VisibleSymbols=5;}
   else if(action==PaimonMotion.Action.HeartBounce){for(int i=0;i<4;i++){float p=Mathf.Repeat(t*.3f+i*.25f,1),a=t*1.4f+i*1.8f;Heart(new Vector2(.25f*Mathf.Sin(a),-.19f+p*.50f),.035f+.018f*p,new Color(1,.48f+.08f*i,.69f,alpha*Mathf.Sin(p*Mathf.PI)));VisibleSymbols++;}for(int i=0;i<2;i++)Star(new Vector2((i==0?-1:1)*.29f,.16f),.029f,new Color(1,.88f,.50f,alpha),t+i);}
   else{for(int i=0;i<5;i++){float a=t*2.5f+i*Mathf.PI*2/5;Vector2 center=new Vector2(.32f*Mathf.Cos(a),.17f*Mathf.Sin(a));Color color=i%2==0?new Color(.45f,.9f,1,alpha):new Color(1,.77f,.49f,alpha);Star(center,.036f,color,a);for(int j=1;j<=3;j++){float b=a-j*.13f;Vector2 tail=new Vector2(.32f*Mathf.Cos(b),.17f*Mathf.Sin(b));Color trail=color;trail.a*=1-j*.22f;Line(center,tail,.009f,trail);}VisibleSymbols++;}}
  }else if(concern){Color blue=new Color(.48f,.82f,1,alpha);Vector2 center=new Vector2(.12f,.02f+.008f*Mathf.Sin(t*5));
   var drop=new Vector2[18];drop[0]=new Vector2(0,.055f);for(int i=1;i<18;i++){float a=Mathf.Lerp(0,Mathf.PI*2,(i-1)/16f);drop[i]=new Vector2(Mathf.Sin(a)*.022f,Mathf.Cos(a)*.027f-.01f);}Polygon(drop,blue,center);
   for(int i=0;i<3;i++)Line(new Vector2(-.12f+i*.035f,.09f),new Vector2(-.125f+i*.035f,.035f),.009f,new Color(.65f,.72f,1,alpha));VisibleSymbols=4;
  }else if(shock){float pop=1+.18f*Mathf.Sin(Mathf.Clamp01((t-.35f)/.6f)*Mathf.PI);Polygon(new[]{new Vector2(-.023f,.025f),new Vector2(.023f,.025f),new Vector2(.035f,.19f),new Vector2(-.035f,.19f)},new Color(1,.80f,.19f,alpha),Vector2.zero,pop);Disk(new Vector2(0,-.025f),.026f,new Color(1,.80f,.19f,alpha));VisibleSymbols=1;
   for(int i=0;i<2;i++)Star(new Vector2((i==0?-1:1)*.105f,.11f),.035f,new Color(1,.95f,.63f,alpha),t+i);
  }else if(joy){for(int i=0;i<3;i++){float offset=(i-1)*.13f;Vector2 center=new Vector2(offset,.04f+.07f*Mathf.Sin(t*2.8f+i*.9f));float radius=.045f+.009f*Mathf.Sin(t*4+i);if(action==PaimonMotion.Action.ShoulderPeek&&i==1)Heart(center,radius*1.3f,new Color(1,.48f,.63f,alpha));else Star(center,radius,new Color(1,.82f,.26f,alpha),t*.6f+i);VisibleSymbols++;}}
  else if(question){ // A curved question mark constructed without font dependencies.
   Color color=new Color(.55f,.91f,1,alpha);Vector2 last=new Vector2(-.04f,.13f);for(int i=1;i<=12;i++){float angle=Mathf.Lerp(150,-110,i/12f)*Mathf.Deg2Rad;Vector2 next=new Vector2(Mathf.Cos(angle)*.05f,.11f+Mathf.Sin(angle)*.055f);Line(last,next,.018f,color);last=next;}Line(last,new Vector2(0,.005f),.018f,color);Disk(new Vector2(0,-.04f),.017f,color);VisibleSymbols=1;}
  mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateBounds();
 }
 void Polygon(Vector2[] points,Color color,Vector2 center,float scale=1){int first=vertices.Count;vertices.Add(center);colors.Add(color);foreach(var p in points){vertices.Add(center+p*scale);colors.Add(color);}for(int i=0;i<points.Length;i++){triangles.Add(first);triangles.Add(first+1+i);triangles.Add(first+1+(i+1)%points.Length);}}
 void Disk(Vector2 center,float radius,Color color){var points=new Vector2[12];for(int i=0;i<12;i++){float a=i*Mathf.PI/6;points[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}Polygon(points,color,center);}
 void Line(Vector2 a,Vector2 b,float width,Color color){Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Vector2 center=(a+b)*.5f;Polygon(new[]{a+n-center,b+n-center,b-n-center,a-n-center},color,center);}
 void Star(Vector2 center,float radius,Color color,float rotation){var points=new Vector2[8];for(int i=0;i<8;i++){float a=i*Mathf.PI/4+rotation;points[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*(i%2==0?1:.33f);}Polygon(points,color,center);}
 void Heart(Vector2 center,float radius,Color color){var points=new Vector2[32];for(int i=0;i<32;i++){float t=i*Mathf.PI/16;points[i]=new Vector2(16*Mathf.Pow(Mathf.Sin(t),3),13*Mathf.Cos(t)-5*Mathf.Cos(2*t)-2*Mathf.Cos(3*t)-Mathf.Cos(4*t))*radius/16;}Polygon(points,color,center);}
 void OnDisable(){if(surface)surface.SetActive(false);if(fadingSurface)fadingSurface.SetActive(false);}
 void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);if(surface)Destroy(surface);if(fadingMesh)Destroy(fadingMesh);if(fadingSurface)Destroy(fadingSurface);}
}
