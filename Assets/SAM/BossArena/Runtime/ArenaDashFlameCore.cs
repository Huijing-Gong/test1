using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // The proxy bounds a three-dimensional fire density field. Both that shader
    // field and the damage query use this same capsule, without proxy padding.
    // No renderer AABB, camera padding or target distance defines its radius.
    internal sealed class ArenaDashFlameCore : IDisposable
    {
        private const int Slices = 24, Rings = 12;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Vector3[] vertices = new Vector3[(Slices+1)*Rings];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Vector3 a, b;
        private float radius;
        internal bool Visible => renderer != null && renderer.enabled;
        internal int VertexCount => vertices.Length;
        internal Vector3 A => a;
        internal Vector3 B => b;
        internal float Radius => radius;

        internal ArenaDashFlameCore(Transform owner, Material material)
        {
            root = new GameObject("Dash release physical fire core"); root.transform.SetParent(owner, false);
            renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            mesh = new Mesh { name = "SAM18 fire core physical capsule shell" }; mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length];
            var triangles = new int[(Rings-1)*Slices*6];
            for (int ring=0; ring<Rings; ++ring)
                for (int slice=0; slice<=Slices; ++slice)
                {
                    int i=ring*(Slices+1)+slice;
                    uv[i]=new Vector2(ring/(Rings-1f),slice/(float)Slices);
                    colors[i]=new Color(1,.78f,.48f,.78f);
                    if (ring==Rings-1 || slice==Slices) continue;
                    int q=(ring*Slices+slice)*6;
                    // Outward winding is required for the volume's front/back
                    // selection; its proxy surface is never shaded as a shell.
                    triangles[q]=i; triangles[q+1]=i+1; triangles[q+2]=i+Slices+1;
                    triangles[q+3]=i+1; triangles[q+4]=i+Slices+2; triangles[q+5]=i+Slices+1;
                }
            mesh.vertices=vertices; mesh.uv=uv; mesh.colors=colors; mesh.triangles=triangles;
            renderer.enabled=false;
        }

        internal void Show(Vector3 near, Vector3 far, float physicalRadius, float age, float opacity, long execution)
        {
            a=near; b=far; radius=physicalRadius;
            Vector3 forward=(b-a).normalized;
            if (forward.sqrMagnitude<.5f || radius<=0) { Hide(); return; }
            float length=Vector3.Distance(a,b);
            root.transform.SetPositionAndRotation(a,Quaternion.LookRotation(forward,
                Mathf.Abs(Vector3.Dot(forward,Vector3.up))>.95f?Vector3.right:Vector3.up));
            for (int ring=0; ring<Rings; ++ring)
            {
                float z, radial;
                if (ring<5)
                {
                    float angle=Mathf.Lerp(-Mathf.PI*.5f,0,ring/4f);
                    z=Mathf.Sin(angle)*radius; radial=Mathf.Cos(angle)*radius;
                }
                else if (ring<7) { z=length*(ring-4)/3f; radial=radius; }
                else
                {
                    float angle=Mathf.Lerp(0,Mathf.PI*.5f,(ring-7)/4f);
                    z=length+Mathf.Sin(angle)*radius; radial=Mathf.Cos(angle)*radius;
                }
                for (int slice=0;slice<=Slices;++slice)
                {
                    float angle=slice*Mathf.PI*2/Slices;
                    vertices[ring*(Slices+1)+slice]=new Vector3(Mathf.Cos(angle)*radial,Mathf.Sin(angle)*radial,z);
                }
            }
            mesh.vertices=vertices; mesh.RecalculateBounds();
            properties.SetFloat("_LocalTime",age);
            properties.SetFloat("_Seed",(execution&65535)*.0137f);
            properties.SetFloat("_Opacity",Mathf.Clamp01(opacity));
            properties.SetFloat("_CapsuleRadius",radius);
            properties.SetFloat("_CapsuleLength",length);
            renderer.SetPropertyBlock(properties); renderer.enabled=opacity>.003f;
        }
        internal void Hide() { if (renderer!=null) renderer.enabled=false; radius=0; }
        public void Dispose() { if(mesh!=null)UnityEngine.Object.Destroy(mesh); if(root!=null)UnityEngine.Object.Destroy(root); }
    }
}
