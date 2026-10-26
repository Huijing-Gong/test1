using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Presentation only. The motor's original strike, ground contact and .36s wave own damage.
    // No Update, global _Time, physics, particle auto-simulation, or unscaled clock.
    [DisallowMultipleComponent]
    public sealed class ArenaBlackSunFx : MonoBehaviour
    {
        public Shader effectShader;
        public bool reducedMotion;
        public const float ReleaseTime = 3.041667f;
        public float ReleaseAt {get;private set;}=ReleaseTime;
        float visualRadius=4.62f;
        Transform launchBody;
        Vector4 courtBounds=new Vector4(0,0,10000,10000);
        public void Configure(float releaseAt,float coreRadius,Transform body,Bounds court)
        {ReleaseAt=Mathf.Max(1,releaseAt);visualRadius=Mathf.Max(1,coreRadius);launchBody=body;courtBounds=new Vector4(court.center.x,court.center.z,court.extents.x,court.extents.z);}
        const int StreakCount = 36, RockCount = 18;
        const float Lifetime = 1.3f;
        GameObject root;
        Transform core, wave, streakRoot, rockRoot;
        MeshRenderer coreRenderer, waveRenderer, streakRenderer, rockRenderer;
        Mesh quad, streakMesh, rockMesh;
        Material coreMaterial, waveMaterial, streakMaterial, rockMaterial;
        Light bounce;
        Camera view;
        MaterialPropertyBlock coreProperties;
        MaterialPropertyBlock waveProperties;
        readonly Vector3[] streakVertices = new Vector3[StreakCount * 4];
        readonly Color[] streakColors = new Color[StreakCount * 4];
        readonly Vector3[] rockVertices = new Vector3[RockCount * 6];
        readonly Color[] rockColors = new Color[RockCount * 6];
        int generation;
        float radius, age, sourceTime;
        Vector3 centre, corePosition;
        bool released;
        static readonly int AtmosphereId = Shader.PropertyToID("_BlackSunAtmosphere");
        static readonly int EnergyId = Shader.PropertyToID("_Energy");
        static readonly int PhaseId = Shader.PropertyToID("_PhaseTime");
        static readonly int WaveId = Shader.PropertyToID("_Wave");
        public bool Active { get; private set; }
        public bool Released => Active && released;
        public bool Ready => effectShader != null && effectShader.isSupported;
        public float SourceTime => sourceTime + age;
        public float MaximumRadius => radius;
        public bool CoreVisible => coreRenderer != null && coreRenderer.enabled && Active;
        public float EffectAge => age;
        public float CoreVisualRadius => CoreVisible && core != null ? Mathf.Abs(core.localScale.x) : 0;
        public float WaveRadius { get; private set; }
        public Vector3 Centre => centre;
        public Vector3 CorePosition => corePosition;
        public int ReleaseCount { get; private set; }
        public int OwnedRendererCount => root != null ? 4 : 0;
        public float ImpactWeight => Released && !reducedMotion ? Mathf.Exp(-age * 16) * Mathf.Sin(age * 65) : 0;

        void Awake()
        {
            // Standalone fallback; the main-world bridge supplies the live setting on entry.
            reducedMotion = PlayerPrefs.GetInt("LakeDomain.Presentation.ReducedMotion", 0) != 0;
        }

        public int Begin(Vector3 ground, float maximum, Camera camera)
        {
            Clear();
            if (!Ready) return 0;
            EnsureObjects();
            generation++;
            if (generation == 0) generation++;
            centre = ground; radius = Mathf.Max(.1f, maximum); view = camera;
            age = sourceTime = 0; released = false; Active = true;
            root.SetActive(true); waveRenderer.enabled = rockRenderer.enabled = false;
            Pose(generation, 0, ground, true);
            return generation;
        }

        public void Pose(int token, float animationTime, Vector3 ground, bool validGround)
        {
            if (!Active || released || token != generation) return;
            sourceTime = Mathf.Max(0, animationTime);
            if (!validGround || sourceTime > ReleaseAt + .125f)
            {
                coreRenderer.enabled = streakRenderer.enabled = false;
                Shader.SetGlobalFloat(AtmosphereId, 0);
                if (sourceTime > ReleaseAt + .125f) Clear();
                return;
            }
            centre = ground;
            float growth = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, ReleaseAt - .8f, sourceTime));
            float fall = Mathf.Clamp01(Mathf.InverseLerp(ReleaseAt - .8f, ReleaseAt, sourceTime));
            float curve = fall * fall * fall;
            float size = Mathf.Lerp(.1f, visualRadius / 1.4f, growth) * Mathf.Lerp(1, .86f, curve);
            // Assemble above the boss before rising into the full eclipse shot.
            // Only presentation height changes; the motor still owns contact and damage.
            float bodyHeight=launchBody?Mathf.Max(0,launchBody.position.y-centre.y):8*Mathf.SmoothStep(0,1,sourceTime/2.1f);
            float chargeHeight=bodyHeight+3.2f+size+growth*3f;
            Vector3 launch=launchBody?launchBody.position:centre;
            launch.y=centre.y+chargeHeight;
            corePosition=Vector3.Lerp(launch,centre+Vector3.up*.65f,curve);
            PlaceCore(size, .55f + growth * .65f);
            coreRenderer.enabled = growth > .005f;
            streakRenderer.enabled = growth > .06f;
            UpdateStreaks(sourceTime, false, growth);
            Shader.SetGlobalFloat(AtmosphereId, growth);
        }

        public bool Release(int token, Vector3 contact)
        {
            if (!Active || released || token != generation) return false;
            centre = contact; released = true; age = 0; ReleaseCount++;
            UpdateRelease();
            return true;
        }

        public void Tick(float delta)
        {
            if (!Active || !released || delta <= 0) return;
            age += delta;
            if (age >= Lifetime) { Clear(); return; }
            UpdateRelease();
        }

        void PlaceCore(float size, float energy, float flatten = 1)
        {
            core.position = corePosition;
            if (view) core.rotation = Quaternion.LookRotation(view.transform.forward, view.transform.up);
            core.localScale = new Vector3(size, size * flatten, size) * 1.4f;
            coreProperties.SetFloat(EnergyId, energy);
            coreProperties.SetFloat(PhaseId, (sourceTime + age) * (reducedMotion ? .12f : 1));
            coreRenderer.SetPropertyBlock(coreProperties);
        }

        void UpdateRelease()
        {
            float collapse = 1 - Mathf.SmoothStep(0, 1, age / .14f);
            corePosition = centre + Vector3.up * (.20f * collapse + .06f);
            PlaceCore(visualRadius / 1.4f * .86f * collapse, 1 + collapse * .7f, Mathf.Lerp(1, .08f, Mathf.SmoothStep(0, 1, age / .08f)));
            coreRenderer.enabled = collapse > .01f;
            WaveRadius = ArenaCombatFx.PulseRadius(age, radius);
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.30f, .85f, age));
            wave.position = centre + Vector3.up * .045f;
            wave.localScale = Vector3.one * (radius * 1.4f);
            waveProperties.SetFloat(WaveId, WaveRadius / radius);
            waveProperties.SetVector("_CourtBounds",courtBounds);
            waveProperties.SetFloat(EnergyId, fade * (reducedMotion ? .65f : 1));
            waveProperties.SetFloat(PhaseId, age);
            waveRenderer.SetPropertyBlock(waveProperties);
            waveRenderer.enabled = fade > .005f;
            streakRenderer.enabled = age < .85f;
            rockRenderer.enabled = age < 1.05f;
            UpdateStreaks(age, true, 1);
            UpdateRocks(age);
            bounce.transform.position = centre + Vector3.up * 1.4f;
            bounce.intensity = (reducedMotion ? .3f : 1.2f) * Mathf.Exp(-age * 12);
            Shader.SetGlobalFloat(AtmosphereId, 1 - Mathf.Clamp01(age / .8f));
        }

        void EnsureObjects()
        {
            if (root != null) return;
            coreProperties = new MaterialPropertyBlock();
            waveProperties = new MaterialPropertyBlock();
            root = new GameObject("Black sun | pooled presentation");
            root.transform.SetParent(transform, false);
            coreMaterial = Material("Black sun corona", 0, new Color(1, .80f, .50f));
            waveMaterial = Material("Black sun contact wave", 1, new Color(1, .77f, .39f));
            streakMaterial = Material("Black sun motes", 2, new Color(1, .82f, .52f));
            rockMaterial = Material("Black sun fragments", 3, new Color(.19f, .22f, .29f));
            quad = new Mesh { name = "Black sun shared quad" };
            quad.vertices = new[] { new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0) };
            quad.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            quad.colors = new[] { Color.white,Color.white,Color.white,Color.white };
            quad.triangles = new[] { 0,1,2,0,2,3 };quad.RecalculateBounds();
            core = Renderer("Black sun core and corona", quad, coreMaterial, out coreRenderer);
            wave = Renderer("Black sun visible damage front", quad, waveMaterial, out waveRenderer);
            wave.localRotation = Quaternion.Euler(90,0,0);
            streakMesh = new Mesh { name = "Black sun batched motes" };streakMesh.MarkDynamic();
            var uv = new Vector2[StreakCount*4];var triangles = new int[StreakCount*6];
            for(int i=0;i<StreakCount;i++){int v=i*4,t=i*6;uv[v]=Vector2.zero;uv[v+1]=Vector2.up;uv[v+2]=Vector2.one;uv[v+3]=Vector2.right;triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;}
            streakMesh.vertices=streakVertices;streakMesh.uv=uv;streakMesh.colors=streakColors;streakMesh.triangles=triangles;
            streakRoot=Renderer("Black sun motes",streakMesh,streakMaterial,out streakRenderer);
            rockMesh=new Mesh{name="Black sun batched fragments"};rockMesh.MarkDynamic();
            var rockUV=new Vector2[RockCount*6];var indices=new int[RockCount*24];
            int[] shape={0,2,4,0,4,3,0,3,5,0,5,2,1,4,2,1,3,4,1,5,3,1,2,5};
            for(int i=0;i<RockCount;i++) {for(int j=0;j<6;j++)rockUV[i*6+j]=new Vector2(.5f,.5f);for(int j=0;j<24;j++)indices[i*24+j]=i*6+shape[j];}
            rockMesh.vertices=rockVertices;rockMesh.uv=rockUV;rockMesh.colors=rockColors;rockMesh.triangles=indices;
            rockRoot=Renderer("Black sun fragments",rockMesh,rockMaterial,out rockRenderer);
            bounce=new GameObject("Black sun restrained bounce").AddComponent<Light>();bounce.transform.SetParent(root.transform,false);bounce.type=LightType.Point;bounce.range=9;bounce.color=new Color(1,.77f,.4f);bounce.intensity=0;bounce.shadows=LightShadows.None;
        }

        Material Material(string label, int mode, Color tint)
        {
            var m=new Material(effectShader){name=label,hideFlags=HideFlags.DontSave};
            m.SetFloat("_Mode",mode);m.SetColor("_Tint",tint);return m;
        }

        Transform Renderer(string label,Mesh mesh,Material material,out MeshRenderer renderer)
        {
            var go=new GameObject(label);go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return go.transform;
        }

        void UpdateStreaks(float t,bool burst,float strength)
        {
            Vector3 forward=view?view.transform.forward:Vector3.forward;
            for(int i=0;i<StreakCount;i++)
            {
                float seed=i*.61803399f,angle=i*2.399963f;
                float phase=Mathf.Repeat(t*(reducedMotion?.18f:.45f)+seed,1);
                Vector3 dir=new Vector3(Mathf.Cos(angle),Mathf.Sin(i*1.73f)*.56f,Mathf.Sin(angle)).normalized;
                Vector3 p;float length,alpha;
                if(burst){float speed=2.0f+Mathf.Repeat(seed*9,1)*4;p=centre+dir*(.35f+t*speed)+Vector3.up*(.25f+Mathf.Sin(i*1.32f)*.12f+1.1f*t-2.2f*t*t);p.y=Mathf.Max(centre.y+.07f,p.y);length=.25f*(1-t/.9f);alpha=Mathf.Clamp01(1-t/.9f);}
                else {float orbit=Mathf.Max(.3f,CoreVisualRadius)*(.85f+.55f*(1-phase));p=corePosition+dir*orbit;length=.12f+.32f*phase;alpha=Mathf.Sin(phase*Mathf.PI)*strength;}
                Vector3 tangent=burst?dir:-dir;
                Vector3 side=Vector3.Cross(forward,tangent).normalized*(burst?.026f:.018f);
                Vector3 end=p+tangent*Mathf.Max(.01f,length);int n=i*4;
                streakVertices[n]=streakRoot.InverseTransformPoint(p-side);streakVertices[n+1]=streakRoot.InverseTransformPoint(p+side);streakVertices[n+2]=streakRoot.InverseTransformPoint(end+side);streakVertices[n+3]=streakRoot.InverseTransformPoint(end-side);
                var color=new Color(1,Mathf.Lerp(.62f,1,Mathf.Repeat(seed,1)),.55f,alpha);
                streakColors[n]=streakColors[n+1]=streakColors[n+2]=streakColors[n+3]=color;
            }
            streakMesh.vertices=streakVertices;streakMesh.colors=streakColors;streakMesh.RecalculateBounds();
        }

        void UpdateRocks(float t)
        {
            for(int i=0;i<RockCount;i++)
            {
                float a=i*2.399963f,seed=Mathf.Repeat(i*.61803f,1),travel=(1.3f+seed*2.4f)*t;
                Vector3 p=centre+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(.6f+travel);
                p.y+=Mathf.Max(.04f,(2.8f+seed*2)*t-5*t*t);
                Quaternion rotation=Quaternion.Euler(i*33+t*170,i*19+t*95,i*81);
                float size=(.06f+seed*.13f)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1.05f,t)));
                for(int j=0;j<6;j++)
                {
                    Vector3 v=j==0?Vector3.up:j==1?Vector3.down:j==2?Vector3.right:j==3?Vector3.left:j==4?Vector3.forward:Vector3.back;
                    rockVertices[i*6+j]=rockRoot.InverseTransformPoint(p+rotation*Vector3.Scale(v,new Vector3(size,size*1.6f,size*.8f)));
                    rockColors[i*6+j]=new Color(.6f+j*.07f,.65f+j*.06f,.8f+j*.04f,Mathf.Clamp01(size*20));
                }
            }
            rockMesh.vertices=rockVertices;rockMesh.colors=rockColors;rockMesh.RecalculateBounds();
        }

        public void Clear()
        {
            Active=released=false;age=sourceTime=WaveRadius=0;
            if(root)root.SetActive(false);
            if(bounce)bounce.intensity=0;
            Shader.SetGlobalFloat(AtmosphereId,0);
        }
        void OnDisable(){Clear();}
        void OnDestroy()
        {
            Clear();if(root)Destroy(root);
            foreach(var m in new[]{quad,streakMesh,rockMesh})if(m)Destroy(m);
            foreach(var m in new[]{coreMaterial,waveMaterial,streakMaterial,rockMaterial})if(m)Destroy(m);
        }
    }
}
