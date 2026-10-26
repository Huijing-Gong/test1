using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Visual-only, owned by the cached Eclipse art root. The atmosphere supplies
// scaled time: no independent _Time clock, colliders, physics or saved assets.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class BattleEclipseLevitation : MonoBehaviour
{
    public bool Ready { get; private set; }
    public Renderer[] Renderers { get; private set; }
    public const int SparkCount = 56;
    Material material;
    Mesh[] meshes;
    MaterialPropertyBlock block;

    public void Bind()
    {
        if(Ready)return;
        var shader=Resources.Load<Shader>("SideA/Eclipse/EclipseLevitation");
        if(!shader){Debug.LogError("Eclipse levitation shader missing.",this);return;}
        material=new Material(shader){name="Eclipse crimson gravity (shared)"};
        meshes=new Mesh[3];Renderers=new Renderer[3];block=new MaterialPropertyBlock();
        for(int i=0;i<3;i++)
        {
            bool main=i==0;
            meshes[i]=Build(main,i*.379f);
            var go=new GameObject("Eclipse Crimson Suspension "+i,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(transform,false);
            go.transform.localPosition=main?new Vector3(-2,0,0):new Vector3(i==1?-10.3f:6.3f,3.7f,0);
            go.GetComponent<MeshFilter>().sharedMesh=meshes[i];
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            Renderers[i]=renderer;
        }
        Ready=true;Evaluate(0,1);
    }

    public void Evaluate(float time,float motion)
    {
        if(!Ready)return;
        foreach(var renderer in Renderers)
        {
            if(!renderer)continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat("_GravityClock",time);block.SetFloat("_Motion",motion);
            renderer.SetPropertyBlock(block);block.Clear();
        }
    }

    static Mesh Build(bool main,float seed)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();
        var data=new List<Vector4>();var triangles=new List<int>();
        float rx=main?11.9f:3.35f,rz=main?3.35f:1.95f;
        // Bright thin broken currents; no flat disk or opaque cloud geometry.
        for(int ring=0;ring<3;ring++)
            Ribbon(vertices,uv,data,triangles,rx*(1-ring*.16f),rz*(1-ring*.09f),
                (main?-2.25f:-1.65f)-ring*(main?.50f:.25f),main?.115f:.080f,
                ring*.193f+seed,0,main?128:80,main?.24f:.16f);
        // Two broad translucent streams counter-flow between the thin lines.
        for(int ring=0;ring<2;ring++)
            Ribbon(vertices,uv,data,triangles,rx*(.94f-ring*.16f),rz*(.95f-ring*.08f),
                (main?-2.45f:-1.74f)-ring*(main?.43f:.26f),main?.68f:.39f,
                .217f+ring*.381f+seed,1,main?112:72,main?.31f:.19f);
        int sparks=main?28:14;
        for(int i=0;i<sparks;i++)
        {
            float s=Mathf.Repeat(i*.618034f+seed,1),a=s*Mathf.PI*2;
            float radius=.72f+.24f*Mathf.Repeat(i*.317f,1);
            var centre=new Vector3(Mathf.Cos(a)*rx*radius,main?-3.5f:-2.05f,Mathf.Sin(a)*rz*radius);
            float size=.012f+.015f*Mathf.Repeat(i*.437f,1);
            int start=vertices.Count;
            for(int k=0;k<4;k++)
            {
                var corner=new Vector2(k==0||k==3?-1:1,k<2?-1:1);
                vertices.Add(centre+new Vector3(corner.x,corner.y,0)*size);
                uv.Add(corner*.5f+Vector2.one*.5f);
                data.Add(new Vector4(2,s,main?1.5f:.8f,.09f+.035f*Mathf.Repeat(i*.71f,1)));
            }
            Quad(triangles,start,start+1,start+2,start+3);
        }
        var mesh=new Mesh{name=main?"Eclipse main gravity ribbons":"Eclipse upper gravity ribbons"};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,data);mesh.SetTriangles(triangles,0);
        mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(new Vector3(.3f,main?3.4f:2,.3f));mesh.bounds=bounds;
        return mesh;
    }
    static void Ribbon(List<Vector3> vertices,List<Vector2> uv,List<Vector4> data,List<int> triangles,
        float rx,float rz,float y,float halfWidth,float seed,int mode,int segments,float wave)
    {
        int start=vertices.Count;
        for(int i=0;i<=segments;i++)
        {
            float u=(float)i/segments,a=u*Mathf.PI*2;
            var centre=new Vector3(rx*Mathf.Cos(a),y+wave*Mathf.Sin(a*2+seed*6.283185f),rz*Mathf.Sin(a));
            for(int edge=0;edge<2;edge++)
            {
                vertices.Add(centre+Vector3.up*((edge==0?-1:1)*halfWidth));
                uv.Add(new Vector2(u,edge));data.Add(new Vector4(mode,seed,halfWidth,0));
            }
            if(i>0){int k=start+i*2;Quad(triangles,k-2,k,k+1,k-1);}
        }
    }
    static void Quad(List<int> indices,int a,int b,int c,int d)
    {indices.Add(a);indices.Add(b);indices.Add(c);indices.Add(a);indices.Add(c);indices.Add(d);}

    void OnEnable(){if(Renderers!=null)foreach(var r in Renderers)if(r)r.enabled=true;}
    void OnDisable(){if(Renderers!=null)foreach(var r in Renderers)if(r)r.enabled=false;}
    static void Release(Object item){if(!item)return;if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
    void OnDestroy()
    {
        if(Renderers!=null)foreach(var r in Renderers)if(r)Release(r.gameObject);
        if(meshes!=null)foreach(var mesh in meshes)Release(mesh);
        Release(material);
    }
}
