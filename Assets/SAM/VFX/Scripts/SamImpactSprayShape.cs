using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>Three disconnected short flame packets per existing release path; shorten before fading.</summary>
    internal sealed class SamImpactSprayShape : IDisposable
    {
        const int Streams=23,Packets=3,Segments=8,Columns=5;
        const int PerPacket=(Segments+1)*Columns;
        const int PerStream=Packets*PerPacket;
        readonly GameObject root;
        readonly Transform parent;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly SamPunchEnergySettings settings;
        readonly Vector3[] vertices=new Vector3[Streams*PerStream];
        readonly Color[] colors=new Color[Streams*PerStream];
        readonly Vector2[] uv=new Vector2[Streams*PerStream];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        int seed;
        float scale;

        internal SamImpactSprayShape(Transform parent,Material material,SamPunchEnergySettings settings)
        {
            this.parent=parent;this.settings=settings;
            root=new GameObject("Energy / unequal upper and lower impact jets");root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM short curved impact packets with depth"};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.enabled=false;
            int[] indices=new int[Streams*Packets*Segments*(Columns-1)*6];
            for(int s=0;s<Streams;s++)for(int packet=0;packet<Packets;packet++)for(int j=0;j<=Segments;j++)for(int k=0;k<Columns;k++)
            {
                int v=s*PerStream+packet*PerPacket+j*Columns+k;uv[v]=new Vector2((float)j/Segments,(float)k/(Columns-1));
                if(j==Segments||k==Columns-1)continue;
                int t=(((s*Packets+packet)*Segments+j)*(Columns-1)+k)*6;
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+Columns;
                indices[t+3]=v+1;indices[t+4]=v+Columns+1;indices[t+5]=v+Columns;
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.triangles=indices;
        }
        internal void Reset(int instanceSeed,float visualScale){seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);}
        internal void SetTime(float age,float releasedAt,bool visible,int diagnosticMode,bool depthFade)
        {
            float duration=Mathf.Max(.02f,settings.petalLifetime),globalPhase=Mathf.Clamp01(age/duration);
            float opacity=settings.petalEnvelope!=null?Mathf.Clamp01(settings.petalEnvelope.Evaluate(globalPhase)):1-globalPhase;
            if(releasedAt>=0)opacity*=Mathf.Clamp01(1-(age-releasedAt)/.12f);
            renderer.enabled=visible&&age>=0&&age<duration&&opacity>.002f;
            if(!renderer.enabled)return;
            Vector3 origin=Vector3.forward*settings.impactDistance+parent.InverseTransformVector(settings.impactWorldOffset);
            float timeScale=duration/.43f;
            float size=Mathf.Max(.1f,settings.forwardLength/1.75f)*SamImpactCoverageProfile.Reach;
            float widthScale=Mathf.Max(.1f,settings.impactRadius/.78f)*Mathf.Max(.1f,settings.spreadRadius/.56f)*SamImpactCoverageProfile.Width;
            // Release coordinates use the actual fist direction, with gravity defining the transverse up.
            // No world-space fist position, view vector or screen-space alignment enters the geometry.
            Vector3 transverseUp=parent.InverseTransformDirection(Vector3.up);
            transverseUp.z=0;
            transverseUp=transverseUp.sqrMagnitude>.0001f?transverseUp.normalized:Vector3.up;
            Vector3 transverseRight=Vector3.Cross(transverseUp,Vector3.forward).normalized;
            for(int s=0;s<Streams;s++)
            {
                float a=Hash(s+17,seed),b=Hash(s+53,seed),c=Hash(s+113,seed),d=Hash(s+271,seed);
                bool remnant=s>=15;
                int family=s%5;
                float birth=(remnant?.055f+.042f*a:s<4?0:.004f+.025f*a)*timeScale;
                float life=(remnant?Mathf.Lerp(.245f,.292f,c):Mathf.Lerp(.228f,.280f,c))*timeScale;
                float localAge=age-birth,phase=Mathf.Clamp01(localAge/Mathf.Max(.01f,life));
                float attack=s<4?Mathf.Lerp(.72f,1,Mathf.Clamp01(localAge/(.012f*timeScale))):Mathf.SmoothStep(0,1,Mathf.Clamp01(localAge/(.019f*timeScale)));
                float fadeStart=remnant?.58f:.42f;
                float fade=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-fadeStart)/(1-fadeStart)));
                float temporal=localAge>=0&&localAge<life?attack*fade:0;
                // Unequal vertical lobes are interleaved with forward, oblique and backward escapes.
                // Jitter and different reach/width avoid a regular spoke wheel.
                float angle;
                if(family==0)angle=Mathf.PI*.5f+Mathf.Lerp(-.56f,.42f,a);
                else if(family==1)angle=-Mathf.PI*.5f+Mathf.Lerp(-.43f,.60f,a);
                else angle=s*2.399963f+(a-.5f)*.84f;
                Vector3 radial=transverseRight*Mathf.Cos(angle)+transverseUp*Mathf.Sin(angle);
                Vector3 across=Vector3.Cross(Vector3.forward,radial).normalized;
                float axial=family<2?Mathf.Lerp(.25f,.48f,b):family==2?Mathf.Lerp(.68f,1.0f,b):family==3?Mathf.Lerp(.25f,.60f,b):Mathf.Lerp(-.33f,-.07f,b);
                float radialWeight=family<2?1:family==2?.65f:family==3?.90f:.78f;
                Vector3 axis=(Vector3.forward*axial+radial*radialWeight).normalized;
                Vector3 normal=Vector3.Cross(axis,across).normalized;
                float reach=(family<2?Mathf.Lerp(1.82f,2.52f,b):family==2?Mathf.Lerp(1.45f,2.15f,b):family==3?Mathf.Lerp(1.38f,2.10f,b):Mathf.Lerp(.62f,1.05f,b))*size;
                if(remnant)reach*=Mathf.Lerp(.80f,1.05f,d);
                reach*=Mathf.Lerp(.42f,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(localAge/(.065f*timeScale))));
                float baseWidth=(family<2?Mathf.Lerp(.085f,.145f,c):family==2?Mathf.Lerp(.060f,.115f,c):family==3?Mathf.Lerp(.060f,.13f,c):Mathf.Lerp(.028f,.060f,c))*widthScale;
                // Keep a readable flame area during recession; individual packet length and alpha
                // disappear instead of squeezing the entire path into a surviving thin wire.
                if(remnant)baseWidth*=Mathf.Lerp(.68f,.88f,c);
                baseWidth*=1.85f;
                Vector3 start=origin+radial*((a-.5f)*.14f)+Vector3.forward*((b-.5f)*.16f)+axis*(Mathf.Max(0,localAge)*.38f);
                float bendSign=d<.5f?-1:1;
                float curve=reach*Mathf.Lerp(.08f,.19f,d)*bendSign*Mathf.Clamp(settings.curl/.33f,.2f,2);
                for(int packet=0;packet<Packets;packet++)
                {
                    float packetSeed=Hash(s*7+packet+631,seed);
                    float begin=packet==0?.025f:packet==1?.40f:.77f;
                    float end=packet==0?.34f:packet==1?.70f:.98f;
                    float centerAlong=(begin+end)*.5f+(packetSeed-.5f)*.024f;
                    // The farthest packet releases first. All are driven by the same absolute age;
                    // visibility or camera changes cannot resample or reseed them.
                    float recessionStart=.63f-packet*.105f+(packetSeed-.5f)*.035f;
                    float recession=Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-recessionStart)/.30f));
                    float packetAlive=1-recession;
                    float packetLength=(end-begin)*reach*Mathf.Lerp(1,.26f,recession);
                    float packetWidth=baseWidth*Mathf.Lerp(.80f,1.15f,packetSeed)*Mathf.Lerp(1,.78f,recession);
                    float centerArch=Mathf.Sin(Mathf.PI*centerAlong);
                    Vector3 centerOrigin=start+axis*(reach*centerAlong)+across*(centerArch*curve)
                        +normal*(Mathf.Sin(centerAlong*4.1f+d*5)*centerArch*reach*.075f);
                    Vector3 packetAxis=(axis+across*(Mathf.Cos(Mathf.PI*centerAlong)*curve*Mathf.PI/Mathf.Max(.01f,reach))).normalized;
                    for(int j=0;j<=Segments;j++)
                    {
                        float u=(float)j/Segments,arch=Mathf.Sin(Mathf.PI*u);
                        // Each piece has an independent rounded shoulder, bent tip and short hot belly.
                        // There are no triangles spanning the transparent spaces between pieces.
                        float tipBend=Mathf.Sin(u*3.4f+packetSeed*4)*arch;
                        Vector3 center=centerOrigin+packetAxis*(packetLength*(u-.5f))
                            +across*(tipBend*packetLength*.12f)
                            +normal*(Mathf.Sin(u*4.9f+d*5)*arch*packetLength*.14f);
                        Vector3 tangent=(packetAxis+across*(Mathf.Cos(u*3.4f+packetSeed*4)*packetLength*.12f)).normalized;
                        float roll=(a-.5f)*1.05f+Mathf.Sin(u*3.8f+d*6+localAge*3+packetSeed*5)*.28f;
                        Vector3 side=Quaternion.AngleAxis(roll*Mathf.Rad2Deg,tangent)*across;
                        side=(side-tangent*Vector3.Dot(side,tangent)).normalized;
                        Vector3 curveNormal=Vector3.Cross(tangent,side).normalized;
                        float unequalBelly=Mathf.Pow(Mathf.Max(0,arch),.56f)*Mathf.Lerp(1.1f,.68f,u);
                        float width=packetWidth*unequalBelly*(.80f+.20f*Mathf.Sin(u*8.3f+b*8+packetSeed*6-localAge*4));
                        float alongAlpha=Mathf.SmoothStep(0,1,Mathf.Clamp01(u/.10f))*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((u-.72f)/.28f)));
                        for(int k=0;k<Columns;k++)
                        {
                            float v=(float)k/(Columns-1)*2-1;
                            int index=s*PerStream+packet*PerPacket+j*Columns+k;
                            vertices[index]=(center+side*(v*width)+curveNormal*((1-v*v)*width*.65f))*scale;
                            float acrossAlpha=Mathf.Pow(Mathf.Max(0,1-v*v),.65f);
                            float warmth=Mathf.Lerp(.94f,.72f,u);
                            colors[index]=new Color(1,warmth,Mathf.Lerp(.82f,.48f,u),temporal*packetAlive*alongAlpha*acrossAlpha);
                        }
                    }
                }
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();
            properties.SetFloat("_ShapeMode",6);properties.SetFloat("_LocalTime",age);
            properties.SetFloat("_Seed",(seed&65535)*.0137f+379);
            properties.SetFloat("_Opacity",opacity*SamImpactCoverageProfile.SprayOpacity);properties.SetFloat("_Dissolve",.06f+globalPhase*.19f);
            properties.SetFloat("_EdgeSoftness",.18f);properties.SetFloat("_Fragmentation",settings.fragmentation*.6f);
            properties.SetFloat("_EmissionPower",settings.petalPower*SamImpactCoverageProfile.SprayPower);properties.SetFloat("_ReviewMode",diagnosticMode);
            properties.SetFloat("_UseDepthFade",depthFade?1:0);
            properties.SetVector("_BaseColor",ShaderColor(settings.outerFlame));
            properties.SetVector("_HotColor",ShaderColor(Color.Lerp(settings.outerHot,settings.hotCore,.48f)));
            renderer.SetPropertyBlock(properties);
        }
        internal void Hide(){renderer.enabled=false;}
        public void Dispose(){if(mesh!=null)UnityEngine.Object.Destroy(mesh);if(root!=null)UnityEngine.Object.Destroy(root);}
        static Vector4 ShaderColor(Color authored)
        {
            Color value=QualitySettings.activeColorSpace==ColorSpace.Linear?authored.linear:authored;
            return new Vector4(value.r,value.g,value.b,value.a);
        }
        static float Hash(int index,int instanceSeed)
        {
            unchecked{uint n=(uint)(index*374761393)^(uint)instanceSeed;n=(n^(n>>13))*1274126177u;n^=n>>16;return(n&0x00ffffff)/16777215f;}
        }
    }
}
