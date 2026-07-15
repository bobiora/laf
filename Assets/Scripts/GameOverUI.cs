using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text resultText;
    public Button menuButton;
    public Button restartButton;

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
    }

    public void Show(int winnerPlayer, Color winnerColor, int winnerScore, bool draw)
    {
        if (panel != null) panel.SetActive(true);

        if (resultText != null)
        {
            if (draw)
            {
                resultText.text = $"Ничья! {winnerScore} : {winnerScore}";
                resultText.color = Color.white;
            }
            else
            {
                resultText.text = $"Игрок {winnerPlayer} победил! {winnerScore} очков";
                resultText.color = winnerColor;
            }
        }
    }

    public void OnMenuClicked()
    {
        SceneManager.LoadScene("MainMenu");
    }

    public void OnRestartClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
