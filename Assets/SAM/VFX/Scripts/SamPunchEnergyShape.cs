using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    [Flags]
    public enum SamVfxLayers
    {
        None=0, HeatCore=1, FlamePetals=2, PressureStreaks=4,
        FlameFragments=8, Embers=16, Dust=32, WarmLight=64, All=127
    }

    /// <summary>Local world-unit art controls, referenced to an energy release, not a mesh collision.</summary>
    [Serializable]
    public sealed class SamPunchEnergySettings
    {
        public bool enabled=true;
        public SamImpactRaySettings rays=new SamImpactRaySettings();
        [Min(.05f)] public float forwardLength=1.75f;
        [Min(.02f)] public float spreadRadius=.56f;
        [Min(0)] public float forwardOffset=.18f;
        [Header("Two spatial stages: fist transfer then free-space impact")]
        public bool transferEnabled=true;
        public bool impactEnabled=true;
        [Min(.05f)] public float transferLength=.78f;
        [Min(.01f)] public float transferRadius=.095f;
        [Min(.1f)] public float impactDistance=.55f;
        [Tooltip("World-space gravity bias for the released mass; does not move the fist origin or actor.")]
        public Vector3 impactWorldOffset=new Vector3(0,-.27f,0);
        [Min(.05f)] public float impactRadius=.78f;
        [Min(.05f)] public float impactHalfDepth=.50f;
        [Range(.2f,1)] public float impactInitialScale=.48f;
        [Range(.1f,1)] public float impactTransmission=.62f;
        [Header("Finite participating flame volume")]
        public bool volumeEnabled=true;
        [Range(16,48)] public int volumeSteps=40;
        [Range(.2f,10)] public float volumeDensity=3.7f;
        [Range(.7f,1.8f)] public float volumeRadiusScale=1.15f;
        [Range(.7f,3.2f)] public float volumeDepthScale=2.45f;
        [Range(.25f,1)] public float volumeInitialScale=.52f;
        [Range(.25f,1)] public float volumeInitialAxialScale=.70f;
        [Tooltip("Integration bounds padding only. Changing this must not scale the physical density field.")]
        [Range(2.6f,3.2f)] public float volumeBoundsPadding=2.6f;
        [Range(0,1)] public float volumeTurbulence=.62f;
        [Range(0,1)] public float volumeCoreBreakup=.72f;
        [Range(0,1)] public float edgeBreakup=.42f;
        [Range(0,1)] public float curl=.33f;
        [Range(0,1)] public float detachedFragments=.8f;
        [Range(.2f,1.1f)] public float coreWidth=.62f;
        [Range(0,1)] public float fanOpening=.78f;
        [Range(0,1)] public float fragmentation=.65f;
        [Range(0,.65f)] public float pulseContrast=.32f;
        [Min(.015f)] public float pulseSpacing=.058f;
        [Min(.02f)] public float coreLifetime=.25f;
        [Min(.02f)] public float petalLifetime=.43f;
        [Min(.02f)] public float streakLifetime=.21f;
        [Min(.02f)] public float fragmentEmission=.22f;
        [ColorUsage(true,true)] public Color hotCore=new Color(1f,.91f,.55f,1);
        [ColorUsage(true,true)] public Color warmCore=new Color(1f,.38f,.035f,1);
        [ColorUsage(true,true)] public Color outerFlame=new Color(.72f,.033f,.001f,1);
        [ColorUsage(true,true)] public Color outerHot=new Color(1f,.43f,.038f,1);
        [Range(0,8)] public float corePower=3.2f;
        [Range(0,8)] public float petalPower=2.6f;
        [Range(0,8)] public float streakPower=2.1f;
        public AnimationCurve coreEnvelope=new AnimationCurve(new Keyframe(0,.52f),new Keyframe(.11f,1),new Keyframe(.28f,.72f),new Keyframe(.43f,1),new Keyframe(.67f,.58f),new Keyframe(.90f,0),new Keyframe(1,0));
        public AnimationCurve petalEnvelope=new AnimationCurve(new Keyframe(0,.30f),new Keyframe(.15f,.85f),new Keyframe(.28f,1),new Keyframe(.40f,.75f),new Keyframe(.58f,.42f),new Keyframe(.78f,.08f),new Keyframe(1,0));
        public AnimationCurve streakEnvelope=new AnimationCurve(new Keyframe(0,.48f),new Keyframe(.13f,1),new Keyframe(.36f,.62f),new Keyframe(.59f,.80f),new Keyframe(.82f,.18f),new Keyframe(1,0));
        public AnimationCurve impactEnvelope=new AnimationCurve(new Keyframe(0,.78f),new Keyframe(.28f,1),new Keyframe(.50f,.90f),new Keyframe(.70f,.54f),new Keyframe(.90f,.06f),new Keyframe(1,0));
    }

    /// <summary>
    /// Independently fading world-space transfer, pressure, volume and original flame surfaces.
    /// The optional curved texture lobes billboard per rendering camera at released world positions.
    /// </summary>
    internal sealed class SamPunchEnergyShape : IDisposable
    {
        sealed class Layer
        {
            internal GameObject root;
            internal Mesh mesh;
            internal MeshRenderer renderer;
            internal Vector3[] vertices;
            internal Vector2[] uv;
            internal Color[] colors;
            internal int[] indices;
            internal int strokes,segments;
            internal readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        }
        readonly Layer core,streaks;
        readonly SamImpactSprayShape petals;
        readonly SamImpactCloudShape impactCloud;
        readonly SamImpactVolumeShape impactVolume;
        readonly SamImpactTextureShape originalFlame;
        readonly SamImpactRayShape rays;
        readonly SamReleasedTransferFrame transferFrame=new SamReleasedTransferFrame();
        readonly Transform parent;
        readonly SamPunchEnergySettings settings;
        int seed;
        float scale;
        float releasedAt=-1;
        static readonly int TimeId=Shader.PropertyToID("_LocalTime");
        static readonly int SeedId=Shader.PropertyToID("_Seed");
        static readonly int ShapeId=Shader.PropertyToID("_ShapeMode");
        static readonly int OpacityId=Shader.PropertyToID("_Opacity");
        static readonly int DissolveId=Shader.PropertyToID("_Dissolve");
        static readonly int BaseId=Shader.PropertyToID("_BaseColor");
        static readonly int HotId=Shader.PropertyToID("_HotColor");
        static readonly int PowerId=Shader.PropertyToID("_EmissionPower");
        static readonly int EdgeId=Shader.PropertyToID("_EdgeSoftness");
        static readonly int ReviewId=Shader.PropertyToID("_ReviewMode");
        static readonly int DepthFadeId=Shader.PropertyToID("_UseDepthFade");
        static readonly int FragmentationId=Shader.PropertyToID("_Fragmentation");
        int reviewMode;
        bool useDepthFade=true;

        internal SamPunchEnergyShape(Transform root,Material material,SamPunchEnergySettings art,Material textureMaterial=null,SamImpactTextureSettings textureArt=null)
        {
            parent=root;settings=art;
            core=Create("Energy / short fist transfer",3,11,material);
            petals=new SamImpactSprayShape(root,material,art);
            streaks=Create("Energy / short pressure stream fragments",7,11,material);
            impactCloud=new SamImpactCloudShape(root,material,art);
            impactVolume=new SamImpactVolumeShape(root,material,art);
            rays=new SamImpactRayShape(root,material,art);
            if(textureMaterial!=null&&textureArt!=null)originalFlame=new SamImpactTextureShape(root,textureMaterial,art,textureArt);
        }
        Layer Create(string name,int strokes,int segments,Material material)
        {
            Layer layer=new Layer{root=new GameObject(name),strokes=strokes,segments=segments};
            layer.root.transform.SetParent(parent,false);
            layer.mesh=new Mesh{name=name+" mesh"};layer.mesh.MarkDynamic();
            layer.root.AddComponent<MeshFilter>().sharedMesh=layer.mesh;
            layer.renderer=layer.root.AddComponent<MeshRenderer>();
            layer.renderer.sharedMaterial=material;
            layer.renderer.shadowCastingMode=ShadowCastingMode.Off;
            layer.renderer.receiveShadows=false;
            layer.vertices=new Vector3[strokes*(segments+1)*2];
            layer.uv=new Vector2[layer.vertices.Length];
            layer.colors=new Color[layer.vertices.Length];
            layer.indices=new int[strokes*segments*6];
            for(int s=0;s<strokes;s++)for(int j=0;j<=segments;j++)
            {
                int v=(s*(segments+1)+j)*2;
                layer.uv[v]=new Vector2((float)j/segments,0);
                layer.uv[v+1]=new Vector2((float)j/segments,1);
                layer.colors[v]=layer.colors[v+1]=Color.white;
                if(j==segments)continue;
                int k=(s*segments+j)*6;
                layer.indices[k]=v;layer.indices[k+1]=v+1;layer.indices[k+2]=v+2;
                layer.indices[k+3]=v+1;layer.indices[k+4]=v+3;layer.indices[k+5]=v+2;
            }
            layer.mesh.vertices=layer.vertices;layer.mesh.uv=layer.uv;
            layer.mesh.colors=layer.colors;layer.mesh.triangles=layer.indices;
            return layer;
        }
        internal void Reset(int instanceSeed,float visualScale,Vector3 actualHandWorld)
        {
            seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);
            releasedAt=-1;
            // Same release center and visualScale convention as the untouched volume/cloud.
            Vector3 center=Vector3.forward*settings.impactDistance+parent.InverseTransformVector(settings.impactWorldOffset);
            transferFrame.Reset(parent,actualHandWorld,parent.TransformPoint(center*scale));
            impactCloud.Reset(seed,scale);
            impactVolume.Reset(seed,scale);
            petals.Reset(seed,scale);
            originalFlame?.Reset(seed,scale);
            rays.Reset(seed,scale);
            SetTime(0,SamVfxLayers.All,false);
        }
        internal void SampleTransfer(Vector3 actualHandWorld){transferFrame.Sample(actualHandWorld,false);}
        internal void FreezeTransfer(){transferFrame.Freeze();}
        internal void ClearTransfer(){transferFrame.Clear();}
        internal bool TryGetTransferEndpoints(out Vector3 nearWorld,out Vector3 farWorld,out bool frozen)
        {
            return transferFrame.TryGetEndpoints(out nearWorld,out farWorld,out frozen);
        }
        internal void SetTime(float age,SamVfxLayers visible,bool releasing,int diagnosticMode=0,bool depthFade=true)
        {
            if(!settings.enabled){Hide();return;}
            reviewMode=diagnosticMode;useDepthFade=depthFade;
            if(releasing){transferFrame.Freeze();if(releasedAt<0)releasedAt=age;}
            UpdateLayer(core,0,age,settings.coreLifetime,settings.coreEnvelope,SamVfxLayers.HeatCore,visible,releasing);
            bool textureReplacesSpray=originalFlame!=null&&originalFlame.ReplacesSpray;
            petals.SetTime(age,releasedAt,!textureReplacesSpray&&(visible&SamVfxLayers.FlamePetals)!=0,reviewMode,useDepthFade);
            originalFlame?.SetTime(age,releasedAt,settings.impactEnabled&&(visible&SamVfxLayers.FlamePetals)!=0,reviewMode,useDepthFade);
            UpdateLayer(streaks,2,age,settings.streakLifetime,settings.streakEnvelope,SamVfxLayers.PressureStreaks,visible,releasing);
            rays.SetTime(age,releasedAt,settings.impactEnabled&&(visible&SamVfxLayers.PressureStreaks)!=0,reviewMode,useDepthFade);
            impactCloud.SetTime(age,releasedAt,settings.impactEnabled&&(visible&SamVfxLayers.HeatCore)!=0,reviewMode,useDepthFade);
            impactVolume.SetTime(age,releasedAt,settings.impactEnabled&&settings.volumeEnabled&&(visible&SamVfxLayers.HeatCore)!=0,reviewMode,useDepthFade);
        }
        internal bool IsAlive(float age)
        {
            float duration=Mathf.Max(settings.coreLifetime,settings.petalLifetime,settings.streakLifetime);
            if(settings.rays!=null&&settings.rays.enabled)duration=Mathf.Max(duration,settings.rays.lifetime);
            return settings.enabled&&(age<duration||(originalFlame!=null&&originalFlame.IsAlive(age)))&&(releasedAt<0||age-releasedAt<.12f);
        }
        void UpdateLayer(Layer layer,int kind,float age,float duration,AnimationCurve envelope,SamVfxLayers flag,SamVfxLayers visible,bool releasing)
        {
            float t=Mathf.Clamp01(age/Mathf.Max(.02f,duration));
            float opacity=Mathf.Clamp01(envelope!=null?envelope.Evaluate(t):1-t);
            if(releasedAt>=0)opacity*=Mathf.Clamp01(1-(age-releasedAt)/.12f);
            SamReleasedTransferFrame.Frame connection=default;
            bool validConnection=kind!=0||transferFrame.TryGetFrame(out connection);
            layer.renderer.enabled=(kind!=0||settings.transferEnabled)&&validConnection&&(visible&flag)!=0&&t<1&&opacity>.002f;
            if(!layer.renderer.enabled)return;
            float expansion=1-Mathf.Exp(-age*32);
            float length=settings.forwardLength*(.44f+.63f*expansion);
            float radius=kind==0?settings.transferRadius:settings.impactRadius*Mathf.Lerp(settings.impactInitialScale,1,expansion);
            Vector3 impactOffset=parent.InverseTransformVector(settings.impactWorldOffset);
            for(int s=0;s<layer.strokes;s++)
            {
                float a=Hash(s+11,seed),b=Hash(s+61,seed),c=Hash(s+191,seed);
                float d=Hash(s+317,seed),e=Hash(s+443,seed);
                float angle=s*2.399963f+(a-.5f)*1.25f;
                bool anchored=kind==0||s<4;
                float birth=kind==0?(s<3?0:.012f+.025f*a):(kind==1?(anchored?0:s>=9?.082f+.034f*a:.012f+.064f*a):.008f+.032f*a);
                float life=kind==0?duration-birth:(kind==1?duration*(anchored?Mathf.Lerp(.48f,.68f,c):Mathf.Lerp(.48f,.72f,c)):duration*Mathf.Lerp(.48f,.90f,c));
                float strokeAge=age-birth;
                float phase=Mathf.Clamp01(strokeAge/Mathf.Max(.015f,life));
                float attack=anchored?Mathf.Lerp(.58f,1,Mathf.Clamp01(strokeAge/.015f)):Mathf.SmoothStep(0,1,Mathf.Clamp01(strokeAge/.018f));
                float decayStart=kind==0?.56f:kind==1?.40f:.25f;
                float decay=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-decayStart)/(1-decayStart)));
                float temporal=strokeAge>=0&&strokeAge<life?attack*decay:0;
                float pulsePhase=Mathf.Max(0,strokeAge)/Mathf.Max(.015f,settings.pulseSpacing*(.78f+.44f*d));
                float pulse=1-settings.pulseContrast*(.5f+.5f*Mathf.Sin(pulsePhase*Mathf.PI*2+a*5.7f));
                temporal*=pulse;
                float reach=kind==0?settings.transferLength*Mathf.Lerp(.85f,1.10f,b):length*(kind==1?Mathf.Lerp(.52f,1.10f,b):Mathf.Lerp(.46f,.88f,b));
                float zStart=kind==0?0:settings.impactDistance-settings.impactHalfDepth*.25f+settings.impactHalfDepth*.25f*d;
                // Released tongues travel forward independently. Their tips do not curl back to the axis.
                if(kind!=0)zStart+=Mathf.Max(0,strokeAge)*Mathf.Lerp(.65f,1.6f,e);
                float fan=radius*settings.fanOpening*(kind==0?.16f:kind==1?Mathf.Lerp(.75f,1.65f,c):Mathf.Lerp(.80f,1.42f,c));
                for(int j=0;j<=layer.segments;j++)
                {
                    float u=(float)j/layer.segments;
                    float twist=settings.curl*(kind==0?.10f:.20f)*Mathf.Sin(u*4.7f+a*5.3f+strokeAge*5);
                    float theta=angle+twist+(kind==1?(d-.5f)*u*.32f:0);
                    Vector3 radial=new Vector3(Mathf.Cos(theta),Mathf.Sin(theta),0);
                    Vector3 across=new Vector3(-radial.y,radial.x,0);
                    float swell=Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*u)),kind==0?.48f:.67f);
                    if(kind==0&&(j==0||j==layer.segments))swell=0;
                    float spread=kind==0?fan*(.12f+.50f*u)*swell:fan*((anchored?.04f:.15f)+u*Mathf.Lerp(.52f,1.03f,a));
                    float bend=Mathf.Sin(u*5.3f+b*6.28f+strokeAge*7)*settings.curl*radius*(kind==0?.045f:kind==1?.22f:.09f)*swell;
                    Vector3 center=radial*spread+across*bend;
                    center.z=(kind==0?settings.forwardOffset:0)+zStart+u*reach;
                    // The connection frame already contains the frozen world offset.
                    if(kind!=0)center+=impactOffset;
                    // Coarse lobes and unequal deep notches break the contour without all-over fine noise.
                    float lobes=.76f+.20f*Mathf.Sin(u*13.7f+a*9)+.12f*Mathf.Sin(u*24.1f+b*12);
                    float notch=Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*29.3f+c*7)),4);
                    float teeth=Mathf.Max(.12f,lobes-settings.edgeBreakup*notch*(kind==0?.35f:.70f));
                    float widthBase=kind==0?radius*settings.coreWidth/.62f:
                        (kind==1?radius*Mathf.Lerp(.14f,.27f,e):radius*Mathf.Lerp(.012f,.034f,e))*Mathf.Max(.05f,settings.spreadRadius/.56f);
                    float width=widthBase*(.76f+.38f*c)*swell*teeth;
                    if(kind!=0)width*=1-phase*.43f;
                    int index=(s*(layer.segments+1)+j)*2;
                    Vector3 ribbonWidth=kind==0?radial:(radial*.72f+across*.69f).normalized;
                    if(kind==0)
                    {
                        // Map only the short transfer into the sampled hand -> fixed released center frame.
                        // Endpoint distance replaces legacy forwardOffset/transferLength; widths retain art scale.
                        Vector3 edgeA=center+ribbonWidth*width;
                        Vector3 edgeB=center-ribbonWidth*width*(.58f+.36f*b)+across*width*.10f*Mathf.Sin(u*9+a);
                        layer.vertices[index]=connection.Point(u,edgeA.x*scale,edgeA.y*scale);
                        layer.vertices[index+1]=connection.Point(u,edgeB.x*scale,edgeB.y*scale);
                    }
                    else
                    {
                        layer.vertices[index]=(center+ribbonWidth*width)*scale;
                        layer.vertices[index+1]=(center-ribbonWidth*width*(.58f+.36f*b)+across*width*.10f*Mathf.Sin(u*9+a))*scale;
                    }
                    float alpha=Mathf.Lerp(.68f,1,a)*temporal;
                    if(kind==0&&(j==0||j==layer.segments))alpha=0;
                    layer.colors[index]=layer.colors[index+1]=new Color(1,1,1,alpha);
                }
            }
            layer.mesh.vertices=layer.vertices;layer.mesh.colors=layer.colors;layer.mesh.RecalculateBounds();
            layer.properties.SetFloat(TimeId,age);
            layer.properties.SetFloat(SeedId,(seed&65535)*.0137f+kind*19);
            layer.properties.SetFloat(ShapeId,2);
            layer.properties.SetFloat(OpacityId,opacity);
            layer.properties.SetFloat(DissolveId,Mathf.SmoothStep(0,kind==0?.70f:.78f,Mathf.Clamp01((t-.32f)/.68f)));
            layer.properties.SetFloat(EdgeId,kind==0?.075f:.11f);
            layer.properties.SetFloat(FragmentationId,settings.fragmentation*(kind==0?.34f:(kind==1?1:.62f)));
            layer.properties.SetFloat(ReviewId,reviewMode);
            layer.properties.SetFloat(DepthFadeId,useDepthFade?1:0);
            // MPB.SetColor does not support HDR directly. Convert authored sRGB exactly once
            // and upload the actual shader value using SetVector, which does not convert it.
            layer.properties.SetVector(BaseId,ShaderColor(kind==0?settings.warmCore:settings.outerFlame));
            Color hot=kind==0||kind==2?settings.hotCore:Color.Lerp(settings.outerHot,settings.hotCore,.22f);
            layer.properties.SetVector(HotId,ShaderColor(hot));
            layer.properties.SetFloat(PowerId,kind==0?settings.corePower:(kind==1?settings.petalPower:settings.streakPower));
            layer.renderer.SetPropertyBlock(layer.properties);
        }
        static Vector4 ShaderColor(Color authored)
        {
            Color value=QualitySettings.activeColorSpace==ColorSpace.Linear?authored.linear:authored;
            return new Vector4(value.r,value.g,value.b,value.a);
        }
        internal void Hide(){core.renderer.enabled=streaks.renderer.enabled=false;petals.Hide();impactCloud.Hide();impactVolume.Hide();originalFlame?.Hide();rays.Hide();}
        public void Dispose()
        {
            transferFrame.Clear();
            Release(core);petals.Dispose();Release(streaks);
            impactCloud.Dispose();
            impactVolume.Dispose();
            originalFlame?.Dispose();
            rays.Dispose();
        }
        static void Release(Layer layer)
        {
            if(layer.mesh!=null)UnityEngine.Object.Destroy(layer.mesh);
            if(layer.root!=null)UnityEngine.Object.Destroy(layer.root);
        }
        static float Hash(int index,int seed)
        {
            unchecked
            {
                uint n=(uint)(index*374761393)^(uint)seed;
                n=(n^(n>>13))*1274126177u;n^=n>>16;
                return (n&0x00ffffff)/16777215f;
            }
        }
    }
}
