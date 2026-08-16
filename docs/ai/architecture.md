# Architecture

## Singletons & statics

| Name | Kind | Path | Set where |
|------|------|------|-----------|
| `GameManager.Instance` | MonoBehaviour singleton | `Assets/Scripts/GameManager.cs` | `Awake` (`Instance = this`) |
| `HapticFeedback.Instance` | MonoBehaviour singleton | `Assets/Scripts/HapticFeedback.cs` | `Awake` (guards duplicates) |
| `GameSettings` | static class | `Assets/Scripts/GameSettings.cs` | `MainMenu.OnStartGameClicked` writes; `BoardGenerator.Start` reads |
| `SaveSystem.Current` | static facade (`ISaveSystem`) | `Assets/Scripts/Save/SaveSystem.cs` | defaults to `PlayerPrefsSaveSystem`; reassign at startup to swap backend |
| `PlayerIcons` | static registry | `Assets/Scripts/PlayerIcons.cs` | lazy `EnsureLoaded` |

## Runtime-added components (no scene wiring)

- `GameManager.Awake` → adds `InputController` and `HapticFeedback` to its own GameObject if missing.
- `BoardGenerator.Start` → finds or adds `CameraFitter` on `Camera.main`, then calls `Fit()`.

## Ownership & allowed calls

```
MainMenu ──writes──► GameSettings ──read──► BoardGenerator ──generates──► PointClick[] (dots)
                                                    │
                                                    ├─ publishes GridBounds ──► CameraFitter (frames camera)
                                                    └─ (no game-logic ownership)

GameManager (Instance) ── central authority
   ├─ owns: edges, edgeSet, adjacency, closedShapes, claimedRegions, scores[], players[], currentPlayerIndex
   ├─ adds + is called by: InputController (input → TryCommitLine / OnPointClicked)
   ├─ calls: ShapeRecognizer.Recognize (classify), PolygonFill.Create/PointInPolygon (fills), HapticFeedback.Instance (buzz)
   └─ drives: GameOverUI.Show (end)

InputController ── reads pointer, calls GameManager only (GetPointAtScreenPos, GetAdjacentPoints,
                   IsLegalMove, ShowPreview/UpdatePreview/HidePreview, TryCommitLine, OnPointClicked,
                   CancelSelection). Never touches ShapeRecognizer / scoring directly.

PointClick ── passive; only GameManager/InputController call SetSelected/SetHighlighted.

TurnUI / GameOverUI / BackToMenu / MainMenu / IconPickerDialog ── UI; read GameManager + statics.
```

### Who may call whom (rules)

- **Only `GameManager`** mutates game state (edges, scores, turn, claimed regions). UI and input **read** or **request commits**; they never mutate directly.
- **`InputController` talks only to `GameManager`** (via its public API), never to `ShapeRecognizer`/`PolygonFill`.
- **Both input styles funnel through `GameManager.TryCommitLine`** so tap-tap and drag share identical rules.
- **`PointClick` never reads input** — hit-testing is `GameManager.GetPointAtScreenPos`.

## Folder layout — `Assets/Scripts/`

| Path | One line |
|------|----------|
| `GameManager.cs` | central authority: turns, scoring, edges, validation, planar faces, claiming, end-game, preview |
| `InputController.cs` | pointer state machine (Idle→Pressed→Dragging); drag + tap-tap; commits via GameManager |
| `BoardGenerator.cs` | builds dot grid from GameSettings; computes GridBounds; auto-attaches CameraFitter |
| `CameraFitter.cs` | sizes/positions orthographic camera so whole grid fits, reserving UI pixel margins |
| `PointClick.cs` | one dot: gridX/gridY + selection/highlight visuals; passive |
| `PolygonFill.cs` | static ear-clip mesh builder for fills; `PointInPolygon` |
| `ShapeRecognizer.cs` | orders + simplifies a face boundary, runs priority list of shape defs → ShapeType + points/name |
| `GameSettings.cs` | static BoardWidth/BoardHeight (menu → Game scene) |
| `HapticFeedback.cs` | Android vibration (line buzz / shape buzz); singleton, auto-added |
| `TurnUI.cs` | in-game score + turn indicator + icons; updates on change |
| `GameOverUI.cs` | end panel: winner/draw text, Restart, Menu |
| `BackToMenu.cs` | in-game back button → MainMenu |
| `MainMenu.cs` | width/height sliders, settings panel, icon-picker wiring, Start → Game |
| `IconPickerDialog.cs` | reusable modal grid of selectable icons |
| `PlayerIcons.cs` | static icon registry: loads Resources/Icons, persists choices, fallback circle |
| `Shapes/IShapeDefinition.cs` | interface: Name, Points, Matches(boundary) |
| `Shapes/SquareShape.cs` | 3 pts; before Parallelogram |
| `Shapes/ParallelogramShape.cs` | 4 pts; opposite sides equal & parallel |
| `Shapes/AcuteTriangleShape.cs` | 2 pts; no right/obtuse angle, also large isosceles-right |
| `Shapes/RightTriangleShape.cs` | 1 pt; has ~90° angle, excluding large isosceles-right |
| `Shapes/UnknownShape.cs` | fallback; Matches always true; last in list |
| `Players/IPlayer.cs` | interface: Id, Color, Icon, DisplayName, IsHuman |
| `Players/HumanPlayer.cs` | only IPlayer impl; icon pulled live from PlayerIcons |
| `Save/ISaveSystem.cs` | key/value persistence interface |
| `Save/PlayerPrefsSaveSystem.cs` | default PlayerPrefs-backed impl |
| `Save/SaveSystem.cs` | static facade `SaveSystem.Current` |
