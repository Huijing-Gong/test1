using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

// Opens the normal title without starting a journey or altering save data.
[InitializeOnLoad]
public static class ResponsiveOpenTitle
{
    const string Request="Temp/open-initial-title.txt";
    static ResponsiveOpenTitle(){EditorApplication.update+=()=>{
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request);
        SessionState.SetBool("HeroQA.Enabled",false);SessionState.SetBool("ResponsiveSword.PlayCheck",false);
        EditorApplication.isPaused=false;
        EditorSceneManager.OpenScene("Assets/_Demo/Scenes/OverworldBaseline.unity");
        EditorApplication.isPlaying=true;
    };}
    [MenuItem("Tools/Hero/Open initial game title")]
    static void Open(){File.WriteAllText(Request,"open");}
}
