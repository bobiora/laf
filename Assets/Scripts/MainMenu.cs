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

    [Header("Player icons")]
    public Button player1IconButton;   // button with player 1 icon slot
    public Button player2IconButton;   // button with player 2 icon slot
    public IconPickerDialog iconPicker; // shared icon picker dialog

    void Start()
    {
        // Listen to slider changes to update labels.
        widthSlider.onValueChanged.AddListener(v => widthLabel.text = $"Width: {(int)v}");
        heightSlider.onValueChanged.AddListener(v => heightLabel.text = $"Height: {(int)v}");

        // Load icons and saved player selection.
        PlayerIcons.EnsureLoaded();

        if (player1IconButton != null)
            player1IconButton.onClick.AddListener(OnPlayer1IconClicked);
        if (player2IconButton != null)
            player2IconButton.onClick.AddListener(OnPlayer2IconClicked);

        RefreshIconSlots();
    }

    // Updates sprites on both slot buttons from the current selection.
    void RefreshIconSlots()
    {
        SetSlotSprite(player1IconButton, PlayerIcons.GetPlayer1Icon());
        SetSlotSprite(player2IconButton, PlayerIcons.GetPlayer2Icon());
    }

    // Draws the sprite on the button's child Image (named "IconImage" if present),
    // without changing the button's own colored background.
    static void SetSlotSprite(Button button, Sprite sprite)
    {
        if (button == null) return;

        Image iconImage = null;
        foreach (var img in button.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject == button.gameObject) continue; // button background
            iconImage = img;
            if (img.gameObject.name == "IconImage") break;     // prefer named child
        }
        if (iconImage == null) return;

        iconImage.sprite = sprite;
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
    }

    void OnPlayer1IconClicked()
    {
        if (iconPicker == null) return;
        iconPicker.Open(index =>
        {
            PlayerIcons.SetPlayer1Icon(index);
            RefreshIconSlots();
        });
    }

    void OnPlayer2IconClicked()
    {
        if (iconPicker == null) return;
        iconPicker.Open(index =>
        {
            PlayerIcons.SetPlayer2Icon(index);
            RefreshIconSlots();
        });
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
