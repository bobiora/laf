using UnityEngine;
using System.Collections.Generic;

public static class ShapeRecognizer
{
    // Shape types and points awarded for each. Kept as a thin wrapper over the
    // IShapeDefinition list so existing call sites (GameManager) do not change.
    public enum ShapeType
    {
        Unknown = 0,
        RightTriangle = 1,      // right triangle
        AcuteTriangle = 2,      // acute triangle
        Square = 3,             // square
        Parallelogram = 4       // parallelogram
    }

    // Registered shape definitions in priority order. The first whose Matches returns
    // true wins. Order matters:
    //   - Square before Parallelogram (a square is also a parallelogram).
    //   - AcuteTriangle before RightTriangle (large isosceles-right counts as acute).
    //   - UnknownShape last (its Matches always returns true — the fallback).
    // To add a new shape type, implement IShapeDefinition and insert it here in the
    // correct priority position. No other change to the recognizer is needed.
    static readonly List<IShapeDefinition> definitions = new List<IShapeDefinition>
    {
        new SquareShape(),
        new ParallelogramShape(),
        new AcuteTriangleShape(),
        new RightTriangleShape(),
        new UnknownShape()
    };

    // Main entry: given shape boundary points, returns the matching definition.
    public static IShapeDefinition RecognizeShape(List<PointClick> points)
    {
        // Convert to grid coordinates.
        List<Vector2> coords = new List<Vector2>();
        foreach (var p in points)
            coords.Add(new Vector2(p.gridX, p.gridY));

        // Order around the perimeter and remove collinear intermediate vertices:
        // if the player built a triangle but drew a side through an intermediate grid point,
        // the cycle has 4 vertices but is still geometrically a triangle.
        // Simplification MUST run BEFORE Matches — each shape assumes simplified input.
        coords = OrderByAngle(coords);
        coords = SimplifyPolygon(coords);

        // First matching definition wins. UnknownShape guarantees a non-null result.
        foreach (var def in definitions)
            if (def.Matches(coords))
                return def;

        return definitions[definitions.Count - 1]; // fallback (unreachable in practice)
    }

    // Backward-compatible entry: returns the ShapeType enum used by existing call sites.
    public static ShapeType Recognize(List<PointClick> points)
    {
        return ToShapeType(RecognizeShape(points));
    }

    // Maps a recognized definition back onto the legacy enum.
    static ShapeType ToShapeType(IShapeDefinition def)
    {
        switch (def)
        {
            case SquareShape _: return ShapeType.Square;
            case ParallelogramShape _: return ShapeType.Parallelogram;
            case AcuteTriangleShape _: return ShapeType.AcuteTriangle;
            case RightTriangleShape _: return ShapeType.RightTriangle;
            default: return ShapeType.Unknown;
        }
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
