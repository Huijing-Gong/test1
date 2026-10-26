using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Owned by the third-stage art instance. No persistent objects, physics or saves.
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(120)]
public sealed class BattleEclipseAtmosphere : MonoBehaviour
{
    public bool Ready { get; private set; }
    public float AtmosphereTime { get; private set; }
    public int FogLayerCount => mist == null ? 0 : mist.Length;
    public int EmberCount => Ready ? 16 : 0;
    public Renderer CloudRenderer => clouds;
    public Renderer[] MistRenderers => mist;
    Renderer[] mist, platforms;
    Renderer clouds, embers;
    Material fogMaterial, cloudMaterial, emberMaterial;
    Mesh quad, emberMesh;
    MaterialPropertyBlock properties;
    UniversalAdditionalCameraData cameraData;
    bool depthApplied;
    CameraOverrideOption previousDepth;
    BattleArenaStages owner;
    BattleEclipseLevitation levitation;
    static readonly int Clock = Shader.PropertyToID("_AtmosphereTime");

    public void Bind(BattleArenaStages stages, Transform paintedBackdrop)
    {
        if (Ready) return;
        if (!stages || !paintedBackdrop || !stages.battleCamera) return;
        var shader = Resources.Load<Shader>("SideA/Eclipse/EclipseAtmosphere");
        if (!shader) { Debug.LogError("Eclipse atmosphere shader is missing.", this); return; }
        owner = stages;
        // Capture only original platform meshes, before adding any FX renderers.
        platforms = GetComponentsInChildren<MeshRenderer>(true);
        properties = new MaterialPropertyBlock();
        cameraData = stages.battleCamera.GetComponent<Camera>().GetUniversalAdditionalCameraData();
        quad = CreateQuad();
        fogMaterial = LayerMaterial(shader, "Eclipse cold mist", 0, new Color(.40f,.47f,.59f,.38f));
        cloudMaterial = LayerMaterial(shader, "Eclipse bloodmoon cloud veil", 1, new Color(.075f,.08f,.115f,.15f));
        emberMaterial = LayerMaterial(shader, "Eclipse faint embers", 2, new Color(.85f,.16f,.075f,.42f));
        mist = new Renderer[3];
        mist[0] = Layer("Eclipse Mist Lake", transform, quad, fogMaterial,
            new Vector3(-2,-2.7f,5.2f), new Vector3(46,6,1));
        mist[1] = Layer("Eclipse Mist Lower Arches", transform, quad, fogMaterial,
            new Vector3(-3,-3.5f,-3.6f), new Vector3(38,3.4f,1));
        mist[2] = Layer("Eclipse Mist Far Banks", transform, quad, fogMaterial,
            new Vector3(-1,-.8f,8.5f), new Vector3(43,4.5f,1));
        for (int i=0;i<mist.Length;i++)
        {
            mist[i].GetPropertyBlock(properties);
            properties.SetFloat("_Seed", i*2.13f+.7f);
            properties.SetFloat("_Density", i==0?1:i==1?.68f:.62f);
            mist[i].SetPropertyBlock(properties);properties.Clear();
        }
        // Exact image-local coordinates: the cloud veil shares the painting's
        // zoom, aspect, overscan and translation, and cannot drift in front of art.
        clouds = Layer("Eclipse Bloodmoon Clouds", paintedBackdrop, quad, cloudMaterial,
            new Vector3(0,0,-.025f), Vector3.one, true);
        emberMesh = CreateEmbers();
        embers = Layer("Eclipse Embers", transform, emberMesh, emberMaterial, Vector3.zero,Vector3.one);

        // Faint, fighter-layer-only secondary directional light; the original
        // cold sun remains the brighter main light. Platform rim is in its shader.
        var rimObject = new GameObject("Eclipse Fighter Moon Rim");
        rimObject.transform.SetParent(transform,false);
        rimObject.transform.rotation = Quaternion.LookRotation(new Vector3(.12f,-.22f,-1).normalized,Vector3.up);
        var rim = rimObject.AddComponent<Light>();
        rim.type=LightType.Directional;rim.color=new Color(.65f,.12f,.08f);
        rim.intensity=.24f;rim.shadows=LightShadows.None;
        rim.cullingMask=(1<<6)|(1<<7);rim.renderMode=LightRenderMode.Auto;
        Ready=true;
        var aura=GetComponent<BattleEclipseFighterAura>();
        if(!aura)aura=gameObject.AddComponent<BattleEclipseFighterAura>();
        aura.Bind(stages);
        levitation=GetComponent<BattleEclipseLevitation>();
        if(!levitation)levitation=gameObject.AddComponent<BattleEclipseLevitation>();
        levitation.Bind();
        EnableDepth();
        Evaluate();
    }

    Material LayerMaterial(Shader shader,string label,int mode,Color colour)
    {
        var material=new Material(shader){name=label};
        material.SetFloat("_LayerMode",mode);material.SetColor("_Tint",colour);
        material.renderQueue=mode==1?2900:mode==0?2950:3000;
        return material;
    }
    static Mesh CreateQuad()
    {
        var mesh=new Mesh { name="Eclipse shared mist quad" };
        mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
        mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static Mesh CreateEmbers()
    {
        const int count=16;
        var vertices=new Vector3[count*4];var uv=new Vector2[count*4];
        var seeds=new Vector4[count*4];var indices=new int[count*6];
        Vector2[] corners={new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
        for(int i=0;i<count;i++)
        {
            bool upper=i>=12;
            float x=upper?(i<14?-10.3f:6.3f)+(i%2==0?-1.9f:1.8f):-12.5f+i*1.91f;
            float y=upper?2.56f:-1.6f-(i%3)*.22f;
            float z=upper?-1.95f:-3.3f;
            float size=.018f+(i%4)*.007f;
            for(int j=0;j<4;j++)
            {
                int k=i*4+j;
                vertices[k]=new Vector3(x+corners[j].x*size,y+corners[j].y*size,z);
                uv[k]=corners[j]*.5f+Vector2.one*.5f;
                seeds[k]=new Vector4(i*.618033f,i*.41f,upper?.62f:1.22f,.13f+(i%3)*.025f);
            }
            int a=i*6,b=i*4;indices[a]=b;indices[a+1]=b+2;indices[a+2]=b+1;
            indices[a+3]=b;indices[a+4]=b+3;indices[a+5]=b+2;
        }
        var mesh=new Mesh{name="Eclipse sixteen batched ember quads"};
        mesh.vertices=vertices;mesh.uv=uv;mesh.SetUVs(1,seeds);mesh.triangles=indices;
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        var bounds=mesh.bounds;bounds.Expand(new Vector3(1,3,1));mesh.bounds=bounds;return mesh;
    }
    static Renderer Layer(string label,Transform parent,Mesh mesh,Material material,Vector3 position,Vector3 scale,bool local=false)
    {
        var go=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(parent,false);
        if(local)go.transform.localPosition=position;else go.transform.position=position;
        go.transform.localScale=scale;
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        return renderer;
    }
    void EnableDepth()
    {
        if(!cameraData || depthApplied)return;
        previousDepth=cameraData.requiresDepthOption;
        cameraData.requiresDepthTexture=true;depthApplied=true;
    }
    void RestoreDepth()
    {
        if(cameraData && depthApplied)cameraData.requiresDepthOption=previousDepth;
        depthApplied=false;
    }
    void OnEnable()
    {
        if(!Ready)return;
        if(clouds)clouds.gameObject.SetActive(true);
        EnableDepth();Evaluate();
    }
    void Update()
    {
        if(!Ready || !Application.isPlaying)return;
        if(!BattlePresentationSettings.ReducedMotion)AtmosphereTime+=Time.deltaTime;
        Evaluate();
    }
    // Deterministic authored preview without driving or bypassing gameplay time.
    public void PreviewAt(float seconds)
    {
        if(Application.isPlaying || !Ready)return;
        AtmosphereTime=Mathf.Max(0,seconds);Evaluate();
    }
    void Evaluate()
    {
        float motion=BattlePresentationSettings.ReducedMotion?0:1;
        foreach(var renderer in platforms)
        {
            if(!renderer)continue;
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_EclipseEnabled",1);properties.SetFloat("_EclipseMotion",motion);
            properties.SetFloat("_EclipseClock",AtmosphereTime);renderer.SetPropertyBlock(properties);properties.Clear();
        }
        foreach(var renderer in mist)SetClock(renderer,motion);
        SetClock(clouds,motion);SetClock(embers,motion);
        if(levitation)levitation.Evaluate(AtmosphereTime,motion);
    }
    void SetClock(Renderer renderer,float motion)
    {
        if(!renderer)return;
        renderer.GetPropertyBlock(properties);properties.SetFloat(Clock,AtmosphereTime);
        properties.SetFloat("_Motion",motion);renderer.SetPropertyBlock(properties);properties.Clear();
    }
    void OnDisable()
    {
        if(clouds)clouds.gameObject.SetActive(false);
        RestoreDepth();
        if(platforms==null || properties==null)return;
        foreach(var renderer in platforms)
        {
            if(!renderer)continue;
            renderer.GetPropertyBlock(properties);properties.SetFloat("_EclipseEnabled",0);
            properties.SetFloat("_EclipseMotion",0);renderer.SetPropertyBlock(properties);properties.Clear();
        }
    }
    static void Release(Object item)
    {
        if(!item)return;
        if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);
    }
    void OnDestroy()
    {
        RestoreDepth();
        if(clouds)Release(clouds.gameObject);
        Release(fogMaterial);Release(cloudMaterial);Release(emberMaterial);Release(quad);Release(emberMesh);
    }
}
