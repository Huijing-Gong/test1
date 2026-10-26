using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class PlayerPerfectDodgeFeedback : MonoBehaviour
{
    sealed class Ghost { public Mesh mesh; public Matrix4x4 matrix; public float end; }
    readonly List<Ghost> ghosts=new List<Ghost>();
    MaterialPropertyBlock properties;
    SkinnedMeshRenderer[] skins;
    Material spectral,ringMaterial;
    LineRenderer ring;
    AudioSource sound;
    AudioClip success,rush,release;
    PlayerBattleActions owner;
    Camera view;
    float burstAt=-10,nextGhost;
    Vector3 ringCenter;
    bool active;
    public int SuccessCues {get;private set;}
    public int RushCues {get;private set;}
    public bool Ready=>spectral&&ringMaterial&&success&&rush&&release;
    void Awake(){
        properties=new MaterialPropertyBlock();
        EnsureListener();
        owner=GetComponent<PlayerBattleActions>();skins=GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var shader=Shader.Find("Combat/Focus Spectral");if(shader)spectral=new Material(shader);
        var contactShader=Shader.Find("Combat/Focus Ring");if(contactShader)ringMaterial=new Material(contactShader);
        var go=new GameObject("Perfect evade pulse");go.transform.SetParent(transform,false);ring=go.AddComponent<LineRenderer>();
        ring.useWorldSpace=true;ring.loop=true;ring.positionCount=64;ring.widthMultiplier=.025f;ring.sharedMaterial=ringMaterial;
        ring.shadowCastingMode=ShadowCastingMode.Off;ring.receiveShadows=false;ring.enabled=false;
        sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.spatialBlend=0;sound.volume=.72f;sound.dopplerLevel=0;
        success=Resources.Load<AudioClip>("PerfectDodge/PerfectSuccess");rush=Resources.Load<AudioClip>("PerfectDodge/RushStep");release=Resources.Load<AudioClip>("PerfectDodge/FocusRelease");
    }
    void EnsureListener(){
        foreach(var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))if(listener.isActiveAndEnabled)return;
        var camera=Camera.main;if(!camera){var rig=FindFirstObjectByType<ThirdPersonCamera>();if(rig)camera=rig.GetComponent<Camera>();}if(!camera)return;var target=camera.GetComponent<AudioListener>();if(!target)target=camera.gameObject.AddComponent<AudioListener>();target.enabled=true;
    }
    public void Begin(){EnsureListener();active=true;SuccessCues++;burstAt=Time.unscaledTime;ringCenter=transform.position+Vector3.up*.07f;nextGhost=0;if(success)sound.PlayOneShot(success,.85f);}
    public void Rush(){RushCues++;nextGhost=0;if(rush)sound.PlayOneShot(rush,.48f);}
    public void End(){active=false;if(release)sound.PlayOneShot(release,.45f);}
    void LateUpdate(){
        float now=Time.unscaledTime;
        if(active&&owner&&(owner.IsDodging||owner.IsCountering)&&now>=nextGhost){BakeGhost(now);nextGhost=now+.055f;}
        for(int i=ghosts.Count-1;i>=0;i--){var ghost=ghosts[i];if(now>=ghost.end){Destroy(ghost.mesh);ghosts.RemoveAt(i);continue;}
            properties.SetColor("_Tint",new Color(.18f,.75f,1,Mathf.Clamp01((ghost.end-now)/.26f)*.33f));
            if(spectral)for(int sub=0;sub<ghost.mesh.subMeshCount;sub++)Graphics.DrawMesh(ghost.mesh,ghost.matrix,spectral,gameObject.layer,null,sub,properties,ShadowCastingMode.Off,false);
        }
        float age=now-burstAt;ring.enabled=age>=0&&age<.42f;
        if(ring.enabled){float t=age/.42f;float radius=Mathf.Lerp(.2f,1.35f,1-(1-t)*(1-t));ring.startColor=ring.endColor=new Color(.48f,.92f,1,(1-t)*.85f);ring.widthMultiplier=(1-t)*.04f;
            for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;ring.SetPosition(i,ringCenter+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius);}}
    }
    void BakeGhost(float now){
        if(!spectral)return;
        foreach(var skin in skins){if(!skin||!skin.enabled||!skin.gameObject.activeInHierarchy||!skin.sharedMesh)continue;
            var mesh=new Mesh();skin.BakeMesh(mesh,false);ghosts.Add(new Ghost{mesh=mesh,matrix=skin.transform.localToWorldMatrix,end=now+.26f});}
    }
    void OnGUI(){
        if(!owner||(Time.timeScale<=0&&!owner.HitStopActive))return;
        float age=Time.unscaledTime-burstAt;
        Color old=GUI.color;
        if(age>=0&&age<.16f){GUI.color=new Color(.7f,.96f,1,.2f*(1-age/.16f));GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);}
        if(owner.FocusActive){
            if(!view)view=Camera.main;
            float fade=Mathf.Clamp01(owner.FocusRemaining/.3f);
            GUI.color=new Color(.25f,.8f,1,.32f*fade);GUI.DrawTexture(new Rect(0,0,Screen.width,5),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(0,Screen.height-5,Screen.width,5),Texture2D.whiteTexture);
            var title=new GUIStyle(GUI.skin.label){fontSize=age<.3f?25:22,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            GUI.color=new Color(.78f,.97f,1,fade);GUI.Label(new Rect(0,Screen.height*.13f,Screen.width,36),age<.5f?"PERFECT DODGE":"LINK TIME",title);
            var hint=new GUIStyle(GUI.skin.label){fontSize=13,alignment=TextAnchor.MiddleCenter};GUI.Label(new Rect(0,Screen.height*.13f+33,Screen.width,23),owner.FocusHint,hint);
            if(owner.CounterPromptReady){
                float pulse=.5f+.5f*Mathf.Sin(Time.unscaledTime*9);float x=Screen.width*.5f,y=Screen.height*.74f;
                GUI.color=new Color(.5f,.94f,1,.18f+.18f*pulse);GUI.DrawTexture(new Rect(x-34-5*pulse,y-5*pulse,68+10*pulse,78+10*pulse),Texture2D.whiteTexture);
                GUI.color=new Color(.025f,.065f,.09f,.94f);GUI.DrawTexture(new Rect(x-27,y+6,54,65),Texture2D.whiteTexture);
                GUI.color=new Color(.65f,.97f,1,.55f+.45f*pulse);GUI.DrawTexture(new Rect(x-23,y+10+3*pulse,21,25),Texture2D.whiteTexture);
                GUI.color=new Color(.65f,.97f,1,1);GUI.DrawTexture(new Rect(x+1,y+10,2,25),Texture2D.whiteTexture);
                GUI.Label(new Rect(x-100,y+78,200,30),WorldInputBindings.Label("Attack")+"  /  STRIKE",title);
            }
            float width=150;GUI.color=new Color(.06f,.2f,.25f,.8f*fade);GUI.DrawTexture(new Rect((Screen.width-width)*.5f,Screen.height*.13f+60,width,3),Texture2D.whiteTexture);
            GUI.color=new Color(.5f,.94f,1,fade);GUI.DrawTexture(new Rect((Screen.width-width)*.5f,Screen.height*.13f+60,width*owner.FocusFraction,3),Texture2D.whiteTexture);
            if(owner.FocusTarget&&view){var at=view.WorldToScreenPoint(owner.FocusTarget.transform.position+Vector3.up);if(at.z>0){GUI.Label(new Rect(at.x-45,Screen.height-at.y-12,90,24),"◇",title);}}
        }
        GUI.color=old;
    }
    public void Clear(){active=false;burstAt=-10;if(ring)ring.enabled=false;if(sound)sound.Stop();foreach(var ghost in ghosts)if(ghost.mesh)Destroy(ghost.mesh);ghosts.Clear();}
    void OnDisable(){Clear();}
    void OnDestroy(){Clear();if(spectral)Destroy(spectral);if(ringMaterial)Destroy(ringMaterial);}
}
