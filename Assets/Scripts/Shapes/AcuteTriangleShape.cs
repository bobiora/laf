using System.Collections.Generic;
using UnityEngine;

// Acute triangle (worth 2 points). Matches when the triangle has no right angle and
// no obtuse angle, OR when it is a "large" isosceles right triangle (45-45-90 with
// equal legs longer than a single cell), which the original recognizer classifies as
// acute for scoring purposes. Registered BEFORE RightTriangleShape so the large
// isosceles-right case is claimed here first.
// Input is assumed already ordered around the perimeter and simplified.
public class AcuteTriangleShape : IShapeDefinition
{
    public string Name => "Acute triangle";
    public int Points => 2;

    public bool Matches(List<Vector2> boundary)
    {
        if (boundary.Count != 3) return false;

        const float tolerance = 1f;

        float[] angles = {
            AngleAt(boundary[0], boundary[1], boundary[2]),
            AngleAt(boundary[1], boundary[0], boundary[2]),
            AngleAt(boundary[2], boundary[0], boundary[1])
        };

        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(angles[i] - 90f) <= tolerance)
            {
                // Right angle at vertex i. If the two legs are equal and large
                // (45-45-90, isosceles right) it counts as acute; otherwise it is a
                // plain right triangle and belongs to RightTriangleShape.
                int j = (i + 1) % 3;
                int k = (i + 2) % 3;
                float leg1 = (boundary[j] - boundary[i]).sqrMagnitude;
                float leg2 = (boundary[k] - boundary[i]).sqrMagnitude;
                return Mathf.Abs(leg1 - leg2) < 0.01f && leg1 > 1.01f;
            }
        }

        // No right angle: an obtuse angle disqualifies it (that is Unknown territory).
        foreach (float a in angles)
            if (a >= 90f - tolerance)
                return false;

        return true;
    }

    // Angle at vertex between edges to neighbors n1 and n2 (degrees).
    static float AngleAt(Vector2 vertex, Vector2 n1, Vector2 n2)
    {
        return Vector2.Angle(n1 - vertex, n2 - vertex);
    }
}
