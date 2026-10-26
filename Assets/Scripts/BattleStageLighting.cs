using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// Uses BattleScene's single sun. Ambient belongs to the active scene: additive
// loading must never write the suspended main world's RenderSettings.
[DisallowMultipleComponent]
public sealed class BattleStageLighting:MonoBehaviour
{
    public Light sceneSun;
    public bool IsTwilight {get;private set;}
    public bool IsEclipse {get;private set;}
    bool sunCaptured,ambientCaptured;
    Quaternion dayRotation;
    Color daySun,daySky,dayEquator,dayGround;
    float dayIntensity,dayShadowStrength,dayBias,dayNormalBias,dayAmbientIntensity;
    bool dayTemperature;
    AmbientMode dayAmbientMode;
    readonly List<SkinnedMeshRenderer> fighterSurfaces=new List<SkinnedMeshRenderer>();
    MaterialPropertyBlock fighterProperties;
    static readonly int EclipseTopLight=Shader.PropertyToID("_EclipseTopLight");

    void OnEnable()
    {
        CaptureSun();
        SceneManager.activeSceneChanged+=ActiveSceneChanged;
    }
    void CaptureSun()
    {
        if(sunCaptured||!sceneSun)return;
        sunCaptured=true;dayRotation=sceneSun.transform.rotation;
        daySun=sceneSun.color;dayIntensity=sceneSun.intensity;dayTemperature=sceneSun.useColorTemperature;
        dayShadowStrength=sceneSun.shadowStrength;dayBias=sceneSun.shadowBias;dayNormalBias=sceneSun.shadowNormalBias;
    }
    public void ApplyStage(int index)
    {
        IsTwilight=index==1;IsEclipse=index==2;CaptureSun();ApplySun();
        CaptureFighters();SetFighterTopLight(IsEclipse?1:0);
        if(SceneManager.GetActiveScene()==gameObject.scene)ApplyAmbient();
    }
    void ActiveSceneChanged(Scene previous,Scene next)
    {
        if(next==gameObject.scene){ApplySun();ApplyAmbient();}
    }
    void ApplySun()
    {
        if(!sunCaptured||!sceneSun)return;
        if(IsEclipse)
        {
            // Almost vertical key: luminous walkable tops, dark stone facades.
            // A small front component keeps faces readable without flattening
            // the stage with a frontal floodlight.
            sceneSun.transform.rotation=Quaternion.LookRotation(new Vector3(.15f,-.98f,.10f).normalized,Vector3.up);
            sceneSun.useColorTemperature=false;
            // The ivory atlas is already warm. A moon-white key keeps the deck
            // cream/ivory rather than tinting the whole floor orange.
            sceneSun.color=new Color(.88f,.94f,1f);sceneSun.intensity=1.35f;
            sceneSun.shadowStrength=.76f;sceneSun.shadowBias=.08f;sceneSun.shadowNormalBias=.12f;
            return;
        }
        if(!IsTwilight){RestoreSun();return;}
        // Low sun from the upper-right of the painted landscape. Modest front
        // component keeps faces readable without adding a second realtime light.
        sceneSun.transform.rotation=Quaternion.LookRotation(new Vector3(-.82f,-.44f,.36f).normalized,Vector3.up);
        sceneSun.useColorTemperature=false; // Explicit RGB in this Gamma project.
        sceneSun.color=new Color(1f,.77f,.50f,1);sceneSun.intensity=1.32f;
        sceneSun.shadowStrength=.58f;sceneSun.shadowBias=.35f;sceneSun.shadowNormalBias=.20f;
    }
    void ApplyAmbient()
    {
        if(!ambientCaptured)
        {
            ambientCaptured=true;dayAmbientMode=RenderSettings.ambientMode;
            daySky=RenderSettings.ambientSkyColor;dayEquator=RenderSettings.ambientEquatorColor;
            dayGround=RenderSettings.ambientGroundColor;dayAmbientIntensity=RenderSettings.ambientIntensity;
        }
        if(IsEclipse)
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.22f,.25f,.34f);
            RenderSettings.ambientEquatorColor=new Color(.09f,.105f,.15f);
            RenderSettings.ambientGroundColor=new Color(.035f,.045f,.07f);
            RenderSettings.ambientIntensity=1;
            return;
        }
        if(!IsTwilight){RestoreAmbient();return;}
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.66f,.65f,.76f,1);
        RenderSettings.ambientEquatorColor=new Color(.60f,.53f,.53f,1);
        RenderSettings.ambientGroundColor=new Color(.32f,.34f,.39f,1);
        RenderSettings.ambientIntensity=1;
    }
    void RestoreSun()
    {
        if(!sunCaptured||!sceneSun)return;
        sceneSun.transform.rotation=dayRotation;sceneSun.color=daySun;sceneSun.intensity=dayIntensity;
        sceneSun.useColorTemperature=dayTemperature;sceneSun.shadowStrength=dayShadowStrength;
        sceneSun.shadowBias=dayBias;sceneSun.shadowNormalBias=dayNormalBias;
    }
    void CaptureFighters()
    {
        fighterSurfaces.Clear();
        foreach(var tag in new[]{"Player","Enemy"})
            foreach(var actor in GameObject.FindGameObjectsWithTag(tag))
            {
                // Additive loading must not change a suspended host actor.
                if(actor.scene!=gameObject.scene)continue;
                var animator=actor.GetComponentInChildren<Animator>(true);
                if(animator)fighterSurfaces.AddRange(animator.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            }
    }
    void SetFighterTopLight(float strength)
    {
        if(fighterProperties==null)fighterProperties=new MaterialPropertyBlock();
        foreach(var surface in fighterSurfaces)
        {
            if(!surface)continue;
            surface.GetPropertyBlock(fighterProperties);
            fighterProperties.SetFloat(EclipseTopLight,strength);
            surface.SetPropertyBlock(fighterProperties);fighterProperties.Clear();
        }
    }
    void RestoreAmbient()
    {
        if(!ambientCaptured)return;
        RenderSettings.ambientMode=dayAmbientMode;RenderSettings.ambientSkyColor=daySky;
        RenderSettings.ambientEquatorColor=dayEquator;RenderSettings.ambientGroundColor=dayGround;
        RenderSettings.ambientIntensity=dayAmbientIntensity;
    }
    void OnDisable()
    {
        SceneManager.activeSceneChanged-=ActiveSceneChanged;
        SetFighterTopLight(0);
        RestoreSun();
        if(SceneManager.GetActiveScene()==gameObject.scene)RestoreAmbient();
    }
}
