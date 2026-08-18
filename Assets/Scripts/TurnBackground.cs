using UnityEngine;

/// <summary>
/// Subtle per-turn background gradient. Attached to the Main Camera, it spawns a single
/// world-space sprite quad that fills the camera view and sits BEHIND every gameplay
/// sprite (sorting order -10, below claimed fills at -1), so dots, lines and fills always
/// render on top. The quad shows a vertical gradient — the current player's color at the
/// bottom of the screen fading to fully transparent by mid-screen — and eases between red
/// and green when the turn changes.
///
/// Nothing is placed on the Screen Space Overlay canvas, so the UI (and the board) is
/// never covered. The gradient texture is generated in code (no asset dependency) and the
/// quad follows the camera every LateUpdate, so CameraFitter's orthographic-size/position
/// changes are tracked automatically.
///
/// Auto-added at runtime by GameManager.Awake (no manual scene wiring required); add it to
/// the Main Camera in the scene only if you want the fields editable in the inspector.
/// </summary>
[RequireComponent(typeof(Camera))]
public class TurnBackground : MonoBehaviour
{
    // Max opacity of the player color at the very bottom of the screen.
    [SerializeField] private float bottomAlpha = 0.30f;
    // Normalized screen height (0 = bottom, 1 = top) where the gradient reaches alpha 0.
    // 0.5 keeps the whole upper half of the board completely clear.
    [SerializeField] private float fadeEndNormalized = 0.5f;
    // Ease speed of the color lerp on a turn change (higher = snappier).
    [SerializeField] private float lerpSpeed = 8f;
    // Log the setup once so it's easy to confirm the effect is live.
    [SerializeField] private bool debugLog = false;

    // Draw behind claimed fills (-1), lines (0), preview (5) and dots (10).
    private const int GradientSortingOrder = -10;
    // Vertical resolution of the generated 1xN gradient texture.
    private const int GradientResolution = 64;
    // Slight overscan so the quad never leaves a gap at the screen edges.
    private const float Overscan = 1.05f;

    private Camera cam;
    private SpriteRenderer sr;
    private Color currentColor;   // lerped RGBA actually pushed to the sprite
    private float builtFadeEnd = -1f; // last fadeEndNormalized baked into the texture

    void Awake()
    {
        cam = GetComponent<Camera>();

        // Own child sprite so it moves/cleans up with the camera and never touches the UI.
        var go = new GameObject("TurnBackgroundGradient");
        go.transform.SetParent(transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        // Unlit sprite shader so the 2D lighting setup can never render it black.
        sr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        sr.sortingOrder = GradientSortingOrder;
    }

    void Start()
    {
        RebuildGradientIfNeeded();
        currentColor = TargetColor();
        sr.color = currentColor;
        FitToCamera();
        if (debugLog)
            Debug.Log($"[TurnBackground] Gradient ready. sortingOrder={GradientSortingOrder}, bottomAlpha={bottomAlpha}, fadeEnd={fadeEndNormalized}, color={currentColor}");
    }

    void LateUpdate()
    {
        if (cam == null || sr == null) return;

        // Allow live inspector tuning of the fade profile.
        RebuildGradientIfNeeded();

        // Ease toward the current player's tint; the target only moves on SwitchPlayer, so
        // an extra turn (same player) leaves the gradient unchanged.
        currentColor = Color.Lerp(currentColor, TargetColor(), lerpSpeed * Time.deltaTime);
        sr.color = currentColor;

        // Track the camera (CameraFitter changes ortho size/position to frame the grid).
        FitToCamera();
    }

    // Current player color with the configured bottom opacity as its alpha.
    private Color TargetColor()
    {
        Color c = GameManager.Instance != null ? GameManager.Instance.GetCurrentColor() : Color.white;
        c.a = bottomAlpha;
        return c;
    }

    // Stretch and center the quad over the orthographic camera's visible rectangle.
    private void FitToCamera()
    {
        float worldHeight = cam.orthographicSize * 2f;
        float worldWidth = worldHeight * cam.aspect;

        Vector3 spriteSize = sr.sprite.bounds.size; // world size at scale 1
        sr.transform.localScale = new Vector3(
            worldWidth * Overscan / spriteSize.x,
            worldHeight * Overscan / spriteSize.y,
            1f);

        // Centered on the view; z on the world plane (sorting order handles depth, not z).
        Vector3 p = cam.transform.position;
        sr.transform.position = new Vector3(p.x, p.y, 0f);
    }

    // Bake a white 1xN texture whose alpha is 1 at the bottom and fades to 0 at
    // fadeEndNormalized. Player color and bottomAlpha are applied via SpriteRenderer.color,
    // so only the fade profile lives in the texture.
    private void RebuildGradientIfNeeded()
    {
        if (Mathf.Approximately(builtFadeEnd, fadeEndNormalized) && sr.sprite != null) return;
        builtFadeEnd = fadeEndNormalized;

        var tex = new Texture2D(1, GradientResolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float fadeEnd = Mathf.Clamp01(fadeEndNormalized);
        for (int y = 0; y < GradientResolution; y++)
        {
            float t = GradientResolution > 1 ? y / (float)(GradientResolution - 1) : 0f; // 0 bottom → 1 top
            float a = fadeEnd <= 0f ? 0f : Mathf.Clamp01(1f - t / fadeEnd);
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, GradientResolution),
            new Vector2(0.5f, 0.5f), GradientResolution);
        sr.sprite = sprite;
    }
}
