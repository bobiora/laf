using System.Collections.Generic;
using UnityEngine;

// A single scorable shape type. Implementations own the geometric test that decides
// whether a boundary matches this shape. Add a new shape by creating a new
// implementation and registering it in ShapeRecognizer's priority list.
//
// Scoring (points + display name) is NOT part of this interface — it lives in the
// editor-tunable ShapeScoringConfig asset, keyed by ShapeRecognizer.ShapeType. Shape
// classes are purely about geometry/recognition.
public interface IShapeDefinition
{
    // Returns true if the given ordered boundary points match this shape's rules.
    // Points are already simplified (no collinear intermediates) and ordered around
    // the perimeter (see ShapeRecognizer.OrderByAngle / SimplifyPolygon).
    bool Matches(List<Vector2> boundary);
}
