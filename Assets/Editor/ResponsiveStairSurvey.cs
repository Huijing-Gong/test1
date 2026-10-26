using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class ResponsiveStairSurvey
{
    static ResponsiveStairSurvey(){EditorApplication.update+=()=>{if(!File.Exists("Temp/stair-survey.txt")||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;File.Delete("Temp/stair-survey.txt");var log=new StringBuilder();foreach(var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(x=>x.name.ToLower().Contains("stair")||x.name.ToLower().Contains("steps"))){if(!c.sharedMesh)continue;var levels=c.sharedMesh.vertices.Select(v=>c.transform.TransformPoint(v).y).Select(y=>Mathf.Round(y*1000)/1000).Distinct().OrderBy(y=>y).ToArray();log.AppendLine(c.name+"|"+c.bounds.center+"|vertices="+c.sharedMesh.vertexCount+"|levels="+string.Join(",",levels.Take(45)));}var source=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).FirstOrDefault(c=>c.name=="Own R4 six short physical stairs75");
if(source){Physics.SyncTransforms();var vertices=source.sharedMesh.vertices;log.AppendLine("DIRECT SOURCE enabled="+source.enabled);for(int k=0;k<6;k++){Bounds b=new Bounds(source.transform.TransformPoint(vertices[k*38]),Vector3.zero);for(int j=1;j<38;j++)b.Encapsulate(source.transform.TransformPoint(vertices[k*38+j]));var point=new Vector3(b.center.x,b.max.y+.06f,b.center.z);var ray=new Ray(point+Vector3.up,Vector3.down);log.AppendLine("sample="+point+" direct="+(source.Raycast(ray,out var direct,3)?direct.point.ToString():"none"));foreach(var h in Physics.RaycastAll(ray,3,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))log.AppendLine("hit "+h.collider.name+"|"+h.point+"|"+h.normal);}}
Directory.CreateDirectory("LocalDocs/StairReview");File.WriteAllText("LocalDocs/StairReview/map-survey.txt",log.ToString());};}
}
