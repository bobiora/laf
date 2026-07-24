using System.Collections.Generic;
using UnityEngine;

// Parallelogram: opposite sides equal and parallel. A square also satisfies this,
// so SquareShape MUST be registered before this one in ShapeRecognizer's priority
// list — squares are caught first, and only non-square parallelograms reach here.
// Input is assumed already ordered around the perimeter and simplified.
public class ParallelogramShape : IShapeDefinition
{
    public string Name => "Parallelogram";
    public int Points => 4;

    public bool Matches(List<Vector2> boundary)
    {
        if (boundary.Count != 4) return false;

        // Side vectors around the perimeter.
        Vector2 s1 = boundary[1] - boundary[0];
        Vector2 s2 = boundary[2] - boundary[1];
        Vector2 s3 = boundary[3] - boundary[2];
        Vector2 s4 = boundary[0] - boundary[3];

        // Opposite sides equal and parallel (s1 == -s3, s2 == -s4).
        return (s1 + s3).sqrMagnitude < 0.01f
            && (s2 + s4).sqrMagnitude < 0.01f;
    }
}
