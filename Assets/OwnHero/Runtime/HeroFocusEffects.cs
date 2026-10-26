using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingOfGraphic.Hero
{
    // Shared presentation only: the combat hosts retain all movement, clocks and damage.
    [DefaultExecutionOrder(2200)]
    public sealed class HeroFocusEffects:MonoBehaviour
    {
        Func<HeroFocusPhase> phase;Func<int> strikes;Func<Transform> target;Func<Vector3> origin;
        HeroPoseDriver hero;SkinnedMeshRenderer[] skins;
        Material energy,spectral,screen;Volume volume;VolumeProfile profile;
        ParticleSystem motes,sparks;TrailRenderer blade;
        LineRenderer[] rings;readonly List<Ghost> ghosts=new List<Ghost>(48);
        sealed class Ghost{public Mesh mesh;public Matrix4x4 matrix;public float end;}
        MaterialPropertyBlock block;HeroFocusPhase previous;int lastStrike;float clock,pulse,weight,nextGhost,nextMote;
        bool initialized;UniversalAdditionalCameraData postCamera;bool oldPost,postOwned;readonly System.Random random=new System.Random(61026);AudioSource sound;AudioClip success,rush,release;
        public int BurstCount{get;private set;}
        public int GhostCount=>ghosts.Count;
        public float FilterWeight=>volume?volume.weight:0;
        public bool Ready=>initialized&&energy&&spectral&&screen&&motes&&sparks&&volume;
        public void Bind(HeroPoseDriver pose,Func<HeroFocusPhase> state,Func<int> count,Func<Transform> enemy,Func<Vector3> start){
            hero=pose;phase=state;strikes=count;target=enemy;origin=start;Initialize();previous=HeroFocusPhase.None;lastStrike=0;nextGhost=nextMote=0;
        }
        void Initialize(){
            if(initialized||!hero)return;initialized=true;block=new MaterialPropertyBlock();skins=hero.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.spatialBlend=0;sound.volume=.5f;success=Resources.Load<AudioClip>("PerfectDodge/PerfectSuccess");rush=Resources.Load<AudioClip>("PerfectDodge/RushStep");release=Resources.Load<AudioClip>("PerfectDodge/FocusRelease");
            energy=new Material(Resources.Load<Shader>("FocusFX/Energy"));screen=new Material(Resources.Load<Shader>("FocusFX/Screen"));
            spectral=new Material(Resources.Load<Shader>("PerfectDodge/Spectral"));
            motes=Particles("Link time drifting crystals",180);sparks=Particles("Link time strike sparks",256);
            rings=new LineRenderer[3];for(int i=0;i<rings.Length;i++){
                var go=new GameObject("Link time energy ring "+i);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();rings[i]=line;
                line.loop=true;line.useWorldSpace=true;line.positionCount=96;line.sharedMaterial=energy;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
            }
            var trail=new GameObject("Link time blade wake");trail.transform.SetParent(hero.bladeTip,false);blade=trail.AddComponent<TrailRenderer>();blade.sharedMaterial=energy;blade.time=.18f;blade.minVertexDistance=.035f;blade.widthMultiplier=.20f;blade.emitting=false;blade.shadowCastingMode=ShadowCastingMode.Off;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.15f,.65f,1),.5f),new GradientColorKey(new Color(.55f,.25f,1),1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(0,1)});blade.colorGradient=gradient;
            var post=new GameObject("Scoped Link time grading");post.transform.SetParent(transform,false);volume=post.AddComponent<Volume>();volume.isGlobal=true;volume.priority=200;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;volume.weight=0;
            var color=profile.Add<ColorAdjustments>(true);color.saturation.Override(-45);color.contrast.Override(12);color.postExposure.Override(.18f);color.colorFilter.Override(new Color(.80f,.92f,1));
            var vignette=profile.Add<Vignette>(true);vignette.color.Override(new Color(.015f,.06f,.13f));vignette.intensity.Override(.23f);vignette.smoothness.Override(.65f);
            profile.Add<ChromaticAberration>(true).intensity.Override(.20f);profile.Add<LensDistortion>(true).intensity.Override(-.09f);
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(1.1f);bloom.threshold.Override(.9f);
        }
        ParticleSystem Particles(string name,int maximum){
            var go=new GameObject(name);go.transform.SetParent(transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.playOnAwake=false;main.useUnscaledTime=true;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=maximum;main.startLifetime=.5f;main.startSpeed=0;main.startSize=.035f;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=energy;renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=2;renderer.velocityScale=.035f;renderer.shadowCastingMode=ShadowCastingMode.Off;
            var fade=ps.colorOverLifetime;fade.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.1f,.65f,1),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=g;
            ps.Play();return ps;
        }
        void Burst(Vector3 position,int count,float speed){
            BurstCount++;var random=new System.Random(BurstCount*7919);
            for(int i=0;i<count;i++){
                Vector3 direction=new Vector3((float)random.NextDouble()*2-1,(float)random.NextDouble()*1.5f-.2f,(float)random.NextDouble()*2-1).normalized;
                var emit=new ParticleSystem.EmitParams{position=position,velocity=direction*speed*(.35f+(float)random.NextDouble()*.65f),startLifetime=.25f+(float)random.NextDouble()*.35f,startSize=.025f+(float)random.NextDouble()*.045f,startColor=i%3==0?new Color(.7f,.35f,1):new Color(.4f,.9f,1)};
                sparks.Emit(emit,1);
            }
        }
        void LateUpdate(){
            if(!initialized)return;if(Time.timeScale<=0)return;float dt=Mathf.Min(.1f,Time.unscaledDeltaTime);clock+=dt;
            var state=phase!=null?phase():HeroFocusPhase.None;bool active=state!=HeroFocusPhase.None;
            if(state!=previous){
                if(previous==HeroFocusPhase.None&&active){pulse=1;if(success)sound.PlayOneShot(success,.7f);Burst(transform.position+Vector3.up,64,6);}
                if(state==HeroFocusPhase.Return){pulse=.65f;if(rush)sound.PlayOneShot(rush,.55f);Burst(transform.position+Vector3.up*.8f,40,7);}
                if(state==HeroFocusPhase.None&&previous!=HeroFocusPhase.None){if(release)sound.PlayOneShot(release,.45f);Burst(transform.position+Vector3.up*.6f,24,3);}
                previous=state;
            }
            int count=strikes!=null?strikes():0;
            if(active&&count>lastStrike&&hero.AuthoredSwordWindow){pulse=.48f;var enemy=target();Burst(enemy?enemy.position+Vector3.up:hero.WeaponTip,36,5);lastStrike=count;}
            pulse=Mathf.MoveTowards(pulse,0,dt*4);weight=Mathf.MoveTowards(weight,active?1:0,dt*(active?9:5));volume.weight=weight;
            if(weight>0&&!postOwned&&Camera.main){postCamera=Camera.main.GetUniversalAdditionalCameraData();oldPost=postCamera.renderPostProcessing;postCamera.renderPostProcessing=true;postOwned=true;}
            if(weight<=0&&postOwned){if(postCamera)postCamera.renderPostProcessing=oldPost;postOwned=false;}
            bool travel=state==HeroFocusPhase.Approach||state==HeroFocusPhase.Return;
            blade.emitting=active&&state==HeroFocusPhase.Striking&&hero.AuthoredSwordWindow;
            if(active&&clock>=nextGhost&&(travel||state==HeroFocusPhase.Striking)){Bake();nextGhost=clock+(travel?.045f:.09f);}
            if(active&&clock>=nextMote){nextMote=clock+.04f;var emit=new ParticleSystem.EmitParams{position=transform.position+new Vector3((float)random.NextDouble()*2-1,(float)random.NextDouble()*2-1,(float)random.NextDouble()*2-1)*1.6f+Vector3.up,velocity=Vector3.up*.15f,startLifetime=.7f,startSize=.024f,startColor=new Color(.2f,.8f,1,.7f)};motes.Emit(emit,3);}
            for(int i=ghosts.Count-1;i>=0;i--){var ghost=ghosts[i];if(clock>ghost.end){Destroy(ghost.mesh);ghosts.RemoveAt(i);continue;}block.SetColor("_Tint",new Color(.12f,.7f,1,(ghost.end-clock)/.28f*.40f));for(int sub=0;sub<ghost.mesh.subMeshCount;sub++)Graphics.DrawMesh(ghost.mesh,ghost.matrix,spectral,gameObject.layer,null,sub,block,ShadowCastingMode.Off,false);}
            for(int r=0;r<3;r++){
                var line=rings[r];line.enabled=weight>.01f;if(!line.enabled)continue;
                Transform enemy=target!=null?target():null;Vector3 centre=r==0?origin():r==1&&enemy?enemy.position:transform.position;centre+=Vector3.up*.055f;
                float radius=r==0?.48f:r==1?1.1f:Mathf.Lerp(.8f,2.6f,pulse);line.widthMultiplier=r==2?.045f:.022f;
                line.startColor=line.endColor=new Color(.2f,.8f,1,weight*(r==2?pulse:.45f));
                for(int n=0;n<96;n++){float angle=n*Mathf.PI*2/96+clock*(r==1?-.2f:.4f);float shape=1+Mathf.Sin(angle*12)*.035f;line.SetPosition(n,centre+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius*shape);}
            }
            
        }
        void Bake(){foreach(var skin in skins){if(ghosts.Count>=48)break;if(!skin||!skin.enabled||!skin.gameObject.activeInHierarchy||!skin.sharedMesh)continue;var mesh=new Mesh();skin.BakeMesh(mesh,false);ghosts.Add(new Ghost{mesh=mesh,matrix=skin.transform.localToWorldMatrix,end=clock+.28f});}}
        void OnGUI(){if(Event.current.type!=EventType.Repaint||!screen||weight<=.001f||Time.timeScale<=0)return;screen.SetFloat("_Strength",weight);screen.SetFloat("_Pulse",pulse);screen.SetFloat("_Travel",previous==HeroFocusPhase.Approach||previous==HeroFocusPhase.Return?1:0);screen.SetFloat("_Clock",clock);Graphics.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture,screen);}
        void OnDisable(){if(postOwned&&postCamera)postCamera.renderPostProcessing=oldPost;postOwned=false;if(sound)sound.Stop();if(volume)volume.weight=0;if(blade){blade.emitting=false;blade.Clear();}if(rings!=null)foreach(var ring in rings)if(ring)ring.enabled=false;if(motes)motes.Clear();if(sparks)sparks.Clear();foreach(var ghost in ghosts)if(ghost.mesh)Destroy(ghost.mesh);ghosts.Clear();weight=0;previous=HeroFocusPhase.None;}
        void OnDestroy(){if(energy)Destroy(energy);if(spectral)Destroy(spectral);if(screen)Destroy(screen);if(profile)Destroy(profile);}
    }
}
