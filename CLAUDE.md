# Dots and Figures — Project Context

> AI context file. Read this first — it captures the project's structure and mechanics so you
> don't have to re-analyze the whole codebase. A skill at `.claude/skills/dots-and-figures/`
> overlaps but is partly stale (it predates `InputController`, `CameraFitter`, and the
> `Shapes/`, `Players/`, `Save/` layers); **this file is the authoritative reference.**

## 1. Project Overview

A **two-player local (hot-seat) Unity 2D game for Android**. Players alternate turns drawing
lines between adjacent dots on a grid. When a line closes a scorable polygon, that region is
**claimed** by the current player, filled with their color, classified by shape, and scored.
Closing at least one shape grants **another turn**. The game ends when **no legal move
remains**; the player with the most points wins (ties = draw).

**Stack:** Unity 6, URP 2D, **new Input System** (`UnityEngine.InputSystem`) only — never
`UnityEngine.Input`. TextMeshPro for text. Two scenes: `MainMenu` (build index 0) → `Game`
(build index 1).

## 2. Architecture Map

All scripts live in `Assets/Scripts/`. Two singletons/statics matter most:
`GameManager.Instance` (game logic) and the static `GameSettings` (scene-to-scene config).

**Core loop**
- `GameManager.cs` — central authority: turns, scoring, edge/line storage, move validation, planar face detection, region claiming, end-game detection, and preview-line support. Auto-adds `InputController` in `Awake`.
- `InputController.cs` — pointer input state machine (`Idle → Pressed → Dragging`). Owns drag-to-draw + tap-tap; commits moves via `GameManager.TryCommitLine`. Attached at runtime, no scene wiring.
- `BoardGenerator.cs` — instantiates the dot grid from `GameSettings.BoardWidth/Height`; computes `GridBounds`; auto-attaches `CameraFitter`.
- `CameraFitter.cs` — sizes/positions the orthographic camera so the whole grid fits any resolution/aspect, reserving pixel margins for UI.
- `PointClick.cs` — one dot. Passive: holds `gridX/gridY` and its visual state (selection color, target glow). Does **not** read input.
- `PolygonFill.cs` — static ear-clipping mesh builder for claimed-area fills; also `PointInPolygon`.
- `GameSettings.cs` — static `BoardWidth`/`BoardHeight` carried from menu into the Game scene.

**Shape recognition**
- `ShapeRecognizer.cs` — orders + simplifies a face's boundary, then runs it through a priority list of shape definitions; maps to `ShapeType` enum + points/name. Contains `SimplifyPolygon`.
- `Shapes/IShapeDefinition.cs` — interface: `Name`, `Points`, `Matches(boundary)`.
- `Shapes/SquareShape.cs` (3 pts), `ParallelogramShape.cs` (4), `AcuteTriangleShape.cs` (2), `RightTriangleShape.cs` (1), `UnknownShape.cs` (fallback, 1).

**Players**
- `Players/IPlayer.cs` — `Id`, `Color`, `Icon`, `DisplayName`, `IsHuman`.
- `Players/HumanPlayer.cs` — the only implementation today; pulls its icon live from `PlayerIcons`.

**Save**
- `Save/ISaveSystem.cs` — key/value persistence interface.
- `Save/PlayerPrefsSaveSystem.cs` — default `PlayerPrefs`-backed impl.
- `Save/SaveSystem.cs` — static facade: `SaveSystem.Current` (swap to change backend).

**UI / Menu**
- `MainMenu.cs` — Width/Height sliders, settings panel, icon-picker wiring, "Start" → loads Game.
- `TurnUI.cs` — in-game score + turn indicator + player icons (updates every frame).
- `GameOverUI.cs` — end panel with winner/draw text, Restart, Menu buttons.
- `BackToMenu.cs` — in-game "back" button → MainMenu scene.
- `IconPickerDialog.cs` — reusable modal grid of selectable icons.
- `PlayerIcons.cs` — static icon registry; loads `Resources/Icons`, persists choices, generates a fallback circle.

**Scenes:** `Assets/Scenes/MainMenu.unity`, `Game.unity`.
**Prefabs:** `Assets/Prefabs/` — `Point.prefab` (SpriteRenderer + Circle Collider 2D + `PointClick`), `Line.prefab` (LineRenderer), `IconCell.prefab` (icon-picker cell).

## 3. Core Mechanics

### Grid generation & sizing
`BoardGenerator.GenerateGrid` lays out a `width × height` grid of `Point` prefabs centered on
its transform, `spacing` (1.2 world units) apart, tagging each with integer `gridX/gridY`.
Width/Height come from `GameSettings` (set by the menu sliders, **3–10, default 4×4**).
`GridBounds` is published for the camera fitter.

### Line drawing (`InputController`)
Two input styles share the exact same rules (both end in `GameManager.TryCommitLine`):

- **Drag (primary):** press on point A → once the finger moves past `dragThresholdPixels`, a
  **preview line** appears. As the finger aims toward a valid adjacent point it **snaps** onto
  it (the `currentTarget`); the preview locks solid on the snapped target, otherwise it trails
  the finger semi-transparent. The target is **re-targetable** — aiming A→B then A→C just
  overwrites it; the last hovered valid neighbor wins. Dragging **back onto A cancels** the
  pending target (preview hides) without ending the drag. **The line is committed only on
  release** — never mid-drag. Releasing with no target (or on A) cancels entirely.
- **Tap-tap (fallback):** tap A, then tap a neighbor. Tapping A twice deselects.

The primary pointer is a touch when a `Touchscreen` is active, else the mouse — one code path
for mobile and desktop. Snap zone = progress ≥ `swipeCommitProgress` (0.9) along A→target
**and** within `swipeCommitPerpTolerance` (0.3) sideways.

### Move validation (order is strict — `GameManager.IsLegalMove`)
1. **Adjacent** — `max(|dx|,|dy|) == 1` in grid coords (8-directional). Blocks long lines through intermediate dots.
2. **Edge doesn't already exist.**
3. **Doesn't cross an existing edge** — segment-intersection test; a *shared endpoint is not* a crossing.
4. **Doesn't pass through a claimed region** — samples the segment at 25/50/75% against claimed polygons.

Rejection is logged with a `[Reject:*]` reason; turn/score are unchanged.

### Loop/face detection & claiming (`GameManager`)
Edges form a **planar graph**. A new edge (a,b) can close at most two minimal faces — one on
each side. `FindAllNewClosedShapes` walks both directions with the **next-clockwise-edge**
rule (`TraceFace` + `GetSortedNeighborsByAngle`) and keeps only **bounded** faces
(signed area > 0; the infinite outer face is CW/negative). Faces are deduped by a normalized
vertex sequence (`NormalizeFace`/`FacesEqual`), so the same cell is never scored twice.

A traced face is **rejected** (not filled/scored) when:
- it has **more than 4 boundary dots** (a shape stretched through intermediate grid points), or
- it **encloses a free grid point** (unfinished region that should still be split), or
- it classifies as **Unknown** (e.g. a trapezoid) — deliberately left unclaimed so it stays splittable into valid shapes.

Otherwise the region is claimed: filled via `PolygonFill.Create` (semi-transparent player
color, sorting order −1) and recorded so future lines can't cross it.

### Shape classification & scoring (`ShapeRecognizer`)
Boundary is ordered by angle and **`SimplifyPolygon`**'d (collinear intermediates removed, so a
triangle drawn through a midpoint is still a triangle) **before** matching. Definitions are
tried in **priority order** — first `Matches` wins:

| Shape | Points | Notes |
|-------|:---:|-------|
| Square | 3 | before Parallelogram (a square is also a parallelogram) |
| Parallelogram | 4 | opposite sides equal & parallel |
| Acute triangle | 2 | no right/obtuse angle — **also claims large isosceles-right** (equal legs > 1 cell) |
| Right triangle | 1 | has a ~90° angle, excluding the large isosceles-right case |
| Unknown | 1 | fallback; but faces classified Unknown are **not** filled/scored (see above) |

### Turns, scoring, end-game
Points go to the current player (`scores[]`, exposed as `player1Score`/`player2Score`).
`SwitchPlayer` toggles `currentPlayerIndex` **only if no shape was closed** — closing a shape
means the same player goes again. After every commit, `AnyLegalMoveRemains` scans all pairs; if
none are legal, `EndGame` shows `GameOverUI` with the winner (or draw).

### Screen fit (`CameraFitter`)
The dots are world-space; the UI is a separate Screen-Space Overlay canvas. So the lever for
"fit the whole grid" is the **camera's orthographic size + position**. `Fit` computes the world
units-per-pixel needed to fit `GridBounds` (plus `worldPadding`) inside the safe area minus
reserved pixel margins (top 200 for score/turn, bottom 150 for the Menu button, sides 40),
clamps to `minOrthographicSize` (5), and centers the grid in the available rectangle. Re-runs on
resolution/orientation/safe-area change (`LateUpdate`). Warns (doesn't fail) if on-screen dot
spacing drops below `minDotSpacingPixels` (44).

## 4. Key Design Decisions & Constraints

- **Grid coordinates are the source of truth** for geometry (adjacency, area membership). World
  positions are used for rendering and segment-intersection math only.
- **Adjacency is 8-directional** (`max(|dx|,|dy|)==1`): orthogonal *and* diagonal neighbors.
- **A shared endpoint is not a crossing** — otherwise adjacent lines meeting at a dot couldn't coexist (`SegmentsIntersect` early-outs on shared vertices).
- **Faces are deduped by normalized sequence, not by vertex set** — distinct faces sharing vertices aren't merged.
- **Two "not a real cell" guards** (>4 boundary dots; encloses a free grid point) prevent oversized/composite fills and premature scoring. Do not remove them.
- **Unknown ≠ scored.** Trapezoids etc. are intentionally left open so they can still be subdivided into scorable shapes.
- **Commit only on release; cancel only mid-drag.** No auto-commit while the finger is down; returning to the start point cancels the pending target but keeps the drag alive.
- **One click processed per frame** (`lastClickFrame` guard) — several `PointClick`s could otherwise dispatch the same physical click.
- **Zero manual wiring for input/camera:** `GameManager` auto-adds `InputController`; `BoardGenerator` auto-adds `CameraFitter`. Don't rely on them being in the scene.
- **Board size limited to 3–10** (slider min/max in `MainMenu.unity`, whole numbers). Large boards can make dots hard to tap — `CameraFitter` warns but still shows the whole grid (pan/zoom is a TODO).
- **`GameManager.debugEndGame` currently defaults to `true`** for diagnostics (verbose `[EndGameCheck]`/`[Face]`/`[Reject:*]`/`[Diag]` logs). **Set to false for shipping builds.**
- **TurnUI score labels are hardcoded "Red:"/"Green:"** even though player colors are inspector-configurable — update `TurnUI.Update` if colors change.

## 5. Conventions

- **Language:** all comments, `Debug.Log`, and UI text are **English**. Translate any legacy strings when you touch them.
- **Input:** new Input System only (`Mouse.current`, `Touchscreen.current`). Never `UnityEngine.Input`.
- **Fields:** `[SerializeField] private` + `public` properties where access is needed (see `BoardGenerator.GridBounds`, `GameManager` delegating props).
- **Sorting orders** (low→high): claimed fills **−1** → lines **0** → preview line **5** → dots **10**. Dots always render above lines.
- **Runtime-instantiated UI** should be a prefab under `Assets/Prefabs/`, not built procedurally.
- **New scripts** go in `Assets/Scripts/` (subfoldered by concern: `Shapes/`, `Players/`, `Save/`).
- **Editor steps:** when a change needs Unity Editor actions (new GameObjects, inspector wiring, anchors), output them as a **numbered checklist at the end** of the response, separate from code.

## 6. How to Extend

- **Add a shape type:** implement `IShapeDefinition` in `Shapes/`, add an enum value to `ShapeRecognizer.ShapeType` (+`GetPoints`/`GetName`), and register the class in `ShapeRecognizer.definitions` at the correct **priority position** (more specific before more general; `UnknownShape` stays last).
- **Change scoring:** edit each shape's `Points` and `ShapeRecognizer.GetPoints` (keep them in sync).
- **Add an AI opponent:** implement `IPlayer` (e.g. `AIPlayer`) and swap it into a slot in `GameManager.Awake` (`players[1] = new AIPlayer(...)`). Turn/score/color handling needs no other change; you'll add the move-generation hook. (Marked `// TODO: AIPlayer` in `GameManager`.)
- **Change grid limits/default:** edit the Width/Height slider `m_MinValue`/`m_MaxValue`/`m_Value` in `MainMenu.unity` (and the defaults in `GameSettings`).
- **Swap the save backend:** implement `ISaveSystem` and assign `SaveSystem.Current` at startup (e.g. cloud save). No call-site changes needed.
- **Tune drag feel:** `InputController` inspector fields — `dragThresholdPixels`, `swipeCommitProgress`, `swipeCommitPerpTolerance`.
- **Adjust screen fit:** `CameraFitter` margins/limits (`topMarginPixels`, `bottomMarginPixels`, `sideMarginPixels`, `minOrthographicSize`, `worldPadding`).
- **Not yet implemented:** **sound** (only Unity's default `AudioListener` exists — no SFX/music system) and **animations** (only the point scale-up highlight in `PointClick.SetHighlighted`). Add an audio manager script + prefab if needed.

### The face-closure idea (the one non-obvious algorithm)
A new edge closes at most two minimal cells. Walk each side by, at every vertex, taking the
neighbor **immediately clockwise** from the edge you arrived on, until you return to the start —
then keep only faces with **positive signed area** (bounded):

```
// GameManager.TraceFace: at vertex v arriving from u, next = neighbor just before u in
// angle-sorted order (wrap). Return the vertex loop when back at prev->start.
List<PointClick> sorted = GetSortedNeighborsByAngle(v);   // atan2(dy,dx)
int idx = sorted.IndexOf(u);
PointClick w = sorted[(idx - 1 + sorted.Count) % sorted.Count];
```
