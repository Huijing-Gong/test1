using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>Open curved flame sheets in released local +Z space; no closed shell or camera-facing geometry.</summary>
    internal sealed class SamImpactCloudShape : IDisposable
    {
        const int Sheets=13, Along=16, Across=4;
        const int PerSheet=(Along+1)*(Across+1);
        readonly GameObject root;
        readonly Transform parent;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly SamPunchEnergySettings settings;
        readonly Vector3[] vertices=new Vector3[PerSheet*Sheets];
        readonly Color[] colors=new Color[PerSheet*Sheets];
        readonly Vector2[] uv=new Vector2[PerSheet*Sheets];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        int seed;
        float scale;

        internal SamImpactCloudShape(Transform parent,Material material,SamPunchEnergySettings art)
        {
            this.parent=parent;settings=art;
            root=new GameObject("Energy / open impact flame masses");
            root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM open impact sheets"};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            int[] indices=new int[Sheets*Along*Across*6];
            for(int s=0;s<Sheets;s++)for(int j=0;j<=Along;j++)for(int k=0;k<=Across;k++)
            {
                int v=s*PerSheet+j*(Across+1)+k;
                uv[v]=new Vector2((float)j/Along,(float)k/Across);
                colors[v]=Color.clear;
                if(j==Along||k==Across)continue;
                int t=((s*Along+j)*Across+k)*6;
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+Across+1;
                indices[t+3]=v+1;indices[t+4]=v+Across+2;indices[t+5]=v+Across+1;
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.triangles=indices;
        }

        internal void Reset(int instanceSeed,float visualScale){seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);}
        internal void SetTime(float age,float releasedAt,bool visible,int reviewMode,bool depthFade)
        {
            float duration=Mathf.Max(.02f,settings.coreLifetime);
            float phase=Mathf.Clamp01(age/duration);
            float opacity=settings.impactEnvelope!=null?Mathf.Clamp01(settings.impactEnvelope.Evaluate(phase)):1-phase;
            if(releasedAt>=0)opacity*=Mathf.Clamp01(1-(age-releasedAt)/.12f);
            renderer.enabled=visible&&age<duration&&opacity>.002f;
            if(!renderer.enabled)return;
            Vector3 origin=Vector3.forward*settings.impactDistance+parent.InverseTransformVector(settings.impactWorldOffset);
            float expansion=Mathf.Lerp(settings.impactInitialScale,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.085f)));
            float radius=Mathf.Max(.05f,settings.impactRadius)*expansion;
            float depth=Mathf.Max(.05f,settings.impactHalfDepth)*expansion;
            for(int s=0;s<Sheets;s++)
            {
                float a=Hash(s+31,seed),b=Hash(s+71,seed),c=Hash(s+193,seed),d=Hash(s+311,seed);
                bool core=s<4;
                float birth=core?0:Mathf.Lerp(.009f,.075f,a);
                float localAge=Mathf.Max(0,age-birth);
                float life=duration*(core?Mathf.Lerp(.68f,.86f,b):Mathf.Lerp(.56f,.77f,b));
                float t=Mathf.Clamp01(localAge/Mathf.Max(.01f,life));
                float attack=core?1:Mathf.SmoothStep(0,1,Mathf.Clamp01(localAge/.025f));
                float fade=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.40f)/.60f));
                float alive=age>=birth&&localAge<life?attack*fade:0;
                float angle=s*2.399963f+(a-.5f)*1.25f;
                Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                Vector3 side=new Vector3(-radial.y,radial.x,0);
                // The nucleus and outward sheets share no perimeter. Each sheet separates
                // as it travels, exposing real space between surfaces instead of an egg shell.
                Vector3 drift=radial*radius*(core?.02f:localAge*Mathf.Lerp(.5f,1.8f,c));
                Vector3 start=origin+drift+radial*radius*(core?-.22f:.06f);
                start.z+=depth*(core?Mathf.Lerp(-.26f,.24f,c):Mathf.Lerp(-.12f,.28f,c));
                float reach=radius*(core?Mathf.Lerp(.78f,1.0f,c):Mathf.Lerp(.67f,1.12f,c));
                float sheetWidth=radius*(core?Mathf.Lerp(.36f,.50f,d):Mathf.Lerp(.16f,.30f,d));
                float forwardReach=depth*(core?Mathf.Lerp(.35f,.72f,b):Mathf.Lerp(.48f,1.06f,b));
                if(core)start.z-=forwardReach*.40f;
                for(int j=0;j<=Along;j++)
                {
                    float u=(float)j/Along;
                    float belly=Mathf.Pow(Mathf.Max(.001f,Mathf.Sin(Mathf.PI*u)),.56f);
                    float notch=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*24.1f+a*7.1f-localAge*17)),3);
                    float lobes=.77f+.19f*Mathf.Sin(u*12.7f+b*11-localAge*8);
                    float width=sheetWidth*belly*Mathf.Max(.09f,lobes-settings.edgeBreakup*.70f*notch);
                    Vector3 center=start+radial*(reach*u)+Vector3.forward*(forwardReach*u);
                    center+=side*(radius*.095f*belly*Mathf.Sin(u*7.7f+c*9-localAge*12));
                    for(int k=0;k<=Across;k++)
                    {
                        float v=(float)k/Across,across=v*2-1;
                        float asymmetric=across<0?Mathf.Lerp(.64f,.88f,a):Mathf.Lerp(.90f,1.12f,b);
                        Vector3 point=center+side*(across*width*asymmetric);
                        point.z+=depth*.19f*belly*(1-across*across)*Mathf.Sin(u*5.8f+a*5-localAge*10);
                        point+=radial*(width*.16f*across*Mathf.Sin(u*15+b*8));
                        int index=s*PerSheet+j*(Across+1)+k;
                        vertices[index]=point*scale;
                        float alpha=alive*(core?.78f:Mathf.Lerp(.50f,.73f,c));
                        colors[index]=new Color(1,core?1:.84f,core?1:.62f,alpha);
                    }
                }
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();
            properties.SetFloat("_ShapeMode",3);
            properties.SetFloat("_LocalTime",age);
            properties.SetFloat("_Seed",(seed&65535)*.0137f+71);
            properties.SetFloat("_Opacity",opacity*Mathf.Clamp01(settings.impactTransmission)*.76f);
            properties.SetFloat("_Dissolve",Mathf.SmoothStep(0,.72f,Mathf.Clamp01((phase-.39f)/.61f)));
            properties.SetFloat("_Fragmentation",settings.fragmentation);
            properties.SetFloat("_EdgeSoftness",.17f);
            properties.SetFloat("_EmissionPower",settings.corePower);
            properties.SetFloat("_ReviewMode",reviewMode);
            properties.SetFloat("_UseDepthFade",depthFade?1:0);
            properties.SetVector("_BaseColor",ShaderColor(settings.warmCore));
            properties.SetVector("_HotColor",ShaderColor(settings.hotCore));
            renderer.SetPropertyBlock(properties);
        }
        internal void Hide(){renderer.enabled=false;}
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
