using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Small depth-tested native mesh glyphs: no font/texture dependencies, no through-wall UI.
public sealed class EnemyAwarenessIndicator : MonoBehaviour
{
    static Material material;
    static Mesh question, exclamation;
    EnemyAwareness source;
    MeshFilter glyph;
    MeshRenderer glyphRenderer, progressRenderer;
    Mesh progress;
    float shownAge, nextArc, lastEvidence = -1f;
    bool wasVisible, wasQuestion;
    readonly List<Vector3> vertices = new List<Vector3>(300);
    readonly List<Color> colors = new List<Color>(300);
    readonly List<int> triangles = new List<int>(500);
    static readonly Color Ink = new Color(.035f,.075f,.085f);
    static readonly Color Cream = new Color(1f,.93f,.67f);
    public bool Visible => glyphRenderer && glyphRenderer.enabled;
    public static EnemyAwarenessIndicator Create(EnemyAwareness source)
    {
        if (!material)
        {
            // Resource material makes the shader a real player-build dependency.
            material=Resources.Load<Material>("EnemyAwarenessIndicator");
            if (!material) return null;
        }
        var root = new GameObject("Enemy awareness - ? / !");
        root.transform.SetParent(source.transform, false);
        var result = root.AddComponent<EnemyAwarenessIndicator>(); result.source=source;
        result.glyph=root.AddComponent<MeshFilter>(); result.glyphRenderer=root.AddComponent<MeshRenderer>();
        result.glyphRenderer.sharedMaterial=material; result.glyphRenderer.shadowCastingMode=ShadowCastingMode.Off; result.glyphRenderer.receiveShadows=false;
        var arc=new GameObject("Suspicion progress"); arc.transform.SetParent(root.transform,false);
        result.progress = new Mesh {name="Bounded suspicion arc"}; result.progress.MarkDynamic(); arc.AddComponent<MeshFilter>().sharedMesh=result.progress;
        result.progressRenderer=arc.AddComponent<MeshRenderer>(); result.progressRenderer.sharedMaterial=material;
        result.progressRenderer.shadowCastingMode=ShadowCastingMode.Off; result.progressRenderer.receiveShadows=false;
        if (!question) question=result.BuildGlyph(true);
        if (!exclamation) exclamation=result.BuildGlyph(false);
        result.Hide(); return result;
    }
    public void Hide() { if(glyphRenderer) glyphRenderer.enabled=false; if(progressRenderer) progressRenderer.enabled=false; wasVisible=false; }
    public void Present(bool show)
    {
        var camera=Camera.main;
        if(!show || !camera || !source || !source.DisplayAllowed) {Hide();return;}
        Vector3 head=source.HeadPosition;
        float distance=Vector3.Distance(camera.transform.position,head);
        Vector3 view=camera.WorldToViewportPoint(head);
        if(distance>34f || view.z<=0f || view.x<-.05f || view.x>1.05f || view.y<-.05f || view.y>1.05f) {Hide();return;}
        bool curious=source.Curious;
        if(!wasVisible || wasQuestion!=curious) {shownAge=0f;nextArc=0f;lastEvidence=-1f;}
        shownAge+=Time.deltaTime; wasVisible=true; wasQuestion=curious;
        float pop=shownAge<.15f?Mathf.Lerp(.7f,1.14f,shownAge/.15f):Mathf.Lerp(1.14f,1f,Mathf.Clamp01((shownAge-.15f)/.16f));
        // Keep it legible at gameplay distance without becoming a screen-sized marker up close.
        float size=camera.orthographic?camera.orthographicSize*.075f:
            Mathf.Clamp(2f*distance*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*.043f,.38f,1.05f);
        transform.SetPositionAndRotation(head+Vector3.up*(curious?.025f*Mathf.Sin(shownAge*4f):0f),camera.transform.rotation);
        transform.localScale=Vector3.one; transform.localScale=Vector3.one*(size*pop)/Mathf.Max(.001f,source.transform.lossyScale.x);
        glyph.sharedMesh=curious?question:exclamation; glyphRenderer.enabled=true;
        bool arcVisible=curious && source.State==EnemyAwareness.AwarenessState.Suspicious;
        progressRenderer.enabled=arcVisible;
        if(arcVisible && Time.time>=nextArc && Mathf.Abs(lastEvidence-source.Evidence)>.012f)
        {
            lastEvidence=source.Evidence;nextArc=Time.time+.07f;
            vertices.Clear();colors.Clear();triangles.Clear();
            Ring(.60f, .045f, 1f, new Color(.03f,.065f,.08f,.8f));
            Ring(.60f, .021f, source.Evidence, Color.Lerp(Cream,new Color(1f,.35f,.12f),source.Evidence));
            progress.Clear();progress.SetVertices(vertices);progress.SetColors(colors);progress.SetTriangles(triangles,0);progress.RecalculateBounds();
        }
    }
    void OnDestroy() {if(progress)Destroy(progress);}
    void Ring(float radius,float width,float fraction,Color color)
    {
        int segments=Mathf.Max(1,Mathf.CeilToInt(fraction*40));
        for(int i=0;i<segments;i++)
        {
            float a=(90-i*fraction*360/segments)*Mathf.Deg2Rad,b=(90-(i+1)*fraction*360/segments)*Mathf.Deg2Rad;
            Quad(new Vector3(Mathf.Cos(a)*(radius-width),Mathf.Sin(a)*(radius-width),.01f),new Vector3(Mathf.Cos(a)*(radius+width),Mathf.Sin(a)*(radius+width),.01f),
                new Vector3(Mathf.Cos(b)*(radius+width),Mathf.Sin(b)*(radius+width),.01f),new Vector3(Mathf.Cos(b)*(radius-width),Mathf.Sin(b)*(radius-width),.01f),color);
        }
    }
    Mesh BuildGlyph(bool isQuestion)
    {
        vertices.Clear();colors.Clear();triangles.Clear();
        var path=new List<Vector2>();
        if(isQuestion)
        {
            // Round upper hook, then a crisp angled stem; drawn twice for a dark silhouette.
            for(int i=0;i<=20;i++){float a=Mathf.Lerp(170,-80,i/20f)*Mathf.Deg2Rad;path.Add(new Vector2(Mathf.Cos(a)*.22f, .19f+Mathf.Sin(a)*.22f));}
            path.Add(new Vector2(0,-.13f));
        }
        else {path.Add(new Vector2(0,.39f));path.Add(new Vector2(0,-.12f));}
        Stroke(path,.16f,.02f,Ink); Dot(new Vector2(0,-.34f),.105f,.02f,Ink);
        Stroke(path,.092f,0f,isQuestion?Cream:new Color(1f,.27f,.12f)); Dot(new Vector2(0,-.34f),.065f,0f,isQuestion?Cream:new Color(1f,.27f,.12f));
        var mesh=new Mesh {name=isQuestion?"Outlined curiosity ?":"Outlined discovery !"};
        mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
    }
    void Stroke(List<Vector2> path,float width,float z,Color color)
    {
        for(int i=0;i<path.Count-1;i++)
        {
            Vector2 p=path[i],q=path[i+1],d=(q-p).normalized,n=new Vector2(-d.y,d.x)*width*.5f;
            Quad(new Vector3(p.x+n.x,p.y+n.y,z),new Vector3(p.x-n.x,p.y-n.y,z),new Vector3(q.x-n.x,q.y-n.y,z),new Vector3(q.x+n.x,q.y+n.y,z),color);
            Dot(q,width*.5f,z,color);
        }
    }
    void Dot(Vector2 p,float radius,float z,Color color)
    {
        for(int i=0;i<12;i++)
        {
            int n=vertices.Count;float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
            vertices.Add(new Vector3(p.x,p.y,z));vertices.Add(new Vector3(p.x+Mathf.Cos(a)*radius,p.y+Mathf.Sin(a)*radius,z));vertices.Add(new Vector3(p.x+Mathf.Cos(b)*radius,p.y+Mathf.Sin(b)*radius,z));
            colors.Add(color);colors.Add(color);colors.Add(color);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
        }
    }
    void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
    {
        int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);for(int i=0;i<4;i++)colors.Add(color);
        triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n);triangles.Add(n+2);triangles.Add(n+3);
    }
}
