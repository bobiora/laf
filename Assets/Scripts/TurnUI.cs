using UnityEngine;
using TMPro;

public class TurnUI : MonoBehaviour
{
    public TMP_Text turnText;
    public TMP_Text score1Text;
    public TMP_Text score2Text;

    void Update()
    {
        if (GameManager.Instance == null) return;

        var gm = GameManager.Instance;

        turnText.text = $"Player {gm.currentPlayer}'s turn";
        turnText.color = gm.GetCurrentColor();

        score1Text.text = $"Red: {gm.player1Score}";
        score2Text.text = $"Green: {gm.player2Score}";
    }
}