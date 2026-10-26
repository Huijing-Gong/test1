using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SideWorldSetup
{
    [MenuItem("ArtLab/Side Worlds/Install test entrances")]
    public static void Install()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save the scene and exit Play Mode before installation.");
        ConfigureTags();
        foreach (string path in new[] { "Assets/_Demo/Scenes/GameplayFeatureDemoScene.unity", "Assets/StartScene.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path);
            if (GameObject.Find("SideWorldTravel")) throw new InvalidOperationException("Entrances already installed in " + path);
            new GameObject("SideWorldTravel").AddComponent<SideWorldTravel>();
            CreateNPC(false, new Vector3(-7f, 0.12f, -11f));
            CreateNPC(true, new Vector3(-7f, 0.12f, -5f));
            EditorSceneManager.SaveScene(scene);
        }
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s=>s.path==SideWorldTravel.BattlePath)) scenes.Add(new EditorBuildSettingsScene(SideWorldTravel.BattlePath,true));
        else for(int i=0;i<scenes.Count;i++) if(scenes[i].path==SideWorldTravel.BattlePath) scenes[i].enabled=true;
        if (!scenes.Any(s=>s.path==SideWorldTravel.SideGameBPath)) scenes.Add(new EditorBuildSettingsScene(SideWorldTravel.SideGameBPath,true));
        else for(int i=0;i<scenes.Count;i++) if(scenes[i].path==SideWorldTravel.SideGameBPath) scenes[i].enabled=true;
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("SIDE_WORLD_INSTALL_PASS: two guides and native A/B scenes registered.");
    }
    private static void ConfigureTags()
    {
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var list=tags.FindProperty("tags");
        if (!Enumerable.Range(0,list.arraySize).Any(i=>list.GetArrayElementAtIndex(i).stringValue=="Enemy"))
        { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize-1).stringValue="Enemy"; }
        var layers=tags.FindProperty("layers");
        for(int i=6;i<=7;i++)
        {
            string name=i==6?"PlayerCombat":"EnemyCombat";
            var p=layers.GetArrayElementAtIndex(i);
            if(!string.IsNullOrEmpty(p.stringValue)&&p.stringValue!=name) throw new InvalidOperationException("Layer conflict: "+i);
            p.stringValue=name;
        }
        tags.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void CreateNPC(bool second, Vector3 position)
    {
        var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/NPC/IntroNPC.prefab");
        var npc=(GameObject)PrefabUtility.InstantiatePrefab(template);
        npc.name=second?"SideWorldB_Guide":"SideWorldA_Guide";
        npc.transform.position=position;
        npc.transform.rotation=Quaternion.Euler(0,90,0);
        UnityEngine.Object.DestroyImmediate(npc.GetComponent<DialogueReward>());
        var dialogue=npc.GetComponent<NPCDialogue>();
        var so=new SerializedObject(dialogue);
        so.FindProperty("useSwordRewardDialogue").boolValue=false;
        so.FindProperty("npcName").stringValue=second?"Runner Guide":"Arena Guide";
        so.FindProperty("interactionPrompt").stringValue=second?"[E] Side World B - Parkour":"[E] Side World A - Platform Fighter";
        var lines=so.FindProperty("dialogueLines"); lines.arraySize=2;
        lines.GetArrayElementAtIndex(0).stringValue=second?"Enter the team's parkour trial and reach the final portal to complete Side World B.":"Enter a platform duel. Build damage and knock your opponent out of the arena.";
        lines.GetArrayElementAtIndex(1).stringValue=second?"Press E to enter, or Esc to stay. B returns here at any time; claim your reward after winning.":"Press E to enter, or Esc to stay. B returns here at any time; R retries after a round.";
        so.ApplyModifiedPropertiesWithoutUndo();
        npc.AddComponent<SideWorldEntrance>().isSideWorldB=second;
        var sign=new GameObject("Entrance label"); sign.transform.SetParent(npc.transform,false);
        sign.transform.localPosition=new Vector3(0,2.5f,0);
        sign.transform.rotation=Quaternion.identity;
        var text=sign.AddComponent<TextMesh>(); text.text=second?"SIDE WORLD B\nPARKOUR":"SIDE WORLD A\nPLATFORM FIGHTER";
        text.characterSize=0.075f;text.fontSize=48;text.anchor=TextAnchor.MiddleCenter;
        text.color=second?new Color(1f,.8f,.3f):new Color(.35f,.9f,1f);
    }
    [MenuItem("ArtLab/Side Worlds/Build WebGL")]
    public static void Build()
    {
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
            locationPathName=Path.GetFullPath("Builds/SideWorldsWebGL"),target=BuildTarget.WebGL,options=BuildOptions.None });
        Directory.CreateDirectory("ToolReports/SideWorldIntegration");
        File.WriteAllText("ToolReports/SideWorldIntegration/build-result.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nBytes: "+report.summary.totalSize);
        if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("WebGL build failed.");
    }
}
