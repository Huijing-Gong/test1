using UnityEngine;
using UnityEngine.Rendering;

// Shared, texture-free status graphics. Driven only by the actual repair channel.
[DisallowMultipleComponent]
public sealed class HealingRecipientStatus : MonoBehaviour
{
    static Mesh cylinder, quad;
    static Material material;
    static int users;
    readonly MeshRenderer[] graphics = new MeshRenderer[4];
    MaterialPropertyBlock block;
    Collider body;
    bool ready;
    public int VisibleCount { get { int n=0; foreach(var r in graphics) if(r && r.enabled) n++; return n; } }

    void Awake()
    {
        block = new MaterialPropertyBlock(); body = GetComponent<CapsuleCollider>(); users++;
    }
    public void Initialize()
    {
        if(ready) return;
        if(!material)
        {
            var shader=Resources.Load<Shader>("HealingRecipientStatus");
            if(!shader) return;
            material=new Material(shader) { name="Shared healing status", hideFlags=HideFlags.DontSave };
        }
        if(!quad)
        {
            quad=new Mesh { name="Healing plus billboard", hideFlags=HideFlags.DontSave };
            quad.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f),new Vector3(.5f,-.5f)};
            quad.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
            quad.triangles=new[]{0,1,2,0,2,3}; quad.RecalculateBounds();
        }
        if(!cylinder)
        {
            const int segments=48;
            var vertices=new Vector3[(segments+1)*2]; var uv=new Vector2[vertices.Length];
            var triangles=new int[segments*6];
            for(int i=0;i<=segments;i++)
            {
                float a=i*Mathf.PI*2/segments;
                vertices[i*2]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                vertices[i*2+1]=vertices[i*2]+Vector3.up;
                uv[i*2]=new Vector2((float)i/segments,0); uv[i*2+1]=new Vector2((float)i/segments,1);
                if(i==segments) continue;
                int j=i*6,k=i*2;
                triangles[j]=k; triangles[j+1]=k+1; triangles[j+2]=k+2;
                triangles[j+3]=k+2; triangles[j+4]=k+1; triangles[j+5]=k+3;
            }
            cylinder=new Mesh { name="Healing open column", hideFlags=HideFlags.DontSave };
            cylinder.vertices=vertices; cylinder.uv=uv; cylinder.triangles=triangles; cylinder.RecalculateBounds();
        }
        for(int i=0;i<graphics.Length;i++)
        {
            var go=new GameObject(i==0?"Healing volume":"Rising healing plus "+i,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(transform,false); go.layer=gameObject.layer;
            go.GetComponent<MeshFilter>().sharedMesh=i==0?cylinder:quad;
            var r=go.GetComponent<MeshRenderer>(); r.sharedMaterial=material; r.enabled=false;
            r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off; r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            graphics[i]=r;
        }
        ready=true;
    }
    public void Show(float strength)
    {
        Initialize(); if(!ready) return;
        bool show=strength>.005f;
        var bounds=body && body.enabled?body.bounds:new Bounds(transform.position+Vector3.up,Vector3.one*1.5f);
        float radius=Mathf.Clamp(Mathf.Max(bounds.extents.x,bounds.extents.z)+.2f,.55f,1.6f);
        float height=Mathf.Max(bounds.size.y+.35f,1.3f);
        Vector3 bottom=new Vector3(bounds.center.x,bounds.min.y+.035f,bounds.center.z);
        var camera=Camera.main;
        Vector3 right=camera?camera.transform.right:Vector3.right;
        for(int i=0;i<graphics.Length;i++)
        {
            var r=graphics[i]; r.enabled=show; if(!show) continue;
            block.Clear(); block.SetFloat("_Strength",strength); block.SetFloat("_Clock",Time.time);
            block.SetFloat("_Mode",i==0?0:1);
            if(i==0)
            {
                r.transform.SetPositionAndRotation(bottom,Quaternion.identity);
                r.transform.localScale=new Vector3(radius,height,radius);
                // Cancel inherited actor scale without changing the actor or its colliders.
                var s=transform.lossyScale;
                r.transform.localScale=new Vector3(radius/Mathf.Max(.001f,Mathf.Abs(s.x)),height/Mathf.Max(.001f,Mathf.Abs(s.y)),radius/Mathf.Max(.001f,Mathf.Abs(s.z)));
            }
            else
            {
                float phase=Mathf.Repeat(Time.time*.62f+(i-1)/3f,1);
                float side=i==2?-1:1;
                r.transform.position=bottom+Vector3.up*(height*(.32f+phase*.63f))+right*(radius*(i==3?.35f:.86f)*side);
                if(camera) { r.transform.rotation=camera.transform.rotation; r.transform.position-=camera.transform.forward*radius*.55f; }
                float size=Mathf.Clamp(height*.23f,.34f,.58f);
                var s=transform.lossyScale;
                r.transform.localScale=new Vector3(size/Mathf.Max(.001f,Mathf.Abs(s.x)),size/Mathf.Max(.001f,Mathf.Abs(s.y)),1);
                block.SetFloat("_Strength",strength*Mathf.Min(1,(1-phase)*5));
            }
            r.SetPropertyBlock(block);
        }
    }
    public void Clear() { foreach(var r in graphics) if(r) r.enabled=false; }
    void OnDisable()=>Clear();
    void OnDestroy()
    {
        foreach(var r in graphics) if(r) Destroy(r.gameObject);
        if(--users==0)
        {
            if(material) Destroy(material); if(quad) Destroy(quad); if(cylinder) Destroy(cylinder);
            material=null; quad=null; cylinder=null;
        }
    }
}
