// Imported standalone authoring tools: opt in only in a dedicated B authoring project.
#if UNITY_EDITOR && SIDE_GAME_B_AUTHORING
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GameControlsTutorialInstaller
{
    private const string RootName="Game Controls Tutorial";
    static GameControlsTutorialInstaller(){EditorApplication.delayCall+=Install;EditorApplication.update+=InstallAfterPlayMode;}
    private static void InstallAfterPlayMode(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;Install();EditorApplication.update-=InstallAfterPlayMode;}
    [MenuItem("Pixel Runner/Add Game Controls Tutorial")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
        Scene scene=SceneManager.GetActiveScene(); if(!scene.IsValid()||scene.name!="PlayerTest")return;
        GameObject go=scene.GetRootGameObjects().FirstOrDefault(x=>x.name==RootName);
        bool created=go==null;
        if(created){go=new GameObject(RootName);go.AddComponent<GameControlsTutorial>();}
        GameControlsTutorial tutorial=go.GetComponent<GameControlsTutorial>();
        Component legacyVideoPlayer=go.GetComponent("VideoPlayer");
        if(legacyVideoPlayer!=null)Object.DestroyImmediate(legacyVideoPlayer);
        if(created)Undo.RegisterCreatedObjectUndo(go,"Add Game Controls Tutorial");
        EditorUtility.SetDirty(tutorial);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
}
#endif

#endif
