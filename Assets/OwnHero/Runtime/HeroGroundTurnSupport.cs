using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfGraphic.Hero
{
    // A conservative full-turn footprint must be covered by one static support.
    // Ray hits locate that support; they do not prove area coverage.
    public sealed class HeroGroundTurnSupport
    {
        const int Sides=32, MaxCandidates=4096, MaxFragments=8192;
        const double NumericalArea=1e-16;
        readonly Collider support;
        readonly Vector3 position,scale,boxCenter,boxSize;
        readonly Quaternion rotation;
        readonly UnityEngine.Matrix4x4 matrix;
        readonly Mesh mesh;
        readonly MeshColliderCookingOptions cooking;
        readonly List<Vector3> vertices=new List<Vector3>(), scratchVertices=new List<Vector3>();
        readonly List<int> indices=new List<int>(), scratchIndices=new List<int>();
        readonly int[] counts;
        Vector3[] world;P[] projected;
        public float Radius {get;private set;}
        public int CandidateTriangles {get;private set;}
        public string Failure {get;private set;}

        struct P
        {
            public double x,z;
            public P(double a,double b){x=a;z=b;}
        }
        HeroGroundTurnSupport(Collider c)
        {
            support=c;matrix=c.transform.localToWorldMatrix;position=c.transform.position;rotation=c.transform.rotation;scale=c.transform.lossyScale;
            if(c is BoxCollider b){boxCenter=b.center;boxSize=b.size;}
            if(c is MeshCollider m){mesh=m.sharedMesh;cooking=m.cookingOptions;if(mesh)counts=new int[mesh.subMeshCount];}
        }
        public static bool TryCreate(Collider c,Vector3 pivot,float floor,float radius,out HeroGroundTurnSupport value,HeroGroundTurnSupport reusable=null)
        {
            value=null;
            if(!c||c.isTrigger||c.attachedRigidbody||!c.enabled||!c.gameObject.activeInHierarchy||
                !float.IsFinite(radius)||radius<=0||radius>2)return false;
            var v=reusable!=null&&reusable.support==c&&reusable.Unchanged(true)?reusable:new HeroGroundTurnSupport(c);v.Radius=radius;v.CandidateTriangles=0;
            if(!v.Covered(pivot,floor,radius))return false;
            value=v;return true;
        }
        public bool Unchanged(bool geometry)
        {
            if(!support||!support.transform.localToWorldMatrix.Equals(matrix)||support.isTrigger||support.attachedRigidbody||!support.enabled||!support.gameObject.activeInHierarchy||
                Vector3.Distance(support.transform.position,position)>.00001f||
                Quaternion.Angle(support.transform.rotation,rotation)>.001f||
                Vector3.Distance(support.transform.lossyScale,scale)>.00001f)return false;
            if(support is BoxCollider b)return b.center.Equals(boxCenter)&&b.size.Equals(boxSize);
            if(!(support is MeshCollider m)||m.convex||m.sharedMesh!=mesh||!mesh||!mesh.isReadable||
                m.cookingOptions!=cooking||mesh.subMeshCount!=counts.Length||mesh.vertexCount!=vertices.Count)return false;
            if(!geometry)return true;
            // Compare exact float components and every index, including same-bounds edits.
            // Lists are reserved at admission; no array allocation per running sample.
            mesh.GetVertices(scratchVertices);
            if(scratchVertices.Count!=vertices.Count)return false;
            for(int i=0;i<vertices.Count;i++)if(!scratchVertices[i].Equals(vertices[i]))return false;
            int at=0;
            for(int s=0;s<counts.Length;s++){
                if(mesh.GetTopology(s)!=MeshTopology.Triangles)return false;
                mesh.GetIndices(scratchIndices,s,true);
                if(scratchIndices.Count!=counts[s])return false;
                for(int i=0;i<scratchIndices.Count;i++)if(scratchIndices[i]!=indices[at++])return false;
            }
            return true;
        }
        bool Covered(Vector3 pivot,float floor,float radius)
        {
            var remaining=new List<List<P>>{new List<P>(Sides)};
            double outer=radius/Math.Cos(Math.PI/Sides);
            for(int i=0;i<Sides;i++){double a=2*Math.PI*i/Sides;remaining[0].Add(new P(Math.Cos(a)*outer,Math.Sin(a)*outer));}
            var points=new List<Vector3>();
            if(support is BoxCollider box){
                if(Vector3.Dot(box.transform.up,Vector3.up)<.99863f)return false;
                var c=box.center;var e=box.size*.5f;
                foreach(var p in new[]{new Vector3(c.x-e.x,c.y+e.y,c.z-e.z),new Vector3(c.x-e.x,c.y+e.y,c.z+e.z),new Vector3(c.x+e.x,c.y+e.y,c.z+e.z),new Vector3(c.x+e.x,c.y+e.y,c.z-e.z)})
                    points.Add(box.transform.TransformPoint(p));
                for(int i=0;i<points.Count;i++)if(Math.Abs(points[i].y-floor)>.001)return false;
                Subtract(remaining,Local(points[0],pivot),Local(points[1],pivot),Local(points[2],pivot));
                Subtract(remaining,Local(points[0],pivot),Local(points[2],pivot),Local(points[3],pivot));
                CandidateTriangles=2;return remaining.Count==0;
            }
            if(!(support is MeshCollider mc)||mc.convex||!mesh||!mesh.isReadable)return false;
            if(world==null){
                mesh.GetVertices(vertices);scratchVertices.Capacity=vertices.Count;
                world=new Vector3[vertices.Count];projected=new P[vertices.Count];
                for(int i=0;i<world.Length;i++){
                    world[i]=mc.transform.TransformPoint(vertices[i]);
                    if(!float.IsFinite(world[i].x)||!float.IsFinite(world[i].y)||!float.IsFinite(world[i].z))return false;
                }
                // Retain a complete immutable geometry snapshot. Reuse requires exact
                // equality, including same-bounds vertex/index changes.
                for(int sub=0;sub<counts.Length;sub++){
                    if(mesh.GetTopology(sub)!=MeshTopology.Triangles)return false;
                    mesh.GetIndices(scratchIndices,sub,true);counts[sub]=scratchIndices.Count;
                    if(counts[sub]%3!=0)return false;indices.AddRange(scratchIndices);
                }
            }
            for(int i=0;i<world.Length;i++)projected[i]=Local(world[i],pivot);
            for(int i=0;i<indices.Count;i+=3){
                int ia=indices[i],ib=indices[i+1],ic=indices[i+2];
                if(ia<0||ib<0||ic<0||ia>=world.Length||ib>=world.Length||ic>=world.Length)return false;
                P a=projected[ia],b=projected[ib],c=projected[ic];
                if(Math.Min(a.x,Math.Min(b.x,c.x))>outer||Math.Max(a.x,Math.Max(b.x,c.x))< -outer||
                    Math.Min(a.z,Math.Min(b.z,c.z))>outer||Math.Max(a.z,Math.Max(b.z,c.z))< -outer)continue;
                Vector3 wa=world[ia],wb=world[ib],wc=world[ic];
                if(Math.Abs(wa.y-floor)>.001||Math.Abs(wb.y-floor)>.001||Math.Abs(wc.y-floor)>.001||
                    Vector3.Cross(wb-wa,wc-wa).normalized.y<.99863f)continue;
                if(++CandidateTriangles>MaxCandidates)return false;
                if(!Subtract(remaining,a,b,c))return false;
            }
            return remaining.Count==0;
        }
        static P Local(Vector3 p,Vector3 origin)=>new P((double)p.x-origin.x,(double)p.z-origin.z);
        static double Cross(P a,P b,P p){
            double dx=b.x-a.x,dz=b.z-a.z,value=dx*(p.z-a.z)-dz*(p.x-a.x);
            // Resolve only sub-picometre classification noise on shared edges.
            return Math.Abs(value)<=1e-12*Math.Sqrt(dx*dx+dz*dz)?0:value;
        }
        static double Area(List<P> p){double a=0;for(int i=0;i<p.Count;i++){var x=p[i];var y=p[(i+1)%p.Count];a+=x.x*y.z-x.z*y.x;}return Math.Abs(a)*.5;}
        static List<P> Clip(List<P> input,P a,P b,bool inside)
        {
            var output=new List<P>(input.Count+1);if(input.Count==0)return output;
            var prev=input[input.Count-1];double dp=Cross(a,b,prev);bool kp=inside?dp>=0:dp<=0;
            foreach(var curr in input){
                double dc=Cross(a,b,curr);bool kc=inside?dc>=0:dc<=0;
                if(kc!=kp){double t=dp/(dp-dc);t=Math.Max(0,Math.Min(1,t));output.Add(new P(prev.x+(curr.x-prev.x)*t,prev.z+(curr.z-prev.z)*t));}
                if(kc)output.Add(curr);
                prev=curr;dp=dc;kp=kc;
            }
            return output;
        }
        static bool Subtract(List<List<P>> fragments,P a,P b,P c)
        {
            double orientation=Cross(a,b,c);if(Math.Abs(orientation)<1e-20)return true;
            if(orientation<0){var swap=b;b=c;c=swap;}
            var next=new List<List<P>>();P[] triangle={a,b,c};
            foreach(var fragment in fragments){
                var inside=fragment;
                for(int e=0;e<3&&inside.Count>=3;e++){
                    var outside=Clip(inside,triangle[e],triangle[(e+1)%3],false);
                    if(outside.Count>=3&&Area(outside)>NumericalArea)next.Add(outside);
                    inside=Clip(inside,triangle[e],triangle[(e+1)%3],true);
                    if(next.Count>MaxFragments)return false;
                }
            }
            fragments.Clear();fragments.AddRange(next);return true;
        }
    }
}
