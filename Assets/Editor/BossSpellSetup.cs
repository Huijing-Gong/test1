using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SamProduction.BossArena;

public static class BossSpellSetup
{
    const string Root = "Assets/BossSpells";
    const string Wizard = Root + "/SourceFX/Hovl Studio/Wizard spells pack/Prefabs/";
    const string Slashes = Root + "/SourceFX/OrdosFX/Magic Slashes FX/Prefabs/EffectPrefabs/";
    [MenuItem("ArtLab/Boss/Prepare guard candidates")]
    public static void PrepareGuards()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Prepare outside play mode");
        foreach(var name in new[]{"CastingBlock01_R - Loop","CastingBlock01_R - Hit","CastingBlock01_L - Loop","CastingBlock01_L - Hit","Parry2H01 - Loop","Parry2H01 - Hit"})Clip(name);
        ConvertFX(Wizard+"Magic shield/Magic shield loop yellow.prefab","GuardLoop");
        ConvertFX(Wizard+"Magic shield/Magic shield blue.prefab","GuardImpact");
        ConvertFX(Wizard+"Magic shield/Shield cast.prefab","GuardCast");
        ConvertFX(Wizard+"Magic shield/Shield buff.prefab","GuardBuff");
        AssetDatabase.SaveAssets();
    }
    [MenuItem("ArtLab/Boss/Apply directional defense")]
    public static void ApplyDefense()
    {
        PrepareGuards();
        var brain=UnityEngine.Object.FindFirstObjectByType<ArenaBossBrain>();
        var defense=brain.GetComponent<ArenaBossDefense>()??Undo.AddComponent<ArenaBossDefense>(brain.gameObject);
        Undo.RecordObject(defense,"Bind Boss defense");
        defense.guardLoop=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Clips/CastingBlock01_R - Loop.anim");
        defense.guardHit=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Clips/CastingBlock01_R - Hit.anim");
        defense.bodyHit=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AuthorMotions/Clips/Core/Hit.anim");
        defense.shieldEffect=null;
        defense.blockEffect=null;
        defense.hitEffect=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/SpellCharge.prefab");
        brain.v13Motor.defense=defense;
        EditorUtility.SetDirty(defense);EditorUtility.SetDirty(brain.v13Motor);
        EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);EditorSceneManager.SaveScene(brain.gameObject.scene);
    }
    [MenuItem("ArtLab/Boss/Configure seven spells")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Configure outside play mode");
        Directory.CreateDirectory(Root + "/Clips"); Directory.CreateDirectory(Root + "/Prefabs"); Directory.CreateDirectory(Root + "/Materials"); AssetDatabase.Refresh();
        var brain = UnityEngine.Object.FindFirstObjectByType<ArenaBossBrain>();
        if (!brain || !brain.v13Motor) throw new InvalidOperationException("Open the formal Boss arena");
        var component = brain.GetComponent<ArenaBossSpells>() ?? Undo.AddComponent<ArenaBossSpells>(brain.gameObject);
        string[] motions = { "MagicAttackDirect2H01", "MagicAttackDirect1H01_R", "MagicAttackCall1H01_R", "MagicAttackCall1H02_R", "SpecialMagicAttack01", "MagicAttackDirect2H02", "MagicAttackCall1H02_R" };
        string[] sources = { Slashes+"Eff5.prefab", Slashes+"Eff12.prefab", Wizard+"Lightning/Lightning strike.prefab", Wizard+"Front attack/Spike.prefab", Wizard+"Sword air attack/ULT Sword.prefab", Wizard+"Lightning/Electro orb.prefab", Wizard+"Temporary explosion/Temporary explosion.prefab" };
        float[] damage = { 20, 20, 24, 12, 32, 16, 24 };
        float[] radius = { 1.35f, .9f, 1.9f, 1.65f, 2.7f, 1.2f, 2.8f };
        float[] scale = { .95f, 1.05f, 1.3f, 1.65f, .8f, .95f, .9f };
        var charge = ConvertFX(Wizard+"Projectiles/Flash 1.prefab", "SpellCharge");
        Undo.RecordObject(component, "Configure Boss spells");
        component.spells = new ArenaBossSpells.Spell[7];
        for (int i = 0; i < 7; i++)
        {
            var attack = (ArenaAttack)((int)ArenaAttack.FireWave + i);
            component.spells[i] = new ArenaBossSpells.Spell { attack=attack, clip=Clip(motions[i]), effect=ConvertFX(sources[i],attack.ToString()), chargeEffect=charge, damage=damage[i],radius=radius[i],visualScale=scale[i] };
        }
        component.spells[4].impactEffect=ConvertFX(Wizard+"Sword air attack/ULT Sword crater.prefab","SwordImpact");
        component.spells[6].chargeEffect=charge;
        brain.v13Motor.spells = component;
        EditorUtility.SetDirty(component);EditorUtility.SetDirty(brain.v13Motor);
        EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);EditorSceneManager.SaveScene(brain.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("BOSS_SPELL_SETUP: seven humanoid clips and adapted particle prefabs saved.");
    }
    static AnimationClip Clip(string name)
    {
        string path=Root+"/SourceAnimations/HumanM@"+name+".fbx";
        var importer=AssetImporter.GetAtPath(path) as ModelImporter;
        if(importer==null)throw new InvalidOperationException("Missing importer "+path);
        importer.animationType=ModelImporterAnimationType.Human;importer.importCameras=false;importer.importLights=false;importer.importBlendShapes=false;
        var definitions=importer.clipAnimations.Length>0?importer.clipAnimations:importer.defaultClipAnimations;
        foreach(var c in definitions){c.loopTime=false;c.lockRootRotation=true;c.lockRootPositionXZ=true;c.lockRootHeightY=true;c.heightFromFeet=true;}
        importer.clipAnimations=definitions;importer.SaveAndReimport();
        var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        if(!source.humanMotion)throw new InvalidOperationException("Not humanoid: "+name);
        string output=Root+"/Clips/"+name+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
        if(!clip){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,output);}
        else EditorUtility.CopySerialized(source,clip);
        clip.name=name;AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        // No source XZ displacement on the retargeted visual; the capsule owns movement.
        foreach(var b in AnimationUtility.GetCurveBindings(clip))
            if(b.type==typeof(Animator)&&(b.propertyName=="RootT.x"||b.propertyName=="RootT.z"))
                AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,clip.length,0));
        EditorUtility.SetDirty(clip);return clip;
    }
    static GameObject ConvertFX(string path,string name)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(!source)throw new InvalidOperationException("Missing FX "+path);
        var instance=UnityEngine.Object.Instantiate(source);instance.name=name;
        try
        {
            foreach(var t in instance.GetComponentsInChildren<Transform>(true))GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach(var b in instance.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(b);
            foreach(var c in instance.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
            foreach(var l in instance.GetComponentsInChildren<Light>(true)){l.intensity=Mathf.Min(l.intensity,1);l.color=Ember(l.color);l.shadows=LightShadows.None;}
            foreach(var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if(r.name.IndexOf("distortion",StringComparison.OrdinalIgnoreCase)>=0){r.enabled=false;continue;}
                var mats=r.sharedMaterials;
                for(int i=0;i<mats.Length;i++)if(mats[i])mats[i]=ConvertMaterial(mats[i]);
                r.sharedMaterials=mats;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                // The source spike includes decorative flying crystal cells/stones.
                // They read as loose confetti after shader conversion; retain the spike and short embers.
                if(name=="GroundRift"&&(ps.name=="PathCell"||ps.name=="PathStones"||ps.name=="EndlDesbis"))
                {ps.gameObject.SetActive(false);continue;}
                var main=ps.main;main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;main.maxParticles=Mathf.Min(main.maxParticles,1000);
                main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                if(ps.name=="Sparks"){main.startLifetime=.45f;main.startSizeMultiplier*=.7f;}
                main.startColor=Warm(main.startColor);
                var colour=ps.colorOverLifetime;if(colour.enabled)colour.color=Warm(colour.color,true);
                var speedColour=ps.colorBySpeed;if(speedColour.enabled)speedColour.color=Warm(speedColour.color,true);
                var trails=ps.trails;if(trails.enabled){trails.colorOverLifetime=Warm(trails.colorOverLifetime,true);trails.colorOverTrail=Warm(trails.colorOverTrail,true);}
                // These prefabs originally included their own anticipation. Our cast/hazard clock owns it.
                if(name=="LightningCall"||name=="DelayedBlast"||name=="SwordImpact")
                {
                    var em=ps.emission;var bursts=new ParticleSystem.Burst[em.burstCount];em.GetBursts(bursts);
                    float lead=name=="LightningCall"?.8f:name=="DelayedBlast"?1.3f:0;
                    for(int i=0;i<bursts.Length;i++){var b=bursts[i];b.time=Mathf.Max(0,b.time-lead);bursts[i]=b;}em.SetBursts(bursts);
                }
                if(name=="FireWave"||name=="FireBird")
                {
                    main.simulationSpace=ParticleSystemSimulationSpace.Local;
                    // The gameplay projectile owns travel; do not add the demo's 70–100 m/s particle motion.
                    if(main.startSpeed.constantMax>50){main.startSpeed=0;main.startLifetime=3;}
                    if(ps.transform.localPosition.z>3)ps.gameObject.SetActive(false);
                }
                if(name=="SwordFall")
                {
                    main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startSpeed=0;main.startLifetime=1.5f;
                    var velocity=ps.velocityOverLifetime;velocity.enabled=false;
                    var shape=ps.shape;shape.position=Vector3.zero;
                    if(ps.name=="Flash")ps.gameObject.SetActive(false);
                }
            }
            // Enlarge only the falling sword, not its damage radius or the crater.
            if(name=="SwordFall")instance.transform.localScale*=2;
            if(instance.GetComponentsInChildren<ParticleSystem>(true).Length==0)throw new InvalidOperationException("FX has no particles: "+path);
            return PrefabUtility.SaveAsPrefabAsset(instance,Root+"/Prefabs/"+name+".prefab");
        }
        finally{UnityEngine.Object.DestroyImmediate(instance);}
    }
    static Material ConvertMaterial(Material source)
    {
        string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
        string path=Root+"/Materials/"+id+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material){material.SetColor("_BaseColor",Ember(material.GetColor("_BaseColor"),true));EditorUtility.SetDirty(material);return material;}
        material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));material.name=source.name+" Boss URP";
        Texture texture=null;string property=null;
        foreach(var key in new[]{"_MainTex","_BaseMap","_Texture","_MainTexture"})if(source.HasProperty(key)&&source.GetTexture(key)){texture=source.GetTexture(key);property=key;break;}
        material.SetTexture("_BaseMap",texture);
        if(property!=null){material.SetTextureScale("_BaseMap",source.GetTextureScale(property));material.SetTextureOffset("_BaseMap",source.GetTextureOffset(property));}
        Color tint=source.HasProperty("_TintColor")?source.GetColor("_TintColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
        material.SetColor("_BaseColor",Ember(tint,true));material.SetFloat("_Surface",1);material.SetFloat("_Blend",2);
        material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
        AssetDatabase.CreateAsset(material,path);return material;
    }
    static Color Ember(Color c,bool neutral=false)
    {
        float brightness=Mathf.Max(c.r,Mathf.Max(c.g,c.b));
        return neutral?new Color(brightness,brightness,brightness,c.a):new Color(brightness,brightness*.28f,brightness*.035f,c.a);
    }
    static Gradient WarmGradient(Gradient source,bool neutral)
    {
        var keys=source.colorKeys;for(int i=0;i<keys.Length;i++)keys[i].color=Ember(keys[i].color,neutral);
        var result=new Gradient();result.SetKeys(keys,source.alphaKeys);result.mode=source.mode;return result;
    }
    static ParticleSystem.MinMaxGradient Warm(ParticleSystem.MinMaxGradient source,bool neutral=false)
    {
        switch(source.mode)
        {
            case ParticleSystemGradientMode.Color:return new ParticleSystem.MinMaxGradient(Ember(source.color,neutral));
            case ParticleSystemGradientMode.TwoColors:return new ParticleSystem.MinMaxGradient(Ember(source.colorMin,neutral),Ember(source.colorMax,neutral));
            case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(WarmGradient(source.gradientMin,neutral),WarmGradient(source.gradientMax,neutral));
            default:var value=new ParticleSystem.MinMaxGradient(WarmGradient(source.gradient,neutral));value.mode=source.mode;return value;
        }
    }
    [MenuItem("ArtLab/Boss/Check seven spell policy")]
    public static void CheckPolicy()
    {
        int checks=0;
        foreach(ArenaAttack a in Enum.GetValues(typeof(ArenaAttack)))
        {
            if(!ArenaSpellRules.IsSpell(a))continue;
            bool ranged=a<=ArenaAttack.SwordFall;
            if(ArenaSpellRules.CanSelect(a,9)!=ranged||ArenaSpellRules.CanSelect(a,2)==ranged)throw new Exception("Wrong range pool "+a);
            if(ArenaSpellRules.CanSelect(a,-1)||ArenaSpellRules.CanSelect(a,21)||ArenaSpellRules.CanSelect(a,float.NaN))throw new Exception("Invalid range accepted "+a);
            if(ArenaSpellRules.IsMobile(a)!=(a==ArenaAttack.GroundRift))throw new Exception("Wrong movement policy "+a);
            if(ArenaSpellRules.Cooldown(a)<8)throw new Exception("Cooldown too short "+a);checks++;
        }
        if(checks!=7)throw new Exception("Expected seven spells");
        Debug.Log("BOSS_SPELL_POLICY PASS: 7 skills, range boundaries, mobility and cooldowns");
    }
}
