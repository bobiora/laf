using UnityEngine;

// A human-controlled player. Its icon is pulled live from PlayerIcons on every access
// (not cached) so that changing the icon selection is reflected immediately.
public class HumanPlayer : IPlayer
{
    public int Id { get; }
    public Color Color { get; }

    public HumanPlayer(int id, Color color)
    {
        Id = id;
        Color = color;
    }

    public Sprite Icon
    {
        get
        {
            // Id is 1 or 2; map to the matching PlayerIcons accessor on demand.
            return Id == 1 ? PlayerIcons.GetPlayer1Icon() : PlayerIcons.GetPlayer2Icon();
        }
    }

    public string DisplayName => $"Player {Id}";

    public bool IsHuman => true;
}
