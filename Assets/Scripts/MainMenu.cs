using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenu : MonoBehaviour
{
    public GameObject settingsPanel;
    public Slider widthSlider;
    public Slider heightSlider;
    public TMP_Text widthLabel;
    public TMP_Text heightLabel;

    void Start()
    {
        // Слушаем изменения слайдеров, чтобы обновлять текст
        widthSlider.onValueChanged.AddListener(v => widthLabel.text = $"Width: {(int)v}");
        heightSlider.onValueChanged.AddListener(v => heightLabel.text = $"Height: {(int)v}");
    }

    public void OnPlayClicked()
    {
        settingsPanel.SetActive(true);
    }

    public void OnCancelClicked()
    {
        settingsPanel.SetActive(false);
    }

    public void OnStartGameClicked()
    {
        GameSettings.BoardWidth = (int)widthSlider.value;
        GameSettings.BoardHeight = (int)heightSlider.value;
        SceneManager.LoadScene("Game");
    }

    public void OnQuitClicked()
    {
        Application.Quit();
    }
}