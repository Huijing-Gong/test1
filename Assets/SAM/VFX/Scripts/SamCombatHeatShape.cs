using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>Surface-attached open tongues; age comes exclusively from the owning emitter clock.</summary>
    internal sealed class SamCombatHeatShape : IDisposable
    {
        const int Tongues=6, Segments=20, Columns=5;
        readonly GameObject root;
        readonly Transform parent;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly SamCombatHeatSettings settings;
        readonly Vector3[] vertices=new Vector3[Tongues*(Segments+1)*Columns];
        readonly Color[] colors=new Color[Tongues*(Segments+1)*Columns];
        readonly Vector2[] uv=new Vector2[Tongues*(Segments+1)*Columns];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        int seed;
        float scale,releaseAt=-1;
        bool chestProfile;

        internal SamCombatHeatShape(Transform parent,Material material,SamCombatHeatSettings settings)
        {
            this.parent=parent;this.settings=settings;
            root=new GameObject("Combat heat / open surface tongues");root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM open surface fire"};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            int[] indices=new int[Tongues*Segments*(Columns-1)*6];
            for(int s=0;s<Tongues;s++)for(int j=0;j<=Segments;j++)for(int k=0;k<Columns;k++)
            {
                int v=(s*(Segments+1)+j)*Columns+k;
                uv[v]=new Vector2((float)j/Segments,(float)k/(Columns-1));
                if(j==Segments||k==Columns-1)continue;
                int t=((s*Segments+j)*(Columns-1)+k)*6;
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+Columns;
                indices[t+3]=v+1;indices[t+4]=v+Columns+1;indices[t+5]=v+Columns;
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.triangles=indices;
        }

        internal void Reset(int instanceSeed,float visualScale){seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);releaseAt=-1;chestProfile=false;}
        internal void SetProfile(bool chest){chestProfile=chest;}
        internal void SetTime(float age,bool releasing,bool visible,int reviewMode,bool depthFade)
        {
            if(releasing&&releaseAt<0)releaseAt=age;
            float released=releaseAt>=0?Mathf.Clamp01(1-(age-releaseAt)/.18f):1;
            renderer.enabled=visible&&settings.enabled&&settings.surfaceTonguesEnabled&&released>.002f;
            if(!renderer.enabled)return;
            Vector3 up=parent.InverseTransformDirection(Vector3.up).normalized;
            Vector3 lateral=Vector3.Cross(up,Vector3.forward);
            if(lateral.sqrMagnitude<.01f)lateral=Vector3.right;else lateral.Normalize();
            Vector3 surfaceUp=Vector3.Cross(Vector3.forward,lateral).normalized;
            for(int s=0;s<Tongues;s++)
            {
                float a=Hash(s+41,seed),b=Hash(s+153,seed),c=Hash(s+277,seed);
                float period=Mathf.Lerp(.29f,.47f,a);
                float cursor=(age+period*c)/period;
                int cycle=Mathf.FloorToInt(cursor);
                float t=cursor-cycle;
                float cycleVariation=Hash(s+cycle*19+739,seed);
                float temporal=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.13f))*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.45f)/.55f)));
                float height=settings.tongueHeight*(chestProfile?settings.chestHeightMultiplier:1)*Mathf.Lerp(.68f,1.20f,b)*Mathf.Lerp(.52f,1,Mathf.Clamp01(t/.4f));
                // Bases spread over a small armor patch, then bend out and rise. Unequal width
                // and phase keep the chest/pauldron surface visible through moving gaps.
                Vector3 start=lateral*((a-.5f)*settings.surfaceSpread*2)+surfaceUp*((c-.5f)*settings.surfaceSpread*.75f)+Vector3.forward*settings.surfaceOffset;
                if(chestProfile)start-=surfaceUp*.065f;
                for(int j=0;j<=Segments;j++)
                {
                    float u=(float)j/Segments;
                    float belly=Mathf.Pow(Mathf.Max(.001f,Mathf.Sin(Mathf.PI*u)),.72f);
                    float curl=Mathf.Sin(u*4.8f+cycleVariation*9-t*3.6f)*settings.surfaceSpread*.23f*belly;
                    Vector3 center=start+up*(u*height)+Vector3.forward*(settings.tongueOutwardReach*u*u)+lateral*curl;
                    center+=lateral*(u*(s%2==0?-1:1)*settings.tongueSideReach*Mathf.Lerp(.65f,1.12f,a));
                    float notch=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*11.4f+b*8-t*4.5f)),2);
                    float width=settings.tongueHalfWidth*belly*Mathf.Lerp(.66f,1.14f,cycleVariation)*(.90f-.24f*notch);
                    Vector3 widthAxis=(lateral+Vector3.forward*(s%2==0?.15f:-.18f)).normalized;
                    for(int k=0;k<Columns;k++)
                    {
                        float across=(float)k/(Columns-1)*2-1;
                        Vector3 point=center+widthAxis*(across*width*(across<0?.74f:1));
                        point+=Vector3.forward*((1-across*across)*belly*.020f);
                        int v=(s*(Segments+1)+j)*Columns+k;
                        vertices[v]=point*scale;
                        colors[v]=new Color(1,1,1,temporal*Mathf.Lerp(.54f,.86f,b));
                    }
                }
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();
            properties.SetFloat("_ShapeMode",5);
            properties.SetFloat("_LocalTime",age);
            properties.SetFloat("_Seed",(seed&65535)*.0137f+173);
            properties.SetFloat("_Opacity",settings.tongueOpacity*released);
            properties.SetFloat("_Dissolve",.05f);
            properties.SetFloat("_Fragmentation",.40f);
            properties.SetFloat("_EdgeSoftness",.22f);
            properties.SetFloat("_EmissionPower",settings.tonguePower);
            properties.SetFloat("_ReviewMode",reviewMode);
            properties.SetFloat("_UseDepthFade",depthFade?1:0);
            properties.SetVector("_BaseColor",ShaderColor(new Color(1,.12f,.006f,1)));
            properties.SetVector("_HotColor",ShaderColor(new Color(1,.72f,.21f,1)));
            renderer.SetPropertyBlock(properties);
        }
        internal void Hide(){renderer.enabled=false;}
        internal bool IsAlive(float age){return releaseAt<0||age-releaseAt<.18f;}
        public void Dispose(){if(mesh!=null)UnityEngine.Object.Destroy(mesh);if(root!=null)UnityEngine.Object.Destroy(root);}
        static Vector4 ShaderColor(Color authored)
        {
            Color color=QualitySettings.activeColorSpace==ColorSpace.Linear?authored.linear:authored;
            return new Vector4(color.r,color.g,color.b,color.a);
        }
        static float Hash(int index,int instanceSeed)
        {
            unchecked{uint n=(uint)(index*374761393)^(uint)instanceSeed;n=(n^(n>>13))*1274126177u;n^=n>>16;return(n&0x00ffffff)/16777215f;}
        }
    }
}
