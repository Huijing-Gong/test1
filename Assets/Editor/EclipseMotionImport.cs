using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Motion/relic payload belongs only to the two authored Eclipse visual FBXs. UV2 is
// not a lightmap channel on these assets. Positions, topology, vertex colours,
// normals, materials and colliders are never rewritten by this importer.
public sealed class EclipseMotionImport : AssetPostprocessor
{
    public const uint MotionDataVersion = 5;
    const string MainPath = "Assets/Art/SideA/Eclipse/Eclipse_Main_v001.fbx";
    const string UpperPath = "Assets/Art/SideA/Eclipse/Eclipse_Upper_v001.fbx";
    const int BannerFlag = 1, GoldFlag = 2, BasaltFlag = 4;

    public sealed class BakeReport
    {
        public int Meshes, ClothVertices, TrimVertices, ChipVertices, ChipComponents;
        public int RelicVertices, FocusVertices, PendantVertices, ChainVertices, GlowingChipComponents, GlowingChipVertices;
    }

    public override uint GetVersion() { return MotionDataVersion; }
    public override int GetPostprocessOrder() { return 100; }
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != MainPath && assetPath != UpperPath) return;
        var report = Bake(root);
        Debug.Log("ECLIPSE_MOTION_UV2 v" + MotionDataVersion + " " + assetPath +
            " cloth=" + report.ClothVertices + " trim=" + report.TrimVertices +
            " chips=" + report.ChipComponents + " chipVertices=" + report.ChipVertices +
            " relics=" + report.RelicVertices + " glowingChips=" + report.GlowingChipComponents);
    }

    // Test/import entry point. Supply a pristine imported model root with its
    // FBX axis-conversion transform intact, before stage placement/rotation.
    // UV2: pinned cloth / rigid chip / motion phase / relic kind.
    // UV3: feature-relative XY / glow gain / feature phase. No texture UV is replaced.
    // UV4: rigid chip pivot in mesh-local space / size-dependent agility.
    public static BakeReport Bake(GameObject modelRoot)
    {
        if (!modelRoot) throw new ArgumentNullException(nameof(modelRoot));
        var report = new BakeReport();
        var visited = new HashSet<Mesh>();
        foreach (var filter in modelRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!mesh || !renderer || !visited.Add(mesh)) continue;
            BakeMesh(modelRoot, filter, renderer, mesh, report);
        }
        return report;
    }

    struct PositionKey : IEquatable<PositionKey>
    {
        readonly int x, y, z;
        public PositionKey(Vector3 p)
        { x = Mathf.RoundToInt(p.x * 100000); y = Mathf.RoundToInt(p.y * 100000); z = Mathf.RoundToInt(p.z * 100000); }
        public bool Equals(PositionKey other) { return x == other.x && y == other.y && z == other.z; }
        public override bool Equals(object obj) { return obj is PositionKey && Equals((PositionKey)obj); }
        public override int GetHashCode() { unchecked { return ((x * 397) ^ y) * 397 ^ z; } }
    }

    sealed class Groups
    {
        readonly int[] parent;
        public Groups(int count) { parent = new int[count]; for (int i = 0; i < count; i++) parent[i] = i; }
        public int Find(int item)
        {
            while (parent[item] != item) { parent[item] = parent[parent[item]]; item = parent[item]; }
            return item;
        }
        public void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
    }

    sealed class Component
    {
        public Bounds Bounds;
        public int Flags;
        public readonly List<int> Vertices = new List<int>();
    }

    static void BakeMesh(GameObject root, MeshFilter filter, MeshRenderer renderer, Mesh mesh, BakeReport report)
    {
        var source = mesh.vertices;
        if (source.Length == 0) return;
        var metrePositions = new Vector3[source.Length];
        var matrix = filter.transform.localToWorldMatrix;
        if (root.transform.parent) matrix = root.transform.parent.worldToLocalMatrix * matrix;
        // Include the import root's -90 degree axis conversion. Stripping the
        // root matrix would confuse Blender Z-up with Unity Y-up.
        for (int i = 0; i < source.Length; i++) metrePositions[i] = matrix.MultiplyPoint3x4(source[i]);
        var groups = new Groups(source.Length);
        var flags = new int[source.Length];
        var materials = renderer.sharedMaterials;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            string name = sub < materials.Length && materials[sub] ? materials[sub].name : "";
            int flag = name.StartsWith("Eclipse_Banner", StringComparison.Ordinal) ? BannerFlag :
                       name.StartsWith("Eclipse_Gold", StringComparison.Ordinal) ? GoldFlag :
                       name.StartsWith("Eclipse_Basalt", StringComparison.Ordinal) ? BasaltFlag : 8;
            var indices = mesh.GetIndices(sub);
            for (int j = 0; j < indices.Length; j++) flags[indices[j]] |= flag;
            if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                for (int j = 0; j + 2 < indices.Length; j += 3)
                { groups.Join(indices[j], indices[j + 1]); groups.Join(indices[j], indices[j + 2]); }
        }
        // FBX hard-normal splits duplicate vertices at face edges. Rejoin only
        // coincident locations for component identification; never weld geometry.
        var locations = new Dictionary<PositionKey, int>();
        for (int i = 0; i < source.Length; i++)
        {
            var key = new PositionKey(metrePositions[i]);
            int first;
            if (locations.TryGetValue(key, out first)) groups.Join(first, i);
            else locations.Add(key, i);
        }
        var components = new Dictionary<int, Component>();
        for (int i = 0; i < source.Length; i++)
        {
            int group = groups.Find(i); Component component;
            if (!components.TryGetValue(group, out component))
            { component = new Component { Bounds = new Bounds(metrePositions[i], Vector3.zero) }; components.Add(group, component); }
            component.Bounds.Encapsulate(metrePositions[i]); component.Flags |= flags[i]; component.Vertices.Add(i);
        }
        var allBounds = new Bounds(metrePositions[0], Vector3.zero);
        foreach (var p in metrePositions) allBounds.Encapsulate(p);
        bool main = allBounds.size.x > 12;
        var payload = new Vector4[source.Length];
        // UV4 stores a rigid chip's mesh-local pivot and size-derived agility.
        // Every face-split vertex in a connected chip receives the same pivot.
        var chipPivots = new Vector4[source.Length];
        var attachedToBanner = new bool[source.Length];
        if (main)
            foreach (var component in components.Values)
            {
                var b = component.Bounds;
                float bannerX = b.center.x < 0 ? -9 : 9;
                // Test complete ornament components. Testing individual vertices
                // would accidentally tug a nearby structural arch/pin and tear
                // its edge. Include the little dark backing disc of each seal.
                bool ornament = (component.Flags == GoldFlag || component.Flags == BasaltFlag) &&
                    b.min.x >= bannerX - .62f && b.max.x <= bannerX + .62f &&
                    b.max.y <= -.625f && b.min.y >= -3.47f &&
                    b.min.z > 2.78f && b.max.z < 3.23f;
                if (ornament) foreach (int vertex in component.Vertices) attachedToBanner[vertex] = true;
            }
        for (int i = 0; i < source.Length; i++)
        {
            var p = metrePositions[i];
            bool cloth = (flags[i] & BannerFlag) != 0;
            bool trim = attachedToBanner[i];
            if (!cloth && !trim) continue;
            float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01((-.65f - p.y) / 2.75f));
            float phase = p.x < 0 ? .73f : 2.47f;
            payload[i] = new Vector4(weight, 0, phase, 0);
            if (cloth) report.ClothVertices++; else report.TrimVertices++;
        }
        foreach (var component in components.Values)
        {
            var b = component.Bounds;
            if (component.Flags != BasaltFlag || b.size.x >= .5f || b.size.z >= .7f ||
                b.size.y >= .9f || b.size.y < .08f || b.max.y >= -1.02f) continue;
            bool attached = false;
            foreach (int vertex in component.Vertices) if (attachedToBanner[vertex]) { attached = true; break; }
            if (attached) continue;
            float phase = Mathf.Repeat(b.center.x * 1.713f + b.center.y * 2.319f + b.center.z * .517f, Mathf.PI * 2);
            var pivot=matrix.inverse.MultiplyPoint3x4(b.center);
            float agility=1-Mathf.InverseLerp(.22f,.9f,b.size.y);
            foreach (int i in component.Vertices)
            {
                payload[i] = new Vector4(0, 1, phase, 0);
                chipPivots[i]=new Vector4(pivot.x,pivot.y,pivot.z,agility);
            }
            report.ChipComponents++; report.ChipVertices += component.Vertices.Count;
        }
        var relics = new Vector4[source.Length];
        BakeRelics(components, metrePositions, main, payload, relics, report);
        mesh.SetUVs(1, new List<Vector4>(payload));
        mesh.SetUVs(2, new List<Vector4>(relics));
        mesh.SetUVs(3, new List<Vector4>(chipPivots));
        report.Meshes++;
    }

    static bool Inside(Bounds b,float x,float y,float halfX,float halfY,float front,float back)
        => b.min.x>=x-halfX && b.max.x<=x+halfX && b.min.y>=y-halfY &&
           b.max.y<=y+halfY && b.min.z>=front && b.max.z<=back;

    static void BakeRelics(Dictionary<int,Component> components,Vector3[] positions,bool main,
        Vector4[] motion,Vector4[] relics,BakeReport report)
    {
        // Connected-component selection avoids illuminating a piece of a flag
        // frame or structural arch just because it passes near a medallion.
        foreach(var component in components.Values)
        {
            if(component.Flags!=GoldFlag)continue;
            var b=component.Bounds;
            Vector2 centre=Vector2.zero;float kind=0,gain=0;
            if(main)
            {
                if(Inside(b,0,-1.12f,1.10f,1.11f,3.02f,3.30f))
                {
                    float outer=0;
                    foreach(int i in component.Vertices)
                        outer=Mathf.Max(outer,new Vector2(positions[i].x,positions[i].y+1.12f).magnitude);
                    // The solid crescent is not a glowing plate. Only its
                    // surrounding circular strokes and radial ticks qualify.
                    if(outer>.64f){kind=1;gain=1;centre=new Vector2(0,-1.12f);}
                }
                if(kind==0 && Inside(b,0,-2.85f,.38f,.38f,3.02f,3.35f))
                {kind=2;gain=.62f;centre=new Vector2(0,-2.85f);}
                foreach(float x in new[]{-6f,6f})
                {
                    if(kind==0 && Inside(b,x,-2.10f,.30f,.31f,2.95f,3.32f))
                    {kind=2;gain=.36f;centre=new Vector2(x,-2.10f);}
                    if(kind==0 && Inside(b,x,-1.55f,.20f,1.45f,2.88f,3.35f))
                    {kind=3;gain=.30f;centre=new Vector2(x,-2.10f);}
                }
                foreach(float x in new[]{-9f,9f})
                    if(kind==0 && Inside(b,x,-1.30f,.31f,.31f,2.88f,3.35f))
                    {kind=2;gain=.25f;centre=new Vector2(x,-1.30f);}
                if(kind==0 && Inside(b,0,-3.05f,.16f,1.05f,2.98f,3.40f))
                {kind=3;gain=.60f;centre=new Vector2(0,-2.85f);}
            }
            else
            {
                if(Inside(b,0,-.76f,.57f,.58f,1.84f,2.12f))
                {kind=2;gain=.46f;centre=new Vector2(0,-.76f);}
                if(kind==0 && Inside(b,0,-1.82f,.18f,.61f,1.80f,2.15f))
                {kind=3;gain=.34f;centre=new Vector2(0,-.76f);}
            }
            if(kind==0)continue;
            float phase=Mathf.Repeat(centre.x*.71f+centre.y*.36f,Mathf.PI*2);
            foreach(int i in component.Vertices)
            {
                var data=motion[i];data.w=kind;motion[i]=data;
                relics[i]=new Vector4(positions[i].x-centre.x,positions[i].y-centre.y,gain,phase);
                report.RelicVertices++;
                if(kind==1)report.FocusVertices++;else if(kind==2)report.PendantVertices++;else report.ChainVertices++;
            }
        }
        // One real floating component per source mesh. The upper mesh is
        // instanced twice, yielding only three red-fissure stones in the arena.
        Component selected=null;float distance=float.MaxValue,targetX=main?7.3f:-1.1f;
        foreach(var component in components.Values)
        {
            bool chip=false;
            foreach(int i in component.Vertices)if(motion[i].y>0){chip=true;break;}
            if(!chip || component.Flags!=BasaltFlag)continue;
            float d=Mathf.Abs(component.Bounds.center.x-targetX);
            if(d<distance){selected=component;distance=d;}
        }
        if(selected==null)return;
        var bounds=selected.Bounds;float seed=Mathf.Repeat(bounds.center.x*.73f+bounds.center.y*1.21f,Mathf.PI*2);
        foreach(int i in selected.Vertices)
        {
            var data=motion[i];data.w=4;motion[i]=data;
            relics[i]=new Vector4((positions[i].x-bounds.center.x)/Mathf.Max(.01f,bounds.size.x),
                (positions[i].y-bounds.center.y)/Mathf.Max(.01f,bounds.size.y),.52f,seed);
            report.RelicVertices++;report.GlowingChipVertices++;
        }
        report.GlowingChipComponents++;
    }
}
