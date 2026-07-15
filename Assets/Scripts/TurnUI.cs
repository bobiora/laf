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

        turnText.text = $"Ход игрока {gm.currentPlayer}";
        turnText.color = gm.GetCurrentColor();

        score1Text.text = $"Красный: {gm.player1Score}";
        score2Text.text = $"Зелёный: {gm.player2Score}";
    }
}