using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SamProduction.BossArena
{
    // Presentation only. Never poses the rig, moves actors, receives hits or owns outcomes.
    [DefaultExecutionOrder(500)]
    public sealed class ArenaTeamVfxBridge : MonoBehaviour
    {
        public ArenaSession session;
        public Shader[] retainedShaders; // Assign the nine shipped shaders so build stripping cannot remove them.
        public bool enableDeathDust=true;
        public bool enableBlackGoldPunch=false;
        ArenaBossBrain brain; ArenaEclipseBossMotor motor; ArenaAnimation animationDriver;
        GameObject effectsRoot;
        FireDragonVFX dragon; ChargeFlameVFX charge; BlackGoldPunchVFX rightGold,leftGold; LandingImpactVFX landing;
        readonly List<DeathDissolveVFX> deaths=new List<DeathDissolveVFX>();
        readonly List<SkinnedMeshRenderer> skins=new List<SkinnedMeshRenderer>();
        readonly List<Mesh> baked=new List<Mesh>();
        Transform[] bones; Transform right,left;
        struct PoseFrame { public float time; public Vector3[] points; }
        readonly List<PoseFrame> history=new List<PoseFrame>();
        AnimationClip historyClip; float deathTime; int eventCursor; long attempt=-1;
        double landingStarted=-100; float landingMaximum;
        static readonly string[] ShaderNames={"Boss/DragonSurface","Boss/DragonFlame","Boss/ChargeFire","Boss/ChargeAura","Boss/BlackGoldFlame","Boss/BlackGoldInk","Boss/LandingImpact","Boss/DeathDissolve","Boss/DeathDust"};
        void Start()
        {
            if(!session) session=GetComponentInParent<ArenaSession>();
            if(!session || !session.boss || !session.boss.v13Motor) { enabled=false; return; }
            foreach(string name in ShaderNames) if(!Shader.Find(name)) { Debug.LogError("TEAM_VFX_SHADER_MISSING: "+name); enabled=false;return; }
            brain=session.boss;motor=brain.v13Motor;animationDriver=brain.animationDriver;
            effectsRoot=new GameObject("Team V13 visual additions");effectsRoot.transform.SetParent(brain.transform,false);
            effectsRoot.layer=brain.gameObject.layer;
            dragon=effectsRoot.AddComponent<FireDragonVFX>(); charge=effectsRoot.AddComponent<ChargeFlameVFX>();
            rightGold=effectsRoot.AddComponent<BlackGoldPunchVFX>(); leftGold=effectsRoot.AddComponent<BlackGoldPunchVFX>();landing=effectsRoot.AddComponent<LandingImpactVFX>();
            var all=brain.visual.GetComponentsInChildren<Transform>(true);
            Func<string,Transform> bone=name=>Array.Find(all,t=>t.name==name);
            bones=new Transform[charge.boneNames.Length];for(int i=0;i<bones.Length;i++)bones[i]=bone(charge.boneNames[i]);
            right=bone("Right_Hand");left=bone("Left_Hand");
            if(!right || !left) { Debug.LogError("TEAM_VFX_HAND_BINDINGS_MISSING");enabled=false;Destroy(effectsRoot);return; }
            dragon.fist=right;dragon.LiveTrack=t=>Historical(8,t);
            charge.anchors=bones;charge.LiveTrack=Historical;charge.stepTimes=Array.Empty<float>();charge.stepPositions=Array.Empty<Vector3>();
            rightGold.BindLive(right,bone("Right_UpperArm"),bone("Right_LowerArm"));
            leftGold.BindLive(left,bone("Left_UpperArm"),bone("Left_LowerArm"));
            // Renderer materials are instance substitutions, restored on disable/reset/unload.
            foreach(var skin in brain.visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!skin.enabled || !skin.sharedMesh || skin.sharedMesh.vertexCount==0) continue;
                var effect=effectsRoot.AddComponent<DeathDissolveVFX>();effect.Bind(skin);
                effect.startTime=motor.clips.J026.length*.55f;effect.duration=Mathf.Max(.1f,motor.clips.J026.length-effect.startTime);
                skins.Add(skin);deaths.Add(effect);baked.Add(new Mesh { name="Team death-only baked pose" });
            }
            animationDriver.Evaluated+=Capture; ResetVisuals();
        }
        void Capture(AnimationClip clip,float time)
        {
            if(clip==motor.clips.J026) deathTime=time;
            if(historyClip!=clip || (history.Count>0 && time<history[history.Count-1].time-.3f)) {history.Clear();historyClip=clip;}
            // Contact poses can query earlier endpoints. They must not create a backwards history.
            if(history.Count>0 && time<=history[history.Count-1].time) return;
            var points=new Vector3[bones.Length];for(int i=0;i<points.Length;i++)points[i]=bones[i]?bones[i].position:brain.transform.position;
            history.Add(new PoseFrame{time=time,points=points});
            while(history.Count>0 && history[0].time<time-.8f)history.RemoveAt(0);
        }
        Vector3 Historical(int bone,float time)
        {
            Vector3 world=bones[bone]?bones[bone].position:brain.transform.position;
            if(history.Count>0)
            {
                world=history[0].points[bone];
                for(int i=1;i<history.Count;i++)
                {
                    if(history[i].time>=time) {var a=history[i-1];var b=history[i];world=Vector3.Lerp(a.points[bone],b.points[bone],Mathf.InverseLerp(a.time,b.time,time));break;}
                    world=history[i].points[bone];
                }
            }
            return effectsRoot.transform.InverseTransformPoint(world);
        }
        void LateUpdate()=>RefreshVisuals();
        public void RefreshVisuals()
        {
            if(!effectsRoot || !session || !brain || !motor) return;
            if(attempt!=session.AttemptId || eventCursor>session.Events.Count) {attempt=session.AttemptId;eventCursor=0;ResetVisuals();}
            Camera camera=session.view?session.view.GetComponent<Camera>():Camera.main;
            if(!camera) {Hide(camera);return;}
            for(;eventCursor<session.Events.Count;eventCursor++)
            {
                var e=session.Events[eventCursor];
                if(e.kind!="boss_v13_release" || e.source!=brain.fighter.team.ToString())continue;
                if(e.skill.StartsWith("Meteor_",StringComparison.Ordinal)&&e.blackSunOwned)continue;
                if(e.skill.StartsWith("Stomp_",StringComparison.Ordinal)||e.skill.StartsWith("DiveKick_",StringComparison.Ordinal)||e.skill.StartsWith("Meteor_",StringComparison.Ordinal))
                {
                    landingStarted=e.time;landing.landingPoint=e.point+Vector3.up*.005f;
                    landingMaximum=e.skill.StartsWith("Meteor_",StringComparison.Ordinal)?motor.meteorRadius:e.skill.StartsWith("DiveKick_",StringComparison.Ordinal)?motor.diveRadius:motor.stompRadius;
                }
            }
            bool attack=session.Active && motor.Attacking && !brain.fighter.Dead;
            float time=motor.SkillTime;
            dragon.Sample(time,attack&&motor.CurrentAttack==ArenaAttack.Jab);
            charge.targetCamera=camera;charge.Sample(time,attack&&motor.CurrentAttack==ArenaAttack.DashPunch);
            bool combo=enableBlackGoldPunch&&attack&&(motor.CurrentAttack==ArenaAttack.ComboTwo||motor.CurrentAttack==ArenaAttack.ComboThree);
            SampleGold(rightGold,time,combo, motor.CurrentAttack==ArenaAttack.ComboThree&&time>=3.1f?3.416667f:1.5f,camera);
            SampleGold(leftGold,time,combo,2.458333f,camera);
            float age=(float)(session.BattleTime-landingStarted);
            landing.impactTime=0;landing.waveRadius=ArenaCombatFx.PulseRadius(Mathf.Clamp(age,0,.36f),landingMaximum);
            AdoptWorldRoot(rightGold.RuntimeRoot,false);AdoptWorldRoot(leftGold.RuntimeRoot,false);
            landing.Sample(age,session.Active&&!brain.fighter.Dead&&age>=0&&age<.68f,camera);
            AdoptWorldRoot(landing.RuntimeRoot,true);
            for(int i=0;i<deaths.Count;i++)
            {
                bool dead=brain.fighter.Dead;
                if(dead&&enableDeathDust)skins[i].BakeMesh(baked[i]);
                deaths[i].Sample(deathTime,dead,enableDeathDust?baked[i]:null,skins[i].transform,camera);
                AdoptWorldRoot(deaths[i].RuntimeRoot,false);
            }
        }
        void AdoptWorldRoot(Transform root,bool keepPosition)
        {
            if(!root)return;
            if(root.gameObject.scene!=brain.gameObject.scene)SceneManager.MoveGameObjectToScene(root.gameObject,brain.gameObject.scene);
            if(!keepPosition)root.position=Vector3.zero;
            root.rotation=Quaternion.identity;root.localScale=Vector3.one;
        }
        void SampleGold(BlackGoldPunchVFX effect,float time,bool active,float release,Camera camera)
        {
            effect.chargeStart=release-.4f;effect.releaseTime=release;effect.peakTime=release+.06f;effect.endTime=release+.28f;
            effect.Sample(time,active&&time>=effect.chargeStart&&time<effect.endTime,camera);
        }
        void Hide(Camera camera)
        {
            if(!effectsRoot)return;
            dragon.Sample(0,false);charge.Sample(0,false);rightGold.Sample(0,false,camera);leftGold.Sample(0,false,camera);landing.Sample(0,false,camera);
            for(int i=0;i<deaths.Count;i++)deaths[i].Sample(0,false,null,null,null);
        }
        void ResetVisuals(){history.Clear();historyClip=null;deathTime=0;landingStarted=-100;Hide(null);}
        void OnDisable(){if(effectsRoot)ResetVisuals();}
        void OnDestroy()
        {
            if(animationDriver)animationDriver.Evaluated-=Capture;
            if(effectsRoot)Destroy(effectsRoot);
            foreach(var mesh in baked)if(mesh)Destroy(mesh);
        }
    }
}
