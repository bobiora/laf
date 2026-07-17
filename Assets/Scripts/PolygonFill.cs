using UnityEngine;
using System.Collections.Generic;

// Строит закрашенный полигон из списка вершин в мировых координатах.
// Использует простое ушное отсечение (ear clipping). Полигон предполагается простым
// (без самопересечений); порядок вершин может быть как CW, так и CCW — при
// необходимости он инвертируется.
public static class PolygonFill
{
    public static GameObject Create(List<Vector2> worldBoundary, Color color, int sortingOrder, Transform parent = null)
    {
        if (worldBoundary == null || worldBoundary.Count < 3) return null;

        List<Vector2> verts = new List<Vector2>(worldBoundary);
        if (SignedArea(verts) < 0) verts.Reverse(); // приводим к CCW

        int[] triangles = EarClip(verts);
        if (triangles == null || triangles.Length == 0) return null;

        Vector3[] v3 = new Vector3[verts.Count];
        for (int i = 0; i < verts.Count; i++) v3[i] = new Vector3(verts[i].x, verts[i].y, 0f);

        Mesh mesh = new Mesh();
        mesh.vertices = v3;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new GameObject("ShapeFill");
        if (parent != null) go.transform.SetParent(parent, false);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.color = color;
        mr.sharedMaterial = mat;
        mr.sortingOrder = sortingOrder;

        return go;
    }

    static float SignedArea(List<Vector2> pts)
    {
        float a = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i];
            Vector2 q = pts[(i + 1) % pts.Count];
            a += (q.x - p.x) * (q.y + p.y);
        }
        return -a * 0.5f; // положительная для CCW
    }

    static int[] EarClip(List<Vector2> verts)
    {
        int n = verts.Count;
        List<int> indices = new List<int>(n);
        for (int i = 0; i < n; i++) indices.Add(i);

        List<int> triangles = new List<int>();
        int guard = 0;
        int maxGuard = n * n * 4;
        while (indices.Count > 3 && guard++ < maxGuard)
        {
            bool clipped = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                int i1 = indices[i];
                int i2 = indices[(i + 1) % indices.Count];

                Vector2 a = verts[i0];
                Vector2 b = verts[i1];
                Vector2 c = verts[i2];

                // Треугольник должен быть выпуклым в CCW-полигоне (cross > 0).
                if (Cross(b - a, c - b) <= 0f) continue;

                bool containsOther = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    if (idx == i0 || idx == i1 || idx == i2) continue;
                    if (PointInTriangle(verts[idx], a, b, c)) { containsOther = true; break; }
                }
                if (containsOther) continue;

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);
                indices.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // страховка от вырожденной геометрии
        }

        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }

        return triangles.ToArray();
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    // Стандартный ray-casting: возвращает true, если точка p строго внутри полигона.
    public static bool PointInPolygon(Vector2 p, List<Vector2> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = polygon[i];
            Vector2 pj = polygon[j];
            if (((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-12f) + pi.x))
                inside = !inside;
        }
        return inside;
    }
}
