using System;
using UnityEngine;

namespace SamProduction.BossArena
{
    // Source time, pose, contact and damage have one owner, advanced by ArenaSession.
    [DisallowMultipleComponent]
    public sealed class ArenaStompMotor : MonoBehaviour
    {
        [Serializable] public sealed class Contract
        {
            public string id, source_sha256;
            public float chargeSeconds, stampTime, completeSeconds, waveRadiusMetres, waveDurationSeconds;
        }
        public AnimationClip clip;
        public TextAsset motionData, contractData;
        public ArenaStompPresentation presentation;
        public Vector3 localStampContact;
        [Tooltip("Cosmetic foot-contact flash only; does not change the travelling damage wave.")]
        public bool emitFootImpact = true;
        public bool Active { get; private set; }
        public bool Cancelled { get; private set; }
        public float SourceTime { get; private set; }
        public float WaveRadius { get; private set; }
        public int ReleaseCount { get; private set; }
        public long ExecutionId { get; private set; }
        public long StrikeId { get; private set; }
        public Vector3 Centre { get; private set; }
        public float ContactMismatch { get; private set; }
        public bool Telegraph => Active && !Cancelled && SourceTime < Settings.stampTime;
        public bool Recovering => Active && SourceTime >= Settings.stampTime;
        public float Progress => Settings == null ? 0 : SourceTime / Settings.completeSeconds;
        public Contract Settings { get; private set; }
        private ArenaBossBrain owner;
        private ArenaSession arena;
        private ArenaMotionClip motion;
        private bool released;
        private double sourceClock;

        public void Validate()
        {
            if (clip == null || contractData == null || presentation == null)
                throw new InvalidOperationException("Stomp requires its exported source, contract and presentation.");
            Settings = JsonUtility.FromJson<Contract>(contractData.text);
            motion = ArenaMotionData.Read(motionData).Get("furnace_stomp");
            if (Settings == null || Settings.id != motion.id || Mathf.Abs(clip.length-motion.duration) > .002f
                || Mathf.Abs(Settings.completeSeconds-motion.duration) > .0001f
                || Settings.chargeSeconds != 2 || Settings.stampTime <= Settings.chargeSeconds
                || Settings.waveRadiusMetres != 9.75f || Settings.waveDurationSeconds <= 0
                || Settings.stampTime+Settings.waveDurationSeconds > Settings.completeSeconds)
                throw new InvalidOperationException("Stomp source and confirmed timing/range contract disagree.");
            foreach (var sample in motion.trajectory)
                if (sample.Position.sqrMagnitude > 1e-10f || Mathf.Abs(sample.yaw) > .0001f)
                    throw new InvalidOperationException("The grounded stomp requires its actual in-place source.");
        }
        public void Initialize(ArenaBossBrain brain) { owner=brain; Validate(); }
        public void ResetMotor()
        {
            Active=Cancelled=released=false; sourceClock=0; SourceTime=WaveRadius=ContactMismatch=0; ReleaseCount=0;
            presentation?.Clear();
        }
        public bool TryBegin(ArenaSession session)
        {
            if (Active || !session.Active || owner.fighter.Dead || !owner.FoundationMotor.IsGuard) return false;
            arena=session; sourceClock=0; SourceTime=WaveRadius=ContactMismatch=0; Cancelled=released=false; ReleaseCount=0;
            ExecutionId=arena.NewStrike(); StrikeId=arena.NewStrike(); Active=true;
            owner.FoundationMotor.SuspendForAttack();
            Centre=owner.transform.TransformPoint(localStampContact); Centre=new Vector3(Centre.x, owner.transform.position.y, Centre.z);
            owner.animationDriver.PoseContactClip(clip,0,0);
            presentation.Begin(owner.Socket("socket_foot.R"),Centre,Settings.waveRadiusMetres);
            Record("stomp_started"); return true;
        }
        public void Cancel(bool death)
        {
            if (!Active) return;
            if (!Cancelled) { Cancelled=true; presentation.Clear(); Record("stomp_cancelled"); }
            // Grounded source has no airborne root. Death owns a complete grounded
            // animation; ordinary cancellation finishes this exact source without damage.
            if (death) { Active=false; owner.OnGroundSkillFinished(true); }
        }
        public void Tick(float delta, ArenaSession session)
        {
            arena=session;
            if (!Active) return;
            if (owner.fighter.Dead) { Cancel(true); return; }
            if (!session.Active) Cancel(false);
            if (delta < 0 || delta > .25f || float.IsNaN(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            double remaining=delta;
            while (remaining > 1e-10 && Active)
            {
                double next=Math.Min(Settings.completeSeconds,sourceClock+Math.Min(remaining,1d/120));
                if (sourceClock < Settings.stampTime && next > Settings.stampTime) next=Settings.stampTime;
                float waveEnd=Settings.stampTime+Settings.waveDurationSeconds;
                if (sourceClock < waveEnd && next > waveEnd) next=waveEnd;
                double consumed=next-sourceClock;
                if (consumed <= 0) throw new InvalidOperationException("Stomp source clock could not advance.");
                float part=(float)consumed;
                owner.capsule.Move(Vector3.down*(2*part));
                owner.animationDriver.PoseContactClip(clip,next,part);
                sourceClock=next; SourceTime=(float)next; remaining-=consumed;
                if (!released && sourceClock >= Settings.stampTime)
                {
                    released=true;
                    Vector3 actual=owner.FootPosition; actual.y=Centre.y;
                    ContactMismatch=Vector3.Distance(Centre,actual);
                    if (ContactMismatch > .015f)
                    {
                        Cancel(false); Record("stomp_contact_failure");
                    }
                    if (!Cancelled)
                    {
                        ReleaseCount++; presentation.Release();
                        if (emitFootImpact && arena.defenseFeedback != null &&
                            arena.defenseFeedback.ShowFootImpact(StrikeId, actual, owner.transform.forward))
                            Record("stomp_foot_impact");
                        Record("stomp_released");
                    }
                }
                if (!Cancelled && released && sourceClock <= waveEnd)
                {
                    float oldRadius=WaveRadius;
                    WaveRadius=Settings.waveRadiusMetres*Mathf.Clamp01((SourceTime-Settings.stampTime)/Settings.waveDurationSeconds);
                    // Sweep the entire ring interval; a slow display frame cannot skip a fighter.
                    float inner=Mathf.Max(0,oldRadius-.75f);
                    arena.StrikeRing(owner.fighter,StrikeId,"furnace_stomp",Centre,WaveRadius,inner,owner.stompDamage,6);
                }
                if (!Cancelled) presentation.Tick(part,SourceTime,Settings.stampTime,Settings.waveDurationSeconds,WaveRadius);
                if (sourceClock >= Settings.completeSeconds-1e-10)
                { Active=false; presentation.Clear(); Record("stomp_recovery_complete"); owner.OnGroundSkillFinished(false); }
            }
        }
        private void Record(string kind) => arena?.Record(kind,"furnace_stomp",owner.fighter,StrikeId,owner.transform.position);
    }
}
