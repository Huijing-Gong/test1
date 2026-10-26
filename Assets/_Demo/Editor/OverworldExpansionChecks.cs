using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class OverworldExpansionChecks
{
    const string Dir="ToolReports/MapExpansion/";
    public static void Geometry()
    {
        Physics.SyncTransforms();var w=UnityEngine.Object.FindFirstObjectByType<BaselineWorld>();w.RefreshNavigation();var lines=new List<string>();
        foreach(var e in w.routeEdges)if(!w.NavigationSegment(w.routeNodes[e.x],w.routeNodes[e.y]))lines.Add("INVALID EDGE "+e.x+" "+e.y+" "+w.routeNodes[e.x]+" -> "+w.routeNodes[e.y]);
        foreach(var p in new[]{new Vector3(0,1,-67),new Vector3(-51,1,-10),new Vector3(3,1,-4),new Vector3(58,3,4),new Vector3(-49,1,19),new Vector3(57,1,-26)})
            for(int i=0;i<3;i++){var route=w.WalkingRoute(p,w.objectives[i].position);lines.Add("ROUTE "+p+" task="+i+" samples="+route.Length);}
        int missing=0;foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        lines.Add("Missing scripts="+missing);
        foreach(var a in UnityEngine.Object.FindObjectsByType<BaselineInteraction>(FindObjectsSortMode.None))
        {
            var terrain=GameObject.Find("LakeTerrain_REPLACE_OR_SCULPT").GetComponent<Collider>();terrain.Raycast(new Ray(a.transform.position+Vector3.up*30,Vector3.down),out var hit,60);
            lines.Add("GROUND "+a.name+" rootY="+a.transform.position.y.ToString("F2")+" terrainY="+hit.point.y.ToString("F2")+" delta="+(a.transform.position.y-hit.point.y).ToString("F2"));
        }
        File.WriteAllLines(Dir+"geometry.txt",lines);Debug.Log(string.Join("\n",lines));
    }
    public static void Shot(string name)
    {
        Capture(Camera.main,Dir+name+".png");
        File.AppendAllText(Dir+"captures.txt",name+": game camera offscreen render; controlled placement; camera="+Camera.main.transform.position+" rotation="+Camera.main.transform.eulerAngles+"\n");
    }
    static void Capture(Camera cam,string path)
    {
        var old=cam.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1600,1000,24);var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally{cam.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
    public static void Overview()
    {
        var g=new GameObject("Temporary overview camera");var cam=g.AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=92;cam.farClipPlane=300;cam.transform.position=new Vector3(0,170,0);cam.transform.rotation=Quaternion.Euler(90,0,0);cam.backgroundColor=new Color(.7f,.8f,.85f);cam.clearFlags=CameraClearFlags.SolidColor;
        var labels=new List<GameObject>();
        try
        {
            string[] names={"START / GUIDE / 2 CLUES","WEST GROVE / TASK","ISLAND / TASK","EAST RUINS / TASK","SIDE A","SIDE B","TOWER"};
            Vector2[] pos={new Vector2(0,-68),new Vector2(-53,-9),new Vector2(0,4),new Vector2(52,18),new Vector2(-50,25),new Vector2(59,-25),new Vector2(0,66)};
            for(int i=0;i<names.Length;i++){var label=new GameObject("Temporary label");labels.Add(label);label.transform.position=new Vector3(pos[i].x,35,pos[i].y);label.transform.rotation=Quaternion.Euler(90,0,0);var tm=label.AddComponent<TextMesh>();tm.text=names[i];tm.anchor=TextAnchor.MiddleCenter;tm.fontSize=64;tm.characterSize=.48f;tm.color=Color.black;}
            Capture(cam,Dir+"overview.png");
        }
        finally{foreach(var l in labels)UnityEngine.Object.DestroyImmediate(l);UnityEngine.Object.DestroyImmediate(g);}
        File.AppendAllText(Dir+"captures.txt","overview: temporary orthographic camera offscreen render with temporary annotation labels; not Scene View\n");
    }
}
