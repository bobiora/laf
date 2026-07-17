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
        if (panel == null)
        {
            Debug.LogError("GameOverUI.panel is not assigned! Drag GameOverPanel into the panel field in the inspector.");
            return;
        }

        panel.SetActive(true);
        panel.transform.SetAsLastSibling();

        if (resultText != null)
        {
            if (draw)
            {
                resultText.text = $"Draw! {winnerScore} : {winnerScore}";
                resultText.color = Color.white;
            }
            else
            {
                resultText.text = $"Player {winnerPlayer} wins! {winnerScore} points";
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
