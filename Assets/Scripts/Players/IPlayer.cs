using UnityEngine;

// Abstraction over a participant in the match. Today only HumanPlayer implements it;
// a future AIPlayer will implement the same interface so GameManager can drive either
// without branching on player kind.
public interface IPlayer
{
    int Id { get; }                    // 1 or 2
    Color Color { get; }
    Sprite Icon { get; }               // may be null if none selected
    string DisplayName { get; }        // "Player 1" / "Player 2" for now
    bool IsHuman { get; }              // true for current human players; false will be AI later
}
