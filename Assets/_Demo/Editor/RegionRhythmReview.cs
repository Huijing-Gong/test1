using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Reflection;
public static class RegionRhythmReview
{
 public const string Dir="Docs/RegionRhythm/";
 static Vector3[] points={new Vector3(0,1.06f,-67),new Vector3(-52,1.06f,-30),new Vector3(3,1.06f,-7),new Vector3(58,3.06f,4),new Vector3(20,1.06f,38)};
 static float[] yaws={0,0,0,-45,-38};
 static string[] names={"spawn","west","island","east","tower"};
 public static void Capture(string phase)
 {
  Directory.CreateDirectory(Dir);var w=Object.FindFirstObjectByType<BaselineWorld>();var cc=w.player.GetComponent<CharacterController>();var orbit=Object.FindFirstObjectByType<ThirdPersonCamera>();int i=-1;double last=0;
  EditorApplication.CallbackFunction tick=null;tick=()=>{if(!EditorApplication.isPlaying){EditorApplication.update-=tick;return;}double now=EditorApplication.timeSinceStartup;if(now-last<1.5)return;
   if(i>=0){OverworldExpansionChecks.Shot("rhythm-"+phase+"-"+names[i]);File.Copy("ToolReports/MapExpansion/rhythm-"+phase+"-"+names[i]+".png",Dir+phase+"-"+names[i]+".png",true);var cam=Camera.main;File.AppendAllText(Dir+phase+"-views.txt",names[i]+" player="+w.player.position+" camera="+cam.transform.position+" rotation="+cam.transform.eulerAngles+" distance="+Vector3.Distance(cam.transform.position,w.player.position+Vector3.up*1.6f)+"\n");}
   i++;if(i==points.Length){EditorApplication.update-=tick;EditorApplication.isPlaying=false;return;}cc.enabled=false;w.player.position=points[i];cc.enabled=true;Physics.SyncTransforms();typeof(ThirdPersonCamera).GetField("yaw",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(orbit,yaws[i]);typeof(ThirdPersonCamera).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(orbit,12f);orbit.SetLookInputLocked(true);last=now;
  };EditorApplication.update+=tick;
 }
}
