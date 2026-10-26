using System.Collections;
using UnityEngine;

/// <summary>Display controls; never awards items or changes progression.</summary>
public sealed class TeamShaderShowcase : MonoBehaviour
{
    public Transform sword;
    public PixelShieldController shield;
    public PortalScanController portal;
    public ChestDissolveController chest;
    private bool qa;
    private int selected;
    private float density = .48f;
    private float opacity = .38f;
    private Material swordMaterial;
    private float sampleTime;
    private int sampleFrames;
    private float fps;
    private GUIStyle label;

    private IEnumerator Start()
    {
        qa = Application.absoluteURL.Contains("teamqa=1");
        yield return null;
        if (qa)
        {
            foreach (var camera in FindObjectsByType<ThirdPersonCamera>(FindObjectsSortMode.None)) camera.enabled = false;
            foreach (var player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                player.enabled = false;
                foreach (var renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Frame(0);
        }
        Debug.Log("TEAM_SHADERS_READY: chest, sword, shield, portal; scene=" + gameObject.scene.name);
    }

    private void Update()
    {
        sampleTime += Time.unscaledDeltaTime;
        sampleFrames++;
        if (sampleTime >= 10)
        {
            fps = sampleFrames / sampleTime;
            Debug.Log("TEAM_PERFORMANCE: " + fps.ToString("F1") + " FPS; " + Screen.width + "x" + Screen.height + "; view=" + selected);
            sampleFrames = 0;
            sampleTime = 0;
        }
    }

    private void Frame(int index)
    {
        selected = index;
        Transform target = index == 1 ? sword : index == 2 ? shield.transform : index == 3 ? portal.transform : chest.transform;
        Vector3 focus = target.position + Vector3.up * (index == 2 ? 1.2f : index == 3 ? 0 : .7f);
        Camera camera = Camera.main;
        if (!camera) return;
        camera.transform.position = focus + (index == 2 ? new Vector3(4, 2, -6) : new Vector3(2.5f, 1.5f, -5));
        camera.transform.LookAt(focus);
        camera.fieldOfView = 44;
    }

    private void OnGUI()
    {
        if (!qa) return;
        if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
        GUI.Box(new Rect(0, Screen.height - 135, Screen.width, 135), "");
        for (int i = 0; i < 4; i++)
            if (GUI.Button(new Rect(16 + i * 155, Screen.height - 126, 145, 30), new[] { "Chest", "Hologram sword", "Pixel barrier", "Portal" }[i])) Frame(i);
        if (GUI.Button(new Rect(16, Screen.height - 87, 110, 28), "Chest 0")) chest.ResetDissolve();
        if (GUI.Button(new Rect(132, Screen.height - 87, 110, 28), "Chest 0.5")) chest.SetProgress(.5f);
        if (GUI.Button(new Rect(248, Screen.height - 87, 110, 28), "Chest 1")) chest.SetProgress(1);
        if (GUI.Button(new Rect(364, Screen.height - 87, 110, 28), "Replay portal")) portal.Replay();
        float nextDensity = GUI.HorizontalSlider(new Rect(505, Screen.height - 75, 155, 20), density, 0, 1);
        if (nextDensity != density)
        {
            density = nextDensity;
            shield.ConfigureAppearance(.035f, density, 1.25f, .28f, 1, 2.2f, 10);
        }
        float nextOpacity = GUI.HorizontalSlider(new Rect(700, Screen.height - 75, 155, 20), opacity, .05f, .8f);
        if (nextOpacity != opacity)
        {
            opacity = nextOpacity;
            var display = sword.GetComponent<RewardSwordDisplay>();
            if (swordMaterial == null)
            {
                swordMaterial = new Material(display.hologramMaterial);
                display.hologramMaterial = swordMaterial;
            }
            display.hologram = true;
            display.ApplyAppearance();
            swordMaterial.SetFloat("_Opacity", opacity);
        }
        GUI.Label(new Rect(16, Screen.height - 44, 1000, 35), "Integrated WebGL | " + fps.ToString("F1") + " FPS (10 s sample) | Barrier density " + density.ToString("F2") + " | Sword opacity " + opacity.ToString("F2"), label);
    }

    private void OnDestroy() { if (swordMaterial) Destroy(swordMaterial); }
}
