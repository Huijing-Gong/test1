using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Only draws and plays source audio. The motor supplies the actual wave radius.
    public sealed class ArenaStompPresentation : MonoBehaviour
    {
        public Shader flameShader;
        public bool muteAudio;
        public AudioClip charge, footLift, impact, fireWave;
        public float liftCueTime=.5f, strongerBootTime=1.4f;
        public int AudioReleaseCount { get; private set; }
        public float VisibleRadius { get; private set; }
        public bool WarningVisible => warning != null && warning.enabled;
        private LineRenderer warning;
        private Mesh mesh;
        private MeshRenderer wave;
        private Material warningMaterial, waveMaterial;
        private AudioSource chargeVoice, detailVoice, waveVoice;
        private ParticleSystem tongues, bootFire;
        private Material particleMaterial;
        private float waveEmission, bootEmission;
        private uint seed=18371;
        private Transform foot;
        private GameObject root;
        private Vector3 centre;
        private float radius;
        private bool lifted;
        private const int Segments=128;
        private readonly Vector3[] vertices=new Vector3[(Segments+1)*4];
        private readonly Color[] colors=new Color[(Segments+1)*4];

        private AudioSource Voice(string name)
        {
            var obj=new GameObject(name); obj.transform.SetParent(root.transform,false);
            var source=obj.AddComponent<AudioSource>(); source.playOnAwake=false; source.spatialBlend=.85f;
            source.mute=muteAudio;source.minDistance=4; source.maxDistance=45; source.rolloffMode=AudioRolloffMode.Linear; return source;
        }
        private void Prepare()
        {
            if (root != null) return;
            if (flameShader == null || charge == null || footLift == null || impact == null || fireWave == null)
                throw new InvalidOperationException("Stomp presentation source assets missing.");
            root=new GameObject("Stomp18 owned presentation");
            warningMaterial=new Material(flameShader); waveMaterial=new Material(flameShader);
            waveMaterial.SetFloat("_Erosion",1);
            var ring=new GameObject("Declared 9.75 metre warning"); ring.transform.SetParent(root.transform,false);
            warning=ring.AddComponent<LineRenderer>(); warning.sharedMaterial=warningMaterial;
            warning.useWorldSpace=true; warning.loop=true; warning.positionCount=Segments; warning.widthMultiplier=.055f;
            warning.startColor=warning.endColor=new Color(1,.25f,.045f,.55f);
            warning.shadowCastingMode=ShadowCastingMode.Off; warning.receiveShadows=false;
            var flame=new GameObject("Expanding grounded fire wave"); flame.transform.SetParent(root.transform,false);
            mesh=new Mesh { name="Stomp18 radial flame source" }; mesh.MarkDynamic();
            var uv=new Vector2[vertices.Length]; var indices=new int[Segments*18];
            for(int i=0;i<=Segments;i++) for(int j=0;j<4;j++) uv[i*4+j]=new Vector2(i/(float)Segments,j/3f);
            for(int i=0;i<Segments;i++) for(int j=0;j<3;j++)
            {
                int at=(i*3+j)*6,a=i*4+j,b=a+4;
                indices[at]=a;indices[at+1]=b;indices[at+2]=a+1;indices[at+3]=a+1;indices[at+4]=b;indices[at+5]=b+1;
            }
            mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=indices;
            flame.AddComponent<MeshFilter>().sharedMesh=mesh;
            wave=flame.AddComponent<MeshRenderer>(); wave.sharedMaterial=waveMaterial;
            wave.shadowCastingMode=ShadowCastingMode.Off; wave.receiveShadows=false;
            chargeVoice=Voice("Ground pressure"); detailVoice=Voice("Lift and impact"); waveVoice=Voice("Outward flame");
            var source=GetComponentInParent<ArenaDashPresentation>()?.sourceFire;
            if(source==null || source.flameShader==null)throw new InvalidOperationException("Preserved SAM flame shader is required.");
            particleMaterial=new Material(source.flameShader);
            particleMaterial.SetVector("_BaseColor",new Color(1,.035f,.002f));particleMaterial.SetVector("_HotColor",new Color(1,.5f,.06f));
            particleMaterial.SetFloat("_EmissionPower",2.8f);particleMaterial.SetFloat("_Opacity",.62f);
            particleMaterial.SetFloat("_ShapeMode",0);particleMaterial.SetFloat("_UseDepthFade",1);particleMaterial.SetFloat("_SoftDistance",.08f);
            if(source.erosionTexture!=null)particleMaterial.SetTexture("_MainTex",source.erosionTexture);
            tongues=Particles("Broken rising fire front",1200);bootFire=Particles("Right boot gathering fire",100);
            Clear();
        }
        private ParticleSystem Particles(string name,int capacity)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root.transform,false);var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.maxParticles=capacity;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startSpeed=0;main.startSize3D=true;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.3f,.03f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.9f,.14f),new GradientAlphaKey(.55f,.55f),new GradientAlphaKey(0,1)});
            var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.5f),new Keyframe(.2f,1),new Keyframe(1,.05f)));
            var renderer=obj.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=particleMaterial;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            ps.useAutoRandomSeed=false;ps.randomSeed=18371;ps.Pause();return ps;
        }
        private float Random01(){seed=1664525*seed+1013904223;return (seed&0x00ffffff)/16777216f;}
        private void Fire(ParticleSystem ps,Vector3 position,Vector3 velocity,float width,float height,float life)
        {ps.Emit(new ParticleSystem.EmitParams{position=position,velocity=velocity,startLifetime=life,startSize3D=new Vector3(width,height,width),startColor=Color.white},1);}
        public void Begin(Transform sourceFoot,Vector3 worldCentre,float declaredRadius)
        {
            Prepare(); Clear(); foot=sourceFoot;centre=worldCentre;radius=declaredRadius;lifted=false;AudioReleaseCount=0;waveEmission=bootEmission=0;seed=18371;
            warning.enabled=true;
            for(int i=0;i<Segments;i++) { float a=i*Mathf.PI*2/Segments;warning.SetPosition(i,centre+new Vector3(Mathf.Cos(a)*radius,.035f,Mathf.Sin(a)*radius)); }
            chargeVoice.transform.position=foot.position;chargeVoice.clip=charge;chargeVoice.volume=.52f;chargeVoice.Play();
        }
        public void Release()
        {
            warning.enabled=false;wave.enabled=true;chargeVoice.Stop();
            detailVoice.transform.position=waveVoice.transform.position=centre;
            detailVoice.PlayOneShot(impact,.8f);waveVoice.PlayOneShot(fireWave,.62f);AudioReleaseCount++;
        }
        public void SetCentre(Vector3 point)
        {
            centre=point;
            if(warning==null||!warning.enabled)return;
            for(int i=0;i<Segments;i++){float a=i*Mathf.PI*2/Segments;warning.SetPosition(i,centre+new Vector3(Mathf.Cos(a)*radius,.035f,Mathf.Sin(a)*radius));}
        }
        public void Tick(float delta,float sourceTime,float stampTime,float duration,float actualRadius)
        {
            if (root == null) return;
            particleMaterial.SetFloat("_LocalTime",sourceTime);
            tongues.Simulate(delta,false,false,true);tongues.Pause();bootFire.Simulate(delta,false,false,true);bootFire.Pause();
            if (foot != null) chargeVoice.transform.position=foot.position;
            if (!lifted && sourceTime >= liftCueTime) { lifted=true; detailVoice.transform.position=foot.position;detailVoice.PlayOneShot(footLift,.48f); }
            if (sourceTime < stampTime)
            {
                bootEmission+=delta*(sourceTime<strongerBootTime?28:70);
                while(bootEmission>=1){bootEmission--;float a=Random01()*Mathf.PI*2;Fire(bootFire,foot.position+new Vector3(Mathf.Cos(a)*.14f,0,Mathf.Sin(a)*.14f),Vector3.up*(.3f+Random01()*.55f),.18f,.38f,.22f+Random01()*.2f);}
                return;
            }
            float age=sourceTime-stampTime;VisibleRadius=actualRadius;
            waveMaterial.SetFloat("_SourceTime",sourceTime);
            wave.enabled=age <= duration;
            if(!wave.enabled)return;
            waveEmission+=delta*(180+actualRadius*300);
            while(waveEmission>=1)
            {
                waveEmission--;float a=Random01()*Mathf.PI*2;float r=Mathf.Max(0,actualRadius-Random01()*.65f);
                var direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Fire(tongues,centre+direction*r+Vector3.up*.1f,Vector3.up*(1+Random01()*1.6f)+direction*.2f,.22f+Random01()*.25f,.55f+Random01()*.55f,.2f+Random01()*.17f);
            }
            float fade=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((age/duration-.78f)/.22f));
            for(int i=0;i<=Segments;i++)
            {
                float angle=i*Mathf.PI*2/Segments;
                float flare=.22f+.13f*Mathf.Sin(angle*13-age*17)+.07f*Mathf.Sin(angle*29+age*23);
                for(int j=0;j<4;j++)
                {
                    float r=Mathf.Max(0,actualRadius-.75f+j*.25f);
                    float height=j==0||j==3?.035f:.065f+flare*.1f;
                    vertices[i*4+j]=centre+new Vector3(Mathf.Cos(angle)*r,height,Mathf.Sin(angle)*r);
                    colors[i*4+j]=j==0||j==3?new Color(.8f,.08f,.005f,0):new Color(1.6f,.22f,.012f,.3f*fade);
                }
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
        }
        public void Clear()
        {
            if(warning!=null)warning.enabled=false;if(wave!=null)wave.enabled=false;
            if(chargeVoice!=null)chargeVoice.Stop();if(detailVoice!=null)detailVoice.Stop();if(waveVoice!=null)waveVoice.Stop();
            if(tongues!=null)tongues.Clear();if(bootFire!=null)bootFire.Clear();
            VisibleRadius=0;
        }
        private void OnDestroy()
        { if(root!=null)Destroy(root);if(mesh!=null)Destroy(mesh);if(warningMaterial!=null)Destroy(warningMaterial);if(waveMaterial!=null)Destroy(waveMaterial);if(particleMaterial!=null)Destroy(particleMaterial); }
    }
}
