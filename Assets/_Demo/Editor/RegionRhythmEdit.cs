using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
public static class RegionRhythmEdit
{
 public static void Apply()
 {
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.isDirty||scene.name!="OverworldBaseline")throw new System.Exception("Expected clean baseline in Edit mode");
 if(Mathf.Abs(Mathf.DeltaAngle(GameObject.Find("Expansion_IslandScreen").transform.eulerAngles.y,-22))<.1f)throw new System.Exception("Already applied; edit current scene locally instead");
 var land=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<MeshCollider>();var mesh=land.sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Region rhythm terrain");var vs=mesh.vertices;
 for(int i=0;i<vs.Length;i++){var p=vs[i];float edge=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.z));if(edge>69){float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(69,82,edge));p.y-=t*(1.8f+1.2f*Mathf.Sin(p.x*.095f+p.z*.063f));}var rel=new Vector2(p.x-3,p.z-3);float d=rel.magnitude;if(d>7&&d<15&&p.z>-4){float w=Mathf.Sin(Mathf.InverseLerp(7,15,d)*Mathf.PI)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-4,0,p.z));float a=Mathf.Atan2(rel.y,rel.x);p.x+=1.7f*Mathf.Sin(a*2+.5f)*w;p.z+=.8f*Mathf.Cos(a*3)*w;}vs[i]=p;}
 mesh.vertices=vs;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);land.sharedMesh=null;land.sharedMesh=mesh;Physics.SyncTransforms();
 var ts=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);var trunks=ts.Where(t=>t.name=="TreeTrunk_REPLACE").OrderBy(t=>t.position.x).ThenBy(t=>t.position.z).ToArray();var crowns=ts.Where(t=>t.name=="TreeCanopy_REPLACE").ToList();
 Vector2[] places={new Vector2(-60,-27),new Vector2(-63,-24),new Vector2(-59,-22),new Vector2(-47,-25),new Vector2(-44,-22),new Vector2(-47,-18),new Vector2(-52,-17),new Vector2(-44,-3),new Vector2(-47,1),new Vector2(-43,4),new Vector2(-61,17),new Vector2(-65,21),new Vector2(-59,22),new Vector2(-37,30),new Vector2(66,-27),new Vector2(67,5),new Vector2(62,29),new Vector2(29,44)};
 for(int i=0;i<trunks.Length;i++){var t=trunks[i];var crown=crowns.OrderBy(c=>Vector2.Distance(new Vector2(c.position.x,c.position.z),new Vector2(t.position.x,t.position.z))).First();crowns.Remove(crown);Undo.RecordObject(t,"Cluster tree");Undo.RecordObject(crown,"Cluster crown");var dest=new Vector3(places[i].x,Ground(land,places[i])+1.4f,places[i].y);crown.position+=dest-t.position;t.position=dest;crown.localScale=Vector3.one*(i%3==0?5.2f:i%3==1?4.2f:3.6f);}
 var rocks=ts.Where(t=>t.name=="Expansion_PerimeterRock").OrderBy(t=>t.position.x).ThenBy(t=>t.position.z).ToArray();Vector2[] rp={new Vector2(-77,-51),new Vector2(-71,-43),new Vector2(-78,-9),new Vector2(-73,33),new Vector2(-69,39),new Vector2(-76,62),new Vector2(75,-61),new Vector2(69,-56),new Vector2(78,-19),new Vector2(74,17),new Vector2(68,54),new Vector2(76,62)};
 for(int i=0;i<rocks.Length;i++){var t=rocks[i];Undo.RecordObject(t,"Break perimeter repetition");float h=2.5f+(i%4)*1.15f;t.localScale=new Vector3(6+i%3*2,h,7+i%4*2);t.position=new Vector3(rp[i].x,Ground(land,rp[i])+h*.36f,rp[i].y);t.rotation=Quaternion.Euler(0,i*37,0);}
 var screen=GameObject.Find("Expansion_IslandScreen").transform;Undo.RecordObject(screen,"Angle island reveal");screen.rotation=Quaternion.Euler(0,-22,0);screen.localScale=new Vector3(6,2.5f,.7f);screen.position=new Vector3(5,2.25f,-.5f);
 var walls=ts.Where(t=>t.name=="EasternRuinWall_REPLACE").OrderBy(t=>t.position.z).ToArray();Vector3[] wp={new Vector3(45,4.3f,10),new Vector3(39,4.8f,14),new Vector3(39,4.6f,19),new Vector3(39,4.4f,24),new Vector3(61,4.6f,14)};for(int i=0;i<walls.Length;i++){Undo.RecordObject(walls[i],"Court enclosure");walls[i].position=wp[i];walls[i].localScale=i==0?new Vector3(8,2.6f,.8f):i==4?new Vector3(.8f,3.2f,10):new Vector3(.8f,(wp[i].y-3)*2,4.8f);}
 Physics.SyncTransforms();Object.FindFirstObjectByType<BaselineWorld>().RefreshNavigation();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
 }
 static float Ground(Collider c,Vector2 p){c.Raycast(new Ray(new Vector3(p.x,60,p.y),Vector3.down),out var h,100);return h.point.y;}
}


