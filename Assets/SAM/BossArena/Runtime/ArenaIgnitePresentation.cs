using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace SamProduction.BossArena
{
    public sealed class ArenaIgnitePresentation : MonoBehaviour
    {
        public AudioClip corePressure,armorVent,release,furnaceLoop;
        private GameObject root;
        private ParticleSystem fire;
        private Material material;
        private AudioSource pressureVoice,ventVoice,releaseVoice,loopVoice;
        private ArenaBossBrain owner;
        private float emission;
        private bool opened,burst,overheat;
        private uint seed;
        private Transform[] anchors;
        private Transform[] shoulders;
        private Light warmth;
        private float localTime;
        public int ReleaseCount {get;private set;}
        public bool Overheat => overheat;
        private AudioSource Voice(string name)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root.transform,false);var a=obj.AddComponent<AudioSource>();
            a.playOnAwake=false;a.spatialBlend=.8f;a.minDistance=4;a.maxDistance=40;a.rolloffMode=AudioRolloffMode.Linear;return a;
        }
        private void Prepare()
        {
            if(root!=null)return;
            var source=GetComponent<ArenaDashPresentation>()?.sourceFire;
            if(source==null || source.flameShader==null || corePressure==null || armorVent==null || release==null || furnaceLoop==null)
                throw new InvalidOperationException("Actual ignition shader/audio assets missing.");
            root=new GameObject("Ignite18 owned fire and audio");
            material=new Material(source.flameShader);
            material.SetVector("_BaseColor",new Color(1,.025f,.001f));material.SetVector("_HotColor",new Color(1,.48f,.04f));
            material.SetFloat("_EmissionPower",3);material.SetFloat("_Opacity",.65f);material.SetFloat("_ShapeMode",0);
            material.SetFloat("_Dissolve",.16f);material.SetFloat("_Seed",18.373f);
            material.SetFloat("_UseDepthFade",1);material.SetFloat("_SoftDistance",.08f);
            if(source.erosionTexture!=null)material.SetTexture("_MainTex",source.erosionTexture);
            var obj=new GameObject("Vent flames and overheat");obj.transform.SetParent(root.transform,false);fire=obj.AddComponent<ParticleSystem>();
            fire.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=fire.main;
            main.playOnAwake=false;main.loop=true;main.maxParticles=700;main.startSpeed=0;main.startSize3D=true;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var e=fire.emission;e.enabled=false;var shape=fire.shape;shape.enabled=false;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.2f,.01f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.85f,.12f),new GradientAlphaKey(.3f,.6f),new GradientAlphaKey(0,1)});
            var color=fire.colorOverLifetime;color.enabled=true;color.color=gradient;
            var size=fire.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.18f,1),new Keyframe(1,.05f)));
            var r=obj.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=material;r.renderMode=ParticleSystemRenderMode.Billboard;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            fire.useAutoRandomSeed=false;fire.randomSeed=18373;fire.Pause();
            pressureVoice=Voice("Core pressure");ventVoice=Voice("Armour opening");releaseVoice=Voice("Ignition release");loopVoice=Voice("Sustained furnace");loopVoice.loop=true;
            var lightObject=new GameObject("Reflected furnace light");lightObject.transform.SetParent(root.transform,false);warmth=lightObject.AddComponent<Light>();
            warmth.type=LightType.Point;warmth.color=new Color(1,.2f,.025f);warmth.range=5;warmth.shadows=LightShadows.None;warmth.enabled=false;
        }
        public void Begin(ArenaBossBrain brain)
        {
            Prepare();Clear();owner=brain;seed=18373;ReleaseCount=0;
            anchors=new[]{owner.Socket("socket_hand.R"),owner.Socket("socket_hand.L"),owner.Socket("socket_foot.R"),owner.Socket("socket_foot.L")};
            shoulders=new[]{owner.Socket("upper_arm.R"),owner.Socket("upper_arm.L")};localTime=0;
            FollowAudio();pressureVoice.clip=corePressure;pressureVoice.volume=.55f;pressureVoice.Play();
        }
        public void Ignite(){opened=true;ventVoice.PlayOneShot(armorVent,.6f);}
        public void Release(){burst=true;ReleaseCount++;pressureVoice.Stop();releaseVoice.PlayOneShot(release,.8f);}
        public void EnableOverheat(){overheat=true;loopVoice.clip=furnaceLoop;loopVoice.volume=.22f;loopVoice.Play();}
        private float R(){seed=1664525*seed+1013904223;return (seed&0xffffff)/16777216f;}
        private void FollowAudio()
        {
            if(owner==null)return;var p=owner.transform.position+Vector3.up*1.8f;
            pressureVoice.transform.position=ventVoice.transform.position=releaseVoice.transform.position=loopVoice.transform.position=p;
        }
        public void Tick(float delta,float time)
        {
            float strength=burst?Mathf.Lerp(620,145,Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.4f,4.8f,time))):opened?175:8;
            Advance(delta,strength,burst?.72f:.3f);
        }
        public void TickOverheat(float delta)
        {if(overheat){if(owner.fighter.Dead){Clear();return;}Advance(delta,145,.32f);}}
        private void Advance(float delta,float rate,float height)
        {
            if(fire==null)return;FollowAudio();localTime+=delta;material.SetFloat("_LocalTime",localTime);
            warmth.enabled=opened;warmth.transform.position=owner.transform.position+Vector3.up*1.8f;warmth.intensity=burst?.8f:.3f;
            fire.Simulate(delta,false,false,true);fire.Pause();emission+=delta*rate;
            while(emission>=1)
            {
                emission--;int index=opened?(int)(R()*6):4+(int)(R()*2);Vector3 p,flow;
                float side=index%2==0?1:-1;
                if(index<4){p=anchors[index].position;flow=Vector3.up*.45f+(index<2?owner.transform.right*side*.65f:-owner.transform.forward*.3f);}
                else {p=shoulders[index-4].position-owner.transform.forward*.18f;flow=owner.transform.right*side*.9f-owner.transform.forward*.7f+Vector3.up*.55f;}
                p+=new Vector3((R()-.5f)*.2f,(R()-.5f)*.16f,(R()-.5f)*.2f);
                float width=.15f+R()*.2f;float h=height*(index==2||index==3?.6f:1);
                fire.Emit(new ParticleSystem.EmitParams{position=p,velocity=flow*(.6f+R()*.8f),startLifetime=.18f+R()*.25f,rotation=-35+R()*70,startColor=Color.white,startSize3D=new Vector3(width,h*(.6f+R()*.6f),width)},1);
            }
        }
        public void Clear()
        {
            opened=burst=overheat=false;emission=0;
            if(warmth!=null)warmth.enabled=false;
            if(fire!=null)fire.Clear();if(pressureVoice!=null)pressureVoice.Stop();if(ventVoice!=null)ventVoice.Stop();if(releaseVoice!=null)releaseVoice.Stop();if(loopVoice!=null)loopVoice.Stop();
        }
        private void OnDestroy(){if(root!=null)Destroy(root);if(material!=null)Destroy(material);}
    }
}
