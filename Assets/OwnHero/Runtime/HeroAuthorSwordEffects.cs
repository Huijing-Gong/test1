using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingOfGraphic.Hero
{
    // Presentation only. Samples the same blade endpoints used by combat.
    [DefaultExecutionOrder(2100)]
    public sealed class HeroAuthorSwordEffects : MonoBehaviour
    {
        public HeroPoseDriver pose;
        public Material ribbonMaterial;
        public float ribbonLifetime = .13f;
        public bool visualsEnabled = true;
        public int TrailSamples => samples.Count;
        public int SparkCount => sparks.Count;
        public int TotalContactBursts { get; private set; }
        public int TotalEmissionFrames { get; private set; }
        public float VisualClock => clock;
        public bool Emitting { get; private set; }
        struct Sample { public Vector3 a, b; public float time; }
        struct Spark { public Vector3 p, v; public float time, life, width; }
        readonly List<Sample> samples = new List<Sample>(32);
        readonly List<Spark> sparks = new List<Spark>(48);
        readonly List<Vector3> vertices = new List<Vector3>(400);
        readonly List<Color> colors = new List<Color>(400);
        readonly List<Vector2> uvs = new List<Vector2>(400);
        readonly List<int> indices = new List<int>(1600);
        Mesh mesh;
        GameObject display;
        float clock;
        Vector3 previousPlayer;
        bool initialized, subscribed, wasAttacking;

        void Start(){Initialize();}
        void Initialize()
        {
            if(!pose)pose=GetComponent<HeroPoseDriver>();
            if(initialized||!pose||!ribbonMaterial)return;
            display=new GameObject("Authored silver blade ribbon");
            display.transform.SetParent(transform,false);
            mesh=new Mesh{name="Shared live blade ribbon",hideFlags=HideFlags.DontSave};mesh.MarkDynamic();
            display.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=display.AddComponent<MeshRenderer>();renderer.sharedMaterial=ribbonMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            previousPlayer=pose.transform.position;initialized=true;
        }
        public void Clear(){samples.Clear();sparks.Clear();Emitting=false;if(mesh)mesh.Clear();}
        public void Contact(Vector3 point,Vector3 outward)
        {
            if(!initialized||Time.timeScale<=0)return;TotalContactBursts++;
            var random=new System.Random(7919+TotalContactBursts*97);
            for(int i=0;i<10;i++){
                var v=new Vector3((float)random.NextDouble()*2-1,(float)random.NextDouble()*1.6f,(float)random.NextDouble()*2-1);
                if(sparks.Count>=40)sparks.RemoveAt(0);
                sparks.Add(new Spark{p=point,v=(v+outward.normalized*.6f).normalized*2,time=clock,life=.22f,width=.012f});
            }
        }
        void LateUpdate()
        {
            Initialize();if(!initialized)return;
            if(!visualsEnabled||!pose||!pose.isActiveAndEnabled||!pose.sword||!pose.sword.gameObject.activeInHierarchy){Clear();return;}
            if(Time.timeScale<=0)return;
            clock+=Time.deltaTime;
            if(Vector3.Distance(previousPlayer,pose.transform.position)>1.5f)Clear();
            previousPlayer=pose.transform.position;
            Emitting=pose.AuthoredSwordWindow;
            if(wasAttacking&&!Emitting&&pose.CurrentAction!=null&&!pose.CurrentAction.StartsWith("ATK_"))Clear();
            wasAttacking=Emitting;
            for(int i=samples.Count-1;i>=0;i--)if(clock-samples[i].time>ribbonLifetime)samples.RemoveAt(i);
            for(int i=sparks.Count-1;i>=0;i--)if(clock-sparks[i].time>sparks[i].life)sparks.RemoveAt(i);
            if(Emitting){
                var a=pose.WeaponBase;var b=pose.WeaponTip;
                if(samples.Count>0&&Vector3.Distance(samples[samples.Count-1].b,b)>.75f)samples.Clear();
                if(samples.Count==0||Vector3.Distance(samples[samples.Count-1].b,b)>.007f){
                    if(samples.Count>=24)samples.RemoveAt(0);samples.Add(new Sample{a=a,b=b,time=clock});TotalEmissionFrames++;
                }
            }
            DrawMesh();
        }
        void Vertex(Vector3 world, Color color, Vector2 uv)
        { vertices.Add(display.transform.InverseTransformPoint(world)); colors.Add(color); uvs.Add(uv); }
        void Quad(int a, int b, int c, int d)
        { indices.Add(a); indices.Add(b); indices.Add(c); indices.Add(c); indices.Add(b); indices.Add(d); }
        void DrawMesh()
        {
            vertices.Clear(); colors.Clear(); uvs.Clear(); indices.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                float fade = Mathf.Pow(Mathf.Clamp01(1 - (clock - s.time) / ribbonLifetime), 1.5f);
                // Soft inner edge and a bright silver tip, with a narrow blue falloff.
                Vertex(s.a, new Color(.2f, .55f, .85f, 0), new Vector2(i, 0));
                Vertex(Vector3.Lerp(s.a, s.b, .24f), new Color(.2f, .55f, .85f, fade * .10f), new Vector2(i, .24f));
                Vertex(Vector3.Lerp(s.a, s.b, .85f), new Color(.4f, .85f, 1.2f, fade * .30f), new Vector2(i, .85f));
                Vertex(Vector3.Lerp(s.a, s.b, .97f), new Color(.9f, .98f, 1.1f, fade * .85f), new Vector2(i, .97f));
                Vertex(s.b, new Color(.25f, .7f, 1.2f, 0), new Vector2(i, 1));
                if (i > 0) for (int row = 0; row < 4; row++) Quad((i-1)*5+row, i*5+row, (i-1)*5+row+1, i*5+row+1);
            }
            Vector3 cameraPosition = Camera.main ? Camera.main.transform.position : transform.position + Vector3.back * 5;
            foreach (var s in sparks)
            {
                float age = clock - s.time, fade = Mathf.Clamp01(1 - age / s.life);
                Vector3 pos = s.p + s.v * age + Vector3.down * age * age * 1.8f;
                Vector3 dir = (s.v + Vector3.down * age * 3.6f).normalized;
                Vector3 side = Vector3.Cross(dir, cameraPosition - pos).normalized * s.width * fade;
                Vector3 tail = pos - dir * (.055f + .12f * fade);
                Color head = new Color(2.1f, 1.6f, .65f, fade), end = new Color(1.2f, .65f, .2f, 0);
                int n = vertices.Count;
                Vertex(pos - side, head, Vector2.zero); Vertex(pos + side, head, Vector2.right);
                Vertex(tail - side * .15f, end, Vector2.up); Vertex(tail + side * .15f, end, Vector2.one);
                Quad(n, n+1, n+2, n+3);
            }
            mesh.Clear();
            if (vertices.Count == 0) return;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        }
        void OnDisable(){Clear();}
        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (display) Destroy(display);
        }
    }
}
