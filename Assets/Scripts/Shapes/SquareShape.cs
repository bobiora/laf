using System.Collections.Generic;
using UnityEngine;

// Square: a parallelogram with all sides equal and adjacent sides perpendicular.
// Registered BEFORE ParallelogramShape so that squares are recognized as squares.
// Input is assumed already ordered around the perimeter and simplified.
public class SquareShape : IShapeDefinition
{
    public string Name => "Square";
    public int Points => 3;

    public bool Matches(List<Vector2> boundary)
    {
        if (boundary.Count != 4) return false;

        // Side vectors around the perimeter.
        Vector2 s1 = boundary[1] - boundary[0];
        Vector2 s2 = boundary[2] - boundary[1];
        Vector2 s3 = boundary[3] - boundary[2];
        Vector2 s4 = boundary[0] - boundary[3];

        // Parallelogram: opposite sides equal and parallel (s1 == -s3, s2 == -s4).
        bool parallelogram = (s1 + s3).sqrMagnitude < 0.01f
                          && (s2 + s4).sqrMagnitude < 0.01f;
        if (!parallelogram) return false;

        // All sides equal + adjacent sides perpendicular.
        float len1 = s1.sqrMagnitude;
        float len2 = s2.sqrMagnitude;

        bool equalSides = Mathf.Abs(len1 - len2) < 0.01f;
        bool rightAngle = Mathf.Abs(Vector2.Dot(s1, s2)) < 0.01f;

        return equalSides && rightAngle;
    }
}
