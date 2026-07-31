using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    public GameObject pointPrefab;  // assign the point prefab here
    public int width = 4;
    public int height = 4;
    public float spacing = 1.2f;    // distance between grid points

    // World-space bounding box of the dot grid (point centers), computed after the grid is
    // generated. CameraFitter reads this to frame the whole board on screen.
    public Bounds GridBounds { get; private set; }

    // Distance between adjacent grid points in world units. Exposed so CameraFitter can
    // reason about on-screen dot spacing (readability check).
    public float Spacing => spacing;

    void Start()
    {
        // Read settings from the main menu (when starting from there)
        width = GameSettings.BoardWidth;
        height = GameSettings.BoardHeight;
        GenerateGrid();

        // Frame the freshly generated grid immediately (avoids a one-frame flash before
        // CameraFitter's own resize check kicks in). Safe to call from Start: every
        // component's Awake has already run, so the camera reference is set.
        //
        // Auto-attach the fitter to the main camera if it isn't already in the scene, so the
        // fit-to-screen behavior works WITHOUT any manual inspector wiring (mirrors how
        // GameManager auto-adds InputController). Without this, forgetting to add the
        // component leaves the camera at its fixed default size and the grid overflows.
        CameraFitter fitter = FindFirstObjectByType<CameraFitter>();
        if (fitter == null)
        {
            Camera main = Camera.main;
            if (main != null) fitter = main.gameObject.AddComponent<CameraFitter>();
            else Debug.LogWarning("[BoardGenerator] No Camera.main found — cannot fit grid to screen.");
        }
        if (fitter != null) fitter.Fit();
    }

    void GenerateGrid()
    {
        // Offset so the grid is centered on screen
        float offsetX = (width - 1) * spacing / 2f;
        float offsetY = (height - 1) * spacing / 2f;

        // The grid is centered on this transform: local x spans [-offsetX, +offsetX] and
        // local y spans [-offsetY, +offsetY], so its world center is transform.position and
        // its full extent is (width-1)*spacing by (height-1)*spacing.
        GridBounds = new Bounds(
            transform.position,
            new Vector3((width - 1) * spacing, (height - 1) * spacing, 0f));

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 pos = new Vector3(x * spacing - offsetX, y * spacing - offsetY, 0);
                GameObject obj = Instantiate(pointPrefab, pos, Quaternion.identity, transform);
                obj.name = $"Point_{x}_{y}";

                PointClick pc = obj.GetComponent<PointClick>();
                pc.gridX = x;
                pc.gridY = y;

                // Points must render above lines (LineRenderer sortingOrder = 0).
                SpriteRenderer psr = obj.GetComponent<SpriteRenderer>();
                if (psr != null) psr.sortingOrder = 10;
            }
        }
    }
}