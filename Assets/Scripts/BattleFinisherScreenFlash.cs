using UnityEngine;

public class BattleFinisherScreenFlash : MonoBehaviour
{
    private const float FlashLifetime = 0.16f;

    private Color flashColor;
    private float elapsed;

    public static void Create(bool isEnemyAttack)
    {
        GameObject flashObject = new GameObject("FinisherScreenFlash");
        BattleFinisherScreenFlash flash =
            flashObject.AddComponent<BattleFinisherScreenFlash>();
        flash.flashColor = isEnemyAttack
            ? new Color(0.32f, 0.002f, 0.006f, 0.30f)
            : new Color(1f, 0.82f, 0.28f, 0.34f);
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= FlashLifetime)
        {
            Destroy(gameObject);
        }
    }

    void OnGUI()
    {
        float progress = Mathf.Clamp01(elapsed / FlashLifetime);
        float flash = 1f - Mathf.SmoothStep(0f, 1f, progress);
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        GUI.color = new Color(
            flashColor.r,
            flashColor.g,
            flashColor.b,
            flashColor.a * flash
        );
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            Texture2D.whiteTexture
        );
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }
}
