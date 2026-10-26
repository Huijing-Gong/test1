using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Read-only authored arena survey. Does not flatten terrain or move other content.
public static class SentinelArenaChecks
{
    public static void Run()
    {
        if(!Application.dataPath.StartsWith("/private/tmp/",StringComparison.Ordinal))throw new InvalidOperationException("Use disposable QA project.");
        EditorSceneManager.OpenScene("Assets/_Demo/Scenes/OverworldBaseline.unity");
        var knight=UnityEngine.Object.FindFirstObjectByType<BishopKnightAI>();
        if(!knight)throw new InvalidOperationException("Missing authored sentinel");
        var query=new EnemyGroundQuery(knight.transform);var report=new StringBuilder();
        Vector3 centre=knight.transform.position;
        report.AppendLine("Authored sentinel arena read-only physical survey at "+centre);
        int safe=0,total=0;float min=float.PositiveInfinity,max=float.NegativeInfinity;
        for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++)
        {
            if(x*x+z*z>20)continue;total++;
            Vector3 p=centre+new Vector3(x,0,z);
            if(!query.TrySample(p,3,4,35,out RaycastHit floor)) {report.AppendLine("UNSUPPORTED "+p);continue;}
            p=floor.point;bool blocked=false;
            foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.55f,p+Vector3.up*1.45f,.3f,~0,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(knight.transform) || query.AllowsSupportContact(c,p))continue;
                blocked=true;report.AppendLine("BLOCKED "+p+" "+c.name);break;
            }
            if(!blocked)safe++;
            min=Mathf.Min(min,p.y);max=Mathf.Max(max,p.y);
        }
        report.AppendLine("Grounded clear samples: "+safe+" / "+total+"; height span: "+(max-min).ToString("F3")+" m");
        string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"LocalDocs/Enemies/SilverSentinelSkills-20261025/Runtime");
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"arena-survey.txt"),report.ToString());
        Debug.Log(report.ToString());EditorApplication.Exit(0);
    }
}
