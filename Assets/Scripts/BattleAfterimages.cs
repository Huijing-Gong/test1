using UnityEngine;
using UnityEngine.Rendering;

// Fixed world-space pose snapshots, sampled after the presentation pose.
[DefaultExecutionOrder(300)]
public sealed class BattleAfterimages:MonoBehaviour
{
    BattleSpecialMoves moves;SkinnedMeshRenderer[] sources;Material material;
    Mesh[][] meshes;MeshRenderer[][] renderers;Transform[][] nodes;
    const int Capacity=4;
    const float Lifetime=.32f;
    readonly float[] life=new float[Capacity];MaterialPropertyBlock properties;
    bool[][] captured;
    float jumpTrail;
    float clock;int slot;Vector3 lastPosition;bool activeBefore;
    public int ActiveCount=0;
    void Start()
    {
        moves=GetComponent<BattleSpecialMoves>();sources=GetComponentsInChildren<SkinnedMeshRenderer>();
        material=new Material(Resources.Load<Shader>("VFX/BattleGhost")){hideFlags=HideFlags.DontSave};properties=new MaterialPropertyBlock();
        meshes=new Mesh[Capacity][];renderers=new MeshRenderer[Capacity][];nodes=new Transform[Capacity][];captured=new bool[Capacity][];
        for(int s=0;s<Capacity;s++)
        {
            captured[s]=new bool[sources.Length];
            meshes[s]=new Mesh[sources.Length];renderers[s]=new MeshRenderer[sources.Length];nodes[s]=new Transform[sources.Length];
            for(int i=0;i<sources.Length;i++)
            {
                var go=new GameObject("Brief K afterimage "+s+" / "+i);nodes[s][i]=go.transform;
                meshes[s][i]=new Mesh{hideFlags=HideFlags.DontSave};meshes[s][i].MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=meshes[s][i];
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.enabled=false;renderers[s][i]=r;
            }
        }
    }
    void LateUpdate()
    {
        if(meshes==null || Time.timeScale<=0)return;
        jumpTrail=Mathf.Max(0,jumpTrail-Time.deltaTime);
        bool active=!BattlePresentationSettings.ReducedMotion && (jumpTrail>0 || moves && moves.Phase==BattleSpecialPhase.Active && (moves.Kind==BattleSpecialKind.Dash || moves.Kind==BattleSpecialKind.Rise));
        if(BattlePresentationSettings.ReducedMotion){Clear();return;}
        if(activeBefore && Vector3.Distance(lastPosition,transform.position)>5f){Clear();return;}
        ActiveCount=0;
        for(int s=0;s<Capacity;s++)
        {
            life[s]=Mathf.Max(0,life[s]-Time.deltaTime);if(life[s]>0)ActiveCount++;
            float fade=Mathf.SmoothStep(0,1,life[s]/Lifetime)*Mathf.Clamp01((Lifetime-life[s])/.035f);
            properties.SetColor("_Tint",new Color(.40f,.82f,.74f,.62f*fade));
            for(int i=0;i<sources.Length;i++){renderers[s][i].enabled=life[s]>0 && captured[s][i];renderers[s][i].SetPropertyBlock(properties);}
        }
        if(!active){activeBefore=false;return;}
        if(!activeBefore){clock=0;lastPosition=transform.position;activeBefore=true;}
        clock-=Time.deltaTime;
        if(clock>0 || Vector3.Distance(lastPosition,transform.position)<.65f)return;
        clock=.065f;lastPosition=transform.position;
        int chosen=slot++%Capacity;
        life[chosen]=Lifetime;
        properties.SetColor("_Tint",new Color(.40f,.82f,.74f,0));
        for(int i=0;i<sources.Length;i++)
        {
            var source=sources[i];captured[chosen][i]=source && source.enabled && source.gameObject.activeInHierarchy;
            renderers[chosen][i].enabled=captured[chosen][i];
            if(!captured[chosen][i])continue;
            source.BakeMesh(meshes[chosen][i]);
            var node=nodes[chosen][i];node.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            node.localScale=source.transform.lossyScale;
            renderers[chosen][i].enabled=true;
            renderers[chosen][i].SetPropertyBlock(properties);
        }
        ActiveCount=0;foreach(float remaining in life)if(remaining>0)ActiveCount++;
    }
    public void DoubleJump()=>jumpTrail=.24f;
    public void Clear()
    {
        ActiveCount=0;System.Array.Clear(life,0,life.Length);activeBefore=false;jumpTrail=0;
        if(renderers!=null)foreach(var group in renderers)foreach(var r in group)if(r)r.enabled=false;
    }
    void OnDisable()=>Clear();
    void OnDestroy()
    {
        if(meshes!=null)foreach(var group in meshes)foreach(var mesh in group)if(mesh)Destroy(mesh);
        if(nodes!=null)foreach(var group in nodes)foreach(var node in group)if(node)Destroy(node.gameObject);
        if(material)Destroy(material);
    }
}
