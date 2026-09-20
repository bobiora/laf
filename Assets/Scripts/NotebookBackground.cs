using UnityEngine;

/// <summary>
/// Squared-exercise-book paper behind the board. Attached to the Main Camera, it fills the
/// orthographic view with warm off-white paper ruled by pale blue grid lines whose
/// intersections sit exactly on the gameplay dots — the player is drawing on notebook paper,
/// and the paper continues past the playable dots (extra cells are visible), like a real page.
///
/// How the alignment stays exact:
///   * ONE cell is baked into a small repeating Texture2D (wrapMode = Repeat). The line is
///     centered on the tile wrap (half the stroke on each opposite edge), so integer UVs land
///     on line intersections.
///   * A world-space quad is stretched to cover the camera (like TurnBackground.FitToCamera),
///     and its UVs are computed from WORLD corners every fit:  uv = (worldXY - Origin) / spacing.
///     Origin is the center of dot (0,0) and spacing is the dot spacing (both from
///     BoardGenerator), so one UV unit == one grid cell regardless of camera zoom/position.
///     This keeps the ruling locked to the dots at every board size (3-7) and after every
///     CameraFitter change (resolution / orientation / safe area).
///
/// Drawn at sortingOrder -20, behind TurnBackground (-10), claimed fills (-1), lines (0),
/// preview (5) and dots (10). Generated entirely in code — no PNGs, no Shader Graph, no art.
///
/// Auto-added at runtime by BoardGenerator (no manual scene wiring required); add it to the
/// Main Camera in the scene only if you want the fields editable in the inspector.
/// </summary>
[RequireComponent(typeof(Camera))]
public class NotebookBackground : MonoBehaviour
{
    [Header("Paper")]
    [Tooltip("Warm off-white page color (also set as the camera clear color so gaps never flash).")]
    [SerializeField] private Color paperColor = new Color(0.965f, 0.945f, 0.894f, 1f); // #F6F1E4
    [Tooltip("Pale school-notebook blue for the ruled grid lines.")]
    [SerializeField] private Color lineColor = new Color(0.655f, 0.769f, 0.847f, 1f);   // #A7C4D8
    [Tooltip("Grid line thickness in tile pixels (soft-filtered when stretched on screen).")]
    [SerializeField] private float strokePixels = 3f;

    [Header("Red margin line (classic notebook)")]
    [Tooltip("Draw a faint vertical red margin line just left of the playable grid.")]
    [SerializeField] private bool showMarginLine = true;
    [Tooltip("How many cells to the left of column 0 the margin line sits.")]
    [SerializeField] private float marginCellsLeft = 1f;
    [Tooltip("Faint red used for the margin line.")]
    [SerializeField] private Color marginColor = new Color(0.85f, 0.36f, 0.36f, 0.5f);

    // Draw behind TurnBackground (-10), fills (-1), lines (0), preview (5), dots (10).
    private const int PaperSortingOrder = -20;
    private const int MarginSortingOrder = -19;
    // One-cell tile resolution. Small is fine: it just repeats.
    private const int TileResolution = 64;
    // Slight overscan so the quad never leaves a gap at the screen edges.
    private const float Overscan = 1.05f;

    // --- Public color/appearance API -----------------------------------------------------
    // Change these from code at runtime (e.g. NotebookBackground.Instance.LineColor = ...).
    // The setters flag a rebake; the next Fit() (every LateUpdate) regenerates the tile, so
    // the change shows up on the following frame without any extra call.
    // (You can also edit the same values in the inspector — see the class comment.)

    /// <summary>Pale color of the ruled grid lines.</summary>
    public Color LineColor
    {
        get => lineColor;
        set => lineColor = value;
    }

    /// <summary>Warm off-white page color (also kept in sync as the camera clear color).</summary>
    public Color PaperColor
    {
        get => paperColor;
        set
        {
            paperColor = value;
            if (cam != null) { Color c = value; c.a = 1f; cam.backgroundColor = c; }
        }
    }

    /// <summary>Grid line thickness in tile pixels.</summary>
    public float StrokePixels
    {
        get => strokePixels;
        set => strokePixels = value;
    }

    /// <summary>Color of the optional left-hand red margin line.</summary>
    public Color MarginColor
    {
        get => marginColor;
        set => marginColor = value;
    }

    /// <summary>Convenience: set paper and line color together (e.g. a theme swap).</summary>
    public void SetColors(Color paper, Color line)
    {
        PaperColor = paper;
        LineColor = line;
    }

    private Camera cam;
    private BoardGenerator board;

    private MeshFilter paperFilter;
    private MeshRenderer paperRenderer;
    private Mesh paperMesh;

    private SpriteRenderer margin;

    // Last bake inputs, so the tile is only regenerated when something actually changed.
    private Color builtPaper;
    private Color builtLine;
    private float builtStroke = -1f;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();

        BuildPaperQuad();
        BuildMargin();

        // Match the camera clear color to the paper so no blue ever flashes in gaps.
        cam.clearFlags = CameraClearFlags.SolidColor;
        Color clear = paperColor; clear.a = 1f;
        cam.backgroundColor = clear;
    }

    // Called by BoardGenerator once the grid (and thus Origin/spacing) exists.
    public void SetBoard(BoardGenerator b)
    {
        board = b;
        Fit();
    }

    void Start()
    {
        Fit();
    }

    void LateUpdate()
    {
        // CameraFitter re-frames the grid on resolution/orientation/safe-area changes; track it.
        Fit();
    }

    // Stretch the paper quad over the camera view and re-derive its UVs from world corners so
    // the ruling stays locked to the dots. Also positions the margin line.
    public void Fit()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (board == null)
        {
            board = FindFirstObjectByType<BoardGenerator>();
            if (board == null) return;
        }
        if (!cam.orthographic || paperMesh == null) return;

        RebuildTileIfNeeded();

        float spacing = board.Spacing;
        if (spacing <= 0f) return;
        Vector2 origin = board.Origin;

        Vector3 camPos = cam.transform.position;
        float worldHeight = cam.orthographicSize * 2f;
        float worldWidth = worldHeight * cam.aspect;
        float hx = worldWidth * Overscan * 0.5f;
        float hy = worldHeight * Overscan * 0.5f;

        // The quad's own object sits at the camera XY on the world plane (z = 0); sortingOrder
        // (not z) controls depth. Vertices are in the quad's LOCAL space (corners around 0).
        paperFilter.transform.position = new Vector3(camPos.x, camPos.y, 0f);

        Vector3[] verts =
        {
            new Vector3(-hx, -hy, 0f),
            new Vector3( hx, -hy, 0f),
            new Vector3( hx,  hy, 0f),
            new Vector3(-hx,  hy, 0f),
        };

        // UVs from WORLD positions: one UV unit == one grid cell, anchored on dot (0,0).
        Vector2[] uvs = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            Vector2 world = new Vector2(camPos.x + verts[i].x, camPos.y + verts[i].y);
            uvs[i] = (world - origin) / spacing;
        }

        paperMesh.vertices = verts;
        paperMesh.uv = uvs;
        paperMesh.RecalculateBounds();

        FitMargin(origin, spacing, camPos, hy);
    }

    // ---- construction -------------------------------------------------------------------

    private void BuildPaperQuad()
    {
        var go = new GameObject("NotebookPaper");
        go.transform.SetParent(transform, false); // parented so it cleans up with the camera
        paperFilter = go.AddComponent<MeshFilter>();
        paperRenderer = go.AddComponent<MeshRenderer>();
        paperRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        paperRenderer.sortingOrder = PaperSortingOrder;
        paperRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        paperRenderer.receiveShadows = false;

        paperMesh = new Mesh { name = "NotebookPaperQuad" };
        paperMesh.vertices = new Vector3[4];
        paperMesh.uv = new Vector2[4];
        paperMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        paperMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        paperFilter.sharedMesh = paperMesh;
    }

    private void BuildMargin()
    {
        var go = new GameObject("NotebookMarginLine");
        go.transform.SetParent(transform, false);
        margin = go.AddComponent<SpriteRenderer>();
        margin.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        margin.sprite = MakeWhitePixelSprite();
        margin.color = marginColor;
        margin.sortingOrder = MarginSortingOrder;
        margin.enabled = showMarginLine;
    }

    private void FitMargin(Vector2 origin, float spacing, Vector3 camPos, float halfHeight)
    {
        if (margin == null) return;
        margin.enabled = showMarginLine;
        if (!showMarginLine) return;

        margin.color = marginColor;
        float x = origin.x - marginCellsLeft * spacing;
        // Center vertically on the view and cover its full (overscanned) height.
        margin.transform.position = new Vector3(x, camPos.y, 0f);

        Vector3 size = margin.sprite.bounds.size; // world size at scale 1
        float thickness = spacing * 0.05f;        // thin, proportional to a cell
        margin.transform.localScale = new Vector3(
            thickness / size.x,
            (halfHeight * 2f) / size.y,
            1f);
    }

    // ---- tile bake ----------------------------------------------------------------------

    // Regenerate the one-cell repeating tile only when paper/line/stroke changed.
    private void RebuildTileIfNeeded()
    {
        if (paperRenderer.sharedMaterial.mainTexture != null &&
            builtPaper == paperColor && builtLine == lineColor &&
            Mathf.Approximately(builtStroke, strokePixels))
            return;

        builtPaper = paperColor;
        builtLine = lineColor;
        builtStroke = strokePixels;

        var tex = new Texture2D(TileResolution, TileResolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        float half = Mathf.Max(0.5f, strokePixels * 0.5f);
        var pixels = new Color[TileResolution * TileResolution];
        for (int py = 0; py < TileResolution; py++)
        {
            // Distance from the tile's wrap seam (edges 0 / TileResolution) along Y.
            float cy = py + 0.5f;
            bool onH = Mathf.Min(cy, TileResolution - cy) <= half;
            for (int px = 0; px < TileResolution; px++)
            {
                float cx = px + 0.5f;
                bool onV = Mathf.Min(cx, TileResolution - cx) <= half;
                pixels[py * TileResolution + px] = (onH || onV) ? lineColor : paperColor;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        paperRenderer.sharedMaterial.mainTexture = tex;
    }

    // 1x1 opaque white sprite used (tinted) for the margin line.
    private static Sprite MakeWhitePixelSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }
}
