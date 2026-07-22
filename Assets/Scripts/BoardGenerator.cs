using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    public GameObject pointPrefab;  // assign the point prefab here
    public int width = 4;
    public int height = 4;
    public float spacing = 1.2f;    // distance between grid points

    void Start()
    {
        // Read settings from the main menu (when starting from there)
        width = GameSettings.BoardWidth;
        height = GameSettings.BoardHeight;
        GenerateGrid();
    }

    void GenerateGrid()
    {
        // Offset so the grid is centered on screen
        float offsetX = (width - 1) * spacing / 2f;
        float offsetY = (height - 1) * spacing / 2f;

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