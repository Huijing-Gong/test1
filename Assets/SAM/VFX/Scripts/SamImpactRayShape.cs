using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    [Serializable]
    public sealed class SamImpactRaySettings
    {
        public bool enabled=false;
        [Range(4,32)] public int count=24;
        [Range(.1f,.7f)] public float lifetime=.36f;
        [Range(.2f,4)] public float reach=2.65f;
        [Range(.005f,.15f)] public float width=.035f;
        [Range(0,8)] public float power=4;
        [Range(0,1)] public float opacity=.82f;
        [Range(0,1)] public float forwardBias=.55f;
        [Range(0,1)] public float directionalBreakup=0;
        public void ApplyPreset(bool broad)
        {enabled=true;count=broad?18:24;lifetime=.36f;reach=broad?2.05f:2.65f;width=broad?.080f:.035f;power=broad?3.6f:4;opacity=.82f;forwardBias=.55f;directionalBreakup=broad?1:0;}
    }

    /// <summary>Released, camera-independent tapered tubes, evaluated from absolute age and seed.</summary>
    internal sealed class SamImpactRayShape : IDisposable
    {
        const int Maximum=32,Segments=12,Sides=6,Ring=Sides+1;
        readonly Transform parent;
        readonly SamPunchEnergySettings settings;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Vector3[] vertices=new Vector3[Maximum*(Segments+1)*Ring];
        readonly Vector3[] normals=new Vector3[Maximum*(Segments+1)*Ring];
        readonly Vector2[] uv=new Vector2[Maximum*(Segments+1)*Ring];
        readonly Color[] colors=new Color[Maximum*(Segments+1)*Ring];
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        int seed;float scale=1;
        internal SamImpactRayShape(Transform parent,Material shared,SamPunchEnergySettings settings)
        {
            this.parent=parent;this.settings=settings;
            root=new GameObject("Energy / three dimensional release rays");root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM bounded 3D release rays"};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=shared;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            int[] indices=new int[Maximum*Segments*Sides*6];int k=0;
            for(int r=0;r<Maximum;r++)for(int j=0;j<=Segments;j++)for(int s=0;s<=Sides;s++)
            {
                int v=(r*(Segments+1)+j)*Ring+s;uv[v]=new Vector2((float)j/Segments,(float)s/Sides);
                if(j==Segments||s==Sides)continue;
                // With a cross-section basis whose cross product points along the ray.
                indices[k++]=v;indices[k++]=v+1;indices[k++]=v+Ring;
                indices[k++]=v+1;indices[k++]=v+Ring+1;indices[k++]=v+Ring;
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.colors=colors;mesh.triangles=indices;
            renderer.enabled=false;
        }
        internal void Reset(int seed,float scale){this.seed=seed;this.scale=Mathf.Max(.01f,scale);}
        internal void SetTime(float age,float releasedAt,bool visible,int reviewMode,bool depthFade)
        {
            SamImpactRaySettings art=settings.rays;
            float lifetime=art==null?.36f:Mathf.Clamp(art.lifetime,.1f,.7f);
            renderer.enabled=art!=null&&art.enabled&&visible&&age>=0&&age<lifetime;
            if(!renderer.enabled)return;
            float cancel=releasedAt<0?1:Mathf.Clamp01(1-(age-releasedAt)/.12f);
            if(cancel<=.002f){renderer.enabled=false;return;}
            Vector3 origin=Vector3.forward*settings.impactDistance+parent.InverseTransformVector(settings.impactWorldOffset);
            int count=Mathf.Clamp(art.count,4,Maximum);
            for(int r=0;r<Maximum;r++)
            {
                float a=Hash(r+7,seed),b=Hash(r+59,seed),c=Hash(r+137,seed),d=Hash(r+283,seed);
                float birth=r<4?0:.004f+.024f*a;
                float phase=Mathf.Clamp01((age-birth)/Mathf.Max(.02f,lifetime-birth));
                float attack=Mathf.SmoothStep(0,1,Mathf.Clamp01((age-birth)/.018f));
                float decay=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.36f)/.64f));
                float alpha=r<count&&age>=birth?Mathf.Clamp01(art.opacity)*attack*decay*cancel:0;
                float angle=r*2.39996323f+(a-.5f)*.65f;
                Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                // Fixed directions with small analytic curvature, never a camera-facing surface.
                float z=Mathf.Clamp(art.forwardBias,0,1)*Mathf.Lerp(.10f,1.25f,b)+(r%5==0?-.38f:0);
                float directed=Mathf.Clamp01(art.directionalBreakup);
                z=Mathf.Lerp(z,Mathf.Lerp(.8f,1.75f,b),directed);
                Vector3 direction=(radial*Mathf.Lerp(1,.7f,directed)+Vector3.forward*z).normalized;
                Vector3 across=Vector3.Cross(direction,Vector3.forward).normalized;
                Vector3 second=Vector3.Cross(direction,across).normalized;
                float growth=Mathf.SmoothStep(0,1,Mathf.Clamp01((age-birth)/.075f));
                float reach=Mathf.Clamp(art.reach,.2f,4)*Mathf.Lerp(Mathf.Lerp(.62f,1.18f,c),Mathf.Lerp(.32f,1.05f,c),directed)*Mathf.Lerp(.25f,1,growth);
                float departure=Mathf.Max(0,age-birth-.07f)*Mathf.Lerp(.4f,1.6f,d);
                for(int j=0;j<=Segments;j++)
                {
                    float u=(float)j/Segments;
                    float bend=Mathf.Sin(u*Mathf.PI)*Mathf.Lerp(.045f,.28f,directed)*(d-.5f)*reach;
                    Vector3 center=origin+direction*(departure+u*reach)+across*bend;
                    float section=Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*u)),.62f)*(1-u*.78f);
                    float width=Mathf.Clamp(art.width,.005f,.15f)*Mathf.Lerp(.50f,1.55f,a)*section*(1-phase*.48f);
                    for(int s=0;s<=Sides;s++)
                    {
                        float theta=(float)s/Sides*Mathf.PI*2;
                        Vector3 normal=across*Mathf.Cos(theta)+second*Mathf.Sin(theta);
                        int index=(r*(Segments+1)+j)*Ring+s;
                        vertices[index]=(center+normal*width)*scale;normals[index]=normal;
                        colors[index]=new Color(a,b,c,(j==0||j==Segments)?0:alpha);
                    }
                }
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.colors=colors;mesh.RecalculateBounds();
            block.SetFloat("_ShapeMode",8);block.SetFloat("_Opacity",1);
            block.SetFloat("_LocalTime",age);block.SetFloat("_Fragmentation",Mathf.Clamp01(art.directionalBreakup));
            block.SetFloat("_EmissionPower",Mathf.Clamp(art.power,0,8));
            block.SetFloat("_UseDepthFade",depthFade?1:0);block.SetFloat("_SoftDistance",.06f);
            block.SetFloat("_ReviewMode",reviewMode);
            block.SetVector("_BaseColor",Linear(new Color(1,.44f,.018f,1)));
            block.SetVector("_HotColor",Linear(new Color(1,.98f,.72f,1)));
            renderer.SetPropertyBlock(block);
        }
        static Vector4 Linear(Color c){return QualitySettings.activeColorSpace==ColorSpace.Linear?c.linear:c;}
        static float Hash(int index,int seed)
        {unchecked{uint n=(uint)(index*374761393)^(uint)seed;n=(n^(n>>13))*1274126177u;n^=n>>16;return(n&0xffffff)/16777215f;}}
        internal void Hide(){renderer.enabled=false;}
        public void Dispose(){if(mesh!=null)UnityEngine.Object.Destroy(mesh);if(root!=null)UnityEngine.Object.Destroy(root);}
    }
}
