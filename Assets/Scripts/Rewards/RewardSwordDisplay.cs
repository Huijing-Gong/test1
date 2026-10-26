// Material switching and display motion; surface effects stay in the shader.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public sealed class RewardSwordDisplay : MonoBehaviour
{
    [Header("Play Mode: press H to switch")]
    public bool hologram = true;
    public Material hologramMaterial;
    public Material[] solidMaterials;

    [Header("Display motion (Play Mode only)")]
    public bool rotate = true;
    [Range(-60f, 60f)] public float rotationSpeed = 25f;
    public bool floatUpAndDown = true;
    [Range(0f, 0.1f)] public float floatAmplitude = 0.05f;
    [Range(0f, 2f)] public float floatFrequency = 0.4f;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private float rotationAngle;
    private float motionTime;
    private bool hasRestPose;

    private MeshRenderer targetRenderer;
    private bool lastHologram;
    private bool needsRefresh = true;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            rotationAngle = 0f;
            motionTime = 0f;
            hasRestPose = true;
        }
        ApplyAppearance();
    }

    private void OnDisable()
    {
        // Restore the starting pose instead of accumulating offsets on re-enable.
        if (hasRestPose)
        {
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            hasRestPose = false;
        }
    }

    private void OnValidate()
    {

        needsRefresh = true;
    }

    private void Update()
    {
        if (Application.isPlaying && hasRestPose)
        {
            motionTime += Time.deltaTime;
            if (rotate)
            {
                rotationAngle = (rotationAngle + rotationSpeed * Time.deltaTime) % 360f;
                // Multiply after the starting rotation to turn around local Y.
                transform.localRotation = restRotation * Quaternion.AngleAxis(rotationAngle, Vector3.up);
            }
            // Use the starting position each frame to prevent vertical drift.
            float offset = 0f;
            if (floatUpAndDown)
            {
                offset = Mathf.Sin(motionTime * floatFrequency * Mathf.PI * 2f) * floatAmplitude;
            }
            transform.localPosition = restPosition + Vector3.up * offset;
        }

        if (Application.isPlaying && Keyboard.current != null)
        {
            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                hologram = !hologram;
            }
        }

        if (needsRefresh || lastHologram != hologram)
        {
            ApplyAppearance();
        }
    }

    public void ApplyAppearance()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<MeshRenderer>();
        }

        if (hologramMaterial == null || solidMaterials == null || solidMaterials.Length == 0)
        {
            needsRefresh = true;
            return;
        }

        if (hologram)
        {

            Material[] materials = new Material[solidMaterials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = hologramMaterial;
            }
            targetRenderer.sharedMaterials = materials;
            targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
            targetRenderer.receiveShadows = false;
        }
        else
        {
            targetRenderer.sharedMaterials = solidMaterials;
            targetRenderer.shadowCastingMode = ShadowCastingMode.On;
            targetRenderer.receiveShadows = true;
        }

        lastHologram = hologram;
        needsRefresh = false;
    }
}
