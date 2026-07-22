using UnityEngine;

// Static registry of player icons.
//
// Icons live in Assets/Resources/Icons/ (Resources folder is required — otherwise
// Resources.LoadAll won't find sprites). Each imported PNG with
// Texture Type = Sprite (2D and UI) becomes an available icon.
//
// Each player's choice is stored in PlayerPrefs, so it survives restarts and
// scene changes MainMenu -> Game.
public static class PlayerIcons
{
    // Filled at runtime from Resources/Icons. Never null after EnsureLoaded().
    public static Sprite[] AvailableIcons = new Sprite[0];

    public static int Player1IconIndex = 0;
    public static int Player2IconIndex = 1;

    const string Key1 = "player1_icon";
    const string Key2 = "player2_icon";

    static bool loaded = false;
    static Sprite fallbackSprite;   // colored circle when no icons exist

    // Lazily loads icons and preferences on first access. Safe to call repeatedly —
    // real work runs once per app launch.
    public static void EnsureLoaded()
    {
        if (loaded) return;
        LoadIcons();
        LoadPreferences();
        loaded = true;
    }

    // Reads all sprites from Resources/Icons. Order is defined by Unity (file name).
    public static void LoadIcons()
    {
        AvailableIcons = Resources.LoadAll<Sprite>("Icons");
        if (AvailableIcons == null) AvailableIcons = new Sprite[0];
    }

    // Call at scene start (see MainMenu.Start / TurnUI.Start).
    public static void LoadPreferences()
    {
        Player1IconIndex = PlayerPrefs.GetInt(Key1, 0);
        Player2IconIndex = PlayerPrefs.GetInt(Key2, 1);
        ClampIndices();
    }

    // Call after any change to a player's icon choice.
    public static void SavePreferences()
    {
        ClampIndices();
        PlayerPrefs.SetInt(Key1, Player1IconIndex);
        PlayerPrefs.SetInt(Key2, Player2IconIndex);
        PlayerPrefs.Save();
    }

    // Keep indices within valid bounds.
    //  - If there are fewer than two icons, both players use icon 0 (or fallback).
    //  - Otherwise clamp to [0, N-1].
    static void ClampIndices()
    {
        int n = AvailableIcons != null ? AvailableIcons.Length : 0;
        if (n < 2)
        {
            Player1IconIndex = 0;
            Player2IconIndex = 0;
            return;
        }
        Player1IconIndex = Mathf.Clamp(Player1IconIndex, 0, n - 1);
        Player2IconIndex = Mathf.Clamp(Player2IconIndex, 0, n - 1);
    }

    public static Sprite GetPlayer1Icon() => GetIcon(Player1IconIndex);
    public static Sprite GetPlayer2Icon() => GetIcon(Player2IconIndex);

    static Sprite GetIcon(int index)
    {
        EnsureLoaded();
        if (AvailableIcons != null && index >= 0 && index < AvailableIcons.Length)
            return AvailableIcons[index];
        return GetFallbackSprite();
    }

    // Sets a player's icon and saves immediately.
    public static void SetPlayer1Icon(int index)
    {
        Player1IconIndex = index;
        SavePreferences();
    }

    public static void SetPlayer2Icon(int index)
    {
        Player2IconIndex = index;
        SavePreferences();
    }

    // Fallback sprite: simple white circle generated at runtime. Lets the game run
    // even when Resources/Icons has no PNGs. Tint (red/green) is applied via
    // Image.color at the use site.
    public static Sprite GetFallbackSprite()
    {
        if (fallbackSprite != null) return fallbackSprite;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                // Soft edge (anti-aliasing) at the circle boundary.
                float a = Mathf.Clamp01(r - d);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        fallbackSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                       new Vector2(0.5f, 0.5f), 100f);
        fallbackSprite.name = "FallbackCircle";
        return fallbackSprite;
    }
}
