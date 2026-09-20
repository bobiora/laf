using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text resultText;
    [SerializeField] private TMP_Text secondPlaceText;
    public Button menuButton;
    public Button restartButton;

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
    }

    // Receives both places so the panel can show the winner (line 1) and 2nd place (line 2).
    // On a draw, loser fields are unused and the second-place line is hidden.
    public void Show(int winnerPlayer, Color winnerColor, int winnerScore,
                     int loserPlayer, Color loserColor, int loserScore, bool draw)
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

        if (secondPlaceText != null)
        {
            if (draw)
            {
                // No 2nd place on a draw — hide the line.
                secondPlaceText.gameObject.SetActive(false);
            }
            else
            {
                secondPlaceText.gameObject.SetActive(true);
                secondPlaceText.text = $"Player {loserPlayer} - {loserScore} points";
                secondPlaceText.color = loserColor;
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
