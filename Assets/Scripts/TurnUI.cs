using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TurnUI : MonoBehaviour
{
    public TMP_Text turnText;
    public TMP_Text score1Text;
    public TMP_Text score2Text;

    [Header("Player icons")]
    public Image score1Icon;  // small icon next to player 1's score
    public Image score2Icon;  // small icon next to player 2's score
    public Image turnIcon;    // current player's icon next to "Player X's turn"

    void Start()
    {
        // Icons and saved player selection.
        PlayerIcons.EnsureLoaded();

        // Score icons are fixed for the whole match — set once.
        SetIcon(score1Icon, PlayerIcons.GetPlayer1Icon());
        SetIcon(score2Icon, PlayerIcons.GetPlayer2Icon());
    }

    void Update()
    {
        if (GameManager.Instance == null) return;

        var gm = GameManager.Instance;

        turnText.text = $"Player {gm.currentPlayer}'s turn";
        turnText.color = gm.GetCurrentColor();

        score1Text.text = $"Red: {gm.player1Score}";
        score2Text.text = $"Green: {gm.player2Score}";

        // Current player's icon next to the turn text.
        Sprite current = gm.currentPlayer == 1
            ? PlayerIcons.GetPlayer1Icon()
            : PlayerIcons.GetPlayer2Icon();
        SetIcon(turnIcon, current);
    }

    static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
    }
}
