using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using SamProduction.BossArena;

public static class BossSpellPlayChecks
{
    public static string Run()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Play the arena first");
        var arena=UnityEngine.Object.FindFirstObjectByType<ArenaSession>();
        var spells=arena.boss.GetComponent<ArenaBossSpells>();
        if(spells==null||spells.spells.Length!=7)throw new Exception("Seven spell bindings missing");
        bool automatic=arena.automaticTick,decisions=arena.boss.decisionEnabled;
        var report=new System.Text.StringBuilder();
        try
        {
            arena.automaticTick=false;arena.boss.decisionEnabled=false;
            foreach(var s in spells.spells)
            {
                arena.Restart(true);arena.player.fighter.TrainingInvincible=true;
                float distance=s.attack>=ArenaAttack.ThunderOrb?2.5f:9f;
                Place(arena.player.capsule,arena.boss.transform.position+arena.boss.transform.forward*distance);
                arena.player.transform.rotation=Quaternion.LookRotation(arena.boss.transform.position-arena.player.transform.position);
                var origin=arena.boss.transform.position;
                if(!s.clip.humanMotion||!arena.boss.ForceAttack(s.attack,arena))throw new Exception("Could not start "+s.attack);
                float release=spells.ReleaseTime;
                bool captured=false;float maxSpeed=0;int maxParticles=0;
                for(int i=0;i<480;i++)
                {
                    arena.Simulate(default,1f/60);maxSpeed=Mathf.Max(maxSpeed,arena.boss.ActualSpeed);
                    if(!captured&&arena.BattleTime>release+(s.attack==ArenaAttack.SwordFall?1.0f:s.attack==ArenaAttack.LightningCall?.6f:.25f))
                    {
                        Capture(arena,s.attack.ToString());captured=true;
                        foreach(var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))if(ps.transform.root.name.StartsWith("Boss spell FX"))maxParticles+=ps.particleCount;
                    }
                }
                int releases=arena.Events.Count(e=>e.kind=="boss_spell_released"&&e.skill==s.attack.ToString());
                int impacts=arena.Events.Count(e=>e.kind=="boss_spell_impact"&&e.skill==s.attack.ToString());
                if(releases!=1||impacts!=(s.attack==ArenaAttack.GroundRift?5:1))throw new Exception("Wrong release/impact count "+s.attack+" "+releases+"/"+impacts);
                if(s.attack==ArenaAttack.DelayedBlast)
                {
                    double released=arena.Events.First(e=>e.kind=="boss_spell_released").time;
                    double impact=arena.Events.First(e=>e.kind=="boss_spell_impact").time;
                    if(impact-released>1.0/120+.0001)throw new Exception("Downstroke blast has an extra fuse");
                    report.AppendLine("Downstroke to blast: "+(impact-released).ToString("F4")+" seconds");
                }
                if(arena.player.fighter.HitCount==0)throw new Exception("No hit on stationary in-range player: "+s.attack);
                if(s.attack==ArenaAttack.GroundRift&&maxSpeed<.2f)throw new Exception("Ground rift stopped locomotion");
                if(arena.effects.WarningVisible)throw new Exception("Ground warning outlines returned");
                if(arena.boss.Attacking||spells.HazardCount!=0)throw new Exception("Skill did not finish "+s.attack);
                report.AppendLine(s.attack+": releases="+releases+", impacts="+impacts+", hits="+arena.player.fighter.HitCount+", moving="+maxSpeed.ToString("F2")+", particles="+maxParticles);
                arena.Restart(false);
                if(spells.HazardCount!=0||spells.VisualCount!=0||spells.Casting)throw new Exception("Retry did not clear "+s.attack);
            }
            // A targeted area must not chase a player who leaves the committed position.
            arena.Restart(true);arena.player.fighter.TrainingInvincible=true;
            var point=arena.boss.transform.position+arena.boss.transform.forward*9;
            Place(arena.player.capsule,point);arena.boss.ForceAttack(ArenaAttack.LightningCall,arena);
            Place(arena.player.capsule,point+arena.boss.transform.right*5);
            for(int i=0;i<360;i++)arena.Simulate(default,1f/60);
            if(arena.player.fighter.HitCount!=0)throw new Exception("Lightning tracked a departed target");
            report.AppendLine("Committed lightning target dodge: PASS");
            arena.Restart(true);Place(arena.player.capsule,point);arena.boss.ForceAttack(ArenaAttack.FireWave,arena);
            spells.EndCast();
            if(spells.Begin(ArenaAttack.FireWave,arena))throw new Exception("Cooldown bypassed");
            arena.Restart(false);if(spells.HazardCount!=0||spells.VisualCount!=0)throw new Exception("Mid-cast retry leak");
            report.AppendLine("Cooldown and mid-cast reset: PASS");
            Directory.CreateDirectory("Temp/BossSpellReview");File.WriteAllText("Temp/BossSpellReview/checks.txt",report.ToString());
            return report.ToString();
        }
        finally
        {
            arena.Restart(false);arena.player.fighter.TrainingInvincible=false;arena.automaticTick=automatic;arena.boss.decisionEnabled=decisions;
        }
    }
    static void Place(CharacterController cc,Vector3 p)
    {
        cc.enabled=false;cc.transform.position=p;cc.enabled=true;Physics.SyncTransforms();cc.Move(Vector3.down*.3f);
    }
    public static void Capture(ArenaSession arena,string name,bool close=false)
    {
        arena.hud.Refresh();Canvas.ForceUpdateCanvases();
        var camera=arena.view.GetComponent<Camera>();var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
        var previous=camera.targetTexture;var active=RenderTexture.active;
        var target=RenderTexture.GetTemporary(960,540,24);var image=new Texture2D(960,540,TextureFormat.RGB24,false);
        // Manual multi-sample checks run inside one Editor frame; bake the evaluated pose instead
        // of reading Unity's previous-frame GPU skinning cache into every screenshot.
        var skins=arena.boss.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled).ToArray();
        var baked=new System.Collections.Generic.List<GameObject>();var meshes=new System.Collections.Generic.List<Mesh>();
        try
        {
            foreach(var skin in skins)
            {
                var mesh=new Mesh();skin.BakeMesh(mesh);meshes.Add(mesh);
                var go=new GameObject("Pose capture mesh");go.transform.SetParent(skin.transform,false);
                // This imported rig has a 265x mesh transform; BakeMesh returns metre-sized vertices.
                var scale=skin.transform.lossyScale;go.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                skin.enabled=false;baked.Add(go);
            }
            Vector3 centre=(arena.boss.transform.position+arena.player.transform.position)*.5f+Vector3.up;
            camera.transform.position=centre+arena.boss.transform.right*9-arena.boss.transform.forward*7+Vector3.up*5;
            if(close){centre=arena.boss.transform.position+Vector3.up*1.35f;camera.transform.position=centre+arena.boss.transform.forward*3.5f+arena.boss.transform.right*1.8f+Vector3.up*.4f;}
            camera.transform.LookAt(centre);camera.fieldOfView=52;camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
            Directory.CreateDirectory("Temp/BossSpellReview");File.WriteAllBytes("Temp/BossSpellReview/"+name+".png",image.EncodeToPNG());
        }
        finally{foreach(var s in skins)if(s)s.enabled=true;foreach(var go in baked)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);camera.targetTexture=previous;camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(image);}
    }
}
