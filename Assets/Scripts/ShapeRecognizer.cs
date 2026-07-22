using UnityEngine;
using System.Collections.Generic;

public static class ShapeRecognizer
{
    // Shape types and points awarded for each
    public enum ShapeType
    {
        Unknown = 0,
        RightTriangle = 1,      // right triangle
        AcuteTriangle = 2,      // acute triangle
        Square = 3,             // square
        Parallelogram = 4       // parallelogram
    }

    // Main entry: given shape boundary points, returns its type
    public static ShapeType Recognize(List<PointClick> points)
    {
        // Convert to grid coordinates
        List<Vector2> coords = new List<Vector2>();
        foreach (var p in points)
            coords.Add(new Vector2(p.gridX, p.gridY));

        // Order around the perimeter and remove collinear intermediate vertices:
        // if the player built a triangle but drew a side through an intermediate grid point,
        // the cycle has 4 vertices but is still geometrically a triangle.
        coords = OrderByAngle(coords);
        coords = SimplifyPolygon(coords);

        if (coords.Count == 3)
            return RecognizeTriangle(coords);
        else if (coords.Count == 4)
            return RecognizeQuadrilateralOrdered(coords);

        return ShapeType.Unknown;
    }

    // Removes points lying on the line between cycle neighbors.
    // For each P with neighbors A and B: if (P-A) x (B-P) ≈ 0, then P is on segment AB.
    public static List<Vector2> SimplifyPolygon(List<Vector2> pts)
    {
        List<Vector2> result = new List<Vector2>(pts);
        bool changed = true;
        while (changed && result.Count > 3)
        {
            changed = false;
            for (int i = 0; i < result.Count; i++)
            {
                Vector2 prev = result[(i - 1 + result.Count) % result.Count];
                Vector2 cur = result[i];
                Vector2 next = result[(i + 1) % result.Count];
                Vector2 v1 = cur - prev;
                Vector2 v2 = next - cur;
                float cross = v1.x * v2.y - v1.y * v2.x;
                if (Mathf.Abs(cross) < 0.01f)
                {
                    result.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        return result;
    }

    // --- Triangles ---
    // Compute three angles directly and classify:
    //   - at least one angle ≈ 90° (±1°) → right
    //   - all three angles < 90° (same tolerance) → acute
    //   - otherwise (obtuse present) → Unknown
    static ShapeType RecognizeTriangle(List<Vector2> pts)
    {
        const float tolerance = 1f;

        float[] angles = {
            AngleAt(pts[0], pts[1], pts[2]),
            AngleAt(pts[1], pts[0], pts[2]),
            AngleAt(pts[2], pts[0], pts[1])
        };

        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(angles[i] - 90f) <= tolerance)
            {
                // Right angle at vertex i. If the two legs are equal — 45-45-90
                // (isosceles right, visually "acute") → AcuteTriangle.
                int j = (i + 1) % 3;
                int k = (i + 2) % 3;
                float leg1 = (pts[j] - pts[i]).sqrMagnitude;
                float leg2 = (pts[k] - pts[i]).sqrMagnitude;
                if (Mathf.Abs(leg1 - leg2) < 0.01f && leg1 > 1.01f)
                    return ShapeType.AcuteTriangle; // large isosceles right triangle
                return ShapeType.RightTriangle;     // including basic 1x1 half-cell
            }
        }

        foreach (float a in angles)
            if (a >= 90f - tolerance)
                return ShapeType.Unknown; // obtuse — 1 point by default

        return ShapeType.AcuteTriangle;
    }

    // Angle at vertex between edges to neighbors n1 and n2 (degrees)
    static float AngleAt(Vector2 vertex, Vector2 n1, Vector2 n2)
    {
        return Vector2.Angle(n1 - vertex, n2 - vertex);
    }

    // --- Quadrilaterals ---
    // Input points are already ordered around the perimeter (see Recognize).
    static ShapeType RecognizeQuadrilateralOrdered(List<Vector2> ordered)
    {
        // Side vectors
        Vector2 s1 = ordered[1] - ordered[0];
        Vector2 s2 = ordered[2] - ordered[1];
        Vector2 s3 = ordered[3] - ordered[2];
        Vector2 s4 = ordered[0] - ordered[3];

        // Parallelogram: opposite sides equal and parallel
        // (s1 == -s3) means s1 + s3 == 0
        bool parallelogram = (s1 + s3).sqrMagnitude < 0.01f
                          && (s2 + s4).sqrMagnitude < 0.01f;

        if (!parallelogram)
            return ShapeType.Unknown;

        // Square: all sides equal + adjacent sides perpendicular
        float len1 = s1.sqrMagnitude;
        float len2 = s2.sqrMagnitude;

        bool equalSides = Mathf.Abs(len1 - len2) < 0.01f;
        bool rightAngle = Mathf.Abs(Vector2.Dot(s1, s2)) < 0.01f;

        if (equalSides && rightAngle)
            return ShapeType.Square;

        return ShapeType.Parallelogram;
    }

    // Order points by angle around their centroid (clockwise)
    static List<Vector2> OrderByAngle(List<Vector2> pts)
    {
        Vector2 center = Vector2.zero;
        foreach (var p in pts) center += p;
        center /= pts.Count;

        List<Vector2> sorted = new List<Vector2>(pts);
        sorted.Sort((a, b) =>
        {
            float angA = Mathf.Atan2(a.y - center.y, a.x - center.x);
            float angB = Mathf.Atan2(b.y - center.y, b.x - center.x);
            return angA.CompareTo(angB);
        });
        return sorted;
    }

    // Points awarded for a shape type
    public static int GetPoints(ShapeType type)
    {
        switch (type)
        {
            case ShapeType.RightTriangle: return 1;
            case ShapeType.AcuteTriangle: return 2;
            case ShapeType.Square: return 3;
            case ShapeType.Parallelogram: return 4;
            default: return 1;
        }
    }

    public static string GetName(ShapeType type)
    {
        switch (type)
        {
            case ShapeType.RightTriangle: return "right triangle";
            case ShapeType.AcuteTriangle: return "acute triangle";
            case ShapeType.Square: return "square";
            case ShapeType.Parallelogram: return "parallelogram";
            default: return "shape";
        }
    }
}
