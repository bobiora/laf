# Invariants & Pitfalls — what agents routinely break

Format: **NEVER / ALWAYS / WHY.** Each cites code.

## Input

- **NEVER** use `UnityEngine.Input`. **ALWAYS** use the new Input System (`Mouse.current`, `Touchscreen.current`). **WHY:** the project ships with the new Input System package; the old API is disabled. (`InputController.ReadPointer`.)
- **NEVER** commit a line mid-drag. **ALWAYS** commit only in `HandleDragRelease` (drag) or the second tap (`OnPointClicked`). **WHY:** the design is "commit on release"; returning to the start dot must cancel, not commit.
- **NEVER** make `PointClick` read input or hit-test itself. **WHY:** `InputController` owns all pointer logic; `GameManager.GetPointAtScreenPos` does hit-testing. `PointClick` is passive.
- **ALWAYS** route both tap-tap and drag through `GameManager.TryCommitLine`. **WHY:** identical rules for both styles.

## Face / scoring guards

- **NEVER** remove the `shape.Count > 4` guard or the `EnclosesGridPoint` guard in `CommitLine`. **WHY:** they prevent oversized/composite fills and premature scoring of unfinished regions. Removing them reintroduces the "too much fill" bug.
- **NEVER** fill or score an `Unknown` face. **ALWAYS** skip it (`CommitLine`). **WHY:** trapezoids etc. must stay splittable into valid shapes. (`UnknownShape.Points` is 1 but never reached.)
- **ALWAYS** dedup faces by **normalized sequence** (`NormalizeFace`/`FacesEqual`), not by vertex set. **WHY:** distinct faces sharing vertices must not merge.
- **ALWAYS** treat a bounded face as signed area **> 0** (grid coords, y-up); the outer face is negative. (`TryAddFace`, `SignedAreaGrid`.)

## Move validation

- **ALWAYS** keep `IsLegalMove` order: adjacent → not-exists → no-cross → not-through-claimed. **WHY:** adjacency-first blocks long lines before expensive geometry; `[Reject:*]` messages assume this order.
- **NEVER** treat a shared endpoint as a crossing. **WHY:** adjacent lines meeting at a dot must coexist (`SegmentsIntersect` early-out).
- **NEVER** count "grazing along a border" as inside a claimed area. **WHY:** legal lines may run along a claimed shape's edge (`IsPointOnPolygonBorder` guard in `IsInsideAnyClaimedArea`).

## Performance

- **NEVER** call `FindObjectsByType` in the move / drag / per-frame hot paths. **ALWAYS** use the caches: `cachedPoints`, `gridLookup` (`PointAt`), `edgeSet` (`EdgeExists`), `adjacency` (`GetNeighbors`). **WHY:** real cost on 10×10 boards.
- **ALWAYS** keep `AnyLegalMoveRemains` bounded: fast-path `cachedLegalA/B`, else scan each dot against its 8 grid neighbors only. **WHY:** it runs after every commit (and chains during multi-shape moves); O(points²) would hitch.
- **ALWAYS** keep debug logging O(1) per move. **WHY:** `debugEndGame` diagnostics must not flood the console on large boards (`DiagnoseBoard` caps examples).

## Debug flags

- **NEVER** delete `GameManager.debugEndGame` or `InputController.debugInput`. **ALWAYS** ship them **false**. **WHY:** they are the diagnostic infrastructure, opt-in only.
- `[Reject:*]` logs are always on by design (fire only on a real rejection) — leave them.

## Turns & UI

- **ALWAYS** switch player only when no shape was closed (`CommitLine` calls `SwitchPlayer` iff `!gotShape`). **WHY:** closing a shape grants another turn.
- **PITFALL:** `TurnUI.Update` **hardcodes** the score labels `"Red:"` / `"Green:"`. If player colors change (inspector `player1Color`/`player2Color`), update `TurnUI` too — the labels won't follow automatically.

## Camera / board

- **PITFALL:** `CameraFitter` always fits the **whole grid**; it only *warns* when dots get too small to tap (`WarnIfDotsTooSmall`). **Pan/zoom is a TODO**, not implemented.
- **ALWAYS** let `GameManager` auto-add `InputController`/`HapticFeedback` and `BoardGenerator` auto-add `CameraFitter`. **WHY:** zero manual wiring; don't assume they're placed in the scene.

## Conventions

- **ALWAYS** write comments, `Debug.Log`, and UI text in **English**; translate legacy strings when you touch them.
- **ALWAYS** put runtime-instantiated UI in a prefab under `Assets/Prefabs/`, not built procedurally.
- **Sorting orders** (low→high): claimed fills **−1** → lines **0** → preview **5** → dots **10**. Don't reorder.

## Stale docs

- **PITFALL:** `.claude/skills/dots-and-figures/SKILL.md` is **partly stale** — it predates `InputController`, `CameraFitter`, and the `Shapes/`, `Players/`, `Save/` layers. Prefer `CLAUDE.md` + `docs/ai/**` + the code.
