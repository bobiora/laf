# Mechanics — rules as checkable invariants

Each rule cites the method that enforces it. All in `Assets/Scripts/GameManager.cs` unless noted.

## Adjacency

- **INV:** two dots are adjacent iff `max(|dx|,|dy|) == 1` (8-directional) and not the same dot.
- Enforced: `AreAdjacent(a,b)`.
- Consequence: a long line through an intermediate dot (e.g. (0,2)–(2,0) through (1,1)) is **not** adjacent → rejected.

## Legal move — strict order (early-exit)

`IsLegalMove(a,b)` returns true only if, in order:

1. `a`, `b` non-null and `a != b`.
2. `AreAdjacent(a,b)` — else `[Reject:NotAdjacent]`.
3. `!EdgeExists(a,b)` — else `[Reject:EdgeExists]`. (O(1) via `edgeSet`.)
4. `!WouldCrossExistingEdge(a,b)` — else `[Reject:Crossing]`.
5. `!IsInsideAnyClaimedArea(a,b)` — else `[Reject:InClaimed]`.

- Rejection is logged by `LogRejection` (only when `IsLegalMove` already returned false). Turn/score unchanged.
- `[Reject:*]` logs are **always on** (they fire only on an actual rejection).

## Crossing

- **INV:** a shared endpoint is **not** a crossing.
- Enforced: `SegmentsIntersect` early-outs when any endpoint coincides (`Approximately`, eps 1e-4). Otherwise standard orientation test.
- `EdgeCrossesExisting` skips edges sharing a vertex with the candidate before the segment test.

## Claimed-area blocking

- **INV:** a move is illegal if its segment enters the **interior** of a claimed region; running **along** a border or sharing a vertex is allowed.
- Enforced: `IsInsideAnyClaimedArea` samples the segment at 25/50/75%. A sample on a border edge (`IsPointOnPolygonBorder`) is skipped; interior samples use `PolygonFill.PointInPolygon`. Regions whose border includes this exact edge are skipped (`IsEdgeOnBorder`).

## Commit timing

- **INV:** a line is committed **only on pointer release** (drag) — never mid-drag.
- Enforced: `InputController.HandleDragRelease` is the sole caller of commit in the drag path. Tap-tap commits on the second tap (`GameManager.OnPointClicked` → `TryCommitLine`).
- **INV:** dragging back onto the start dot cancels the pending target but keeps the drag alive (`InputController.UpdateDrag` cancel branch).

## Face detection & the two "not a real cell" guards

After `CommitLine` draws the edge, `FindAllNewClosedShapes(a,b)` traces at most two new bounded faces (one per side). For each face, in `CommitLine`:

- **GUARD 1 — `shape.Count > 4`:** more than 4 boundary dots (a shape stretched through intermediate grid points) → **skip** (no fill/score). Counted **before** collinear simplification.
- **GUARD 2 — `EnclosesGridPoint(shape)`:** a free grid point lies strictly inside → unfinished region → **skip**.
- **Unknown:** `ShapeRecognizer.Recognize(shape) == Unknown` → **skip** (leave splittable).
- **Already closed:** `ShapeAlreadyClosed` (normalized compare) → **skip** (idempotent).
- Otherwise: add to `closedShapes`, `AddScore`, `ClaimRegion`.

## Bounded face test

- **INV:** a bounded (real) face has signed area **> 0** in grid coords (CCW, y-up). The infinite outer face is CW/negative and discarded.
- Enforced: `TryAddFace` drops `area <= 1e-4` (`SignedAreaGrid`).

## Extra turn

- **INV:** the current player moves again **iff** at least one scorable face was claimed this move.
- Enforced: `CommitLine` calls `SwitchPlayer()` only when `!gotShape`.

## Haptics

- **INV:** exactly one buzz per committed move: shape buzz if `gotShape`, else line buzz. (`CommitLine`, `HapticFeedback.Vibrate*`.)

## Game over

- **INV:** game ends when no legal move remains.
- Enforced: after each commit, `AnyLegalMoveRemains()` (fast-path re-check of `cachedLegalA/B`, else bounded 8-neighbor scan). If none, `EndGame()` → `GameOverUI.Show(winner/draw)`.

## Shape classification & scoring

`ShapeRecognizer.Recognize` orders boundary by angle (`OrderByAngle`) then `SimplifyPolygon` (removes collinear intermediates) **before** matching. First `Matches` in the priority list wins.

| Shape | Class | Points | Vertices after simplify | Notes |
|-------|-------|:--:|:--:|-------|
| Square | `SquareShape` | 3 | 4 | before Parallelogram (a square is also a parallelogram) |
| Parallelogram | `ParallelogramShape` | 4 | 4 | opposite sides equal & parallel |
| Acute triangle | `AcuteTriangleShape` | 2 | 3 | no right/obtuse angle; **also** claims large isosceles-right (equal legs, leg² > 1.01) |
| Right triangle | `RightTriangleShape` | 1 | 3 | has ~90° angle, excluding the large isosceles-right case |
| Unknown | `UnknownShape` | 1* | any | fallback (Matches always true); **but faces classified Unknown are not filled/scored** — `CommitLine` skips them |

`*` `UnknownShape.Points == 1`, but Unknown never reaches scoring because `CommitLine` skips it. Points live in each `IShapeDefinition.Points` **and** `ShapeRecognizer.GetPoints` (kept in sync).
