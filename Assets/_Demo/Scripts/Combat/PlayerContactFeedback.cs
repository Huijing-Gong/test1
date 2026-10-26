using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared by every target accepted by PlayerCombat. Runs during the short hit stop.</summary>
[DisallowMultipleComponent]
public sealed class PlayerContactFeedback : MonoBehaviour
{
    ParticleSystem sparks,flash; Material material; AudioSource sound; AudioClip body,metal;
    public int ContactCount {get;private set;}
    public int FatalContactCount {get;private set;}
    public Vector3 LastContact {get;private set;}
    public int SoundCount {get;private set;}
    public AudioClip LastSoundClip {get;private set;}
    public bool Ready => sparks&&flash&&body&&metal;
    void Awake(){
        var shader=Shader.Find("Combat/Player Contact");if(shader){material=new Material(shader);sparks=Create("Directional contact sparks",true);flash=Create("Contact flash",false);}
        sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.spatialBlend=.25f;sound.minDistance=2;sound.maxDistance=18;sound.volume=.6f;
        body=Resources.Load<AudioClip>("CombatImpact/HitBody");metal=Resources.Load<AudioClip>("CombatImpact/HitMetal");
    }
    ParticleSystem Create(string label,bool streak){
        var o=new GameObject(label);o.transform.SetParent(transform,false);var ps=o.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=false;main.maxParticles=streak?160:16;main.simulationSpace=ParticleSystemSimulationSpace.World;main.useUnscaledTime=true;main.gravityModifier=streak?.45f:0;
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.55f,.15f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.2f),new GradientAlphaKey(0,1)});color.color=gradient;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
        var r=o.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=material;r.renderMode=streak?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;r.lengthScale=streak?1.6f:1;r.velocityScale=streak?.07f:0;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return ps;
    }
    public void Contact(Vector3 point,Vector3 direction,bool fatal,bool armored,bool playSound=true){
        ContactCount++;if(fatal)FatalContactCount++;LastContact=point;
        Vector3 outward=direction.sqrMagnitude>.001f?-direction.normalized:-transform.forward;
        if(flash){flash.Play();flash.Emit(new ParticleSystem.EmitParams{position=point+outward*.045f,velocity=Vector3.zero,startSize=fatal?.65f:.4f,startLifetime=fatal?.14f:.09f,startColor=new Color(1,.92f,.64f,1),rotation=Random.Range(0,180)},1);}
        if(sparks){sparks.Play();for(int i=0;i<(fatal?22:13);i++){
            Vector3 velocity=(outward*.65f+Random.onUnitSphere*.65f+Vector3.up*.25f).normalized*Random.Range(1.4f,fatal?4.4f:3.1f);
            sparks.Emit(new ParticleSystem.EmitParams{position=point+outward*.03f,velocity=velocity,startSize=Random.Range(.035f,.075f),startLifetime=Random.Range(.12f,.29f),startColor=armored?new Color(1,.76f,.35f,1):new Color(.45f,.88f,1,1)},1);
        }}
        if(sound&&playSound){sound.pitch=Random.Range(.94f,1.06f);var clip=armored?metal:body;if(clip){sound.PlayOneShot(clip,fatal?.85f:.65f);SoundCount++;LastSoundClip=clip;}}
    }
    public void Clear(){if(sparks)sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);if(flash)flash.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);if(sound)sound.Stop();}
    void OnDisable(){Clear();}
    void OnDestroy(){if(material)Destroy(material);}
}
