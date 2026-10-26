using System.Collections.Generic;
using UnityEngine;

// Runtime instances only: daylight materials and the authored lamp models stay intact.
public sealed class BaselineEclipseLamps : MonoBehaviour
{
    sealed class Lamp
    {
        public Renderer renderer;
        public Material[] originals, lit;
        public GameObject lightObject;
        public Light light, pool;
        public readonly MaterialPropertyBlock glow=new MaterialPropertyBlock();
    }
    readonly List<Lamp> lamps=new List<Lamp>();
    readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
    readonly List<RenderTexture> masks=new List<RenderTexture>();
    bool initialized;
    float lastAmount=-1;
    bool lastGentle;
    public int LampCount => lamps.Count;

    public void SetEclipse(float stage,bool gentle=false)
    {
        if(!initialized) Initialize();
        float amount=Mathf.Clamp01(stage);
        if(Mathf.Abs(amount-lastAmount)<.001f && lastGentle==gentle) return;
        lastAmount=amount; lastGentle=gentle;
        for(int index=0;index<lamps.Count;index++)
        {
            var lamp=lamps[index];
            if(!lamp.renderer) continue;
            float delay=lamps.Count<=1?0:index/(float)(lamps.Count-1)*(gentle?.1f:.45f);
            float glow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(
                (gentle?0:.18f)+delay,(gentle?.9f:.48f)+delay,amount));
            lamp.renderer.sharedMaterials=glow>0?lamp.lit:lamp.originals;
            lamp.renderer.GetPropertyBlock(lamp.glow);
            lamp.glow.SetColor("_EmissionColor",new Color(1f,.62f,.23f)*glow*2.6f);
            lamp.renderer.SetPropertyBlock(lamp.glow);
            // The glass supplies emission; use a single downward light per fixture.
            // Overlapping point + spot lights exhausted per-object light slots,
            // causing nearby ground patches to pop as the camera moved.
            lamp.light.enabled=false;
            lamp.light.intensity=0;
            lamp.pool.enabled=glow>.001f;
            lamp.pool.intensity=3f*glow;
        }
    }

    void Initialize()
    {
        initialized=true;
        var maskShader=Resources.Load<Shader>("EclipseLampEmission");
        Material maskMaterial=maskShader?new Material(maskShader):null;
        foreach(var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(renderer.gameObject.scene!=gameObject.scene || renderer.name!="street_lamp_3d_model") continue;
            var lamp=new Lamp { renderer=renderer, originals=renderer.sharedMaterials };
            lamp.lit=(Material[])lamp.originals.Clone();
            for(int i=0;i<lamp.lit.Length;i++)
            {
                var original=lamp.originals[i];
                if(!original || !original.HasProperty("_EmissionColor")) continue;
                if(!materials.TryGetValue(original,out var lit))
                {
                    lit=new Material(original) { name=original.name+" (eclipse lamp)" };
                    lit.EnableKeyword("_EMISSION");
                    var texture=original.HasProperty("_BaseMap")?original.GetTexture("_BaseMap"):null;
                    if(texture && maskMaterial)
                    {
                        // Isolate the pale glass in the existing texture, not the dark metal pole.
                        var mask=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                        mask.name="Lamp glass emission mask"; mask.Create();
                        var active=RenderTexture.active;
                        Graphics.Blit(texture,mask,maskMaterial);
                        RenderTexture.active=active;
                        lit.SetTexture("_EmissionMap",mask); masks.Add(mask);
                    }
                    else lit.DisableKeyword("_EMISSION");
                    lit.SetColor("_EmissionColor",Color.black);
                    materials.Add(original,lit);
                }
                lamp.lit[i]=lit;
            }
            var bounds=renderer.bounds;
            lamp.lightObject=new GameObject("Eclipse warm lantern light");
            lamp.lightObject.transform.SetParent(renderer.transform,false);
            // The authored hanging lantern sits to the negative-X side of its pole.
            // Use model space so rotated copies illuminate from their glass, not the post.
            var local=renderer.localBounds;
            lamp.lightObject.transform.localPosition=new Vector3(
                Mathf.Lerp(local.min.x,local.max.x,.18f),
                Mathf.Lerp(local.min.y,local.max.y,.68f),local.center.z);
            lamp.light=lamp.lightObject.AddComponent<Light>();
            lamp.light.type=LightType.Point;
            lamp.light.color=new Color(1f,.69f,.36f);
            lamp.light.range=Mathf.Clamp(bounds.size.y*2.5f,5f,9f);
            lamp.light.shadows=LightShadows.None;
            lamp.light.renderMode=LightRenderMode.ForcePixel;
            lamp.light.enabled=false;
            var poolObject=new GameObject("Warm road light pool");
            poolObject.transform.SetParent(lamp.lightObject.transform,false);
            poolObject.transform.rotation=Quaternion.LookRotation(Vector3.down);
            lamp.pool=poolObject.AddComponent<Light>();
            lamp.pool.type=LightType.Spot;
            lamp.pool.color=new Color(1f,.58f,.26f);
            lamp.pool.range=Mathf.Clamp(bounds.size.y*2.2f,6,10);
            lamp.pool.spotAngle=90;
            lamp.pool.innerSpotAngle=45;
            lamp.pool.renderMode=LightRenderMode.ForcePixel;
            lamp.pool.shadows=LightShadows.None;
            lamp.pool.enabled=false;
            lamps.Add(lamp);
        }
        if(maskMaterial) Destroy(maskMaterial);
        Vector3 origin=Camera.main?Camera.main.transform.position:transform.position;
        lamps.Sort((a,b)=>(a.renderer.bounds.center-origin).sqrMagnitude.CompareTo((b.renderer.bounds.center-origin).sqrMagnitude));
    }

    void OnDisable() { if(initialized) SetEclipse(0); }
    void OnDestroy()
    {
        foreach(var lamp in lamps)
        {
            if(lamp.renderer) lamp.renderer.sharedMaterials=lamp.originals;
            if(lamp.lightObject) Destroy(lamp.lightObject);
        }
        foreach(var material in materials.Values) if(material) Destroy(material);
        foreach(var mask in masks) if(mask) { mask.Release(); Destroy(mask); }
    }
}
