using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamProduction
{
    public enum SamVfxDiagnosticMode { Final=0, SolidGeometry=1, IntrinsicAlpha=2, DepthFade=3, UV=4, NoDepthFade=5 }

    [Serializable]
    public sealed class SamVfxDiagnosticReport
    {
        public string presenter,diagnosticMode;
        public bool manualTick,useDepthFade;
        public bool showPunchEffects,showCombatHeat;
        public int visibleLayers;
        public Vector3 cameraPosition,cameraForward;
        public List<SamVfxEffectDiagnostic> effects=new List<SamVfxEffectDiagnostic>();

        internal static SamVfxRendererDiagnostic ReadRenderer(Renderer renderer,Camera camera)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            Material material=renderer.sharedMaterial;
            var entry=new SamVfxRendererDiagnostic
            {
                name=renderer.name,enabled=renderer.enabled,active=renderer.gameObject.activeInHierarchy,
                layer=renderer.gameObject.layer,shader=material!=null?material.shader.name:"<null>",
                shaderSupported=material!=null&&material.shader.isSupported,
                queue=material!=null?material.renderQueue:0,
                boundsCenter=renderer.bounds.center,boundsSize=renderer.bounds.size,
                localTime=block.GetFloat("_LocalTime"),opacity=block.GetFloat("_Opacity"),
                dissolve=block.GetFloat("_Dissolve"),depthFade=block.GetFloat("_UseDepthFade"),
                reviewMode=block.GetFloat("_ReviewMode"),
                materialShapeMode=material!=null?material.GetFloat("_ShapeMode"):0,
                blockShapeMode=block.GetFloat("_ShapeMode"),
                materialPower=material!=null?material.GetFloat("_EmissionPower"):0,
                blockPower=block.GetFloat("_EmissionPower"),
                volumeSteps=block.GetFloat("_VolumeSteps"),volumeDensity=block.GetFloat("_VolumeDensity"),
                volumePhase=block.GetFloat("_VolumePhase"),volumeTurbulence=block.GetFloat("_VolumeTurbulence"),
                volumeDomainScale=block.GetFloat("_VolumeDomainScale"),
                volumeCoreBreakup=block.GetFloat("_VolumeCoreBreakup"),
                materialDepthTest=material!=null&&material.HasProperty("_ZTest")?material.GetFloat("_ZTest"):4,
                blockBaseColor=block.GetVector("_BaseColor"),blockHotColor=block.GetVector("_HotColor")
            };
            if(camera!=null)
            {
                entry.cameraIncludesLayer=(camera.cullingMask&(1<<renderer.gameObject.layer))!=0;
                entry.boundsIntersectFrustum=GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),renderer.bounds);
                entry.boundsCenterViewport=camera.WorldToViewportPoint(renderer.bounds.center);
            }
            var filter=renderer.GetComponent<MeshFilter>();
            Mesh mesh=filter!=null?filter.sharedMesh:null;
            if(mesh!=null)
            {
                entry.vertexCount=mesh.vertexCount;
                for(int sub=0;sub<mesh.subMeshCount;sub++)entry.indexCount+=(int)mesh.GetIndexCount(sub);
                Vector3[] vertices=mesh.vertices;
                foreach(Vector3 vertex in vertices)
                    if(!Finite(vertex))entry.nonFiniteVertices++;
                Color[] colors=mesh.colors;
                entry.minVertexAlpha=1;entry.maxVertexAlpha=0;
                foreach(Color color in colors){entry.minVertexAlpha=Mathf.Min(entry.minVertexAlpha,color.a);entry.maxVertexAlpha=Mathf.Max(entry.maxVertexAlpha,color.a);}
                entry.vertexColorCount=colors.Length;
                entry.uvCount=mesh.uv.Length;
            }
            var particles=renderer.GetComponent<ParticleSystem>();
            if(particles!=null){entry.particleCount=particles.particleCount;entry.particlesPaused=particles.isPaused;}
            return entry;
        }
        static bool Finite(Vector3 value)
        {
            return !(float.IsNaN(value.x)||float.IsNaN(value.y)||float.IsNaN(value.z)||float.IsInfinity(value.x)||float.IsInfinity(value.y)||float.IsInfinity(value.z));
        }
    }

    [Serializable]
    public sealed class SamVfxEffectDiagnostic
    {
        public string kind;
        public int seed;
        public float age,emissionEnd,ttl,emissionRate;
        public bool releasing,rootActive,persistent;
        public Vector3 position,direction;
        public List<SamVfxRendererDiagnostic> renderers=new List<SamVfxRendererDiagnostic>();
    }

    [Serializable]
    public sealed class SamVfxRendererDiagnostic
    {
        public string name,shader;
        public bool enabled,active,shaderSupported,cameraIncludesLayer,boundsIntersectFrustum,particlesPaused;
        public int layer,queue,vertexCount,indexCount,nonFiniteVertices,vertexColorCount,uvCount,particleCount;
        public float localTime,opacity,dissolve,depthFade,reviewMode,minVertexAlpha,maxVertexAlpha;
        public float materialShapeMode,blockShapeMode,materialPower,blockPower;
        public float volumeSteps,volumeDensity,volumePhase,volumeTurbulence,volumeDomainScale,volumeCoreBreakup,materialDepthTest;
        public Vector4 blockBaseColor,blockHotColor;
        public Vector3 boundsCenter,boundsSize,boundsCenterViewport;
    }
}
