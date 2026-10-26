using System.Collections;
using UnityEngine;

public sealed class FighterSlashEffect2D : MonoBehaviour
{
    private const int FrameCount = 8;
    private const float PixelsPerUnit = 300f;
    private static Sprite[] cachedFrames;

    public static void Spawn(Transform fighter, float facingDirection)
    {
        Texture2D sheet = Resources.Load<Texture2D>("Effects/FighterSlash");
        if (sheet == null) return;
        sheet.filterMode = FilterMode.Point;

        if (cachedFrames == null || cachedFrames.Length != FrameCount)
        {
            cachedFrames = new Sprite[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                int xMin = Mathf.RoundToInt(i * sheet.width / (float)FrameCount);
                int xMax = Mathf.RoundToInt((i + 1) * sheet.width / (float)FrameCount);
                cachedFrames[i] = Sprite.Create(sheet, new Rect(xMin, 0, xMax - xMin, sheet.height),
                    new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
        }

        GameObject effect = new("Fighter Slash Effect");
        effect.transform.position = fighter.position + new Vector3(facingDirection * 0.68f, 0.08f, 0f);
        SpriteRenderer renderer = effect.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 12;
        renderer.flipX = facingDirection < 0f;
        FighterSlashEffect2D player = effect.AddComponent<FighterSlashEffect2D>();
        player.StartCoroutine(player.Play(renderer));
    }

    private IEnumerator Play(SpriteRenderer renderer)
    {
        const float frameDuration = 0.035f;
        foreach (Sprite frame in cachedFrames)
        {
            renderer.sprite = frame;
            yield return new WaitForSeconds(frameDuration);
        }
        Destroy(gameObject);
    }
}
