using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A small presentation set, isolated from gameplay and built from shared owned materials.
// This component never owns save data, time scale, cursor state or player input.
public sealed class CloudGateTitleScreen : MonoBehaviour
{
    public Material stoneMaterial, goldMaterial, blueMaterial, cloudMaterial, skyMaterial;
    public Material foldedStoneMaterial, titleLightMaterial;
    public Mesh treeMesh, flowerMesh;
    public Material[] treeMaterials, flowerMaterials;
    public bool Visible { get; private set; }
    public Camera PresentationCamera => titleCamera;
    public float EntryProgress { get; private set; }
    const int PresentationLayer = 30;
    GameObject root;
    Camera titleCamera, worldCamera;
    bool previousCameraEnabled;
    Transform leftDoor, rightDoor;
    readonly List<Object> owned = new List<Object>();
    readonly List<Transform> clouds = new List<Transform>();
    readonly List<Vector3> cloudPositions = new List<Vector3>();
    readonly List<Transform> motes = new List<Transform>();
    Material glow, gateStone, gateShade, compassLight;
    float shownAt;
    Vector3 CameraStart = new Vector3(0, 4.5f, -16.5f);

    public void Show(Camera gameplayCamera)
    {
        if (Visible) return;
        if (!stoneMaterial || !goldMaterial || !blueMaterial || !cloudMaterial || !skyMaterial)
            throw new System.InvalidOperationException("The title scene is missing its shared material references.");
        worldCamera = gameplayCamera;
        previousCameraEnabled = worldCamera && worldCamera.enabled;
        try { Build(); }
        catch { ReleaseSet(); throw; }
        Visible = true;
        shownAt = Time.unscaledTime;
        if (worldCamera) worldCamera.enabled = false;
        root.SetActive(true);
        SetProgress(0, false);
    }

    public void Hide()
    {
        if (!Visible) return;
        Visible = false;
        if (root) root.SetActive(false);
        if (worldCamera) worldCamera.enabled = previousCameraEnabled;
        ReleaseSet();
    }

    void OnDisable() { Hide(); }
    void OnDestroy() { Hide(); ReleaseSet(); }

    void ReleaseSet()
    {
        if (root) Destroy(root);
        root = null; titleCamera = null; leftDoor = rightDoor = null;
        foreach (var item in owned) if (item) Destroy(item);
        owned.Clear(); clouds.Clear(); cloudPositions.Clear(); motes.Clear();
    }

    public void SetProgress(float progress, bool reduceMotion)
    {
        EntryProgress = Mathf.Clamp01(progress);
        if (!root || !Visible) return;
        float seconds = EntryProgress * 1.8f;
        if(compassLight)compassLight.SetColor("_EmissionColor",new Color(1,.65f,.2f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.28f,seconds))*.7f);
        float unfold = reduceMotion ? 0 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, 1.35f, seconds));
        leftDoor.localRotation = Quaternion.Euler(0, -78 * unfold, 0);
        rightDoor.localRotation = Quaternion.Euler(0, 78 * unfold, 0);
        float push = reduceMotion ? 0 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1.8f, seconds));
        titleCamera.transform.localPosition = Vector3.Lerp(CameraStart, new Vector3(0, 3.3f, 3.8f), push);
        titleCamera.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 4.2f, 6) - CameraStart);
        float age = Time.unscaledTime - shownAt;
        for (int i = 0; i < clouds.Count; i++)
            clouds[i].localPosition = cloudPositions[i] + (reduceMotion ? Vector3.zero : new Vector3(Mathf.Sin(age * .025f + i) * 1.4f, Mathf.Sin(age * .055f + i) * .2f, 0));
        for (int i = 0; i < motes.Count; i++)
        {
            float cycle = Mathf.Repeat(age * (reduceMotion ? 0 : .16f) + i * .071f, 1);
            float spread = Mathf.Lerp(.14f, 1.4f, unfold);
            motes[i].localPosition = new Vector3(Mathf.Sin(i * 7.3f) * spread, .8f + cycle * 7, 5.7f - Mathf.Sin(i * 2) * .15f);
            motes[i].localRotation = Quaternion.Euler(15, age * (reduceMotion ? 0 : 20) + i * 38, 45);
        }
    }

    Transform Node(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name); go.layer = PresentationLayer;
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        return go.transform;
    }

    Material Tint(Material original, Color color)
    {
        var mat = new Material(original); mat.name = "Title instance " + original.name;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        owned.Add(mat); return mat;
    }

    void RenderMesh(string name, Mesh mesh, Material[] materials, Transform parent, Vector3 p, Vector3 scale)
    {
        var t = Node(name, parent, p); t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = t.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void Finish(string name, Shape shape, Material material, Transform parent)
    {
        var mesh = shape.Mesh(name); owned.Add(mesh);
        RenderMesh(name, mesh, new[] { material }, parent, Vector3.zero, Vector3.one);
    }

    void Build()
    {
        root = new GameObject("Cloud fold title presentation"); root.transform.position = new Vector3(0, 2000, 0);
        root.layer = PresentationLayer; root.SetActive(false);
        var cameraNode = Node("Title camera", root.transform, CameraStart);
        titleCamera = cameraNode.gameObject.AddComponent<Camera>();
        titleCamera.clearFlags = CameraClearFlags.Skybox; titleCamera.cullingMask = 1 << PresentationLayer;
        titleCamera.fieldOfView = 49; titleCamera.nearClipPlane = .08f; titleCamera.farClipPlane = 240;
        titleCamera.allowHDR = true; titleCamera.depth = 50;
        cameraNode.gameObject.AddComponent<Skybox>().material = skyMaterial;
        gateStone=Tint(foldedStoneMaterial?foldedStoneMaterial:stoneMaterial,Color.white);
        gateShade=Tint(gateStone,new Color(.76f,.84f,.90f));
        var stone = new Shape(); var gold = new Shape(); var blue = new Shape();var compass=new Shape();
        // Stone tiles and a shallow stair allow the camera to cross the doorway without a large world.
        stone.Box(new Vector3(0,-.35f,0), new Vector3(19,.6f,19));
        for (int x = -4; x <= 4; x++) for (int z = -4; z <= 3; z++)
            stone.Box(new Vector3(x*2.08f,.005f,z*2.08f), new Vector3(2.045f,.08f,2.045f));
        for (int i = 0; i < 4; i++) stone.Box(new Vector3(0,.08f+i*.115f,4.8f+i*.48f),new Vector3(13.8f-i*.22f,.12f,2.2f-i*.4f));
        // The compass is real shared-color geometry, not a new texture atlas.
        compass.Ring(new Vector3(0,.065f,1.2f),3.4f,.035f,96);
        compass.Ring(new Vector3(0,.067f,1.2f),3.12f,.022f,96);
        blue.Ring(new Vector3(0,.063f,1.2f),3.26f,.08f,96);
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4; float length=i%2==0?2.75f:1.85f;
            Vector3 center=new Vector3(0,.075f,1.2f), tip=center+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*length;
            Vector3 side=new Vector3(Mathf.Cos(a),0,-Mathf.Sin(a))*.43f;
            compass.Tri(center,tip,center+side); blue.Tri(center,center-side,tip);
        }
        foreach (int side in new[]{-1,1})
        {
            stone.Box(new Vector3(side*8.1f,.45f,-.3f),new Vector3(.65f,1,13));
            gold.Box(new Vector3(side*8.1f,.98f,-.3f),new Vector3(.7f,.045f,13));
            for(int p=0;p<2;p++)
            {
                float z=p==0?-3:3.4f;
                stone.Box(new Vector3(side*7.4f,.7f,z),new Vector3(1.3f,1.4f,1.3f));
                gold.Box(new Vector3(side*7.4f,1.43f,z),new Vector3(1.4f,.045f,1.4f));
            }
            stone.Box(new Vector3(side*8, .5f, 5.4f),new Vector3(2.2f,1,2.4f));
            stone.Box(new Vector3(side*7.35f,.15f,1.0f),new Vector3(1.5f,.3f,6.5f));
            for (int wing=0;wing<2;wing++)
            {
                var fixedPage=Node("Fixed folded wing",root.transform,new Vector3(side*(3+wing*1.7f),.5f,6.1f+wing*.3f));
                FoldPage(fixedPage, side*1.7f, 6.1f-wing*1.2f, 7.8f-wing*1.6f, false);
            }
            Plant(treeMesh,treeMaterials,new Vector3(side*8.2f,1,5.3f),4.9f);
            Plant(treeMesh,treeMaterials,new Vector3(side*8.9f,.25f,1.3f),3.4f);
            for(int f=0;f<25;f++)
                Plant(flowerMesh,flowerMaterials,new Vector3(side*(6.85f+(f%3)*.43f),.31f, (f/3)*.72f-1.7f),.55f+(f%4)*.11f);
            for(int b=0;b<8;b++)Plant(treeMesh,treeMaterials,new Vector3(side*7.35f,.3f,b*.8f-1.7f),.65f,2.5f);
            var ornament = new Shape();
            ornament.Ring(new Vector3(side*7.4f,1.93f,-3),.43f,.035f,40,Quaternion.Euler(90,0,0));
            ornament.Ring(new Vector3(side*7.4f,1.93f,-3),.43f,.035f,40,Quaternion.Euler(0,0,90));
            ornament.Ring(new Vector3(side*7.4f,1.93f,-3),.43f,.035f,40);
            Finish("Armillary gold rings",ornament,goldMaterial,root.transform);
            blue.Sphere(new Vector3(side*7.4f,1.93f,-3),.31f,12,8);
        }
        leftDoor=Node("Left central hinged leaf",root.transform,new Vector3(-3,.5f,6));
        rightDoor=Node("Right central hinged leaf",root.transform,new Vector3(3,.5f,6));
        FoldPage(leftDoor,2.97f,7.5f,9,true);
        FoldPage(rightDoor,-2.97f,7.5f,9,true);
        Finish("Ivory landing and planters",stone,stoneMaterial,root.transform);
        Finish("Compass and fine gold edging",gold,goldMaterial,root.transform);
        compassLight=Tint(titleLightMaterial?titleLightMaterial:goldMaterial,new Color(.86f,.56f,.105f));
        Finish("Activating gold compass",compass,compassLight,root.transform);
        Finish("Cobalt compass inlay",blue,blueMaterial,root.transform);
        // Shared cloud shader and scene sky; nothing is assigned to global RenderSettings.
        var cloudMesh=new Shape();cloudMesh.Quad(new Vector3(-.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0),new Vector3(.5f,-.5f,0));
        var billboard=cloudMesh.Mesh("Title shared cloud card");owned.Add(billboard);
        var cloud=Tint(cloudMaterial,new Color(.93f,.97f,1,.83f));
        for(int i=0;i<30;i++)
        {
            float z=18+(i/6)*23, x=(i%6-2.5f)*24;
            var t=Node("Cloud bank",root.transform,new Vector3(x,-3+(i/6)*1.1f+Mathf.Sin(i*2.4f)*2.4f,z));
            t.localScale=new Vector3(42,15+(i%3)*3,1);
            var mf=t.gameObject.AddComponent<MeshFilter>();mf.sharedMesh=billboard;
            var mr=t.gameObject.AddComponent<MeshRenderer>();mr.sharedMaterial=cloud;mr.shadowCastingMode=ShadowCastingMode.Off;mr.receiveShadows=false;
            clouds.Add(t);cloudPositions.Add(t.localPosition);
        }
        for(int i=0;i<6;i++)
        {
            int side=i%2==0?-1:1;float z=52+i*12;float x=side*(22+i*3);
            var island=new Shape();
            Vector3 peak=new Vector3(x,10+i%3*4,z);
            for(int k=0;k<7;k++)
            {
                float a=k*Mathf.PI*2/7,b=(k+1)*Mathf.PI*2/7;
                Vector3 p=peak+new Vector3(Mathf.Sin(a)*5,0,Mathf.Cos(a)*4),q=peak+new Vector3(Mathf.Sin(b)*5,0,Mathf.Cos(b)*4);
                island.Tri(peak,p,q);island.Tri(q,p,peak+new Vector3(.7f,-13-i,0));
            }
            for(int k=0;k<2;k++)island.Box(peak+new Vector3(k*2.6f-1.3f,2.5f,0),new Vector3(.55f,5,.8f));
            island.Box(peak+new Vector3(0,5,0),new Vector3(3.2f,.45f,.8f));
            Finish("Distant world silhouette",island,Tint(gateStone,Color.Lerp(new Color(.62f,.75f,.83f),new Color(.86f,.91f,.96f),i/5f)),root.transform);
        }
        glow=Tint(titleLightMaterial?titleLightMaterial:goldMaterial,new Color(1,.79f,.27f));
        if(glow.HasProperty("_EmissionColor")){glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",new Color(1,.58f,.12f)*1.5f);}
        for(int i=0;i<14;i++)
        {
            var s=new Shape();s.Box(Vector3.zero,Vector3.one*(.045f+i%3*.012f));
            var t=Node("Square light mote",root.transform,Vector3.zero);
            Finish("Mote",s,glow,t);motes.Add(t);
        }
        var seam=new Shape();seam.Box(new Vector3(0,4.8f,6.12f),new Vector3(.035f,8.6f,.04f));
        Finish("Light behind fold",seam,glow,root.transform);
    }

    void Plant(Mesh mesh, Material[] materials, Vector3 position,float height,float widthMultiplier=1)
    {
        if(!mesh || materials==null || materials.Length==0)return;
        float scale=height/Mathf.Max(.01f,mesh.bounds.size.y);
        Vector3 size=new Vector3(scale*widthMultiplier,scale,scale*widthMultiplier);
        Vector3 offset=Vector3.Scale(new Vector3(-mesh.bounds.center.x,-mesh.bounds.min.y,-mesh.bounds.center.z),size);
        RenderMesh("Shared garden accent",mesh,materials,root.transform,position+offset,size);
    }

    void FoldPage(Transform parent,float width,float outerHeight,float innerHeight,bool central)
    {
        var s=new Shape();var edges=new Shape();var inset=new Shape();var shaded=new Shape();
        Vector3[] bottom={Vector3.zero,new Vector3(width*.5f,0,-.72f),new Vector3(width,0,0)};
        Vector3[] top={new Vector3(0,outerHeight,0),new Vector3(width*.5f,(outerHeight+innerHeight)*.5f-.75f,-.72f),new Vector3(width,innerHeight,0)};
        for(int i=0;i<2;i++)
        {
            (i==0?s:shaded).Quad(bottom[i],top[i],top[i+1],bottom[i+1]);
            s.Quad(bottom[i+1]+Vector3.forward*.2f,top[i+1]+Vector3.forward*.2f,top[i]+Vector3.forward*.2f,bottom[i]+Vector3.forward*.2f);
            s.Quad(top[i],top[i]+Vector3.forward*.2f,top[i+1]+Vector3.forward*.2f,top[i+1]);
            s.Quad(bottom[i+1],bottom[i+1]+Vector3.forward*.2f,bottom[i]+Vector3.forward*.2f,bottom[i]);
            edges.Line(top[i]+Vector3.back*.025f,top[i+1]+Vector3.back*.025f,.026f);
        }
        s.Quad(bottom[0],bottom[0]+Vector3.forward*.2f,top[0]+Vector3.forward*.2f,top[0]);
        s.Quad(bottom[2],top[2],top[2]+Vector3.forward*.2f,bottom[2]+Vector3.forward*.2f);
        // Reversed pages have the same visible front/back and closed thickness.
        if(width<0){s.Reverse();shaded.Reverse();}
        for(int i=0;i<3;i++)edges.Line(bottom[i]+Vector3.back*.025f,top[i]+Vector3.back*.025f,.027f);
        edges.Line(bottom[0]+Vector3.back*.03f,Vector3.Lerp(bottom[1],top[1],.48f)+Vector3.back*.03f,.022f);
        if(central)
        {
            Vector3 a=Vector3.Lerp(bottom[2],top[2],.60f)+Vector3.back*.035f,b=top[2]+Vector3.back*.035f,c=top[2]-new Vector3(width*.25f,.60f,.40f);
            inset.Tri(a,b,c);if(width>0)inset.Reverse();
            edges.Line(a,c,.022f);edges.Line(c,b,.022f);
        }
        Finish("Folded ivory slab",s,gateStone,parent);
        Finish("Fold shadow face",shaded,gateShade,parent);
        Finish("Fine gold fold lines",edges,goldMaterial,parent);
        if(central)Finish("Cobalt inner leaf",inset,blueMaterial,parent);
    }

    sealed class Shape
    {
        readonly List<Vector3> v=new List<Vector3>();readonly List<int> indices=new List<int>();readonly List<Vector2> uv=new List<Vector2>();
        public void Tri(Vector3 a,Vector3 b,Vector3 c){int n=v.Count;v.Add(a);v.Add(b);v.Add(c);uv.Add(Vector2.zero);uv.Add(Vector2.up);uv.Add(Vector2.one);indices.Add(n);indices.Add(n+1);indices.Add(n+2);}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);uv.Add(Vector2.zero);uv.Add(Vector2.up);uv.Add(Vector2.one);uv.Add(Vector2.right);indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        public void Reverse(){for(int i=0;i<indices.Count;i+=3){int a=indices[i];indices[i]=indices[i+2];indices[i+2]=a;}}
        public void Sphere(Vector3 p,float radius,int slices,int rings)
        {
            for(int y=0;y<rings;y++)for(int x=0;x<slices;x++)
            {
                float a=x*Mathf.PI*2/slices,b=(x+1)*Mathf.PI*2/slices,c=y*Mathf.PI/rings,d=(y+1)*Mathf.PI/rings;
                Vector3 p0=new Vector3(Mathf.Sin(c)*Mathf.Sin(a),Mathf.Cos(c),Mathf.Sin(c)*Mathf.Cos(a));
                Vector3 p1=new Vector3(Mathf.Sin(d)*Mathf.Sin(a),Mathf.Cos(d),Mathf.Sin(d)*Mathf.Cos(a));
                Vector3 p2=new Vector3(Mathf.Sin(d)*Mathf.Sin(b),Mathf.Cos(d),Mathf.Sin(d)*Mathf.Cos(b));
                Vector3 p3=new Vector3(Mathf.Sin(c)*Mathf.Sin(b),Mathf.Cos(c),Mathf.Sin(c)*Mathf.Cos(b));
                Quad(p+p0*radius,p+p1*radius,p+p2*radius,p+p3*radius);
            }
        }
        public void Box(Vector3 p,Vector3 size){Box(p,size,Quaternion.identity);}
        public void Box(Vector3 p,Vector3 size,Quaternion rotation)
        {
            Vector3[] b=new Vector3[8];for(int i=0;i<8;i++)b[i]=p+rotation*Vector3.Scale(new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f),size);
            Quad(b[0],b[2],b[3],b[1]);Quad(b[5],b[7],b[6],b[4]);Quad(b[4],b[6],b[2],b[0]);Quad(b[1],b[3],b[7],b[5]);Quad(b[2],b[6],b[7],b[3]);Quad(b[4],b[0],b[1],b[5]);
        }
        public void Line(Vector3 a,Vector3 b,float width){Vector3 d=b-a;Box((a+b)*.5f,new Vector3(width,width,d.magnitude),Quaternion.LookRotation(d));}
        public void Ring(Vector3 p,float radius,float width,int count){Ring(p,radius,width,count,Quaternion.identity);}
        public void Ring(Vector3 p,float radius,float width,int count,Quaternion rotation)
        {
            for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                Vector3 u=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)),w=new Vector3(Mathf.Sin(b),0,Mathf.Cos(b));
                Quad(p+rotation*u*(radius-width),p+rotation*u*(radius+width),p+rotation*w*(radius+width),p+rotation*w*(radius-width));}
        }
        public Mesh Mesh(string name)
        {
            var m=new Mesh{name=name};m.SetVertices(v);m.SetTriangles(indices,0);m.SetUVs(0,uv);
            var colors=new Color[v.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color(.96f,.93f,.86f,0);
            m.colors=colors;m.RecalculateNormals();m.RecalculateBounds();return m;
        }
    }
}
