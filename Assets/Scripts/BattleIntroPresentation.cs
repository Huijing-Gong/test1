using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

// Temporary countdown presentation only. Never moves a fighter or changes its
// physics. All original shared materials are restored before characters drop.
public sealed class BattleIntroPresentation : MonoBehaviour
{
    sealed class Surface
    {
        public Renderer renderer; public Material[] originals, temporary;
        public ShadowCastingMode shadows;
        public Transform fighter;
    }
    readonly List<Surface> surfaces = new List<Surface>();
    BattleCameraController controller;
    TMP_Text number;
    Transform player, enemy;
    Vector3 normalPosition;
    Vector2 originalNumberPosition;
    float normalFov, stepSeconds=1.1f;
    string phase;
    bool active;
    public bool Active => active;
    public int RevealSurfaceCount => surfaces.Count;
    public string Phase => phase;
    public float PhaseProgress { get; private set; }

    // Called before the first rendered frame, including time spent in menus.
    public void PrepareHidden(Transform blue, Transform red)
    {
        RestoreSurfaces();
        if (BattlePresentationSettings.ReducedMotion) return;
        PrepareSurfaces(blue,true);
        PrepareSurfaces(red,false);
    }

    public void Begin(Transform blue, Transform red, TMP_Text countdown, float duration=1.1f)
    {
        End();
        controller = BattleCameraController.Instance;
        if (!controller || !countdown) return;
        player=blue; enemy=red; number=countdown;
        stepSeconds=Mathf.Max(1.1f,duration);
        originalNumberPosition=number.rectTransform.anchoredPosition;
        controller.BeginIntroCamera(out normalPosition,out normalFov);
        active=true; phase=null;
        PrepareHidden(blue,red);
    }

    public void Tick(string value, float progress)
    {
        if (!active) return;
        PhaseProgress=progress;
        bool reduced=BattlePresentationSettings.ReducedMotion;
        if (phase!=value)
        {
            phase=value;
            if (value=="1") RestoreSurfaces();
        }
        if (reduced) RestoreSurfaces();
        // Keep the original shared wide shot for all three numbers.
        controller.SetIntroCamera(normalPosition,normalFov);
        number.rectTransform.anchoredPosition=originalNumberPosition;
        if (surfaces.Count>0)
        {
            float reveal=Mathf.SmoothStep(0,1,Mathf.Clamp01((progress*stepSeconds-.06f)/.72f));
            float glow=Mathf.Sin(Mathf.Clamp01(progress)*Mathf.PI)*.75f;
            foreach(var surface in surfaces)
            {
                Bounds bounds=FighterBounds(surface.fighter);
                float amount=surface.fighter==player ? (value=="3"?reveal:1f) : (value=="2"?reveal:0f);
                float limit=Mathf.Lerp(bounds.min.y-.08f,bounds.max.y+.16f,amount);
                foreach(var material in surface.temporary)
                { material.SetFloat("_RevealHeight",limit); material.SetFloat("_Glow",surface.fighter==(value=="3"?player:enemy)?glow:0f); }
            }
        }
    }

    static Bounds FighterBounds(Transform fighter)
    {
        var animator=fighter.GetComponentInChildren<Animator>();
        var renderers=animator?animator.GetComponentsInChildren<Renderer>():new Renderer[0];
        Bounds bounds=new Bounds(fighter.position,Vector3.one);
        bool found=false;
        foreach(var renderer in renderers)
            if(renderer.enabled) {if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
        return bounds;
    }

    void PrepareSurfaces(Transform fighter,bool blue)
    {
        Shader shader=Resources.Load<Shader>("SideA/IntroReveal");
        var animator=fighter.GetComponentInChildren<Animator>();
        if(!shader || !animator) return;
        foreach(var renderer in animator.GetComponentsInChildren<Renderer>())
        {
            if(!renderer.enabled) continue;
            var surface=new Surface{fighter=fighter,renderer=renderer,originals=renderer.sharedMaterials,shadows=renderer.shadowCastingMode};
            surface.temporary=new Material[surface.originals.Length];
            for(int i=0;i<surface.originals.Length;i++)
            {
                var original=surface.originals[i];
                var material=new Material(shader){name="Countdown reveal (temporary)"};
                if(original) material.CopyPropertiesFromMaterial(original);
                material.SetFloat("_RecolorTeam",original && original.HasProperty("_TeamColor")?1:0);
                material.SetColor("_RimTint",blue?new Color(.52f,.90f,.80f):new Color(1,.65f,.27f));
                material.SetFloat("_RevealHeight",FighterBounds(fighter).min.y-.08f);
                material.SetFloat("_Glow",0f);
                surface.temporary[i]=material;
            }
            surfaces.Add(surface); renderer.sharedMaterials=surface.temporary; renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
    void RestoreSurfaces()
    {
        foreach(var surface in surfaces)
        {
            if(surface.renderer){surface.renderer.sharedMaterials=surface.originals;surface.renderer.shadowCastingMode=surface.shadows;}
            foreach(var material in surface.temporary) if(material) Destroy(material);
        }
        surfaces.Clear();
    }
    public void End()
    {
        RestoreSurfaces();
        if(active && number) number.rectTransform.anchoredPosition=originalNumberPosition;
        if(active && controller) controller.EndIntroCamera();
        active=false; phase=null;
    }
    void OnDisable(){End();}
}
