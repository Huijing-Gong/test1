using UnityEngine;
using UnityEngine.Rendering;

namespace KingOfGraphic.Hero
{
    // Character-only indirect light. Keep direct sunlight and realtime shadows,
    // while avoiding green terrain bounce obscuring skin and black clothing.
    [DisallowMultipleComponent]
    public sealed class HeroPresentationLighting : MonoBehaviour
    {
        [Range(0,1)] public float neutralIndirect = .85f;
        public Color softFill = new Color(.16f,.135f,.115f,1);
        SkinnedMeshRenderer[] bodies;
        LightProbeUsage[] priorUsage;
        MaterialPropertyBlock[] priorBlocks;
        MaterialPropertyBlock block;
        readonly SphericalHarmonicsL2[] probe = new SphericalHarmonicsL2[1];
        readonly Vector4[] occlusion = { Vector4.one };
        readonly Vector3[] samplePositions = new Vector3[1];
        float nextSample;

        void OnEnable()
        {
            if(!Application.isPlaying)return;
            block=new MaterialPropertyBlock();
            bodies=GetComponentsInChildren<SkinnedMeshRenderer>(true);
            priorUsage=new LightProbeUsage[bodies.Length];
            priorBlocks=new MaterialPropertyBlock[bodies.Length];
            for(int i=0;i<bodies.Length;i++) {
                priorUsage[i]=bodies[i].lightProbeUsage;
                priorBlocks[i]=new MaterialPropertyBlock();
                bodies[i].GetPropertyBlock(priorBlocks[i]);
            }
            Refresh();
        }

        void LateUpdate(){if(Application.isPlaying&&Time.unscaledTime>=nextSample)Refresh();}

        void Refresh()
        {
            nextSample=Time.unscaledTime+.25f;
            foreach(var body in bodies) {
                if(!body)continue;
                samplePositions[0]=body.bounds.center;
                if(LightmapSettings.lightProbes&&LightmapSettings.lightProbes.count>0)LightProbes.CalculateInterpolatedLightAndOcclusionProbes(samplePositions,probe,occlusion);
                else {probe[0]=RenderSettings.ambientProbe;occlusion[0]=Vector4.one;}
                var sh=probe[0];
                for(int i=0;i<9;i++) {
                    float luminance=.2126f*sh[0,i]+.7152f*sh[1,i]+.0722f*sh[2,i];
                    for(int c=0;c<3;c++)sh[c,i]=Mathf.Lerp(sh[c,i],luminance,neutralIndirect);
                }
                sh.AddAmbientLight(softFill);
                probe[0]=sh;
                body.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(probe);
                block.CopyProbeOcclusionArrayFrom(occlusion);
                body.lightProbeUsage=LightProbeUsage.CustomProvided;
                body.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            if(bodies==null)return;
            for(int i=0;i<bodies.Length;i++)if(bodies[i]) {
                bodies[i].lightProbeUsage=priorUsage[i];
                bodies[i].SetPropertyBlock(priorBlocks[i]);
            }
        }
    }
}
