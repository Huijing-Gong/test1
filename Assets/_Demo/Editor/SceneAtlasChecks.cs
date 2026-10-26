using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Read-only runtime checks. No scene edits, teleports or save writes.
public static class SceneAtlasChecks
{
    [MenuItem("Tools/Map/Preview saved journey map")]
    public static void Preview()
    {
        var flow = Object.FindFirstObjectByType<BaselineFlow>();
        if (!Application.isPlaying || !flow) throw new InvalidOperationException("Run the overworld first.");
        if (!flow.Started && !flow.ContinueGame()) throw new InvalidOperationException("No saved journey; start the game normally.");
        flow.ShowMap();
    }

    [MenuItem("Tools/Map/Validate scene atlas %#&m")]
    public static void Validate()
    {
        var map = Object.FindFirstObjectByType<BaselineMap>();
        if (!Application.isPlaying || !map) throw new InvalidOperationException("Run the overworld first.");
        var camera = map.GetComponentInChildren<Camera>();
        var layout = map.GetComponent<OverworldV2Layout>();
        var texture = map.TerrainTexture as RenderTexture;
        if (!camera || !texture || !texture.IsCreated()) throw new InvalidOperationException("Atlas has not rendered.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var guiMethod = typeof(BaselineMap).GetMethod("MapGuiPoint", flags);
        var mini = Object.FindFirstObjectByType<BaselineMinimap>();
        var miniMethod = typeof(BaselineMinimap).GetMethod("LocalPoint", flags);
        var world = map.GetComponent<BaselineWorld>();
        float radius = (float)typeof(BaselineMinimap).GetField("visibleWorldRadius", flags).GetValue(mini);
        Vector2 viewCenterUV = (Vector2)typeof(BaselineMinimap).GetField("viewCenterUV", flags).GetValue(mini);
        float maxError = 0f;
        int count = 0;
        void Check(Vector3 p)
        {
            Vector2 actual = camera.WorldToViewportPoint(p);
            Vector2 uv = map.WorldToAtlasUV(p);
            maxError = Mathf.Max(maxError, Vector2.Distance(actual, uv) * texture.width);
            var gui = (Vector2)guiMethod.Invoke(map, new object[] { new Rect(20,30,338,338), p });
            var expectedGUI = new Vector2(20 + actual.x * 338, 30 + (1-actual.y) * 338);
            maxError = Mathf.Max(maxError, Vector2.Distance(gui, expectedGUI));
            var local = (Vector2)miniMethod.Invoke(mini, new object[] { p });
            var expectedLocal = (actual-viewCenterUV) * (190f * camera.orthographicSize / radius);
            maxError = Mathf.Max(maxError, Vector2.Distance(local, expectedLocal));
            count++;
        }
        // Entire playable square, at water level and at elevated tower height.
        Vector2 center = layout ? layout.MapCenter : Vector2.zero;
        float extent = layout ? layout.MapHalfExtent : 85;
        for (int x=0;x<=20;x++) for (int z=0;z<=20;z++)
        {
            Check(new Vector3(center.x-extent+x*extent/10, 0, center.y-extent+z*extent/10));
            Check(new Vector3(center.x-extent+x*extent/10, 60, center.y-extent+z*extent/10));
        }
        foreach (var station in Object.FindObjectsByType<BaselineWaystation>(FindObjectsSortMode.None)) Check(station.InteractionPosition);
        foreach (var entrance in Object.FindObjectsByType<SideWorldEntrance>(FindObjectsSortMode.None)) Check(entrance.transform.position);
        Check(map.TowerLandmarkPosition);
        if (layout)
        {
            Check(layout.LakePosition); Check(layout.IslandRestPosition); Check(layout.CampPosition);
            foreach (var surface in layout.MapSurfaces)
                if (surface && surface.sharedMesh)
                    foreach (var vertex in surface.sharedMesh.vertices) Check(surface.transform.TransformPoint(vertex));
        }
        string folder = "Library/MapValidation";
        Directory.CreateDirectory(folder);
        string report = $"Samples: {count}\nMaximum projection error (pixels): {maxError:F6}\n" +
            "Checked orthographic camera against full-map and edge-clamped minimap mapping, including height invariance.\n";
        File.WriteAllText(folder+"/projection.txt", report);
        var previous = RenderTexture.active;
        var snapshot = new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active = texture;
            snapshot.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
            snapshot.Apply();
            File.WriteAllBytes(folder+"/scene-atlas.png",snapshot.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(snapshot); }
        if (maxError > .05f) throw new InvalidOperationException(report);
        Debug.Log("Scene atlas PASS\n"+report);
    }
}
