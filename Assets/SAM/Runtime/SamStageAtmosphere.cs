using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>Six bounded world-space sectors, sampled deterministically from the skill clock.</summary>
    public sealed class SamStageAtmosphere : MonoBehaviour
    {
        public SamSkillRunner runner;
        public Shader flameShader;
        public Material floorMaterial;
        public Vector3 perimeterCenter = new Vector3(1, .01f, 1);
        public float perimeterRadius = 6.4f;
        [Range(24,256)] public int flameCount = 160;
        public float minimumHeight = .16f, maximumHeight = .80f, flameWidth = .58f;
        [Range(.1f,.65f)] public float bodyMaximumHeight = .38f;
        [Range(0,.2f)] public float tallFlameFraction = .05f;
        [Range(0,1)] public float opacity = .52f;
        [Range(0,.3f)] public float heightVariation = .14f;
        [Range(0,.2f)] public float swayAmplitude = .07f;
        [Range(.1f,2)] public float motionRate = .75f;
        public int seed = 41717;

        const int SectorCount=6, Sheets=3, Rows=9, Columns=3;
        const int VerticesPerFlame=Sheets*Rows*Columns;
        sealed class Sector
        {
            internal GameObject root;
            internal Mesh mesh;
            internal MeshRenderer renderer;
            internal Flame[] flames;
            internal Vector3[] rest,vertices;
        }
        struct Flame
        {
            internal Vector3 origin,outward,tangent;
            internal float phase,frequency;
        }
        Sector[] sectors;
        Material material;
        MaterialPropertyBlock block;
        float lastTime=float.NaN,lastOpacity=float.NaN,lastRate=float.NaN,lastSway=float.NaN,lastHeight=float.NaN;
        bool ready,started;
        static readonly int TimeId=Shader.PropertyToID("_LocalTime"),SeedId=Shader.PropertyToID("_Seed"),OpacityId=Shader.PropertyToID("_Opacity");

        void Start(){started=true;Build();Refresh();}
        void OnEnable()
        {
            if(!ready&&started)Build();
            if(!ready)return;
            SetVisible(true);lastTime=float.NaN;Refresh();
        }
        void OnDisable(){SetVisible(false);}
        void LateUpdate(){Refresh();}

        /// <summary>Explicit authoring rebuild only. Playback and pause never reconstruct meshes.</summary>
        public void Rebuild(){ReleaseResources();if(isActiveAndEnabled){Build();Refresh();}}

        void Build()
        {
            if(ready||flameShader==null)return;
            material=new Material(flameShader){name="Arena perimeter fire / runtime candidate 01"};
            material.SetColor("_BaseColor",new Color(1,.035f,.001f));
            material.SetColor("_HotColor",new Color(1,.52f,.055f));
            material.SetFloat("_EmissionPower",2.25f);
            material.SetFloat("_ShapeMode",2);
            material.SetFloat("_FlowSpeed",1.2f);
            material.SetFloat("_NoiseScale",2.7f);
            material.SetFloat("_Fragmentation",.42f);
            material.SetFloat("_Dissolve",.16f);
            material.SetFloat("_SoftDistance",.075f);
            block=new MaterialPropertyBlock();sectors=new Sector[SectorCount];
            int count=Mathf.Clamp(flameCount,24,256);
            float radius=Mathf.Max(.25f,perimeterRadius);
            float low=Mathf.Max(.04f,minimumHeight);
            float high=Mathf.Max(low,maximumHeight);
            float bodyHigh=Mathf.Clamp(bodyMaximumHeight,low,high);
            for(int g=0;g<SectorCount;g++)
            {
                int first=count*g/SectorCount,number=count*(g+1)/SectorCount-first;
                Sector sector=new Sector{flames=new Flame[number],rest=new Vector3[number*VerticesPerFlame],vertices=new Vector3[number*VerticesPerFlame]};
                Vector2[] uv=new Vector2[sector.rest.Length];Color[] colors=new Color[sector.rest.Length];
                Vector3[] normals=new Vector3[sector.rest.Length];
                int[] indices=new int[number*Sheets*(Rows-1)*(Columns-1)*6];
                for(int f=0;f<number;f++)
                {
                    int global=first+f;
                    float a=(global+.42f*Hash(global,3))*Mathf.PI*2/count;
                    Vector3 outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    Vector3 tangent=Vector3.Cross(Vector3.up,outward).normalized;
                    Vector3 origin=perimeterCenter+outward*(radius+(Hash(global,17)-.5f)*.20f);
                    bool tall=Hash(global,29)<Mathf.Clamp(tallFlameFraction,0,.2f);
                    float height=tall?Mathf.Lerp(Mathf.Max(bodyHigh,high*.70f),high,Hash(global,53)):
                        Mathf.Lerp(low,bodyHigh,Mathf.Pow(Hash(global,71),1.5f));
                    // Reserve headroom so phase variation does not turn every small flame into a spike.
                    height/=1.18f;
                    float width=Mathf.Max(.05f,flameWidth)*Mathf.Lerp(.72f,1.12f,Hash(global,97));
                    float phase=Hash(global,113)*Mathf.PI*2;
                    sector.flames[f]=new Flame{origin=origin,outward=outward,tangent=tangent,phase=phase,frequency=Mathf.Lerp(.75f,1.25f,Hash(global,137))};
                    for(int sheet=0;sheet<Sheets;sheet++)
                    {
                        bool bed=sheet==2;
                        Vector3 right=sheet==1?(tangent*.64f+outward*.77f).normalized:tangent;
                        float sheetHeight=bed?Mathf.Lerp(.075f,.14f,Hash(global,157)):height*(sheet==1?.68f:1);
                        float sheetWidth=bed?Mathf.Max(.30f,2*Mathf.PI*radius/count*1.30f):width*(sheet==1?.56f:1);
                        Vector3 basePoint=origin+outward*(bed?-.025f:sheet==1?.017f:0);
                        for(int row=0;row<Rows;row++)
                        {
                            float u=(float)row/(Rows-1);
                            float shape=(.33f+.67f*Mathf.Sin(Mathf.PI*Mathf.Pow(u,.72f)))*Mathf.Pow(1-u,.72f);
                            float fold=Mathf.Sin(u*4.5f+phase)*.055f*u;
                            Vector3 center=basePoint+Vector3.up*(sheetHeight*u)+outward*fold+tangent*(Mathf.Sin(u*3.8f+phase)*.04f*u*u);
                            for(int col=0;col<Columns;col++)
                            {
                                int v=f*VerticesPerFlame+(sheet*Rows+row)*Columns+col;
                                float across=(float)col/(Columns-1)*2-1;
                                sector.rest[v]=center+right*(across*sheetWidth*shape*.5f)+outward*((1-across*across)*shape*.015f);
                                sector.vertices[v]=sector.rest[v];uv[v]=new Vector2(u,(float)col/(Columns-1));normals[v]=outward;
                                // These are linear modulation ratios, not authored sRGB material colors.
                                // The low bed stays orange even where many strips overlap.
                                colors[v]=bed?new Color(.82f,.24f,.08f,.47f):
                                    new Color(1,Mathf.Lerp(.82f,.48f,u),Mathf.Lerp(.48f,.20f,u),sheet==1?.36f:.72f);
                                if(row==Rows-1||col==Columns-1)continue;
                                int t=(((f*Sheets+sheet)*(Rows-1)+row)*(Columns-1)+col)*6;
                                indices[t]=v;indices[t+1]=v+Columns;indices[t+2]=v+1;
                                indices[t+3]=v+1;indices[t+4]=v+Columns;indices[t+5]=v+Columns+1;
                            }
                        }
                    }
                }
                sector.mesh=new Mesh{name="Arena fire sector "+g};sector.mesh.MarkDynamic();
                sector.mesh.vertices=sector.vertices;sector.mesh.uv=uv;sector.mesh.colors=colors;sector.mesh.normals=normals;sector.mesh.triangles=indices;
                sector.mesh.RecalculateBounds();
                Bounds bounds=sector.mesh.bounds;
                bounds.Expand(new Vector3(.65f,Mathf.Max(.35f,high*.65f),.65f));
                sector.mesh.bounds=bounds;
                sector.root=new GameObject(sector.mesh.name);sector.root.transform.SetParent(transform,false);
                sector.root.AddComponent<MeshFilter>().sharedMesh=sector.mesh;
                sector.renderer=sector.root.AddComponent<MeshRenderer>();sector.renderer.sharedMaterial=material;
                sector.renderer.shadowCastingMode=ShadowCastingMode.Off;sector.renderer.receiveShadows=false;
                sectors[g]=sector;
            }
            ready=true;lastTime=float.NaN;
        }

        /// <summary>Current clock sample only; no accumulated delta, random draw or per-frame managed arrays.</summary>
        public void Refresh()
        {
            if(!ready||!isActiveAndEnabled)return;
            float time=runner!=null?(float)runner.Clock.Time:0;
            if(float.IsNaN(time)||float.IsInfinity(time))time=0;
            float rate=Mathf.Clamp(motionRate,.1f,2),sway=Mathf.Clamp(swayAmplitude,0,.2f),variation=Mathf.Clamp(heightVariation,0,.3f);
            float alpha=Mathf.Clamp01(opacity);
            bool geometryChanged=time!=lastTime||rate!=lastRate||sway!=lastSway||variation!=lastHeight;
            if(!geometryChanged&&alpha==lastOpacity)return;
            for(int g=0;g<sectors.Length;g++)
            {
                Sector sector=sectors[g];
                if(geometryChanged)
                {
                    for(int f=0;f<sector.flames.Length;f++)
                    {
                        Flame flame=sector.flames[f];float phase=time*rate*flame.frequency*2.3f+flame.phase;
                        float lift=variation*(Mathf.Sin(phase)*.68f+Mathf.Sin(phase*.73f+1.8f)*.32f);
                        float side=Mathf.Sin(phase*.83f+.7f)*sway;
                        float depth=Mathf.Sin(phase*.61f+2.1f)*sway*.55f;
                        for(int sheet=0;sheet<Sheets;sheet++)for(int row=0;row<Rows;row++)
                        {
                            float u=(float)row/(Rows-1),weight=u*u*(sheet==2?.22f:1);
                            Vector3 bend=(flame.tangent*side+flame.outward*depth)*weight;
                            for(int col=0;col<Columns;col++)
                            {
                                int v=f*VerticesPerFlame+(sheet*Rows+row)*Columns+col;
                                Vector3 rest=sector.rest[v];
                                sector.vertices[v]=rest+bend+Vector3.up*((rest.y-flame.origin.y)*lift*(sheet==2?.22f:1));
                            }
                        }
                    }
                    sector.mesh.SetVertices(sector.vertices,0,sector.vertices.Length,MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices);
                }
                block.SetFloat(TimeId,time+2.3f);block.SetFloat(SeedId,91+g*19);block.SetFloat(OpacityId,alpha);
                sector.renderer.SetPropertyBlock(block);
            }
            if(floorMaterial!=null)floorMaterial.SetFloat(TimeId,time);
            lastTime=time;lastRate=rate;lastSway=sway;lastHeight=variation;lastOpacity=alpha;
        }
        void SetVisible(bool visible)
        {
            if(sectors==null)return;
            foreach(Sector sector in sectors)if(sector!=null&&sector.root!=null)sector.root.SetActive(visible);
        }
        void ReleaseResources()
        {
            SetVisible(false);
            if(sectors!=null)foreach(Sector sector in sectors)
            {
                if(sector==null)continue;
                if(sector.mesh!=null)Destroy(sector.mesh);
                if(sector.root!=null)Destroy(sector.root);
            }
            if(material!=null)Destroy(material);
            sectors=null;material=null;block=null;ready=false;
        }
        void OnDestroy(){ReleaseResources();}
        float Hash(int index,int salt)
        {
            unchecked{uint h=(uint)(index*374761393)^(uint)(salt*668265263)^(uint)seed;h=(h^(h>>13))*1274126177u;h^=h>>16;return(h&0xffffff)/16777215f;}
        }
    }
}
