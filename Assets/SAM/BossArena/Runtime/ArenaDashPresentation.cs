using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Presentation only. The dash motor supplies every semantic marker and delta.
    // This class never searches targets, changes health, creates strikes or moves actors.
    [DisallowMultipleComponent]
    public sealed class ArenaDashPresentation : MonoBehaviour
    {
        public SamVfxPresenter sourceFire;
        public Shader releaseFlameShader;
        public AudioClip chargeLoop, slideLoop, lockPressure, preload, punchWhoosh, punchRelease, recovery, land, settle;
        [Range(0,1)] public float masterVolume=.72f;
        [Range(0,1)] public float spatialBlend=.9f;
        [Min(.05f)] public float flameRadius=.40f;
        [Min(.05f)] public float flameSegmentLength=.95f;
        [Min(.1f)] public float releaseLifetime=.48f;
        [Range(0,1)] public float releaseOpacity=.57f;
        [Range(0,2)] public float warmLightPower=.85f;
        public long ExecutionId { get; private set; }
        public long StrikeId { get; private set; }
        public int ReleaseCount { get; private set; }
        public int LoopStartCount { get; private set; }
        public int MissingAudioCount { get; private set; }
        public int StaleMarkerCount { get; private set; }
        public string PhaseName => phase.ToString();
        public bool ReleaseSuppressed => releaseSuppressed;
        public bool GeometryVisible => core!=null && core.Visible;
        public int CoreVertexCount => core!=null?core.VertexCount:0;
        public string CoreShaderName => coreMaterial!=null&&coreMaterial.shader!=null?coreMaterial.shader.name:"missing";
        public int ActiveLoopCount => (Playing(chargeVoice)?1:0)+(Playing(slideVoice)?1:0);
        public int OwnedLoopHandleCount => (chargeVoice!=null&&chargeVoice.active?1:0)+(slideVoice!=null&&slideVoice.active?1:0);
        public int ActiveParticleCount { get { int count=0; if(particles!=null)foreach(var p in particles)count+=p.particleCount; return count; } }
        public int ActiveVoiceCount { get { int count=ActiveLoopCount; if(voices!=null)foreach(var v in voices)if(Playing(v))++count; return count; } }
        public int OwnedVoiceHandleCount { get { int count=OwnedLoopHandleCount; if(voices!=null)foreach(var v in voices)if(v.active)++count; return count; } }
        public int ActiveEffectCount
        {
            get
            {
                int count=core!=null&&core.Visible?1:0;
                if(particles!=null)foreach(var p in particles)if(p!=null&&p.particleCount>0)++count;
                if(pressureRing!=null&&pressureRing.enabled)++count;
                if(warmLight!=null&&warmLight.enabled)++count;
                return count;
            }
        }
        public bool WarningVisible => pressureRing!=null && pressureRing.enabled;
        [Serializable] public struct PresentationEvent
        {
            public string kind;
            public long execution, strike;
            public double unityRealtime, dsp;
            public Vector3 position;
        }
        public IReadOnlyList<PresentationEvent> Events => events;
        private enum Phase { Inactive, Charge, Slide, Lock, Released, Suppressed, Recover, Fading }
        private sealed class Voice
        {
            internal AudioSource source;
            internal Transform follow;
            internal bool active, loop;
            internal float gain, target;
            internal double endDsp;
        }
        private readonly List<PresentationEvent> events=new List<PresentationEvent>(128);
        private Phase phase;
        private GameObject ownedRoot;
        private ParticleSystem fistFlame, fistEmbers, leftJet, rightJet, dust, releaseEmbers;
        private ParticleSystem[] particles;
        private Material flameMaterial, emberMaterial, dustMaterial, coreMaterial, ringMaterial;
        private ArenaDashFlameCore core;
        private LineRenderer pressureRing;
        private Light warmLight;
        private Voice chargeVoice, slideVoice;
        private Voice[] voices;
        private Transform fist, chest, leftFoot, rightFoot;
        private ArenaFighter fighter;
        private Vector3 lockedForward, releaseOrigin, releaseForward, actualVelocity;
        private float age, releaseAge, chargeProgress, lockProgress, fadeRemaining;
        private float fistEmission, emberEmission, leftEmission, rightEmission;
        private uint randomState;
        private bool prepared, ownsExecution, released, swung, tookOff, landed, settled, cancelled, releaseSuppressed;

        private void Awake()
        {
            Prepare();
            fighter=GetComponent<ArenaFighter>();
            if(fighter==null)fighter=GetComponentInParent<ArenaFighter>();
            if(fighter!=null)fighter.Died+=ResetAll;
        }
        public bool Prepare()
        {
            if(prepared)return true;
            Shader shader=sourceFire!=null?sourceFire.flameShader:null;
            if(shader==null)shader=Shader.Find("SamProduction/Flame");
            if(shader==null){Debug.LogError("Dash presentation requires the preserved SAM flame shader.",this);return false;}
            if(releaseFlameShader==null)releaseFlameShader=Resources.Load<Shader>("SAM18_DashFlame");
            if(releaseFlameShader==null){Debug.LogError("Dash release requires the dedicated SAM18_DashFlame resource shader.",this);return false;}
            ownedRoot=new GameObject("SAM18 owned dash presentation");
            // A world frame keeps detached release fire independent of recovery/root translation.
            ownedRoot.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            flameMaterial=Material(shader,"SAM18 dash flame",new Color(1,.08f,.006f),new Color(1,.43f,.075f),2.0f,0);
            emberMaterial=Material(shader,"SAM18 dash embers",new Color(1,.17f,.008f),new Color(1,.62f,.14f),2.4f,0);
            dustMaterial=Material(shader,"SAM18 dash ground vapor",new Color(.055f,.046f,.035f),new Color(.16f,.12f,.075f),1,0);
            if(sourceFire!=null&&sourceFire.dustTexture!=null)dustMaterial.SetTexture("_MainTex",sourceFire.dustTexture);
            coreMaterial=Material(releaseFlameShader,"SAM18 dash released volumetric fire D",new Color(1,.11f,.006f),new Color(1,.74f,.22f),4.0f,0);
            coreMaterial.SetFloat("_Density",11f);
            ringMaterial=Material(shader,"SAM18 lock pressure cue",new Color(1,.14f,.005f),new Color(1,.51f,.10f),1.8f,1);
            fistFlame=MakeParticles("Right fist accumulating flame",flameMaterial,96);
            fistEmbers=MakeParticles("Right fist inward energy",emberMaterial,64);
            leftJet=MakeParticles("Left boot exhaust",flameMaterial,64);
            rightJet=MakeParticles("Right boot exhaust",flameMaterial,64);
            dust=MakeParticles("Contact ground vapor",dustMaterial,72);
            releaseEmbers=MakeParticles("Released decorative embers",emberMaterial,96);
            particles=new[]{fistFlame,fistEmbers,leftJet,rightJet,dust,releaseEmbers};
            core=new ArenaDashFlameCore(ownedRoot.transform,coreMaterial);
            var ringObject=new GameObject("Locked punch pressure ring");ringObject.transform.SetParent(ownedRoot.transform,false);
            pressureRing=ringObject.AddComponent<LineRenderer>();pressureRing.sharedMaterial=ringMaterial;
            pressureRing.positionCount=33;pressureRing.useWorldSpace=true;pressureRing.loop=false;
            pressureRing.widthMultiplier=.025f;pressureRing.shadowCastingMode=ShadowCastingMode.Off;pressureRing.receiveShadows=false;
            pressureRing.startColor=pressureRing.endColor=new Color(1,.52f,.13f,.75f);pressureRing.enabled=false;
            warmLight=new GameObject("Dash warm fist bounce").AddComponent<Light>();warmLight.transform.SetParent(ownedRoot.transform,false);
            warmLight.type=LightType.Point;warmLight.shadows=LightShadows.None;warmLight.color=new Color(1,.29f,.045f);
            warmLight.range=2.0f;warmLight.enabled=false;
            chargeVoice=MakeVoice("Owned dash charge loop",true);slideVoice=MakeVoice("Owned dash exhaust loop",true);
            voices=new Voice[6];for(int i=0;i<voices.Length;++i)voices[i]=MakeVoice("Owned dash cue "+i,false);
            prepared=true;return true;
        }
        private Material Material(Shader shader,string label,Color outer,Color hot,float emission,float mode)
        {
            var m=new Material(shader){name=label};
            // Explicit shader vectors avoid hidden SetColor gamma/linear conversion.
            Color a=QualitySettings.activeColorSpace==ColorSpace.Linear?outer.linear:outer;
            Color b=QualitySettings.activeColorSpace==ColorSpace.Linear?hot.linear:hot;
            m.SetVector("_BaseColor",a);m.SetVector("_HotColor",b);m.SetFloat("_EmissionPower",emission);
            if(m.HasProperty("_ShapeMode"))m.SetFloat("_ShapeMode",mode);
            m.SetFloat("_UseDepthFade",1);m.SetFloat("_Opacity",.76f);
            if(m.HasProperty("_SoftDistance"))m.SetFloat("_SoftDistance",.08f);
            if(m.HasProperty("_MainTex")&&sourceFire!=null&&sourceFire.erosionTexture!=null)m.SetTexture("_MainTex",sourceFire.erosionTexture);
            return m;
        }
        private ParticleSystem MakeParticles(string label,Material material,int capacity)
        {
            var obj=new GameObject(label);obj.transform.SetParent(ownedRoot.transform,false);
            var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.maxParticles=capacity;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.startSpeed=0;main.startLifetime=.25f;
            main.startSize3D=true;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.46f,.12f),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.9f,.12f),new GradientAlphaKey(.6f,.48f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.45f),new Keyframe(.23f,1),new Keyframe(1,.08f)));
            var renderer=obj.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.sortMode=ParticleSystemSortMode.Distance;ps.useAutoRandomSeed=false;ps.randomSeed=1802;ps.Pause();return ps;
        }
        private Voice MakeVoice(string label,bool loop)
        {
            var obj=new GameObject(label);obj.transform.SetParent(ownedRoot.transform,false);
            var source=obj.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=loop;
            source.dopplerLevel=0;source.spatialBlend=spatialBlend;source.minDistance=2.5f;source.maxDistance=30;
            source.rolloffMode=AudioRolloffMode.Logarithmic;source.reverbZoneMix=.18f;
            return new Voice{source=source,loop=loop};
        }

        public void BeginCharge(long executionId,Transform rightFist,Transform chestSocket)
        {
            if(ownsExecution&&ExecutionId==executionId)return;
            ResetAll();if(!Prepare()||rightFist==null)return;
            ownsExecution=true;ExecutionId=executionId;fist=rightFist;chest=chestSocket;phase=Phase.Charge;
            // A charge can enter a harmless recovery before BeginSlide. Its actual
            // takeoff/landing still needs the preserved rig's foot sockets.
            var boss=GetComponent<ArenaBossBrain>();
            if(boss!=null){leftFoot=boss.Socket("socket_foot.L");rightFoot=boss.Socket("socket_foot.R");}
            randomState=unchecked((uint)executionId*747796405u+2891336453u);
            StartLoop(chargeVoice,chargeLoop,fist,.12f,.83f);
            OneShot(preload,chest!=null?chest:fist,Position(),.9f);Record("charge_started",Position());
        }
        public void UpdateCharge(long executionId,float progress)
        {
            if(!Match(executionId)||phase!=Phase.Charge)return;
            chargeProgress=Mathf.Clamp01(progress);chargeVoice.target=.12f+.42f*chargeProgress;
            chargeVoice.source.pitch=.83f+.27f*chargeProgress;
        }
        public void BeginSlide(long executionId,Transform leftSocket,Transform rightSocket)
        {
            if(!Match(executionId)||releaseSuppressed||phase==Phase.Slide)return;
            leftFoot=leftSocket;rightFoot=rightSocket;phase=Phase.Slide;age=0;chargeProgress=1;
            chargeVoice.target=.22f;
            StartLoop(slideVoice,slideLoop,chest!=null?chest:fist,.78f,1);
            Record("slide_started",Position());
        }
        public void UpdateSlide(long executionId,Vector3 velocity,float phaseProgress)
        {
            if(!Match(executionId)||phase!=Phase.Slide)return;
            actualVelocity=velocity;
            slideVoice.target=Mathf.Lerp(.26f,.78f,Mathf.Clamp01(velocity.magnitude/8));
        }
        public void Lock(long executionId,Vector3 origin,Vector3 forward)
        {
            if(!Match(executionId)||releaseSuppressed||phase==Phase.Lock)return;
            lockedForward=SafeForward(forward);phase=Phase.Lock;age=0;lockProgress=0;
            slideVoice.target=0;chargeVoice.target=.40f;
            OneShot(lockPressure,fist,Position(),.85f);Record("heading_locked",origin);
        }
        public void UpdateLock(long executionId,float progress)
        { if(Match(executionId)&&phase==Phase.Lock)lockProgress=Mathf.Clamp01(progress); }
        public void BeginSwing(long executionId)
        {
            if(!Match(executionId)||releaseSuppressed||swung)return;swung=true;
            OneShot(punchWhoosh,fist,Position(),.93f);Record("swing",Position());
        }
        public void ReleasePunch(long executionId,long strikeId,Transform actualFist,Vector3 forward)
        {
            if(!Match(executionId)||releaseSuppressed||released||actualFist==null)return;
            released=true;StrikeId=strikeId;++ReleaseCount;phase=Phase.Released;releaseAge=0;
            fist=actualFist;releaseOrigin=actualFist.position;releaseForward=SafeForward(forward);
            chargeVoice.target=slideVoice.target=0;pressureRing.enabled=false;
            UpdateCore();EmitRelease();
            OneShot(punchRelease,null,releaseOrigin,1);Record("punch_released",releaseOrigin);
        }
        // A harmless lock/release abort must finish its authored leg segment before
        // recovery. Remove the current attack presentation without cancelling the
        // execution: later actual takeoff, landing and settlement remain valid.
        public void SuppressRelease(long executionId)
        {
            if(!Match(executionId)||releaseSuppressed)return;
            if(phase!=Phase.Lock&&phase!=Phase.Released)return;
            releaseSuppressed=true;phase=Phase.Suppressed;
            ClearOwned();Record("release_suppressed",Position());
        }
        public void BeginRecover(long executionId)
        {
            if(!Match(executionId)||phase==Phase.Recover)return;
            phase=Phase.Recover;age=0;chargeVoice.target=slideVoice.target=0;pressureRing.enabled=false;
            OneShot(recovery,chest!=null?chest:fist,Position(),.85f);Record("recovery_started",Position());
        }
        public void RecoverTakeoff(long executionId)
        {
            if(!Match(executionId)||tookOff)return;tookOff=true;
            ContactDust(.45f);Record("takeoff",FootCentre());
        }
        public void RecoverLand(long executionId)
        {
            if(!Match(executionId)||landed)return;landed=true;
            Vector3 point=FootCentre();ContactDust(1);
            OneShot(land,null,point,.95f);Record("actual_landing",point);
        }
        public void Complete(long executionId)
        {
            if(!Match(executionId)||settled)return;settled=true;
            OneShot(settle,chest!=null?chest:fist,Position(),.85f);Record("settled",Position());
            phase=Phase.Fading;fadeRemaining=.30f;chargeVoice.target=slideVoice.target=0;
        }
        public bool TryGetDamageGeometry(long executionId,out Vector3 a,out Vector3 b,out float radius)
        {
            a=b=Vector3.zero;radius=0;
            if(!ownsExecution||ExecutionId!=executionId||!released||cancelled||releaseSuppressed||core==null||!core.Visible)return false;
            a=core.A;b=core.B;radius=core.Radius;return radius>0;
        }
        public void Cancel(long executionId,bool immediate)
        {
            if(!ownsExecution||ExecutionId!=executionId)return;
            Record(immediate?"cancel_clear":"cancel_fade",Position());cancelled=true;
            if(immediate){ClearOwned();ownsExecution=false;phase=Phase.Inactive;return;}
            phase=Phase.Fading;fadeRemaining=.12f;chargeVoice.target=slideVoice.target=0;core.Hide();pressureRing.enabled=false;
            foreach(var voice in voices)voice.target=0;
        }
        public void ResetAll()
        {
            if(prepared)ClearOwned();phase=Phase.Inactive;ownsExecution=false;cancelled=releaseSuppressed=false;
            released=swung=tookOff=landed=settled=false;ExecutionId=StrikeId=0;
            age=releaseAge=chargeProgress=lockProgress=fadeRemaining=0;
            fistEmission=emberEmission=leftEmission=rightEmission=0;actualVelocity=Vector3.zero;
            fist=chest=leftFoot=rightFoot=null;
        }
        public void Tick(float delta)
        {
            if(!prepared||!ownsExecution||!Finite(delta)||delta<0)return;
            if(fighter!=null&&fighter.Dead){Cancel(ExecutionId,true);return;}
            age+=delta;
            if(released) { releaseAge+=delta;UpdateCore(); }
            if(phase==Phase.Charge||phase==Phase.Slide||phase==Phase.Lock)EmitAttached(delta);
            if(phase==Phase.Lock)UpdatePressureRing();
            else if(pressureRing!=null)pressureRing.enabled=false;
            foreach(var system in particles)if(delta>0)system.Simulate(delta,false,false,false);
            UpdateVoice(chargeVoice,delta);UpdateVoice(slideVoice,delta);foreach(var voice in voices)UpdateVoice(voice,delta);
            float glow=phase==Phase.Charge?.10f+.42f*chargeProgress:phase==Phase.Slide?.45f:phase==Phase.Lock?.65f:released&&!releaseSuppressed?Mathf.Exp(-releaseAge*11)*.9f:0;
            warmLight.enabled=glow>.005f;warmLight.intensity=warmLightPower*glow;
            warmLight.transform.position=released?releaseOrigin:Position();
            if(phase==Phase.Fading)
            {
                fadeRemaining-=delta;
                if(fadeRemaining<=0){ClearOwned();ownsExecution=false;phase=Phase.Inactive;}
            }
        }
        private void EmitAttached(float delta)
        {
            if(fist==null)return;
            float pressure=phase==Phase.Charge?chargeProgress:1;
            fistEmission+=(18+34*pressure)*delta;emberEmission+=(8+14*pressure)*delta;
            while(fistEmission>=1){--fistEmission;Vector3 jitter=RandomBall()*.115f;Spawn(fistFlame,fist.position+jitter,Vector3.up*(.30f+.22f*pressure)+jitter*.5f,.16f+.09f*Random01(),.11f+.08f*pressure,.22f+.15f*pressure,.42f);}
            while(emberEmission>=1){--emberEmission;Vector3 offset=RandomBall()*(.21f+.15f*pressure);Spawn(fistEmbers,fist.position+offset,-offset*3+Vector3.up*.12f,.13f,.025f,.09f,.65f);}
            if(phase!=Phase.Slide)return;
            Vector3 backwards=actualVelocity.sqrMagnitude>.01f?-actualVelocity.normalized:-SafeForward(transform.forward);
            leftEmission+=36*delta;rightEmission+=36*delta;
            EmitJet(leftJet,leftFoot,backwards,ref leftEmission);EmitJet(rightJet,rightFoot,backwards,ref rightEmission);
        }
        private void EmitJet(ParticleSystem system,Transform foot,Vector3 backwards,ref float remainder)
        {
            if(foot==null){remainder=0;return;}
            while(remainder>=1)
            {
                --remainder;Vector3 position=foot.position+Vector3.up*.11f+backwards*.07f+RandomBall()*.035f;
                Spawn(system,position,backwards*(1.7f+Random01()*.8f)+Vector3.up*.18f,.18f+Random01()*.08f,.11f,.30f,.55f);
            }
        }
        private void UpdatePressureRing()
        {
            if(fist==null)return;
            Vector3 right=Vector3.Cross(Vector3.up,lockedForward).normalized;
            Vector3 up=Vector3.Cross(lockedForward,right);float radius=Mathf.Lerp(.33f,.19f,lockProgress);
            Vector3 centre=fist.position+lockedForward*.04f;
            pressureRing.enabled=true;pressureRing.widthMultiplier=Mathf.Lerp(.025f,.046f,lockProgress);
            for(int i=0;i<=32;i++){float a=i*Mathf.PI*2/32;pressureRing.SetPosition(i,centre+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius);}
        }
        private void UpdateCore()
        {
            if(core==null)return;
            if(cancelled||releaseSuppressed||releaseAge>=releaseLifetime){core.Hide();return;}
            float expansion=Mathf.SmoothStep(0,1,Mathf.Clamp01(releaseAge/.075f));
            float radius=flameRadius*Mathf.Lerp(.65f,1,expansion);
            float length=flameSegmentLength*Mathf.Lerp(.60f,1,expansion);
            float opacity=releaseOpacity*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,releaseLifetime,releaseAge)));
            core.Show(releaseOrigin+releaseForward*.07f,releaseOrigin+releaseForward*(.07f+length),radius,releaseAge,opacity,ExecutionId);
        }
        private void EmitRelease()
        {
            for(int i=0;i<30;++i)
            {
                Vector3 spread=RandomBall();
                Spawn(releaseEmbers,releaseOrigin+spread*.12f,releaseForward*(1.6f+Random01()*1.5f)+spread*.8f,.13f+Random01()*.22f,.03f+Random01()*.035f,.10f,.72f);
            }
        }
        private void ContactDust(float strength)
        {
            foreach(Transform foot in new[]{leftFoot,rightFoot})
                if(foot!=null)for(int i=0;i<Mathf.RoundToInt(8*strength);++i)
                {
                    Vector3 velocity=RandomBall();velocity.y=Mathf.Abs(velocity.y)*.22f;
                    Spawn(dust,foot.position+Vector3.up*.025f,velocity*.65f,.20f+Random01()*.18f,.17f,.14f,.35f*strength);
                }
        }
        private static void Spawn(ParticleSystem ps,Vector3 position,Vector3 velocity,float life,float width,float height,float alpha)
        {
            var particle=new ParticleSystem.EmitParams{position=position,velocity=velocity,startLifetime=life,startSize3D=new Vector3(width,height,width),startColor=new Color(1,1,1,alpha)};
            ps.Emit(particle,1);
        }
        private void StartLoop(Voice voice,AudioClip clip,Transform socket,float gain,float pitch)
        {
            if(voice.active)return;if(!ReadyClip(clip))return;
            voice.source.clip=clip;voice.source.pitch=pitch;voice.follow=socket;voice.gain=0;voice.target=gain;voice.active=true;
            voice.source.volume=0;voice.source.transform.position=socket!=null?socket.position:Position();voice.source.Play();++LoopStartCount;
        }
        private void OneShot(AudioClip clip,Transform follow,Vector3 position,float gain)
        {
            if(!ReadyClip(clip))return;
            Voice voice=null;foreach(var item in voices)if(!item.active){voice=item;break;}
            if(voice==null){voice=voices[0];foreach(var item in voices)if(item.endDsp<voice.endDsp)voice=item;StopVoice(voice);}
            voice.source.clip=clip;voice.source.pitch=1;voice.follow=follow;voice.active=true;voice.gain=voice.target=gain;
            voice.source.transform.position=follow!=null?follow.position:position;voice.source.volume=masterVolume*gain;
            voice.endDsp=AudioSettings.dspTime+clip.length;voice.source.Play();
        }
        private bool ReadyClip(AudioClip clip)
        {
            if(clip!=null&&clip.loadState==AudioDataLoadState.Loaded)return true;
            ++MissingAudioCount;return false;
        }
        private void UpdateVoice(Voice voice,float delta)
        {
            if(!voice.active)return;
            if(voice.follow!=null)voice.source.transform.position=voice.follow.position;
            voice.gain=Mathf.MoveTowards(voice.gain,voice.target,delta/Mathf.Max(.02f,voice.loop?.07f:.04f));
            voice.source.volume=masterVolume*voice.gain;
            if((voice.target<=0&&voice.gain<=.0001f)||(!voice.loop&&AudioSettings.dspTime>=voice.endDsp))StopVoice(voice);
        }
        private static void StopVoice(Voice voice)
        {
            if(voice==null)return;
            if(voice.source!=null){voice.source.Stop();voice.source.clip=null;voice.source.volume=0;}
            voice.active=false;voice.follow=null;voice.gain=voice.target=0;
        }
        private static bool Playing(Voice voice)=>voice!=null&&voice.source!=null&&voice.source.isPlaying;
        private void ClearOwned()
        {
            if(particles!=null)foreach(var p in particles)if(p!=null)p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(core!=null)core.Hide();if(pressureRing!=null)pressureRing.enabled=false;if(warmLight!=null)warmLight.enabled=false;
            StopVoice(chargeVoice);StopVoice(slideVoice);if(voices!=null)foreach(var voice in voices)StopVoice(voice);
        }
        private bool Match(long id)
        { if(ownsExecution&&id==ExecutionId&&!cancelled)return true;++StaleMarkerCount;return false; }
        private Vector3 Position()=>fist!=null?fist.position:transform.position;
        private Vector3 FootCentre()=>leftFoot!=null&&rightFoot!=null?(leftFoot.position+rightFoot.position)*.5f:leftFoot!=null?leftFoot.position:rightFoot!=null?rightFoot.position:transform.position;
        private static Vector3 SafeForward(Vector3 value)
        { Vector3 flat=Vector3.ProjectOnPlane(value,Vector3.up);return flat.sqrMagnitude>.00001f?flat.normalized:Vector3.forward; }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private float Random01(){randomState^=randomState<<13;randomState^=randomState>>17;randomState^=randomState<<5;return (randomState&0xFFFFFF)/16777216f;}
        private Vector3 RandomBall(){Vector3 p=new Vector3(Random01()*2-1,Random01()*2-1,Random01()*2-1);return p.sqrMagnitude>1?p.normalized:p;}
        private void Record(string kind,Vector3 point)
        {
            if(events.Count==512)events.RemoveAt(0);
            events.Add(new PresentationEvent{kind=kind,execution=ExecutionId,strike=StrikeId,unityRealtime=Time.realtimeSinceStartupAsDouble,dsp=AudioSettings.dspTime,position=point});
        }
        private void OnDisable()=>ResetAll();
        private void OnDestroy()
        {
            if(fighter!=null)fighter.Died-=ResetAll;ResetAll();core?.Dispose();
            if(ownedRoot!=null)Destroy(ownedRoot);
            foreach(var m in new[]{flameMaterial,emberMaterial,dustMaterial,coreMaterial,ringMaterial})if(m!=null)Destroy(m);
        }
    }
}
