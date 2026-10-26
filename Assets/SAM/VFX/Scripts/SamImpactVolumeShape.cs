using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>Finite world-space integration bounds. The shader renders gas inside, never the proxy's faces.</summary>
    internal sealed class SamImpactVolumeShape : IDisposable
    {
        readonly GameObject root;
        readonly Transform parent;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Material material;
        readonly SamPunchEnergySettings settings;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        int seed;
        float scale;

        internal SamImpactVolumeShape(Transform parent,Material sharedFlame,SamPunchEnergySettings settings)
        {
            this.parent=parent;this.settings=settings;
            root=new GameObject("Energy / finite flame volume");root.transform.SetParent(parent,false);
            mesh=new Mesh{name="SAM flame integration bounds"};
            mesh.vertices=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
            mesh.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,1,2,6,1,6,5,4,7,3,4,3,0,0,1,5,0,5,4,3,7,6,3,6,2};
            mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white,Color.white,Color.white,Color.white,Color.white};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            // Always run the integral, also when the camera is inside the proxy. The shader
            // clips the interval against opaque scene depth instead of depth-testing its box face.
            material=new Material(sharedFlame){name="SAM runtime finite flame volume"};
            material.SetFloat("_ZTest",(float)CompareFunction.Always);
            material.SetFloat("_ShapeMode",4);
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }

        internal void Reset(int instanceSeed,float visualScale){seed=instanceSeed;scale=Mathf.Max(.01f,visualScale);}
        internal void SetTime(float age,float releasedAt,bool visible,int reviewMode,bool depthFade)
        {
            float phase=Mathf.Clamp01(age/Mathf.Max(.02f,settings.coreLifetime));
            float envelope=settings.impactEnvelope!=null?Mathf.Clamp01(settings.impactEnvelope.Evaluate(phase)):1-phase;
            if(releasedAt>=0)envelope*=Mathf.Clamp01(1-(age-releasedAt)/.12f);
            renderer.enabled=visible&&phase<1&&envelope>.002f;
            if(!renderer.enabled)return;
            float expansion=Mathf.Lerp(settings.volumeInitialScale,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.075f)));
            float axialExpansion=Mathf.Lerp(settings.volumeInitialAxialScale,1,Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.075f)));
            Vector3 center=Vector3.forward*settings.impactDistance+parent.InverseTransformVector(settings.impactWorldOffset);
            root.transform.localPosition=center*scale;
            float radius=Mathf.Max(.05f,settings.impactRadius*settings.volumeRadiusScale)*expansion;
            float depth=Mathf.Max(.05f,settings.impactHalfDepth*settings.volumeDepthScale)*axialExpansion;
            float padding=Mathf.Clamp(settings.volumeBoundsPadding,2.6f,3.2f);
            root.transform.localScale=new Vector3(radius,radius*SamImpactCoverageProfile.VolumeVertical,depth*SamImpactCoverageProfile.VolumeAxial)*scale*padding;
            properties.SetFloat("_ShapeMode",4);
            properties.SetFloat("_LocalTime",age);
            properties.SetFloat("_Seed",(seed&65535)*.0137f+257);
            properties.SetFloat("_Opacity",envelope);
            properties.SetFloat("_VolumeDensity",settings.volumeDensity*SamImpactCoverageProfile.VolumeDensity);
            properties.SetFloat("_VolumeSteps",Mathf.Clamp(settings.volumeSteps,16,48));
            properties.SetFloat("_VolumeTurbulence",settings.volumeTurbulence);
            properties.SetFloat("_VolumeCoreBreakup",settings.volumeCoreBreakup);
            properties.SetFloat("_VolumePhase",phase);
            properties.SetFloat("_VolumeDomainScale",padding);
            properties.SetFloat("_EmissionPower",settings.corePower);
            properties.SetFloat("_ReviewMode",reviewMode);
            properties.SetFloat("_UseDepthFade",depthFade?1:0);
            properties.SetVector("_BaseColor",ShaderColor(settings.warmCore));
            properties.SetVector("_HotColor",ShaderColor(settings.hotCore));
            renderer.SetPropertyBlock(properties);
        }
        internal void Hide(){renderer.enabled=false;}
        public void Dispose()
        {
            if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            if(material!=null)UnityEngine.Object.Destroy(material);
            if(root!=null)UnityEngine.Object.Destroy(root);
        }
        static Vector4 ShaderColor(Color authored)
        {
            Color color=QualitySettings.activeColorSpace==ColorSpace.Linear?authored.linear:authored;
            return new Vector4(color.r,color.g,color.b,color.a);
        }
    }
}
