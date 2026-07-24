using System.Collections.Generic;
using UnityEngine;

// Last-resort fallback. Matches any boundary, so it MUST be registered last in
// ShapeRecognizer's priority list. Corresponds to the original ShapeType.Unknown,
// which scores 1 point (see ShapeRecognizer.GetPoints).
public class UnknownShape : IShapeDefinition
{
    public string Name => "Shape";
    public int Points => 1;

    public bool Matches(List<Vector2> boundary) => true;
}
