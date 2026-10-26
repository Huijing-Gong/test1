using UnityEditor;
using UnityEngine;

public static class EclipsePreview
{
    [MenuItem("ArtLab/Eclipse/Capture lit roadside lamp")]
    static void CaptureLamp()
    {
        var camera=Camera.main;
        Light lamp=null;
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(light.name=="Eclipse warm lantern light" && light.enabled &&
                (!lamp || (camera && Vector3.Distance(light.transform.position,camera.transform.position)<Vector3.Distance(lamp.transform.position,camera.transform.position)))) lamp=light;
        if(!camera || !lamp) { Debug.LogWarning("Preview total eclipse first to capture a lit lamp."); return; }
        var previousTarget=camera.targetTexture; var position=camera.transform.position;
        var rotation=camera.transform.rotation; float fov=camera.fieldOfView;
        var active=RenderTexture.active;
        var target=RenderTexture.GetTemporary(960,720,24);
        var image=new Texture2D(960,720,TextureFormat.RGB24,false);
        try
        {
            var focus=lamp.transform.position;
            camera.transform.SetPositionAndRotation(focus+new Vector3(2.4f,.6f,-3.8f),Quaternion.LookRotation(new Vector3(-2.4f,-.9f,3.8f)));
            camera.fieldOfView=50; camera.targetTexture=target; camera.Render();
            RenderTexture.active=target; image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply();
            string path=System.IO.Path.Combine(Application.temporaryCachePath,"eclipse-lit-lamp.png");
            System.IO.File.WriteAllBytes(path,image.EncodeToPNG());
            Debug.Log("Eclipse lamp capture: "+path+"; intensity="+lamp.intensity);
        }
        finally
        {
            camera.targetTexture=previousTarget; camera.fieldOfView=fov; camera.transform.SetPositionAndRotation(position,rotation);
            RenderTexture.active=active; RenderTexture.ReleaseTemporary(target); Object.Destroy(image);
        }
    }
    [MenuItem("ArtLab/Eclipse/Preview first seal")]
    static void First() => Preview(1);
    [MenuItem("ArtLab/Eclipse/Preview total eclipse and tower")]
    static void Total() => Preview(2);
    [MenuItem("ArtLab/Eclipse/Preview daylight")]
    static void Day() => Preview(0);
    static void Preview(int stage)
    {
        var sequence=Object.FindFirstObjectByType<BaselineEclipseSequence>();
        if(!Application.isPlaying || !sequence)
        { Debug.LogWarning("Press Play and continue into the overworld first. Eclipse previews do not modify your save."); return; }
        sequence.PreviewStage(stage);
    }
    [MenuItem("ArtLab/Eclipse/Validate shader and save compatibility")]
    static void Validate()
    {
        var shader=Resources.Load<Shader>("ProgressiveEclipse");
        if(!shader || ShaderUtil.ShaderHasError(shader)) throw new System.Exception("Eclipse shader failed to import.");
        var oldSave=JsonUtility.FromJson<BaselineSaveData>("{\"version\":4,\"trialACompleted\":true,\"trialBCompleted\":true}");
        if(BaselineEclipseSequence.CompletedStage(oldSave)!=2 || oldSave.eclipsePresentedStage!=0)
            throw new System.Exception("Legacy eclipse progress migration failed.");
        oldSave.eclipsePresentedStage=2;
        var restored=JsonUtility.FromJson<BaselineSaveData>(JsonUtility.ToJson(oldSave));
        if(restored.eclipsePresentedStage!=2 || !restored.trialACompleted || !restored.trialBCompleted)
            throw new System.Exception("Eclipse save round-trip failed.");
        Debug.Log("Eclipse validation passed: imported shader, legacy save compatibility and completed-cutscene round-trip.");
    }
}
