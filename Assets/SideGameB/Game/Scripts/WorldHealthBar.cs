using UnityEngine;

public sealed class WorldHealthBar : MonoBehaviour
{
    [SerializeField] private Transform fill;
    [SerializeField] private float fullWidth = 0.9f;

    public void Configure(Transform fillTransform, float width)
    {
        fill = fillTransform;
        fullWidth = width;
        SetHealth(1f);
    }

    public void SetHealth(float normalizedHealth)
    {
        if (fill == null)
            return;
        float value = Mathf.Clamp01(normalizedHealth);
        Vector3 scale = fill.localScale;
        scale.x = fullWidth * value;
        fill.localScale = scale;
        Vector3 position = fill.localPosition;
        position.x = -(fullWidth - scale.x) * 0.5f;
        fill.localPosition = position;
        gameObject.SetActive(value > 0f);
    }
}
