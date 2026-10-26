using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Third-stage-only visual treatment. GPU skinning shares the real pose; nothing
// is added beneath a fighter, so intro, camera bounds and ghosts stay unchanged.
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(350)]
public sealed class BattleEclipseFighterAura : MonoBehaviour
{
    public bool Ready { get; private set; }
    public int AuraCount => fighters.Count;
    public float AuraClock { get; private set; }
    public int VisibleFighterCount { get; private set; }
    public SkinnedMeshRenderer[] ShellRenderers => shells.ToArray();
    public Renderer[] WispRenderers => wisps.ToArray();

    sealed class Fighter
    {
        public GameObject root;
        public BattleStockLives stocks;
        public BattleSpecialMoves special;
        public Color colour;
        public readonly List<SkinnedMeshRenderer> source = new List<SkinnedMeshRenderer>();
        public readonly List<SkinnedMeshRenderer> shell = new List<SkinnedMeshRenderer>();
        public MeshRenderer wisp;
    }
    readonly List<Fighter> fighters = new List<Fighter>(2);
    readonly List<SkinnedMeshRenderer> shells = new List<SkinnedMeshRenderer>();
    readonly List<Renderer> wisps = new List<Renderer>();
    readonly Dictionary<Mesh,Mesh> smoothMeshes = new Dictionary<Mesh,Mesh>();
    BattleArenaStages owner;
    BattleManager manager;
    Material shellMaterial, wispMaterial;
    Mesh wispMesh;
    MaterialPropertyBlock block;
    static readonly int Strength = Shader.PropertyToID("_EclipseAuraStrength");
    static readonly int Colour = Shader.PropertyToID("_EclipseAuraColor");
    static readonly int Clock = Shader.PropertyToID("_EclipseAuraClock");

    public void Bind(BattleArenaStages stages)
    {
        if (Ready || !stages) return;
        var shader = Resources.Load<Shader>("SideA/Eclipse/EclipseFighterAura");
        if (!shader) { Debug.LogError("Eclipse fighter aura shader is missing.", this); return; }
        owner = stages; manager = stages.GetComponent<BattleManager>();
        block = new MaterialPropertyBlock();
        shellMaterial = new Material(shader) { name = "Eclipse skinned energy edge", hideFlags = HideFlags.DontSave };
        shellMaterial.SetFloat("_AuraMode", 0);
        shellMaterial.renderQueue = 3004;
        wispMaterial = new Material(shader) { name = "Eclipse rising qi ribbons", hideFlags = HideFlags.DontSave };
        wispMaterial.SetFloat("_AuraMode", 1);
        wispMaterial.SetInt("_Cull", (int)CullMode.Off);
        wispMaterial.renderQueue = 3003;
        wispMesh = CreateWisps();
        AddFighter("Player", new Color(.12f,.64f,1f,1));
        AddFighter("Enemy", new Color(1f,.19f,.055f,1));
        Ready = true;
        Evaluate();
    }
    void AddFighter(string tag, Color colour)
    {
        var root = GameObject.FindGameObjectWithTag(tag);
        if (!root) return;
        var animator = root.GetComponentInChildren<Animator>(true);
        if (!animator) return;
        var f = new Fighter { root = root, stocks = root.GetComponent<BattleStockLives>(),
            special = root.GetComponent<BattleSpecialMoves>(), colour = colour };
        foreach (var source in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!source.sharedMesh) continue;
            f.source.Add(source);
            // Face/eyes keep their real colours, and small belts need no extra draw.
            if (!(source.name.EndsWith("_Head_Hood") || source.name.EndsWith("_Body") ||
                source.name.EndsWith("_Arms") || source.name.EndsWith("_Legs")))
            { f.shell.Add(null); continue; }
            var go = new GameObject("Eclipse " + tag + " Pose Energy", typeof(SkinnedMeshRenderer));
            go.hideFlags = HideFlags.DontSave;
            go.layer = source.gameObject.layer;
            go.transform.SetParent(transform, false);
            var shell = go.GetComponent<SkinnedMeshRenderer>();
            shell.sharedMesh = SmoothShell(source.sharedMesh);
            shell.bones = source.bones; shell.rootBone = source.rootBone;
            shell.quality = source.quality; shell.updateWhenOffscreen = false;
            var materials = new Material[source.sharedMesh.subMeshCount];
            for (int i=0; i<materials.Length; i++) materials[i] = shellMaterial;
            shell.sharedMaterials = materials;
            NoShadows(shell);
            f.shell.Add(shell); shells.Add(shell);
        }
        if (f.source.Count == 0) return;
        var wisp = new GameObject("Eclipse " + tag + " Rising Qi", typeof(MeshFilter), typeof(MeshRenderer));
        wisp.hideFlags = HideFlags.DontSave;
        wisp.transform.SetParent(transform, false);
        wisp.GetComponent<MeshFilter>().sharedMesh = wispMesh;
        f.wisp = wisp.GetComponent<MeshRenderer>();
        f.wisp.sharedMaterial = wispMaterial;
        NoShadows(f.wisp); wisps.Add(f.wisp); fighters.Add(f);
    }
    static void NoShadows(Renderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }
    Mesh SmoothShell(Mesh original)
    {
        if(smoothMeshes.TryGetValue(original,out var cached))return cached;
        // Quaternius deliberately splits flat-shaded faces. Extruding their
        // original normals opens cracks; weld normals on a private visual copy
        // once, leaving the source, skin weights and all animation data intact.
        var copy=Instantiate(original); copy.name=original.name+" Eclipse smooth shell";
        copy.hideFlags=HideFlags.DontSave;
        var vertices=copy.vertices;var normals=copy.normals;
        var sums=new Dictionary<Vector3Int,Vector3>();
        for(int i=0;i<vertices.Length;i++)
        {
            var key=Vector3Int.RoundToInt(vertices[i]*10000);
            sums.TryGetValue(key,out var n);sums[key]=n+normals[i];
        }
        for(int i=0;i<vertices.Length;i++)normals[i]=sums[Vector3Int.RoundToInt(vertices[i]*10000)].normalized;
        copy.normals=normals;smoothMeshes.Add(original,copy);return copy;
    }
    void LateUpdate()
    {
        if (!Ready || !Application.isPlaying) return;
        if (!BattlePresentationSettings.ReducedMotion) AuraClock += Time.deltaTime;
        Evaluate();
    }
    public void PreviewAt(float seconds)
    {
        if (Application.isPlaying || !Ready) return;
        AuraClock = Mathf.Max(0, seconds); Evaluate();
    }
    bool CanShow(Fighter fighter)
    {
        if (!isActiveAndEnabled || !owner || owner.ActiveIndex != 2 || !fighter.root ||
            !fighter.root.activeInHierarchy || (fighter.stocks && fighter.stocks.Remaining == 0)) return false;
        if (Application.isPlaying && (!manager || manager.SelectionOpen || manager.IsAwaitingIntroduction ||
            (!manager.IsBattleActive && !manager.IsGameOver))) return false;
        // Intro reveal keeps renderer.enabled=true while clipping the body away.
        // Never expose the silhouette before that reveal has finished.
        foreach (var renderer in fighter.source)
        {
            if (!renderer) continue;
            foreach (var material in renderer.sharedMaterials)
                if (material && material.shader && material.shader.name == "SideA/Intro Reveal") return false;
        }
        return true;
    }
    void Evaluate()
    {
        VisibleFighterCount = 0;
        bool reduced = BattlePresentationSettings.ReducedMotion;
        foreach (var f in fighters)
        {
            // A retry can create stage art in Awake before the manager attaches
            // transient stock/special components in Start.
            if(f.root && !f.stocks)f.stocks=f.root.GetComponent<BattleStockLives>();
            if(f.root && !f.special)f.special=f.root.GetComponent<BattleSpecialMoves>();
            bool show = CanShow(f), anyVisible = false;
            Bounds body = default;
            float power = f.special && (f.special.IsCharging || f.special.IsBeamActive) ? 1.18f : 1f;
            for (int i=0; i<f.source.Count; i++)
            {
                var source=f.source[i]; var shell=f.shell[i];
                bool visible=show && source && source.enabled && source.gameObject.activeInHierarchy && !source.forceRenderingOff;
                if(shell)shell.enabled=visible;
                if (source)
                {
                    source.GetPropertyBlock(block);
                    block.SetFloat(Strength, visible ? power : 0);
                    block.SetColor(Colour, f.colour); block.SetFloat(Clock, AuraClock);
                    source.SetPropertyBlock(block); block.Clear();
                }
                if (!visible) continue;
                if (!anyVisible) body=source.bounds; else body.Encapsulate(source.bounds);
                anyVisible=true;
                if(!shell)continue;
                shell.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                var parentScale=transform.lossyScale; var scale=source.transform.lossyScale;
                shell.transform.localScale=new Vector3(scale.x/parentScale.x,scale.y/parentScale.y,scale.z/parentScale.z);
                var bounds=source.localBounds; bounds.Expand(.5f); shell.localBounds=bounds;
                for(int shape=0;shape<source.sharedMesh.blendShapeCount;shape++)
                    shell.SetBlendShapeWeight(shape, source.GetBlendShapeWeight(shape));
                SetProperties(shell, f.colour, power, reduced, source.bounds);
            }
            f.wisp.enabled=anyVisible;
            if (!anyVisible) continue;
            VisibleFighterCount++;
            f.wisp.transform.position=body.center;
            f.wisp.transform.rotation=Quaternion.identity;
            // Fixed normalized mesh bounds encompass animated ribbons for culling.
            f.wisp.transform.localScale=new Vector3(Mathf.Max(.55f,body.size.x),body.size.y,Mathf.Max(.4f,body.size.z));
            SetProperties(f.wisp, f.colour, power, reduced, body);
        }
    }
    void SetProperties(Renderer renderer,Color colour,float power,bool reduced,Bounds bounds)
    {
        block.SetColor(Colour,colour); block.SetFloat(Strength,power);
        block.SetFloat(Clock,AuraClock); block.SetFloat("_AuraMotion",reduced?0:1);
        block.SetVector("_BodyBounds",new Vector4(bounds.center.x,bounds.min.y,bounds.size.y,bounds.center.z));
        renderer.SetPropertyBlock(block); block.Clear();
    }
    static Mesh CreateWisps()
    {
        const int count=8, steps=12;
        var vertices=new Vector3[count*(steps+1)*2];
        var uv=new Vector2[vertices.Length];var seeds=new Vector4[vertices.Length];
        var indices=new int[count*steps*6];int k=0;
        for(int i=0;i<count;i++)
        {
            for(int j=0;j<=steps;j++) for(int side=0;side<2;side++)
            {
                int v=i*(steps+1)*2+j*2+side;float t=(float)j/steps;
                vertices[v]=new Vector3(side*2-1,t,0);uv[v]=new Vector2(side,t);
                seeds[v]=new Vector4(i*.618034f,i%2==0?-1:1,(i/2)*.13f,i*.93f);
            }
            for(int j=0;j<steps;j++)
            {
                int v=i*(steps+1)*2+j*2;
                indices[k++]=v;indices[k++]=v+2;indices[k++]=v+1;
                indices[k++]=v+1;indices[k++]=v+2;indices[k++]=v+3;
            }
        }
        var mesh=new Mesh { name="Eclipse eight narrow rising energy ribbons", hideFlags=HideFlags.DontSave };
        mesh.vertices=vertices;mesh.uv=uv;mesh.SetUVs(1,seeds);mesh.triangles=indices;
        mesh.bounds=new Bounds(Vector3.zero,new Vector3(2.2f,3.2f,2));return mesh;
    }
    void OnDisable()
    {
        VisibleFighterCount=0;
        foreach(var shell in shells)if(shell)shell.enabled=false;
        foreach(var wisp in wisps)if(wisp)wisp.enabled=false;
        ClearBodyTreatment();
    }
    void ClearBodyTreatment()
    {
        if(block==null)return;
        foreach(var f in fighters)foreach(var source in f.source)
        {
            if(!source)continue;
            source.GetPropertyBlock(block);block.SetFloat(Strength,0);source.SetPropertyBlock(block);block.Clear();
        }
    }
    void OnEnable() { if(Ready)Evaluate(); }
    static void Release(Object item)
    { if(item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);} }
    void OnDestroy()
    {
        ClearBodyTreatment();
        foreach(var shell in shells)if(shell)Release(shell.gameObject);
        foreach(var wisp in wisps)if(wisp)Release(wisp.gameObject);
        Release(shellMaterial);Release(wispMaterial);Release(wispMesh);
        foreach(var mesh in smoothMeshes.Values)Release(mesh);
        smoothMeshes.Clear();
        fighters.Clear();shells.Clear();wisps.Clear();Ready=false;
    }
}
