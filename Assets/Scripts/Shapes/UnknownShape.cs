using System.Collections.Generic;
using UnityEngine;

// Last-resort fallback. Matches any boundary, so it MUST be registered last in
// ShapeRecognizer's priority list. Corresponds to ShapeType.Unknown. Note: faces that
// classify as Unknown are deliberately NOT filled or scored by GameManager (they stay
// splittable), so no scoring entry is needed for it.
public class UnknownShape : IShapeDefinition
{
    public bool Matches(List<Vector2> boundary) => true;
}
