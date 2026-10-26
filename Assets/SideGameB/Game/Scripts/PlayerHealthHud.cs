using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;

public sealed class PlayerHealthHud : MonoBehaviour
{
    [SerializeField] private Image[] hearts;
    [SerializeField] private Sprite fullHeart;
    [SerializeField] private Sprite halfHeart;
    [SerializeField] private Sprite emptyHeart;
    [SerializeField] private GameObject failurePanel;
    [SerializeField] private Button restartButton;

    public void Configure(Image[] heartImages, Sprite full, Sprite half, Sprite empty,
        GameObject gameOverPanel, Button restart)
    {
        hearts = heartImages;
        fullHeart = full;
        halfHeart = half;
        emptyHeart = empty;
        failurePanel = gameOverPanel;
        restartButton = restart;
        SetHealth(1f);
        failurePanel.SetActive(false);
    }

    private void Awake()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
            restartButton.onClick.AddListener(RestartGame);
        }
    }

    public void SetHealth(float normalizedHealth)
    {
        float heartUnits = Mathf.Clamp01(normalizedHealth) * hearts.Length * 2f;
        for (int i = 0; i < hearts.Length; i++)
        {
            float unitsForHeart = heartUnits - i * 2f;
            hearts[i].sprite = unitsForHeart >= 2f ? fullHeart : unitsForHeart >= 1f ? halfHeart : emptyHeart;
        }
    }

    public void SetHeartCount(int count)
    {
        if (hearts == null || hearts.Length == 0 || count <= 0 || hearts.Length == count)
            return;

        List<Image> resized = new(hearts);
        if (count > resized.Count)
        {
            Image template = hearts[hearts.Length - 1];
            RectTransform templateRect = template.rectTransform;
            Vector2 step = hearts.Length > 1
                ? templateRect.anchoredPosition - hearts[hearts.Length - 2].rectTransform.anchoredPosition
                : new Vector2(templateRect.sizeDelta.x + 4f, 0f);
            while (resized.Count < count)
            {
                Image extra = Instantiate(template, template.transform.parent);
                extra.name = $"Heart {resized.Count + 1}";
                extra.rectTransform.anchoredPosition = templateRect.anchoredPosition + step * (resized.Count - hearts.Length + 1);
                extra.gameObject.SetActive(true);
                resized.Add(extra);
            }
        }
        else
        {
            for (int i = count; i < resized.Count; i++)
                if (resized[i] != null) resized[i].gameObject.SetActive(false);
            resized.RemoveRange(count, resized.Count - count);
        }
        hearts = resized.ToArray();
    }

    public void ShowFailure()
    {
        if (failurePanel != null)
            failurePanel.SetActive(true);
    }

    public void RestartGame()
    {
        var travel = SideWorldTravel.Instance;
        if (travel && travel.IsAway)
        {
            if (travel.IsSideWorldB) travel.RetrySideWorldB();
            return;
        }
        Time.timeScale = 1f;
        GameControlsTutorial.ResetForNewVisit();
        CharacterSelectionUI.ResetForNewVisit();
        GameAudio.RestartMusicFromBeginning();
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
            SceneManager.LoadScene(activeScene.buildIndex);
        else
            SceneManager.LoadScene(activeScene.name);
    }
}
