using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>
    /// C's coherent grids hand the unchanged source UV domain to unequal polygon pieces after
    /// semantic split age. Each piece has independent analytic world motion and shape contraction.
    /// No global clock, render callback, camera lookup, reference image or simulation random draw.
    /// </summary>
    internal sealed class SamImpactTextureShape : IDisposable
    {
        internal const string RendererName="Energy / original thick flame lobes";
        const int Lobes=5, Segments=12, Row=Segments+1, Pieces=9, Rings=3, EdgeDivisions=3;
        struct Vertex {internal int lobe,piece;internal Vector2 uv;}
        struct Frame {internal Vector3 center;internal float size,alpha,phase,localAge,a,b,cosine,sine;}
        readonly Transform parent;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly SamPunchEnergySettings energy;
        readonly SamImpactTextureSettings art;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly Vertex[] layout;
        readonly Vector3[] vertices,offsets;
        readonly Vector2[] uv,fragmentData;
        readonly Color[] colors;
        readonly Frame[] coherentFrames=new Frame[Lobes],pieceFrames=new Frame[Lobes*Pieces];
        readonly SamImpactTextureBreakupMath.Motion[] motions=new SamImpactTextureBreakupMath.Motion[Lobes*Pieces];
        int seed;
        float scale=1;

        internal SamImpactTextureShape(Transform parent,Material sharedMaterial,SamPunchEnergySettings energy,SamImpactTextureSettings art)
        {
            this.parent=parent;this.energy=energy;this.art=art;
            root=new GameObject(RendererName);root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM flame coherent grids and moving UV fragments"};mesh.MarkDynamic();
            BuildLayout(out layout,out int[] indices);
            vertices=new Vector3[layout.Length];offsets=new Vector3[layout.Length];
            uv=new Vector2[layout.Length];fragmentData=new Vector2[layout.Length];colors=new Color[layout.Length];
            for(int i=0;i<layout.Length;i++)uv[i]=layout[i].uv;
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=indices;
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=sharedMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.enabled=false;
        }

        static void BuildLayout(out Vertex[] vertices,out int[] triangles)
        {
            var v=new List<Vertex>();var indices=new List<int>();
            // C's original vertex positions, UVs and topology remain the first 845 vertices.
            for(int lobe=0;lobe<Lobes;lobe++)
            {
                int first=v.Count;
                for(int y=0;y<Row;y++)for(int x=0;x<Row;x++)
                {
                    v.Add(new Vertex{lobe=lobe,piece=-1,uv=new Vector2(x/(float)Segments,y/(float)Segments)});
                    if(x==Segments||y==Segments)continue;
                    int at=first+y*Row+x;
                    indices.Add(at);indices.Add(at+Row);indices.Add(at+1);
                    indices.Add(at+1);indices.Add(at+Row);indices.Add(at+Row+1);
                }
            }
            SamImpactTextureBreakupMath.Point[][] cells=SamImpactTextureBreakupMath.Partition();
            for(int lobe=0;lobe<Lobes;lobe++)for(int piece=0;piece<Pieces;piece++)
            {
                var polygon=cells[piece];var site=SamImpactTextureBreakupMath.Sites[piece];
                Vector2 pivot=new Vector2(site.x,site.y);
                int center=v.Count;v.Add(new Vertex{lobe=lobe,piece=piece,uv=pivot});
                int count=polygon.Length*EdgeDivisions;
                for(int ring=1;ring<=Rings;ring++)for(int k=0;k<count;k++)
                {
                    var a=polygon[k/EdgeDivisions];var b=polygon[(k/EdgeDivisions+1)%polygon.Length];
                    float t=(k%EdgeDivisions)/(float)EdgeDivisions;
                    Vector2 boundary=new Vector2(Mathf.Lerp(a.x,b.x,t),Mathf.Lerp(a.y,b.y,t));
                    v.Add(new Vertex{lobe=lobe,piece=piece,uv=Vector2.Lerp(pivot,boundary,ring/(float)Rings)});
                }
                for(int k=0;k<count;k++)
                {
                    int next=(k+1)%count;
                    indices.Add(center);indices.Add(center+1+k);indices.Add(center+1+next);
                    for(int ring=1;ring<Rings;ring++)
                    {
                        int a=center+1+(ring-1)*count+k,b=center+1+(ring-1)*count+next;
                        int c=center+1+ring*count+k,d=center+1+ring*count+next;
                        indices.Add(a);indices.Add(c);indices.Add(b);
                        indices.Add(b);indices.Add(c);indices.Add(d);
                    }
                }
            }
            vertices=v.ToArray();triangles=indices.ToArray();
        }

        internal bool ReplacesSpray=>art.enabled&&art.replaceProceduralSpray;
        internal bool IsAlive(float age)=>art.enabled&&age<Mathf.Max(.02f,art.lifetime);
        internal void Reset(int instanceSeed,float visualScale){seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);Hide();}

        Frame EvaluateFrame(int lobe,float age)
        {
            float a=Hash(lobe+37,seed),b=Hash(lobe+131,seed);
            float born=lobe==0?0:Mathf.Max(0,art.satelliteDelay)*(lobe*.62f+.38f*a);
            float localAge=Mathf.Max(0,age-born);
            float growth=Mathf.Lerp(Mathf.Clamp01(art.initialScale),1,Mathf.SmoothStep(0,1,Mathf.Clamp01(localAge/Mathf.Max(.01f,art.growthSeconds))));
            float baseSize=Mathf.Max(.02f,art.size)*Mathf.Max(.05f,energy.impactRadius)/.78f;
            float size=baseSize*growth*(lobe==0?1:Mathf.Clamp01(art.satelliteScale)*Mathf.Lerp(.86f,1.09f,b));
            float alpha=lobe==0?1:Mathf.Clamp01(art.satelliteOpacity)*Mathf.SmoothStep(0,1,Mathf.Clamp01((age-born)/.025f));
            if(lobe>=Mathf.Clamp(art.lobeCount,1,Lobes)||age<born)alpha=0;
            float angle=lobe==0?0:(lobe-1)*2.399963f+a*.5f;
            Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
            Vector3 center=Vector3.forward*energy.impactDistance+parent.InverseTransformVector(energy.impactWorldOffset);
            center+=Vector3.forward*(Mathf.Max(0,art.forwardDrift)*localAge*(lobe==0?.40f:Mathf.Lerp(.65f,1.1f,a)));
            if(lobe!=0)center+=radial*Mathf.Max(0,art.radialSpread)*growth+Vector3.forward*(b-.5f)*.28f;
            float rotation=(-135+(lobe==0?0:(a-.5f)*50))*Mathf.Deg2Rad;
            return new Frame{center=center*scale,size=size,alpha=alpha,a=a,b=b,localAge=localAge,
                phase=Mathf.Clamp01(age/Mathf.Max(.02f,art.lifetime)),cosine=Mathf.Cos(rotation),sine=Mathf.Sin(rotation)};
        }

        Vector3 Surface(Vector2 t,Frame frame)
        {
            Vector2 p=t-new Vector2(.73f,.30f);
            float along=(frame.cosine*p.x-frame.sine*p.y)*(1+Mathf.Max(0,art.forwardStretch)*frame.phase);
            float across=frame.sine*p.x+frame.cosine*p.y;
            float membrane=Mathf.Sin(t.x*Mathf.PI)*Mathf.Sin(t.y*Mathf.PI);
            across+=Mathf.Clamp(art.curl,0,.5f)*membrane*Mathf.Sin(t.x*5.7f+t.y*4.1f+frame.localAge*12+frame.a*6.28f);
            float depth=Mathf.Clamp(art.surfaceDepth,0,.5f)*membrane*Mathf.Sin(t.x*3.7f+t.y*4.3f+frame.localAge*8+frame.b*6.28f);
            return new Vector3(along,across,depth)*frame.size*scale;
        }

        internal void SetTime(float age,float releasedAt,bool visible,int reviewMode,bool depthFade)
        {
            float duration=Mathf.Max(.02f,art.lifetime),phase=Mathf.Clamp01(age/duration);
            float envelope=Mathf.Clamp01(art.envelope!=null?art.envelope.Evaluate(phase):1-phase);
            if(releasedAt>=0)envelope*=Mathf.Clamp01(1-(age-releasedAt)/.12f);
            float opacity=envelope*Mathf.Clamp01(art.opacity);
            renderer.enabled=visible&&art.enabled&&age>=0&&phase<1&&opacity>.002f;
            if(!renderer.enabled)return;
            bool thin=art.breakupStyle==SamImpactTextureBreakupStyle.FastThinPieces;
            float coherent=SamImpactTextureBreakupMath.CoherentWeight(age,art.breakupAge,art.breakupStrength);
            Vector3 localUp=parent.InverseTransformVector(Vector3.up);
            for(int lobe=0;lobe<Lobes;lobe++)
            {
                coherentFrames[lobe]=EvaluateFrame(lobe,age);
                for(int piece=0;piece<Pieces;piece++)
                {
                    int i=lobe*Pieces+piece;
                    motions[i]=SamImpactTextureBreakupMath.Evaluate(piece,lobe,seed,age,art.breakupAge,art.breakupStrength,thin);
                    // Freeze each piece's source deformation at its own birth, then advect that piece.
                    pieceFrames[i]=EvaluateFrame(lobe,Mathf.Min(age,motions[i].birth));
                }
            }
            Vector3 boundsMin=Vector3.one*float.PositiveInfinity,boundsMax=Vector3.one*float.NegativeInfinity;
            for(int i=0;i<layout.Length;i++)
            {
                Vertex descriptor=layout[i];
                Vector3 q,center;float alpha;
                if(descriptor.piece<0)
                {
                    Frame frame=coherentFrames[descriptor.lobe];
                    q=Surface(descriptor.uv,frame);center=frame.center;alpha=frame.alpha*coherent;
                    fragmentData[i]=new Vector2(-1,0);
                }
                else
                {
                    int index=descriptor.lobe*Pieces+descriptor.piece;
                    Frame frame=pieceFrames[index];var motion=motions[index];
                    var site=SamImpactTextureBreakupMath.Sites[descriptor.piece];
                    Vector3 pivot=Surface(new Vector2(site.x,site.y),frame);
                    Vector3 local=Surface(descriptor.uv,frame)-pivot;
                    local.x*=motion.alongScale;local.y*=motion.acrossScale;local.z*=motion.acrossScale;
                    float c=Mathf.Cos(motion.spin),s=Mathf.Sin(motion.spin);
                    q=pivot+new Vector3(local.x*c-local.y*s,local.x*s+local.y*c,local.z);
                    center=frame.center+(new Vector3(motion.radialX,motion.radialY,motion.forward)+localUp*motion.rise)*scale;
                    alpha=frame.alpha*motion.reveal;
                    fragmentData[i]=new Vector2(descriptor.piece,motion.phase);
                }
                offsets[i]=q;vertices[i]=center+new Vector3(q.y,q.z,q.x);colors[i]=new Color(1,1,1,alpha);
                float radius=q.magnitude;
                boundsMin=Vector3.Min(boundsMin,center-Vector3.one*radius);
                boundsMax=Vector3.Max(boundsMax,center+Vector3.one*radius);
            }
            mesh.vertices=vertices;mesh.SetUVs(1,offsets);mesh.SetUVs(2,fragmentData);mesh.colors=colors;
            mesh.bounds=new Bounds((boundsMin+boundsMax)*.5f,boundsMax-boundsMin+Vector3.one*.02f);
            properties.SetFloat("_LocalTime",age);properties.SetFloat("_Seed",(seed&65535)*.0137f);
            properties.SetFloat("_Opacity",opacity);
            properties.SetFloat("_Dissolve",Mathf.Clamp01(art.erosionStrength)*Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.32f)/.68f)));
            properties.SetFloat("_UVWarp",Mathf.Clamp(art.uvWarp,0,.03f));
            properties.SetFloat("_EmissionPower",Mathf.Clamp(art.emissionPower,0,6));
            properties.SetFloat("_SoftDistance",Mathf.Max(.001f,art.depthFadeDistance));
            properties.SetFloat("_ReviewMode",reviewMode);properties.SetFloat("_UseDepthFade",depthFade?1:0);
            Vector3 direction=parent.forward;
            properties.SetVector("_FlowDirection",new Vector4(direction.x,direction.y,direction.z,0));
            renderer.SetPropertyBlock(properties);
        }
        internal void Hide(){renderer.enabled=false;}
        public void Dispose()
        {
            if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            if(root!=null)UnityEngine.Object.Destroy(root);
        }
        static float Hash(int index,int instanceSeed)=>SamImpactTextureBreakupMath.Hash(index,instanceSeed);
    }
}
