using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Energy-only overlay: the original shield mesh, materials and grip stay intact.
[DisallowMultipleComponent, DefaultExecutionOrder(230)]
public sealed class BishopKnightShieldVfx : MonoBehaviour
{
    BishopKnightAI knight;
    Renderer overlay;
    Transform shieldAnchor;
    GameObject overlayObject, shardsRoot;
    Material shieldMaterial, cueMaterial;
    readonly List<Renderer> armor = new List<Renderer>();
    readonly LineRenderer[] shards = new LineRenderer[8];
    readonly Vector3[] burstPositions = new Vector3[8];
    readonly Vector3[] burstDirections = new Vector3[8];
    MaterialPropertyBlock block;
    float clock, hitFlash, breakAge = 10;
    int lastBreaks;
    public bool OverlayVisible => overlay && overlay.enabled;
    public bool BreakVisible => breakAge < .5f && knight && knight.PresentationActive;
    public string ShieldState => knight && knight.GuardBroken ? "Broken" :
        knight && knight.GuardCritical ? "Critical" : hitFlash > 0 ? "Impact" : "Guard";

    void Start()
    {
        knight = GetComponent<BishopKnightAI>(); block = new MaterialPropertyBlock();
        if (!knight || !knight.animator) { enabled=false; return; }
        Renderer source=null;
        foreach(var r in knight.animator.GetComponentsInChildren<Renderer>(true))
        {
            armor.Add(r);
            if(r.name == "Bishop_Shield")source=r;
        }
        var shader=Resources.Load<Shader>("KnightShieldEnergy");
        var cue=Resources.Load<Shader>("KnightCombatCue");
        if(!source || !shader || !cue){enabled=false;return;}
        shieldAnchor=source.transform;
        Mesh mesh=source is SkinnedMeshRenderer skin?skin.sharedMesh:source.GetComponent<MeshFilter>()?.sharedMesh;
        if(!mesh){enabled=false;return;}
        shieldMaterial=new Material(shader){name="Sentinel shield energy only",hideFlags=HideFlags.DontSave};
        cueMaterial=new Material(cue){name="Sentinel light fragments",hideFlags=HideFlags.DontSave};
        Vector3 size=mesh.bounds.size;
        int vertical=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
        int horizontal=vertical==0?(size.y>size.z?1:2):vertical==1?(size.x>size.z?0:2):(size.x>size.y?0:1);
        Vector3 u=Vector3.zero,v=Vector3.zero;u[horizontal]=1;v[vertical]=1;
        shieldMaterial.SetVector("_BoundsMin",mesh.bounds.min);
        shieldMaterial.SetVector("_BoundsSize",new Vector3(Mathf.Max(.001f,size.x),Mathf.Max(.001f,size.y),Mathf.Max(.001f,size.z)));
        shieldMaterial.SetVector("_UAxis",u);shieldMaterial.SetVector("_VAxis",v);
        overlayObject=new GameObject("Shield energy engraving overlay");
        overlayObject.transform.SetParent(source.transform,false);
        if(source is SkinnedMeshRenderer skinned)
        {
            var copy=overlayObject.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh=mesh;copy.bones=skinned.bones;copy.rootBone=skinned.rootBone;copy.localBounds=skinned.localBounds;
            overlay=copy;
        }
        else {overlayObject.AddComponent<MeshFilter>().sharedMesh=mesh;overlay=overlayObject.AddComponent<MeshRenderer>();}
        var materials=new Material[mesh.subMeshCount];for(int i=0;i<materials.Length;i++)materials[i]=shieldMaterial;
        overlay.sharedMaterials=materials;overlay.shadowCastingMode=ShadowCastingMode.Off;overlay.receiveShadows=false;
        shardsRoot=new GameObject("Shield energy fragments");
        for(int i=0;i<shards.Length;i++)
        {
            var go=new GameObject("Light fragment");go.transform.SetParent(shardsRoot.transform,false);
            var line=go.AddComponent<LineRenderer>();line.sharedMaterial=cueMaterial;
            line.positionCount=4;line.loop=true;line.widthMultiplier=.025f;line.useWorldSpace=true;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;shards[i]=line;
        }
    }
    public void Contact(){hitFlash=.22f;}
    void LateUpdate()
    {
        if(!overlay || !knight)return;
        if(!knight.PresentationActive){Hide();return;}
        clock+=Time.deltaTime;hitFlash=Mathf.Max(0,hitFlash-Time.deltaTime);breakAge+=Time.deltaTime;
        if(knight.GuardBreaks<lastBreaks){breakAge=10;hitFlash=0;}
        if(knight.GuardBreaks>lastBreaks)
        {
            breakAge=0;
            Vector3 centre=overlay.bounds.center;
            for(int i=0;i<shards.Length;i++)
            {
                float a=i*Mathf.PI*2/shards.Length;
                burstPositions[i]=centre+transform.right*Mathf.Cos(a)*.16f+Vector3.up*Mathf.Sin(a)*.25f;
                burstDirections[i]=transform.right*Mathf.Cos(a)*.8f+Vector3.up*(.4f+Mathf.Sin(a)*.7f)+transform.forward*.35f;
            }
        }
        lastBreaks=knight.GuardBreaks;
        overlay.enabled=knight.ShieldRaised || (knight.Enraged && !knight.GuardBroken);
        block.Clear();block.SetFloat("_CueClock",clock);block.SetFloat("_Strain",knight.GuardCritical?1:knight.GuardStrain*.6f);
        block.SetFloat("_HitPulse",hitFlash/.22f);block.SetFloat("_PhaseTwo",knight.Enraged?1:0);
        block.SetFloat("_Guard",knight.ShieldRaised?1:0);overlay.SetPropertyBlock(block);
        for(int i=0;i<shards.Length;i++)
        {
            var line=shards[i];line.enabled=breakAge<.5f;
            if(!line.enabled)continue;
            float f=1-breakAge/.5f;Vector3 p=burstPositions[i]+burstDirections[i]*breakAge;
            Vector3 up=Vector3.up*.1f*f,right=transform.right*.045f*f;
            line.SetPosition(0,p+up);line.SetPosition(1,p+right);line.SetPosition(2,p-up);line.SetPosition(3,p-right);
            line.startColor=line.endColor=new Color(.7f,1,.88f,f);
        }
        SetEyes(knight.Enraged?1:0);
    }
    void SetEyes(float phase)
    {
        // Dedicated additive property; never replace an armor palette or shared material.
        foreach(var r in armor)
        {
            if(!r)continue;
            r.GetPropertyBlock(block);block.SetFloat("_SentinelPhase",phase);r.SetPropertyBlock(block);
        }
    }
    void Hide(){if(overlay)overlay.enabled=false;foreach(var line in shards)if(line)line.enabled=false; if(block!=null)SetEyes(0);}
    void OnDisable(){Hide();}
    void OnDestroy()
    {Hide();if(overlayObject)Destroy(overlayObject);if(shardsRoot)Destroy(shardsRoot);if(shieldMaterial)Destroy(shieldMaterial);if(cueMaterial)Destroy(cueMaterial);}
}
