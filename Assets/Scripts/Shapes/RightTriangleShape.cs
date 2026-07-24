using System.Collections.Generic;
using UnityEngine;

// Right triangle (worth 1 point). Matches when the triangle has a ~90 degree angle,
// EXCEPT the "large" isosceles right triangle (equal legs longer than a single cell),
// which AcuteTriangleShape claims first. Registered AFTER AcuteTriangleShape.
// Input is assumed already ordered around the perimeter and simplified.
public class RightTriangleShape : IShapeDefinition
{
    public string Name => "Right triangle";
    public int Points => 1;

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
                // Right angle at vertex i. A large isosceles right triangle is treated
                // as acute (see AcuteTriangleShape), so exclude it here.
                int j = (i + 1) % 3;
                int k = (i + 2) % 3;
                float leg1 = (boundary[j] - boundary[i]).sqrMagnitude;
                float leg2 = (boundary[k] - boundary[i]).sqrMagnitude;
                if (Mathf.Abs(leg1 - leg2) < 0.01f && leg1 > 1.01f)
                    return false; // large isosceles right — belongs to AcuteTriangleShape
                return true;      // including the basic 1x1 half-cell
            }
        }

        return false; // no right angle
    }

    // Angle at vertex between edges to neighbors n1 and n2 (degrees).
    static float AngleAt(Vector2 vertex, Vector2 n1, Vector2 n2)
    {
        return Vector2.Angle(n1 - vertex, n2 - vertex);
    }
}
