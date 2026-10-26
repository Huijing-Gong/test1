using System.Collections.Generic;
using UnityEngine;

// The navigation collider sits below collider-free paving. Sample the visible surface too.
public sealed class WorldFootstepSurfaces:MonoBehaviour
{
    public MeshFilter[] stoneSurfaces=System.Array.Empty<MeshFilter>();
    sealed class Geometry{public Vector3[] vertices;public int[] triangles;}
    readonly Dictionary<Mesh,Geometry> geometry=new Dictionary<Mesh,Geometry>();
    public bool IsStone(Vector3 point)
    {
        foreach(var filter in stoneSurfaces){
            if(!filter||!filter.gameObject.activeInHierarchy)continue;
            var renderer=filter.GetComponent<Renderer>();if(!renderer||!renderer.enabled)continue;
            var bounds=renderer.bounds;
            if(point.x<bounds.min.x||point.x>bounds.max.x||point.z<bounds.min.z||point.z>bounds.max.z||
                point.y<bounds.min.y-.35f||point.y>bounds.max.y+.35f)continue;
            var mesh=filter.sharedMesh;if(!mesh||!mesh.isReadable)continue;
            if(!geometry.TryGetValue(mesh,out var data)){data=new Geometry{vertices=mesh.vertices,triangles=mesh.triangles};geometry.Add(mesh,data);}
            var local=filter.transform.InverseTransformPoint(point);
            for(int i=0;i+2<data.triangles.Length;i+=3){
                var a=data.vertices[data.triangles[i]];var b=data.vertices[data.triangles[i+1]];var c=data.vertices[data.triangles[i+2]];
                float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(den)<.000001f)continue;
                float u=((b.z-c.z)*(local.x-c.x)+(c.x-b.x)*(local.z-c.z))/den;
                float v=((c.z-a.z)*(local.x-c.x)+(a.x-c.x)*(local.z-c.z))/den;
                if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
                var surface=filter.transform.TransformPoint(new Vector3(local.x,a.y*u+b.y*v+c.y*(1-u-v),local.z));
                if(Mathf.Abs(surface.y-point.y)<.35f)return true;
            }
        }
        return false;
    }
}
