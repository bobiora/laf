using System;
using System.Collections.Generic;
using UnityEngine;

// Editor-tunable scoring balance for the game's scorable shapes.
//
// Points and display names used to be hardcoded in ShapeRecognizer.GetPoints/GetName.
// They now live here as a reusable ScriptableObject asset so balance can be tuned from
// the Inspector (or swapped between presets) without recompiling.
//
// IMPORTANT: this asset only holds SCORING data (points + names). Shape *geometry* — how
// a boundary is recognized as a square/triangle/etc. — stays in code (each IShapeDefinition
// Matches()). Do not try to move recognition here.
//
// Wiring: GameManager references a ShapeScoringConfig. If none is assigned it loads one
// from Resources ("ShapeScoringConfig"); if that is missing too, built-in defaults
// (DefaultPoints/DefaultName, matching the original values) are used, so scoring always works.
[CreateAssetMenu(
    fileName = "ShapeScoringConfig",
    menuName = "Dots and Figures/Shape Scoring Config",
    order = 0)]
public class ShapeScoringConfig : ScriptableObject
{
    // One row of the config: what a given shape type is worth and what it is called.
    [Serializable]
    public struct ShapeScore
    {
        [Tooltip("Which scorable shape type this entry configures.")]
        public ShapeRecognizer.ShapeType type;

        [Tooltip("Points awarded to the current player when a face is claimed as this shape.")]
        public int points;

        [Tooltip("Human-readable name for this shape (used in logs and any UI).")]
        public string displayName;
    }

    [Tooltip("One entry per scorable shape type. Edit points/names here to tune balance " +
             "without recompiling. If a type is missing, built-in defaults are used.")]
    [SerializeField] private List<ShapeScore> entries = new List<ShapeScore>();

    // The shape types that are actually filled and scored (Unknown is never scored).
    static readonly ShapeRecognizer.ShapeType[] ScorableTypes =
    {
        ShapeRecognizer.ShapeType.RightTriangle,
        ShapeRecognizer.ShapeType.AcuteTriangle,
        ShapeRecognizer.ShapeType.Square,
        ShapeRecognizer.ShapeType.Parallelogram
    };

    // Points awarded for a shape type: the asset entry if present, else the built-in default.
    public int GetPoints(ShapeRecognizer.ShapeType type)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].type == type)
                return entries[i].points;
        return DefaultPoints(type);
    }

    // Display name for a shape type: the asset entry if present and non-empty, else the default.
    public string GetName(ShapeRecognizer.ShapeType type)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].type == type && !string.IsNullOrEmpty(entries[i].displayName))
                return entries[i].displayName;
        return DefaultName(type);
    }

    // Built-in fallback values, identical to the original hardcoded scoring. These are the
    // single code-side source of truth for defaults; the asset only overrides them.
    public static int DefaultPoints(ShapeRecognizer.ShapeType type)
    {
        switch (type)
        {
            case ShapeRecognizer.ShapeType.RightTriangle: return 1;
            case ShapeRecognizer.ShapeType.AcuteTriangle: return 2;
            case ShapeRecognizer.ShapeType.Square: return 3;
            case ShapeRecognizer.ShapeType.Parallelogram: return 4;
            default: return 1;
        }
    }

    public static string DefaultName(ShapeRecognizer.ShapeType type)
    {
        switch (type)
        {
            case ShapeRecognizer.ShapeType.RightTriangle: return "right triangle";
            case ShapeRecognizer.ShapeType.AcuteTriangle: return "acute triangle";
            case ShapeRecognizer.ShapeType.Square: return "square";
            case ShapeRecognizer.ShapeType.Parallelogram: return "parallelogram";
            default: return "shape";
        }
    }

#if UNITY_EDITOR
    // Editor-only sanity check: warn if a scorable shape type has no entry (it will fall
    // back to defaults) or if points are negative. Never blocks — just guidance in the console.
    void OnValidate()
    {
        foreach (var type in ScorableTypes)
        {
            bool found = false;
            foreach (var e in entries)
                if (e.type == type) { found = true; break; }
            if (!found)
                Debug.LogWarning(
                    $"[ShapeScoringConfig] No entry for {type} — it will use the built-in " +
                    $"default ({DefaultPoints(type)} pts, \"{DefaultName(type)}\"). " +
                    $"Add it in '{name}' to tune it.", this);
        }

        foreach (var e in entries)
            if (e.points < 0)
                Debug.LogWarning($"[ShapeScoringConfig] {e.type} has negative points " +
                                 $"({e.points}) in '{name}'.", this);
    }
#endif
}
