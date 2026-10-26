using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MountedKnightTerritory : MonoBehaviour
{
    public Collider groundOverride; // Optional explicit surface for isolated combat scenes.
    public Vector2[] boundary = Array.Empty<Vector2>(); // World XZ polygon, independent of the moving actor.
    public Vector3[] nodes = Array.Empty<Vector3>();
    public int[] edgeA = Array.Empty<int>(), edgeB = Array.Empty<int>();
    public Vector3[] patrolPoints = Array.Empty<Vector3>();
    public float clearance = .67f;
    List<int>[] adjacent;bool[] available;Transform agent;
    OverworldV2Layout layout;
    readonly RaycastHit[] groundHits = new RaycastHit[64];
    readonly RaycastHit[] sweepHits = new RaycastHit[64];
    public bool Contains(Vector3 p)
    {
        bool inside=false;
        for(int i=0,j=boundary.Length-1;i<boundary.Length;j=i++){
            Vector2 a=boundary[i],b=boundary[j];
            if((a.y>p.z)!=(b.y>p.z)&&p.x<(b.x-a.x)*(p.z-a.y)/(b.y-a.y)+a.x)inside=!inside;
        }
        return inside;
    }
    bool Surface(Collider c){
        if(!layout)layout=FindFirstObjectByType<OverworldV2Layout>();
        return c==groundOverride||c.name=="LakeTerrain_REPLACE_OR_SCULPT"||(layout&&layout.IsNavigationSurface(c));
    }
    public bool Ground(Vector3 at,out Vector3 point)
    {
        point=at;float nearest=float.PositiveInfinity;bool found=false;
        int count=Physics.RaycastNonAlloc(at+Vector3.up*40,Vector3.down,groundHits,80,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++){
            var h=groundHits[i];if(!Surface(h.collider)||h.normal.y<.8f)continue;
            float d=Mathf.Abs(h.point.y-at.y);
            if(d<nearest){nearest=d;point=h.point+Vector3.up*.025f;found=true;}
        }
        return found;
    }
    public bool Clear(Vector3 a,Vector3 b,Transform owner,Transform player=null)
    {
        Vector3 delta=b-a;float distance=delta.magnitude;if(distance<.001f)return true;
        int count=Physics.CapsuleCastNonAlloc(a+Vector3.up*(clearance+.15f),a+Vector3.up*Mathf.Max(clearance+.15f,1.6f),clearance,delta/distance,sweepHits,distance,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++){
            var c=sweepHits[i].collider;
            if((owner&&c.transform.IsChildOf(owner))||(player&&c.transform.IsChildOf(player))||Surface(c))continue;
            return false;
        }
        return true;
    }
    public bool Fits(Vector3 p,Transform owner=null)
    {
        if(!Contains(p)||!Ground(p,out var floor))return false;
        foreach(var c in Physics.OverlapCapsule(floor+Vector3.up*(clearance+.15f),floor+Vector3.up*Mathf.Max(clearance+.15f,1.6f),clearance,~0,QueryTriggerInteraction.Ignore))
            if(!(owner&&c.transform.IsChildOf(owner))&&!Surface(c))return false;
        return true;
    }
    public void ConfigureAgent(Transform owner,float radius){
        agent=owner;clearance=Mathf.Max(clearance,radius);adjacent=null;available=null;BuildLinks();
        patrolPoints=(Vector3[])patrolPoints.Clone();for(int i=0;i<patrolPoints.Length;i++){int n=Nearest(patrolPoints[i]);if(n>=0)patrolPoints[i]=nodes[n];}
    }
    void BuildLinks(){
        adjacent=new List<int>[nodes.Length];available=new bool[nodes.Length];
        for(int i=0;i<nodes.Length;i++){adjacent[i]=new List<int>();available[i]=!agent||Fits(nodes[i],agent);}
        for(int i=0;i<edgeA.Length;i++){
            int a=edgeA[i],b=edgeB[i];if(!available[a]||!available[b]||(agent&&!Clear(nodes[a],nodes[b],agent)))continue;
            adjacent[a].Add(b);adjacent[b].Add(a);
        }
        // Patrol and chase must share one connected region for this actor's actual size.
        if(agent){
            var seen=new bool[nodes.Length];var largest=new List<int>();
            for(int i=0;i<nodes.Length;i++)if(available[i]&&!seen[i]){
                var component=new List<int>();var queue=new Queue<int>();seen[i]=true;queue.Enqueue(i);
                while(queue.Count>0){int at=queue.Dequeue();component.Add(at);foreach(int n in adjacent[at])if(!seen[n]){seen[n]=true;queue.Enqueue(n);}}
                if(component.Count>largest.Count)largest=component;
            }
            System.Array.Clear(available,0,available.Length);foreach(int n in largest)available[n]=true;
        }
    }
    public int Nearest(Vector3 p){int best=-1;float distance=float.PositiveInfinity;for(int i=0;i<nodes.Length;i++){
        if(available!=null&&!available[i])continue;float d=(nodes[i]-p).sqrMagnitude;if(d<distance){distance=d;best=i;}}
        return best;
    }
    public List<Vector3> Path(Vector3 from,Vector3 destination)
    {
        var result=new List<Vector3>();if(!Contains(from)||!Contains(destination)||nodes.Length==0)return result;
        if(adjacent==null||adjacent.Length!=nodes.Length)BuildLinks();
        int start=Nearest(from),end=Nearest(destination);if(start<0||end<0)return result;
        int n=nodes.Length;var cost=new float[n];var parent=new int[n];var closed=new bool[n];var open=new List<int>{start};
        for(int i=0;i<n;i++){cost[i]=float.PositiveInfinity;parent[i]=-1;}cost[start]=0;
        while(open.Count>0){
            int slot=0;float score=float.PositiveInfinity;
            for(int i=0;i<open.Count;i++){int q=open[i];float s=cost[q]+Vector3.Distance(nodes[q],nodes[end]);if(s<score){score=s;slot=i;}}
            int at=open[slot];open.RemoveAt(slot);if(at==end){for(int k=end;k>=0;k=parent[k])result.Add(nodes[k]);result.Reverse();return result;}closed[at]=true;
            foreach(int next in adjacent[at]){if(closed[next])continue;float c=cost[at]+Vector3.Distance(nodes[at],nodes[next]);if(c>=cost[next])continue;cost[next]=c;parent[next]=at;if(!open.Contains(next))open.Add(next);}
        }
        return result;
    }
    void OnDrawGizmosSelected(){
        Gizmos.color=new Color(1,.72f,.2f,.85f);
        for(int i=0;i<boundary.Length;i++){var a=new Vector3(boundary[i].x,8,boundary[i].y);var b=new Vector3(boundary[(i+1)%boundary.Length].x,8,boundary[(i+1)%boundary.Length].y);Ground(a,out a);Ground(b,out b);Gizmos.DrawLine(a+Vector3.up*.1f,b+Vector3.up*.1f);}
        Gizmos.color=Color.cyan;foreach(var p in patrolPoints)Gizmos.DrawWireSphere(p,.6f);
    }
}
