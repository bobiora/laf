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

    // Last values pushed to the UI. Update() only rewrites TMP text / icons when one of
    // these actually changes, so it does not allocate a new string or touch the mesh every
    // frame (per-frame string churn is needless GC pressure, more noticeable on device).
    private int lastPlayer = -1;
    private int lastScore1 = int.MinValue;
    private int lastScore2 = int.MinValue;

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

        int player = gm.currentPlayer;
        if (player != lastPlayer)
        {
            lastPlayer = player;
            turnText.text = $"Player {player}'s turn";
            turnText.color = gm.GetCurrentColor();

            // Current player's icon next to the turn text (only when the turn changes).
            SetIcon(turnIcon, player == 1
                ? PlayerIcons.GetPlayer1Icon()
                : PlayerIcons.GetPlayer2Icon());
        }

        int score1 = gm.player1Score;
        if (score1 != lastScore1)
        {
            lastScore1 = score1;
            score1Text.text = $"Red: {score1}";
        }

        int score2 = gm.player2Score;
        if (score2 != lastScore2)
        {
            lastScore2 = score2;
            score2Text.text = $"Green: {score2}";
        }
    }

    static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
    }
}
