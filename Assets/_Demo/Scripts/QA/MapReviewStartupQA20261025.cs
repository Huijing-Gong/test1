using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in Windows QA build only. No input, flow mutation, or save API calls.
public sealed class MapReviewStartupQA20261025 : MonoBehaviour
{
    const string Root = @"C:\Users\kenny\Documents\GitHub\2026s2-project-1-kingofgraphic\output\MapAssetProduction20261024\WindowsStartupQA20261025";
    static bool created;
    string folder;
    float began;
    bool finished, oldBackground;
    int errors, exceptions, assertions;
    int titleRenderCallbacks; bool offscreenRequest;
    readonly System.Collections.Generic.List<string> errorMessages = new System.Collections.Generic.List<string>();
    Receipt receipt = new Receipt();

    [Serializable] sealed class Receipt
    {
        public string startUTC, endUTC, outcome, reason, scene, loadedScenes, company, product, unityVersion;
        public int frame, renderers, enabledActiveRenderers, colliders, enabledActiveColliders;
        public string mainCamera, presentationCamera, presentationTargetTexture, presentationPixelRect, rtPath, rtFailure;
        public bool titleVisible, presentationCameraEnabled, presentationCameraActive, screenCapturePass, offscreenCameraPass;
        public int presentationTargetDisplay, presentationCullingMask, titleNormalRenderCallbacks;
        public bool[] screenshotNonBlack;
        public string rtScope = "Additional offscreen camera-only render; excludes OnGUI; never substitutes for actual ScreenCapture PASS.";
        public bool baselineFlowFound, baselineFlowStarted, runInBackground, originalRunInBackground;
        public int errors, exceptions, assertions;
        public string[] errorMessages, screenshots;
        public long[] screenshotBytes;
        public string scope = "Opt-in standalone main-menu startup/render capture only; no gameplay, native-input, performance, or save acceptance.";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (created || Application.companyName != "KingOfGraphic.MapReviewQA" || Application.productName != "Map Review 20261025") return;
        bool enabled = false;
        foreach (string arg in Environment.GetCommandLineArgs())
            if (string.Equals(arg, "--map-review-startup-qa", StringComparison.Ordinal)) enabled = true;
        if (!enabled) return;
        created = true;
        var go = new GameObject("MapReviewStartupQA20261025_OptIn");
        DontDestroyOnLoad(go);
        go.AddComponent<MapReviewStartupQA20261025>();
    }

    void Awake()
    {
        began = Time.realtimeSinceStartup;
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnCameraRendered;
        receipt.startUTC = DateTime.UtcNow.ToString("O");
        receipt.company = Application.companyName; receipt.product = Application.productName;
        receipt.unityVersion = Application.unityVersion;
        receipt.originalRunInBackground = oldBackground;
        try {
            folder = Path.Combine(Root, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "started.txt"), receipt.startUTC);
            StartCoroutine(Run());
        } catch (Exception e) { Finish(false, "Output initialization failed: " + e); }
    }
    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error) errors++;
        else if (type == LogType.Exception) exceptions++;
        else if (type == LogType.Assert) assertions++;
        else return;
        if (errorMessages.Count < 100) errorMessages.Add(type + ": " + message + "\n" + trace);
    }
    void Update()
    {
        if (!finished && Time.realtimeSinceStartup - began >= 60f)
            Finish(false, "60-second unscaled startup/capture timeout");
    }
    IEnumerator Run()
    {
        Scene world;
        do {
            world = SceneManager.GetSceneByName("OverworldBaseline");
            if (!world.IsValid() || !world.isLoaded) yield return null;
        } while (!finished && (!world.IsValid() || !world.isLoaded));
        if (finished) yield break;
        float loadedAt = Time.realtimeSinceStartup;
        int loadedFrame = Time.frameCount;
        while (!finished && (Time.frameCount - loadedFrame < 10 || Time.realtimeSinceStartup - loadedAt < 5f)) yield return null;
        if (finished) yield break;
        float renderWait = Time.realtimeSinceStartup;
        while (!finished && titleRenderCallbacks < 2 && Time.realtimeSinceStartup-renderWait < 10f) yield return null;
        if (finished) yield break;
        Snapshot();
        receipt.screenshots = new string[2]; receipt.screenshotBytes = new long[2]; receipt.screenshotNonBlack = new bool[2];
        for (int i=0; i<2; i++) {
            yield return null;
            if (finished) yield break;
            string path=Path.Combine(folder,"startup-"+(i+1)+".png"); receipt.screenshots[i]=path;
            ScreenCapture.CaptureScreenshot(path);
            long prior=-1; int stable=0; float captureAt=Time.realtimeSinceStartup;
            while (!finished && stable<3 && Time.realtimeSinceStartup-captureAt<8f) {
                yield return null;
                long size=ValidPNGSize(path); if (size>8 && size==prior) stable++; else stable=0; prior=size;
            }
            if (finished) yield break;
            receipt.screenshotBytes[i]=stable>=3?prior:0;
            receipt.screenshotNonBlack[i]=stable>=3 && PNGNonBlack(path);
            float next=Time.realtimeSinceStartup+1f; while (!finished && Time.realtimeSinceStartup<next) yield return null;
        }
        if (finished) yield break;
        Snapshot();
        // Freeze screen verdict BEFORE the explicitly separate render request.
        receipt.screenCapturePass=receipt.titleNormalRenderCallbacks>=2 && receipt.screenshotNonBlack[0] && receipt.screenshotNonBlack[1];
        CaptureOffscreenDiagnostic();
        bool state=receipt.titleVisible && receipt.presentationCameraEnabled && receipt.presentationCameraActive && receipt.baselineFlowFound && !receipt.baselineFlowStarted;
        Finish(state && receipt.screenCapturePass && errors+exceptions+assertions==0,
            "Screen verdict requires natural title-camera render callbacks and two nonblack ScreenCaptures. Offscreen result is separate diagnostic evidence only.");
    }
    Camera TitleCamera()
    {
        var title=FindFirstObjectByType<CloudGateTitleScreen>(FindObjectsInactive.Include);
        return title ? title.PresentationCamera : null;
    }
    void OnCameraRendered(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (!offscreenRequest && camera && camera==TitleCamera()) titleRenderCallbacks++;
    }
    static bool PNGNonBlack(string path)
    {
        Texture2D image=null;
        try {
            image=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if (!ImageConversion.LoadImage(image,File.ReadAllBytes(path),false)) return false;
            foreach (Color32 c in image.GetPixels32()) if (c.r>2 || c.g>2 || c.b>2) return true;
            return false;
        } catch { return false; }
        finally { if(image) Destroy(image); }
    }
    void CaptureOffscreenDiagnostic()
    {
        RenderTexture rt=null; Texture2D image=null; RenderTexture previous=RenderTexture.active;
        try {
            Camera camera=TitleCamera(); if(!camera) { receipt.rtFailure="No title presentation camera"; return; }
            rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32); rt.Create();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest(); request.destination=rt;
            if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera,request)) { receipt.rtFailure="Pipeline does not support StandardRequest"; return; }
            offscreenRequest=true;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=rt; image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
            receipt.rtPath=Path.Combine(folder,"ADDITIONAL-offscreen-title-camera-NO-OnGUI.png");
            File.WriteAllBytes(receipt.rtPath,image.EncodeToPNG());
            receipt.offscreenCameraPass=PNGNonBlack(receipt.rtPath);
        } catch(Exception e) { receipt.rtFailure=e.ToString(); }
        finally { offscreenRequest=false; RenderTexture.active=previous; if(image) Destroy(image); if(rt) {rt.Release();Destroy(rt);} }
    }
    static long ValidPNGSize(string path)
    {
        try {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                if (stream.Length <= 8) return -1;
                byte[] h = new byte[8]; if (stream.Read(h, 0, 8) != 8) return -1;
                if (h[0]!=137 || h[1]!=80 || h[2]!=78 || h[3]!=71 || h[4]!=13 || h[5]!=10 || h[6]!=26 || h[7]!=10) return -1;
                stream.Seek(-12, SeekOrigin.End);
                byte[] tail = new byte[12]; if (stream.Read(tail,0,12)!=12) return -1;
                if (tail[0]!=0 || tail[1]!=0 || tail[2]!=0 || tail[3]!=0 || tail[4]!=73 || tail[5]!=69 || tail[6]!=78 || tail[7]!=68) return -1;
                return stream.Length;
            }
        } catch (IOException) { return -1; }
    }
    void Snapshot()
    {
        receipt.scene = SceneManager.GetActiveScene().path;
        var names = new System.Collections.Generic.List<string>();
        for (int i=0; i<SceneManager.sceneCount; i++) names.Add(SceneManager.GetSceneAt(i).path);
        receipt.loadedScenes = string.Join(" | ", names.ToArray());
        receipt.frame = Time.frameCount; receipt.runInBackground = Application.runInBackground;
        Renderer[] rs = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Collider[] cs = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        receipt.renderers = rs.Length; receipt.colliders = cs.Length;
        receipt.enabledActiveRenderers=0; receipt.enabledActiveColliders=0;
        foreach (var r in rs) if (r.enabled && r.gameObject.activeInHierarchy) receipt.enabledActiveRenderers++;
        foreach (var c in cs) if (c.enabled && c.gameObject.activeInHierarchy) receipt.enabledActiveColliders++;
        receipt.mainCamera = Camera.main ? Camera.main.name : "<none>";
        var title=FindFirstObjectByType<CloudGateTitleScreen>(FindObjectsInactive.Include);
        var presentation=title ? title.PresentationCamera : null;
        receipt.titleVisible=title && title.Visible;
        receipt.presentationCamera=presentation?presentation.name:"<none>";
        receipt.presentationCameraEnabled=presentation && presentation.enabled;
        receipt.presentationCameraActive=presentation && presentation.gameObject.activeInHierarchy;
        receipt.titleNormalRenderCallbacks=titleRenderCallbacks;
        if(presentation) { receipt.presentationTargetTexture=presentation.targetTexture?presentation.targetTexture.name:"<screen>"; receipt.presentationTargetDisplay=presentation.targetDisplay; receipt.presentationCullingMask=presentation.cullingMask; receipt.presentationPixelRect=presentation.pixelRect.ToString(); }
        BaselineFlow flow = FindFirstObjectByType<BaselineFlow>(FindObjectsInactive.Include);
        receipt.baselineFlowFound = flow != null; receipt.baselineFlowStarted = flow != null && flow.Started;
    }
    void Finish(bool success, string reason)
    {
        if (finished) return;
        finished = true;
        receipt.endUTC = DateTime.UtcNow.ToString("O"); receipt.outcome=success?"PASS_STARTUP_CAPTURE_ONLY":"FAIL"; receipt.reason=reason;
        receipt.errors=errors; receipt.exceptions=exceptions; receipt.assertions=assertions; receipt.errorMessages=errorMessages.ToArray();
        try { if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(receipt,true)); }
        catch (Exception e) { Debug.LogError("MapReviewStartupQA receipt failed: " + e); success=false; }
        Application.logMessageReceived -= OnLog; UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnCameraRendered;
        Application.runInBackground=oldBackground;
        Application.Quit(success?0:2);
    }
    void OnDestroy() { Application.logMessageReceived -= OnLog; UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnCameraRendered; Application.runInBackground=oldBackground; }
}
