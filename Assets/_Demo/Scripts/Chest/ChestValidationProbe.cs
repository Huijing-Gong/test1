using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Opt-in rendered-player validation. Open the WebGL URL with ?chestqa=1.
/// ?chestinteraction=1 only places the active player near the chest for real E-input checks.
/// Normal game sessions do not instantiate this component or alter their camera/input.
/// </summary>
public sealed class ChestValidationProbe : MonoBehaviour
{
    private const float PanelHeight = 156f;
    private ChestDissolveController chest;
    private Renderer[] renderers;
    private ShadowCastingMode[] originalShadowModes;
    private bool[] originalEnabled;
    private bool running;
    private string status = "Ready. Run checks to compare actual colour and shadow pixels.";
    private string metrics = "";
    private GUIStyle labelStyle;
    private GUIStyle resultStyle;
    private Color32[] captured;
    private int captureWidth;
    private int captureHeight;
    private string captureError;
    private bool interactionOnly;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateWhenRequested()
    {
        if (Query("chestqa") != "1" && Query("chestinteraction") != "1"
            && Array.IndexOf(Environment.GetCommandLineArgs(), "-chestqa") < 0) return;
        new GameObject("Chest Validation (QA only)").AddComponent<ChestValidationProbe>();
    }

    private void Awake() => interactionOnly = Query("chestinteraction") == "1";

    private IEnumerator Start()
    {
        // Allow player/camera Start methods to finish before framing the QA view.
        yield return new WaitForSecondsRealtime(1f);
        GameObject root = GameObject.Find("DigitalDissolveChest");
        chest = root != null ? root.GetComponent<ChestDissolveController>() : null;
        if (chest == null)
        {
            Fail("DigitalDissolveChest was not found.");
            yield break;
        }
        if (interactionOnly)
        {
            PlacePlayerForInteraction();
            yield break;
        }
        // Isolate this pixel test from the team's unrelated animated exhibits.
        // The chest is a separate scene root and remains the real playable chest.
        var teamTrail = GameObject.Find("TeamShaderTrail");
        if (teamTrail != null) teamTrail.SetActive(false);
        renderers = chest.ChestRenderers;
        originalShadowModes = new ShadowCastingMode[renderers.Length];
        originalEnabled = new bool[renderers.Length];
        for (int index = 0; index < renderers.Length; index++)
        {
            originalShadowModes[index] = renderers[index].shadowCastingMode;
            originalEnabled[index] = renderers[index].enabled;
        }
        foreach (ThirdPersonCamera orbit in FindObjectsByType<ThirdPersonCamera>(FindObjectsSortMode.None)) orbit.enabled = false;
        foreach (PlayerMovement player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            player.enabled = false;
            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        }
        foreach (Animator animator in FindObjectsByType<Animator>(FindObjectsSortMode.None)) animator.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Camera camera = Camera.main;
        if (camera == null)
        {
            Fail("The gameplay scene has no main camera.");
            yield break;
        }
        Bounds worldBounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) worldBounds.Encapsulate(renderer.bounds);
        float scale = Mathf.Max(worldBounds.size.x, worldBounds.size.y, worldBounds.size.z);
        Vector3 focus = worldBounds.center + Vector3.down * worldBounds.extents.y * 0.1f;
        camera.transform.position = focus + new Vector3(1.55f, 1.25f, -2.1f) * scale;
        camera.transform.LookAt(focus);
        camera.fieldOfView = 42f;
        camera.nearClipPlane = 0.1f;
        camera.allowDynamicResolution = false;
        chest.ResetDissolve();
        if (float.TryParse(Query("chestprogress"), NumberStyles.Float, CultureInfo.InvariantCulture, out float progress))
            chest.SetProgress(progress);
        Debug.Log("CHEST_QA_READY: actual gameplay chest; " + SystemInfo.graphicsDeviceType + "; "
            + Screen.width + "x" + Screen.height + "; " + renderers.Length + " renderers. Query chestautotest=1 runs pixel checks.");
        // Warm the newly framed view before collecting a stable baseline.
        yield return new WaitForSecondsRealtime(1f);
        if (Query("chestautotest") == "1") yield return RunChecks();
    }

    private void OnGUI()
    {
        if (interactionOnly) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            resultStyle = new GUIStyle(labelStyle) { fontSize = 14 };
        }
        float width = Mathf.Min(Screen.width - 24f, 1000f);
        GUI.Box(new Rect(12, 12, width, PanelHeight - 20), "");
        GUI.Label(new Rect(24, 19, width - 24, 25), "Chest QA | actual scene | " + SystemInfo.graphicsDeviceType
            + (chest != null ? " | Progress " + chest.Progress.ToString("0.000", CultureInfo.InvariantCulture) : ""), labelStyle);
        GUI.enabled = chest != null && !running;
        if (GUI.Button(new Rect(24, 49, 72, 28), "0%")) { RestoreRenderers(); chest.SetProgress(0); }
        if (GUI.Button(new Rect(102, 49, 72, 28), "50%")) { RestoreRenderers(); chest.SetProgress(0.5f); }
        if (GUI.Button(new Rect(180, 49, 72, 28), "100%")) { RestoreRenderers(); chest.SetProgress(1); }
        if (GUI.Button(new Rect(258, 49, 110, 28), "Play (2 sec)")) { RestoreRenderers(); chest.ResetDissolve(); chest.BeginDissolve(); }
        if (GUI.Button(new Rect(374, 49, 110, 28), "Run checks")) StartCoroutine(RunChecks());
        if (GUI.Button(new Rect(490, 49, 120, 28), "Shadows only"))
            foreach (Renderer renderer in renderers) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        GUI.enabled = true;
        GUI.Label(new Rect(24, 82, width - 24, 24), status, resultStyle);
        GUI.Label(new Rect(24, 105, width - 24, 34), metrics, resultStyle);
    }

    private void PlacePlayerForInteraction()
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player == null)
        {
            Fail("The gameplay scene has no player for the interaction check.");
            return;
        }
        CharacterController capsule = player.GetComponent<CharacterController>();
        bool wasEnabled = capsule != null && capsule.enabled;
        if (capsule != null) capsule.enabled = false;
        try
        {
            Vector3 position = chest.transform.position + Vector3.back * 2f;
            position.y = player.transform.position.y;
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 5f, Vector3.down, 12f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(player.transform) || hit.transform.IsChildOf(chest.transform)
                    || hit.normal.y < 0.55f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                float rootToFeet = capsule != null
                    ? (capsule.height * 0.5f - capsule.center.y) * Mathf.Abs(player.transform.lossyScale.y) : 0f;
                position.y = hit.point.y + rootToFeet + 0.05f;
            }
            Vector3 facing = Vector3.ProjectOnPlane(chest.transform.position - position, Vector3.up);
            player.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            Physics.SyncTransforms();
        }
        finally
        {
            if (capsule != null) capsule.enabled = wasEnabled;
        }
        Debug.Log("CHEST_INTERACTION_READY: controlled placement two metres in front of the actual chest. "
            + "Gameplay input, third-person camera and animation remain active. Press E through the browser; "
            + "no dissolve was started by the probe. This checks E interaction, not traversal from spawn.");
    }

    private IEnumerator RunChecks()
    {
        if (running || chest == null) yield break;
        running = true;
        metrics = "";
        captureError = null;
        string failure = null;
        Color32[] baseline = null, baselineAgain = null, intact = null, halfway = null, dissolved = null;
        Color32[] shadowIntact = null, shadowHalfway = null, shadowDissolved = null;
        float previousProgress = chest.Progress;
        try
        {
            status = "Checking independent materials and progress endpoints...";
            ValidateRuntimeState();
        }
        catch (Exception exception) { failure = exception.Message; }
        if (failure != null)
        {
            Fail(failure);
            RestoreRenderers();
            chest.SetProgress(previousProgress);
            running = false;
            yield break;
        }

        // Every capture uses the same real scene/camera. The status panel is outside the sampled rectangle.
        status = "Capturing disabled-renderer baseline...";
        chest.SetProgress(0);
        foreach (Renderer renderer in renderers) renderer.enabled = false;
        yield return CapturePixels(); baseline = captured;
        yield return CapturePixels(); baselineAgain = captured;
        status = "Capturing visible chest at 0%, 50%, 100%...";
        RestoreRenderers();
        chest.SetProgress(0); yield return CapturePixels(); intact = captured;
        chest.SetProgress(0.5f); yield return CapturePixels(); halfway = captured;
        chest.SetProgress(1); yield return CapturePixels(); dissolved = captured;
        status = "Capturing actual ShadowCaster at 0%, 50%, 100%...";
        foreach (Renderer renderer in renderers) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        chest.SetProgress(0); yield return CapturePixels(); shadowIntact = captured;
        chest.SetProgress(0.5f); yield return CapturePixels(); shadowHalfway = captured;
        chest.SetProgress(1); yield return CapturePixels(); shadowDissolved = captured;

        try
        {
            Require(captureError == null, "Framebuffer capture failed: " + captureError);
            Require(baseline != null && baseline.Length > 0, "No pixels were read from the WebGL framebuffer.");
            int noise = Difference(baseline, baselineAgain);
            int full = Difference(baseline, intact);
            int half = Difference(baseline, halfway);
            int end = Difference(baseline, dissolved);
            int frontChange = Difference(intact, halfway);
            int shadow0 = Difference(baseline, shadowIntact);
            int shadow50 = Difference(baseline, shadowHalfway);
            int shadow100 = Difference(baseline, shadowDissolved);
            int shadowChange = Difference(shadowIntact, shadowHalfway);
            int tolerance = Mathf.Max(20, noise * 3 + Mathf.CeilToInt(baseline.Length * 0.0001f));
            metrics = "Pixels: colour " + full + "/" + half + "/" + end + "; shadow " + shadow0 + "/" + shadow50
                + "/" + shadow100 + "; baseline noise " + noise + "; tolerance " + tolerance + ".";
            Debug.Log("CHEST_QA_PIXELS: ROI " + captureWidth + "x" + captureHeight + "; " + metrics
                + " 0-to-half colour change=" + frontChange + ", shadow change=" + shadowChange);
            Require(noise < baseline.Length / 100, "The sampled scene is changing; pixel evidence is inconclusive.");
            Require(full > tolerance * 4, "The intact chest is not visibly rendered in the sampled area.");
            Require(half > tolerance && frontChange > tolerance, "Half-progress must visibly change the chest while leaving some of it.");
            Require(end <= tolerance, "Progress 1 still differs from disabled renderers: residual geometry or shadow.");
            Require(shadow0 > tolerance * 2, "No measurable chest shadow reaches the ground; shadow verification cannot pass.");
            Require(shadow50 > tolerance && shadowChange > tolerance, "Half-progress must change the actual shadow and leave a partial shadow.");
            Require(shadow50 < shadow0 + tolerance, "Half-progress grew the shadow footprint unexpectedly.");
            Require(shadow100 <= tolerance, "The fully dissolved chest leaves a measurable shadow.");
        }
        catch (Exception exception) { failure = exception.Message; }

        RestoreRenderers();
        if (failure == null)
        {
            status = "Pixel checks passed. Timing the actual two-second animation...";
            chest.ResetDissolve();
            chest.BeginDissolve();
            float started = Time.time;
            while (chest.IsDissolving && Time.time - started < 6f) yield return null;
            float elapsed = Time.time - started;
            try
            {
                Require(chest.Progress == 1f && !chest.IsDissolving, "The animation did not reach progress 1.");
                Require(Mathf.Abs(elapsed - 2f) < Mathf.Max(0.2f, Time.deltaTime * 3), "Unexpected dissolve duration: " + elapsed);
                foreach (Renderer renderer in renderers)
                {
                    Require(renderer.enabled, "The endpoint must be verified with Renderers still enabled.");
                    foreach (Material material in renderer.sharedMaterials)
                        Require(material.GetFloat("_Progress") == 1f, "A material slot did not finish dissolving.");
                }
                status = "PASS: visible dissolve, partial shadow, zero residual colour/shadow, materials and 2s animation.";
                Debug.Log("CHEST_QA_PASS: " + status + " Duration=" + elapsed.ToString("0.000", CultureInfo.InvariantCulture) + "s. " + metrics);
            }
            catch (Exception exception) { failure = exception.Message; }
        }
        if (failure != null) Fail(failure);
        RestoreRenderers();
        chest.SetProgress(previousProgress);
        running = false;
    }

    private void ValidateRuntimeState()
    {
        chest.SetProgress(0);
        Require(renderers.Length > 0, "The chest has no renderers.");
        HashSet<Material> original = new HashSet<Material>();
        foreach (Renderer renderer in renderers)
        {
            Require(renderer.enabled, "A chest Renderer is disabled before testing.");
            foreach (Material material in renderer.sharedMaterials)
            {
                Require(material.shader.name == "ArtLab/Chest Dissolve" && material.shader.isSupported,
                    "Chest shader is missing or unsupported by this graphics backend.");
                Require(material.GetFloat("_Progress") == 0, "A material does not start at zero.");
                original.Add(material);
            }
        }
        GameObject copy = Instantiate(chest.gameObject, chest.transform.position + Vector3.down * 2000f, chest.transform.rotation);
        try
        {
            ChestDissolveController other = copy.GetComponent<ChestDissolveController>();
            other.SetProgress(0.37f);
            foreach (Renderer renderer in other.ChestRenderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(!original.Contains(material), "Different chests share a mutable runtime material.");
                    Require(Mathf.Approximately(material.GetFloat("_Progress"), 0.37f), "A cloned material slot did not update.");
                }
            foreach (Material material in original)
                Require(material.GetFloat("_Progress") == 0, "Dissolving a second chest changed the first one.");
        }
        finally { Destroy(copy); }
        chest.SetProgress(-2); Require(chest.Progress == 0, "Progress must clamp below zero.");
        chest.SetProgress(2); Require(chest.Progress == 1, "Progress must clamp above one.");
        chest.ResetDissolve();
        Debug.Log("CHEST_QA_STATE_PASS: separate chests own separate runtime materials; all material slots update; endpoints clamp.");
    }

    private IEnumerator CapturePixels()
    {
        captured = null;
        // Wait through a complete render after changing the material/shadow state.
        yield return null;
        yield return new WaitForEndOfFrame();
        captureWidth = Screen.width;
        captureHeight = Mathf.Max(1, Screen.height - Mathf.CeilToInt(PanelHeight));
        Texture2D texture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
        try
        {
            texture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
            texture.Apply(false, false);
            captured = texture.GetPixels32();
        }
        catch (Exception exception) { captureError = exception.Message; }
        finally { Destroy(texture); }
    }

    private static int Difference(Color32[] first, Color32[] second)
    {
        Require(first != null && second != null && first.Length == second.Length, "Framebuffer size changed during validation.");
        int count = 0;
        for (int index = 0; index < first.Length; index++)
        {
            Color32 a = first[index], b = second[index];
            // Ignore tiny postprocessing/rounding differences; visible shadow changes are substantially darker.
            if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 18) count++;
        }
        return count;
    }

    private void RestoreRenderers()
    {
        if (renderers == null) return;
        for (int index = 0; index < renderers.Length; index++)
        {
            renderers[index].enabled = originalEnabled[index];
            renderers[index].shadowCastingMode = originalShadowModes[index];
        }
    }

    private void Fail(string message)
    {
        status = "FAIL: " + message;
        Debug.LogError("CHEST_QA_FAIL: " + message + " " + metrics);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Query(string key)
    {
        string url = Application.absoluteURL;
        int start = url.IndexOf('?');
        if (start < 0) return "";
        string query = url.Substring(start + 1).Split('#')[0];
        foreach (string parameter in query.Split('&'))
        {
            string[] pair = parameter.Split(new[] { '=' }, 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key) return Uri.UnescapeDataString(pair[1]);
        }
        return "";
    }
}
