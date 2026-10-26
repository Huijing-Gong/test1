using UnityEngine;
using UnityEngine.Rendering;

// Adapter of MountedKnightEffects.Damaged/Sparks and CombatFeedback timing from
// CombatSandbox commit 4d564d446dc1a0c15de0f77d710e19df9b467a54.
// Keep the stone shader/materials rather than replacing them with horse materials.
[RequireComponent(typeof(Health))]
public sealed class WardenContactPresentation : MonoBehaviour
{
    Health health;ParticleSystem motes;Material energy;SkinnedMeshRenderer[] skins;
    MaterialPropertyBlock[] before;MaterialPropertyBlock flash;
    bool flashing;float until;
    public int HitCount{get;private set;}
    public bool FlashActive=>flashing;
    void Awake()
    {
        flash=new MaterialPropertyBlock();health=GetComponent<Health>();skins=GetComponentsInChildren<SkinnedMeshRenderer>(true);before=new MaterialPropertyBlock[skins.Length];
        for(int i=0;i<before.Length;i++)before[i]=new MaterialPropertyBlock();
        var shader=Shader.Find("Warden/Boundary Energy");
        if(!shader)return;energy=new Material(shader);
        var go=new GameObject("Warden source contact motes");go.transform.SetParent(transform,false);
        motes=go.AddComponent<ParticleSystem>();motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=motes.main;main.playOnAwake=false;main.loop=false;main.maxParticles=220;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startLifetime=.6f;main.startSize=.055f;main.gravityModifier=.18f;
        var emission=motes.emission;emission.enabled=false;var shape=motes.shape;shape.enabled=false;
        var color=motes.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});color.color=gradient;
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=energy;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    void OnEnable(){if(!health)health=GetComponent<Health>();health.Damaged+=Hit;}
    void OnDisable(){if(health)health.Damaged-=Hit;Restore();if(motes)motes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    void Hit(Health target,DamageInfo damage)
    {
        if(target.IsDead)return;HitCount++;
        if(motes){motes.Play();for(int i=0;i<7;i++)motes.Emit(new ParticleSystem.EmitParams{position=damage.HitPoint,velocity=Random.insideUnitSphere+Vector3.up*.7f,startColor=new Color(.16f,.8f,1,.75f),startSize=Random.Range(.025f,.065f),startLifetime=Random.Range(.28f,.85f)},1);}
        for(int i=0;i<skins.Length;i++)if(skins[i]) {
            if(!flashing)skins[i].GetPropertyBlock(before[i]);
            skins[i].GetPropertyBlock(flash);flash.SetColor("_HitColor",new Color(1,.72f,.68f,1));flash.SetFloat("_HitStrength",1);skins[i].SetPropertyBlock(flash);
        }
        flashing=true;until=Time.unscaledTime+.08f;
    }
    void Update(){if(flashing&&Time.unscaledTime>=until)Restore();}
    void Restore(){if(!flashing)return;for(int i=0;i<skins.Length;i++)if(skins[i])skins[i].SetPropertyBlock(before[i].isEmpty?null:before[i]);flashing=false;}
    void OnDestroy(){if(energy)Destroy(energy);}
}
