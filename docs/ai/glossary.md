# Glossary

One precise meaning per term. Aliases noted. No fuzzy synonyms.

| Term | Meaning |
|------|---------|
| **Point** / **dot** | A grid node. The component is `PointClick` (`Assets/Scripts/PointClick.cs`). "dot" = `PointClick`. Holds `gridX`/`gridY` and visual state only; it does **not** read input. |
| **Grid coords** | Integer `(gridX, gridY)` on a `PointClick`. The **source of truth** for geometry (adjacency, area membership). |
| **World coords** | `transform.position` of a dot. Used only for rendering and segment-intersection math. |
| **Edge** / **line** | A committed connection between two adjacent dots. Stored in `GameManager.edges` as a `(PointClick, PointClick)` pair; drawn as a `Line.prefab` LineRenderer. |
| **Adjacent (8-dir)** | `max(|dx|,|dy|) == 1` in grid coords — orthogonal **and** diagonal neighbors. Enforced by `GameManager.AreAdjacent`. |
| **Legal move** | A candidate edge passing all of `GameManager.IsLegalMove`: adjacent → edge doesn't exist → doesn't cross an existing edge → doesn't pass through a claimed region. |
| **Face** | A minimal bounded cell of the planar graph of edges. Traced by `GameManager.TraceFace`; kept only if signed area > 0. |
| **TraceFace** | `GameManager.TraceFace(start, prev)` — walks a face by taking the **next-clockwise** neighbor at each vertex until the directed start edge recurs. |
| **Claimed region** | A scored face, filled with the owner's semi-transparent color. `GameManager.ClaimedRegion` (boundaryPoints, boundaryWorld, owner, visual). Lines cannot pass through its interior. |
| **Scorable shape** | A face classified as Square / Parallelogram / Acute triangle / Right triangle. Only these are filled and scored. |
| **Unknown shape** | `ShapeRecognizer.ShapeType.Unknown` (e.g. trapezoid). Deliberately **not** filled or scored, so it stays splittable. Note: `UnknownShape.Points` is 1, but `GameManager.CommitLine` skips Unknown before scoring. |
| **Extra turn** | The current player moves again iff at least one scorable face was claimed this move. `GameManager.SwitchPlayer` runs only when `!gotShape`. |
| **Drag-to-draw** | Primary input: press a start dot, drag; preview snaps onto a valid neighbor; commit **on release**. `InputController` `Dragging` state. |
| **Tap-tap** | Fallback input: tap dot A, then tap a neighbor. Tapping A twice deselects. `GameManager.OnPointClicked`. |
| **Preview line** | The single reused semi-transparent `LineRenderer` shown mid-drag (`GameManager.PreviewLine`, sorting order 5). |
| **currentTarget** | (`InputController`) the sticky pending neighbor a drag will commit on release; overwritten as the finger re-aims, cleared when the finger returns to the start dot. |
| **originPoint** | (`InputController`) the dot the current press/drag started from; the committed line always starts here. |
| **GridBounds** | World-space bounding box of dot centers (`BoardGenerator.GridBounds`); read by `CameraFitter` to frame the board. |
| **GameSettings** | Static `BoardWidth`/`BoardHeight` (3–10, default 4×4) carried from menu into the Game scene. `Assets/Scripts/GameSettings.cs`. |
| **claimedRegions** | `GameManager` list of all `ClaimedRegion`s so far; sampled by `IsInsideAnyClaimedArea`. |
| **closedShapes** | `GameManager` list of already-scored faces (normalized), so a cell is never scored twice. |
| **EdgeKey** | Normalized `(int,int,int,int)` grid-coord key for an undirected edge; backs the O(1) `edgeSet`. |
| **debugEndGame** | `GameManager` opt-in flag (default false) gating verbose `[EndGameCheck]`/`[Face]`/`[Diag]` logs. Keep it; ship with false. |
| **debugInput** | `InputController` opt-in flag (default false) gating `[Swipe:*]` logs. |
