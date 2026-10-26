using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Uses the AI's supported sweep path verbatim. The pointed tip is inside its last sample.
[DisallowMultipleComponent]
public sealed class RailBeetleWarningArrow : MonoBehaviour
{
    readonly List<Vector3> vertices=new List<Vector3>(130);
    readonly List<Vector2> uv=new List<Vector2>(130);
    readonly List<int> triangles=new List<int>(384);
    static Material material;
    static int users;
    Mesh mesh;
    MeshRenderer graphic;
    GameObject holder;
    MaterialPropertyBlock block;
    float flowClock, chargeProgress;
    public float AnimationPhase => flowClock;
    public bool Visible => graphic && graphic.enabled;
    public float Length { get; private set; }
    public Vector3 Endpoint { get; private set; }
    public Mesh WarningMesh => mesh;
    void Awake() { users++; block=new MaterialPropertyBlock(); }
    void Initialize()
    {
        if(graphic) return;
        if(!material)
        {
            var shader=Resources.Load<Shader>("RailBeetleWarningArrow");
            if(!shader) return;
            material=new Material(shader) { name="Shared simple charge warning",hideFlags=HideFlags.DontSave };
        }
        holder=new GameObject("Supported charge warning arrow",typeof(MeshFilter),typeof(MeshRenderer));
        SceneManager.MoveGameObjectToScene(holder,gameObject.scene); holder.layer=gameObject.layer;
        mesh=new Mesh { name="Bounded charge arrow ribbon",hideFlags=HideFlags.DontSave }; mesh.MarkDynamic();
        holder.GetComponent<MeshFilter>().sharedMesh=mesh;
        graphic=holder.GetComponent<MeshRenderer>(); graphic.sharedMaterial=material; graphic.enabled=false;
        graphic.shadowCastingMode=ShadowCastingMode.Off; graphic.receiveShadows=false;
        graphic.lightProbeUsage=LightProbeUsage.Off; graphic.reflectionProbeUsage=ReflectionProbeUsage.Off;
    }
    public void SetPath(Vector3[] path,int count,float halfWidth,float charge)
    {
        if(path==null || count<2) { Clear(); return; }
        count=Mathf.Min(count,Mathf.Min(path.Length,65));
        Vector3 heading=path[count-1]-path[0]; heading.y=0;
        Length=heading.magnitude;
        if(Length<.1f) { Clear(); return; }
        Initialize(); if(!graphic) return;
        Endpoint=path[count-1];
        heading/=Length; Vector3 side=Vector3.Cross(Vector3.up,heading);
        vertices.Clear(); uv.Clear(); triangles.Clear();
        // No cap, bloom, tip mesh or animated element may extend beyond the endpoint.
        for(int i=0;i<count;i++)
        {
            float along=Mathf.Clamp01(Vector3.Dot(path[i]-path[0],heading)/Length);
            float taper=i==count-1?0f:halfWidth;
            // Local-to-world identity holder avoids moving the telegraph with cosmetic body recoil.
            Vector3 centre=path[i];
            vertices.Add(centre-side*taper); vertices.Add(centre+side*taper);
            uv.Add(new Vector2(0,along)); uv.Add(new Vector2(1,along));
            if(i==count-1) continue;
            int k=i*2;
            triangles.Add(k); triangles.Add(k+1); triangles.Add(k+2);
            triangles.Add(k+2); triangles.Add(k+1); triangles.Add(k+3);
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
        chargeProgress=Mathf.Clamp01(charge);
        if(!graphic.enabled) flowClock=0;
        ApplyAnimation(); graphic.enabled=true;
    }
    void LateUpdate()
    {
        if(!Visible) return;
        // Gameplay time freezes with pause/hit-stop; rate ramps continuously with windup.
        flowClock+=Time.deltaTime*Mathf.Lerp(.8f,1.7f,chargeProgress);
        ApplyAnimation();
    }
    void ApplyAnimation()
    {
        block.SetFloat("_Charge",chargeProgress); block.SetFloat("_Clock",flowClock);
        graphic.SetPropertyBlock(block);
    }
    public void Clear() { if(graphic) graphic.enabled=false; Length=0; flowClock=0; }
    void OnDisable()=>Clear();
    void OnDestroy()
    {
        if(holder) Destroy(holder); if(mesh) Destroy(mesh);
        if(--users==0 && material) { Destroy(material); material=null; }
    }
}
