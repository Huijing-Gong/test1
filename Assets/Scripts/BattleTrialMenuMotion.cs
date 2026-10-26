using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Small scene-owned UI choreography. All timing is unscaled: the menu pauses
// combat. No coroutine queues, tween package, per-frame allocations or textures.
public sealed class BattleTrialMenuMotion : MonoBehaviour
{
    sealed class Track
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector2 current, destination, offset;
        public float start, duration, initialAlpha;
        public bool interactive, move;
    }
    sealed class FloatItem
    {
        public RectTransform rect;
        public Vector2 home, amplitude;
        public float speed, phase, scale, angle;
    }
    readonly List<Track> tracks = new List<Track>(12);
    readonly List<FloatItem> floats = new List<FloatItem>(6);
    readonly List<Track> details = new List<Track>(8);
    float changedAt;

    public void Reveal(RectTransform rect, float delay, Vector2 offset, float duration = .30f)
    {
        var group = rect.GetComponent<CanvasGroup>();
        if (!group) group = rect.gameObject.AddComponent<CanvasGroup>();
        var track = new Track { rect = rect, group = group, current = rect.anchoredPosition,
            destination = rect.anchoredPosition, offset = offset, start = Time.unscaledTime + delay,
            duration = duration, initialAlpha = 0, interactive = true };
        group.alpha = 0; group.blocksRaycasts = false;
        rect.anchoredPosition = track.current + offset;
        tracks.Add(track);
    }

    public void Destination(RectTransform rect, Vector2 destination)
    {
        foreach (var track in tracks)
            if (track.rect == rect) { track.destination = destination; return; }
    }

    public void Detail(RectTransform rect, bool slide = true)
    {
        var group = rect.GetComponent<CanvasGroup>();
        if (!group) group = rect.gameObject.AddComponent<CanvasGroup>();
        details.Add(new Track { rect = rect, group = group, current = rect.anchoredPosition, move = slide });
    }

    public void DetailChanged() => changedAt = Time.unscaledTime;

    public void Float(RectTransform rect, Vector2 amplitude, float speed, float scale = 0, float angle = 0)
    {
        // Only illustration/decorative geometry uses a centre pivot. Hit targets
        // and text keep their original transforms and their layout unchanged.
        Vector2 size = rect.sizeDelta;
        rect.anchoredPosition += new Vector2((.5f - rect.pivot.x) * size.x, (.5f - rect.pivot.y) * size.y);
        rect.pivot = new Vector2(.5f, .5f);
        floats.Add(new FloatItem { rect = rect, home = rect.anchoredPosition, amplitude = amplitude,
            speed = speed, phase = floats.Count * .83f, scale = scale, angle = angle });
    }

    void LateUpdate()
    {
        float time = Time.unscaledTime;
        float damping = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
        foreach (var track in tracks)
        {
            if (!track.rect) continue;
            float t = Mathf.Clamp01((time - track.start) / track.duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            track.current = Vector2.Lerp(track.current, track.destination, damping);
            track.rect.anchoredPosition = track.current + track.offset * (1f - ease);
            track.group.alpha = Mathf.SmoothStep(track.initialAlpha, 1, t);
            // Never leave invisible click targets. Input becomes available early,
            // rather than waiting for the final decorative settling movement.
            track.group.blocksRaycasts = track.interactive && t >= .22f;
        }
        float detailAge = Mathf.Clamp01((time - changedAt) / .20f);
        float detailEase = 1f - Mathf.Pow(1f - detailAge, 3f);
        foreach (var item in details)
        {
            if (!item.rect) continue;
            item.group.alpha = Mathf.Lerp(.40f, 1, detailEase);
            if (item.move) item.rect.anchoredPosition = item.current + Vector2.right * (7f * (1f - detailEase));
        }
        foreach (var item in floats)
        {
            if (!item.rect || !item.rect.gameObject.activeInHierarchy) continue;
            float wave = Mathf.Sin(time * item.speed + item.phase);
            item.rect.anchoredPosition = item.home + item.amplitude * wave;
            item.rect.localScale = Vector3.one * (1f + item.scale * wave);
            item.rect.localRotation = Quaternion.Euler(0, 0, item.angle * wave);
        }
    }
}
