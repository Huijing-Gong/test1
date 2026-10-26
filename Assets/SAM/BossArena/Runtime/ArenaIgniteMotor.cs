using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    [DisallowMultipleComponent]
    public sealed class ArenaIgniteMotor : MonoBehaviour
    {
        [Serializable] public sealed class Contract
        { public string id,source_sha256;public float ignitionTime,releaseTime,phaseVisualTime,completeSeconds; }
        public AnimationClip clip;
        public TextAsset motionData,contractData;
        public ArenaIgnitePresentation presentation;
        public Contract Settings {get;private set;}
        public bool Active {get;private set;}
        public bool Completed {get;private set;}
        public float SourceTime {get;private set;}
        public int BeginCount {get;private set;}
        private ArenaBossBrain owner;
        private ArenaSession arena;
        private double clock;
        private bool ignited,released,phaseVisual;
        private Vector3 cameraStart,focusStart;
        private float fovStart;
        private float[] boundaries;
        public void Validate()
        {
            if(clip==null || motionData==null || contractData==null || presentation==null)throw new InvalidOperationException("Ignition source assets missing.");
            Settings=JsonUtility.FromJson<Contract>(contractData.text);
            boundaries=new[]{Settings.ignitionTime,Settings.releaseTime,Settings.phaseVisualTime};
            var motion=ArenaMotionData.Read(motionData).Get("phase_ignite");
            if(Settings.id!=motion.id || Mathf.Abs(clip.length-motion.duration)>.002f || Settings.completeSeconds!=4.8f
                || Settings.ignitionTime<=0 || Settings.releaseTime<=Settings.ignitionTime || Settings.phaseVisualTime<=Settings.releaseTime
                || Settings.phaseVisualTime>=Settings.completeSeconds)throw new InvalidOperationException("Ignition timing contract mismatch.");
            foreach(var row in motion.trajectory)if(row.Position.sqrMagnitude>1e-10f)throw new InvalidOperationException("Ignition must use grounded source.");
        }
        public void Initialize(ArenaBossBrain brain){owner=brain;Validate();}
        public void ResetMotor()
        {
            if(arena!=null)arena.view.EndCinematic(this);
            if(owner!=null)owner.fighter.Invulnerable=false;
            Active=Completed=ignited=released=phaseVisual=false;clock=0;SourceTime=0;BeginCount=0;presentation?.Clear();
        }
        public bool TryBegin(ArenaSession session)
        {
            if(Active || Completed || BeginCount>0 || !session.Active || owner.fighter.Dead || !owner.FoundationMotor.IsGuard)return false;
            arena=session;clock=0;SourceTime=0;ignited=released=phaseVisual=false;
            cameraStart=arena.view.transform.position;focusStart=cameraStart+arena.view.transform.forward*5;
            fovStart=arena.view.GetComponent<Camera>().fieldOfView;
            arena.view.BeginCinematic(this);Active=true;BeginCount++;owner.fighter.Invulnerable=true;
            owner.FoundationMotor.SuspendForAttack();owner.animationDriver.PoseContactClip(clip,0,0);
            presentation.Begin(owner);Record("ignite_started");Record("player_control_taken");return true;
        }
        // Ordinary hit/cancel cannot interrupt the confirmed transformation. A round reset
        // or terminal state still releases every owned resource through Reset/Finish.
        public void Cancel(bool death)
        {if(Active && (death || !arena.Active))Finish(false);}
        public void TickPresentation(float delta) { if(!Active && Completed)presentation.TickOverheat(delta); }
        public void Tick(float delta,ArenaSession session)
        {
            arena=session;if(!Active)return;
            if(!session.Active || owner.fighter.Dead){Finish(false);return;}
            if(delta<0 || delta>.25f || float.IsNaN(delta))throw new ArgumentOutOfRangeException(nameof(delta));
            owner.fighter.Invulnerable=true;
            double remaining=delta;
            while(remaining>1e-10 && Active)
            {
                double next=Math.Min(Settings.completeSeconds,clock+Math.Min(remaining,1d/120));
                foreach(float boundary in boundaries)
                    if(clock<boundary && next>boundary)next=boundary;
                double amount=next-clock;if(amount<=0)throw new InvalidOperationException("Ignition clock failed to advance.");
                float dt=(float)amount;clock=next;remaining-=amount;SourceTime=(float)clock;
                owner.capsule.Move(Vector3.down*(2*dt));owner.animationDriver.PoseContactClip(clip,clock,dt);
                if(!ignited && clock>=Settings.ignitionTime){ignited=true;presentation.Ignite();Record("ignite_core_open");}
                if(!released && clock>=Settings.releaseTime){released=true;presentation.Release();Record("ignite_flame_release");}
                if(!phaseVisual && clock>=Settings.phaseVisualTime){phaseVisual=true;presentation.EnableOverheat();Record("ignite_overheat_visual");}
                presentation.Tick(dt,SourceTime);PoseCamera();
                if(clock>=Settings.completeSeconds-1e-10)Finish(true);
            }
        }
        private void PoseCamera()
        {
            float t=SourceTime;
            float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.5f));
            float push=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,1.6f,t));
            float open=Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.8f,3.6f,t));
            // Settle into the shoulder grip, hold the core ignition, then reveal
            // the flame release. The motor and its input-lock clock are unchanged.
            var closeFocus=Vector3.Lerp(new Vector3(-.1f,2.35f,.08f),new Vector3(-.15f,2.30f,.08f),push);
            var closePosition=Vector3.Lerp(new Vector3(-2.15f,2.65f,4.1f),new Vector3(-1.65f,2.55f,3.55f),push);
            var focus=owner.transform.TransformPoint(Vector3.Lerp(closeFocus,new Vector3(-.1f,1.9f,.08f),open));
            var position=owner.transform.TransformPoint(Vector3.Lerp(closePosition,new Vector3(-3.7f,2.9f,6.2f),open));
            arena.view.PoseCinematic(this,Vector3.Lerp(focusStart,focus,enter),Vector3.Lerp(cameraStart,position,enter),Mathf.Lerp(fovStart,42,enter));
        }
        private void Finish(bool success)
        {
            Active=false;Completed=success;owner.fighter.Invulnerable=false;
            arena.view.EndCinematic(this);
            if(!success)presentation.Clear();
            Record(success?"ignite_complete":"ignite_terminal_cancel");Record("player_control_restored");
            owner.OnGroundSkillFinished(owner.fighter.Dead);
        }
        private void Record(string kind)=>arena.Record(kind,"phase_ignite",owner.fighter,0,owner.transform.position);
    }
}
