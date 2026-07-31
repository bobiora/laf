using UnityEngine;

// Fits the orthographic Main Camera so the ENTIRE dot grid is visible and reachable on
// screen for any board size (Width x Height) and any resolution / aspect ratio, while
// reserving pixel margins at the top (score + turn labels) and bottom (Menu button) so the
// grid never renders underneath the persistent UI.
//
// Why the camera (and not the UI): the dots are world-space objects rendered by this
// camera (see BoardGenerator), while the score/turn/menu UI lives on a separate
// Screen Space - Overlay Canvas drawn on top. So the lever for "make the whole grid fit"
// is the camera's orthographic size and position.
//
// Margins are expressed in SCREEN PIXELS because the in-game Canvas is Constant-Pixel-Size,
// so its elements occupy a fixed pixel band regardless of resolution. Those pixel margins
// are converted to world units through the orthographic scale, which keeps the reservation
// correct on every aspect ratio and safe area (notches).
//
// Runs once when the grid is generated (BoardGenerator.Start calls Fit) and again whenever
// the resolution / orientation / safe area changes (LateUpdate resize check) — the
// world-space analog of a RectTransform's OnRectTransformDimensionsChange.
[RequireComponent(typeof(Camera))]
public class CameraFitter : MonoBehaviour
{
    [Header("Board")]
    [Tooltip("Board whose grid should be framed. Auto-found if left empty.")]
    [SerializeField] private BoardGenerator board;

    [Header("Reserved UI margins (screen pixels)")]
    [Tooltip("Space kept clear at the TOP for the score and turn labels.")]
    [SerializeField] private float topMarginPixels = 200f;
    [Tooltip("Space kept clear at the BOTTOM for the Menu button.")]
    [SerializeField] private float bottomMarginPixels = 150f;
    [Tooltip("Space kept clear at EACH side so edge dots aren't flush to the screen border.")]
    [SerializeField] private float sideMarginPixels = 40f;

    [Header("Limits")]
    [Tooltip("Camera never zooms IN past this. Keeps small boards from filling the screen " +
             "with oversized dots; this is effectively the default framing for small boards.")]
    [SerializeField] private float minOrthographicSize = 5f;
    [Tooltip("Extra world-unit padding added around the grid on all sides.")]
    [SerializeField] private float worldPadding = 0.4f;

    [Header("Readability warning")]
    [Tooltip("If the on-screen distance between adjacent dots drops below this many pixels, " +
             "a warning is logged (candidate for pan/zoom support). Fit still shows the whole grid.")]
    [SerializeField] private float minDotSpacingPixels = 44f;

    private Camera cam;

    // Last framed screen state, so LateUpdate only re-fits when something actually changed.
    private int lastWidth = -1;
    private int lastHeight = -1;
    private Rect lastSafeArea;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
    }

    void LateUpdate()
    {
        if (ScreenChanged()) Fit();
    }

    bool ScreenChanged()
    {
        Rect safe = Screen.safeArea;
        if (Screen.width == lastWidth && Screen.height == lastHeight && safe == lastSafeArea)
            return false;
        return true;
    }

    // Frames the whole grid on screen. Safe to call repeatedly (idempotent for a given
    // screen + board state). Called by BoardGenerator right after generation and by the
    // LateUpdate resize check.
    public void Fit()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (board == null)
        {
            board = FindFirstObjectByType<BoardGenerator>();
            if (board == null) return;
        }
        if (!cam.orthographic) return; // this fitter only handles orthographic 2D cameras

        // Record the screen state we're fitting for.
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        lastSafeArea = Screen.safeArea;

        // Grid extent in world units (point centers), padded so edge dots aren't flush.
        Bounds b = board.GridBounds;
        float gridW = b.size.x + 2f * worldPadding;
        float gridH = b.size.y + 2f * worldPadding;

        float screenW = Mathf.Max(1f, Screen.width);
        float screenH = Mathf.Max(1f, Screen.height);

        // Available pixel rectangle = safe area minus the reserved UI margins.
        Rect safe = Screen.safeArea;
        float xMin = safe.xMin + sideMarginPixels;
        float xMax = safe.xMax - sideMarginPixels;
        float yMin = safe.yMin + bottomMarginPixels;
        float yMax = safe.yMax - topMarginPixels;

        float availW = Mathf.Max(1f, xMax - xMin);
        float availH = Mathf.Max(1f, yMax - yMin);

        // World units per pixel required so the grid fits BOTH available dimensions.
        // Orthographic pixels are square, so a single scale k applies to both axes.
        // Larger k => grid appears smaller, so take the max ratio to satisfy the tighter axis.
        float k = Mathf.Max(gridW / availW, gridH / availH);

        // Orthographic size = half the visible world height = k * (screenH / 2).
        // Never zoom in past the minimum (keeps small boards from looking huge).
        float ortho = Mathf.Max(k * screenH / 2f, minOrthographicSize);
        cam.orthographicSize = ortho;

        // Recompute the scale after the min clamp so positioning uses the actual value.
        k = 2f * ortho / screenH;

        // Center the grid on the available rectangle's center pixel. The camera maps its
        // own world position to the screen center (W/2, H/2); a world offset (dx, dy) from
        // the camera maps to a pixel offset (dx/k, dy/k) from screen center. Solve for the
        // camera position that lands the grid center on (cx, cy).
        float cx = (xMin + xMax) * 0.5f;
        float cy = (yMin + yMax) * 0.5f;

        Vector3 gc = b.center;
        float camX = gc.x - k * (cx - screenW / 2f);
        float camY = gc.y - k * (cy - screenH / 2f);
        transform.position = new Vector3(camX, camY, transform.position.z);

        Debug.Log($"[CameraFitter] Fit grid {b.size.x:0.#}x{b.size.y:0.#} world @ screen " +
                  $"{Screen.width}x{Screen.height} (safe {safe.width:0}x{safe.height:0}) -> " +
                  $"orthoSize={ortho:0.##}, cam=({camX:0.##},{camY:0.##})");

        WarnIfDotsTooSmall(k);
    }

    // Logs a warning if adjacent dots end up too close to comfortably tap. The whole grid is
    // still shown; this only flags that pan/zoom support may be worth adding for this board.
    void WarnIfDotsTooSmall(float worldUnitsPerPixel)
    {
        if (board.Spacing <= 0f || worldUnitsPerPixel <= 0f) return;
        float dotSpacingPixels = board.Spacing / worldUnitsPerPixel;
        if (dotSpacingPixels < minDotSpacingPixels)
        {
            Debug.LogWarning(
                $"[CameraFitter] Adjacent dots are ~{dotSpacingPixels:0} px apart " +
                $"(target >= {minDotSpacingPixels} px). The whole grid still fits, but dots may " +
                $"be hard to tap at this board size / resolution — consider pan/zoom support.");
        }
    }
}
