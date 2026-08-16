# File Index — every script in `Assets/Scripts/`

Main navigation table for agents. Paths relative to repo root.

| File | Responsibility | Key types / methods | Called by | Must not do |
|------|----------------|---------------------|-----------|-------------|
| `GameManager.cs` | Central game authority: turns, scoring, edges, validation, planar faces, claiming, end-game, preview support, board caches | `Instance`; `IsLegalMove`, `TryCommitLine`, `CommitLine`, `FindAllNewClosedShapes`, `TraceFace`, `GetSortedNeighborsByAngle`, `SignedAreaGrid`, `EnclosesGridPoint`, `AnyLegalMoveRemains`, `ClaimRegion`, `IsInsideAnyClaimedArea`, `SegmentsIntersect`, `GetPointAtScreenPos`, `GetAdjacentPoints`, `ShowPreview/UpdatePreview/HidePreview` | `InputController`, `TurnUI`, `GameOverUI` (via Instance) | Reintroduce `FindObjectsByType` in move/drag hot paths; remove the >4-dot / free-point guards; score Unknown |
| `InputController.cs` | Pointer state machine (Idle→Pressed→Dragging); drag-to-draw + tap-tap; commits on release | `SwipeState`, `Update`, `TickIdle/Pressed/Dragging`, `UpdateDrag`, `HandleDragRelease`, `PointInCommitZone`, `ReadPointer` | Auto-added by `GameManager.Awake` | Use `UnityEngine.Input`; commit mid-drag; bypass `GameManager.TryCommitLine` |
| `BoardGenerator.cs` | Build dot grid from GameSettings; compute GridBounds; auto-attach CameraFitter | `GenerateGrid`, `GridBounds`, `Spacing`, `Start` | Present in Game scene | Own game logic; skip setting sprite sortingOrder 10 |
| `CameraFitter.cs` | Fit orthographic camera to whole grid, reserving UI pixel margins | `Fit`, `ScreenChanged`, `WarnIfDotsTooSmall` | Auto-added by `BoardGenerator.Start` | Handle non-orthographic cameras; move the UI (it fits the camera, not the canvas) |
| `PointClick.cs` | One dot: grid coords + selection/highlight visuals | `gridX`, `gridY`, `SetSelected`, `SetHighlighted` | `GameManager`, `InputController`, `BoardGenerator` | Read input; hit-test itself |
| `PolygonFill.cs` | Static ear-clip mesh builder for fills; point-in-polygon | `Create`, `PointInPolygon`, `EarClip` | `GameManager.ClaimRegion`, `IsInsideAnyClaimedArea` | Assume vertex winding (it reverses to CCW) |
| `ShapeRecognizer.cs` | Order + simplify boundary, run priority shape list → ShapeType | `ShapeType`, `Recognize`, `RecognizeShape`, `SimplifyPolygon`, `GetPoints`, `GetName`, `definitions` | `GameManager.CommitLine` | Match before simplifying; reorder definitions incorrectly (Square before Parallelogram, Acute before Right, Unknown last) |
| `GameSettings.cs` | Static board dimensions carried menu→game | `BoardWidth`, `BoardHeight` | `MainMenu` (write), `BoardGenerator` (read) | — |
| `HapticFeedback.cs` | Android vibration: line buzz / shape buzz | `Instance`, `VibrateLineConnected`, `VibrateShapeCreated`, `Vibrate` | Auto-added by `GameManager.Awake`; called in `CommitLine` | Assume non-Android has a vibrator (it no-ops) |
| `TurnUI.cs` | In-game score + turn indicator + icons | `Update`, `Start`, `SetIcon` | Game scene canvas | Rewrite TMP every frame (updates only on change); note it hardcodes "Red:"/"Green:" |
| `GameOverUI.cs` | End panel: winner/draw text, Restart, Menu | `Show`, `OnMenuClicked`, `OnRestartClicked` | `GameManager.EndGame` | — |
| `BackToMenu.cs` | In-game back button → MainMenu | `GoToMenu` | Button onClick | — |
| `MainMenu.cs` | Width/Height sliders, settings panel, icon picker, Start | `OnStartGameClicked`, `OnPlay/Cancel/QuitClicked`, `RefreshIconSlots` | MainMenu scene | Load Game scene without writing GameSettings |
| `IconPickerDialog.cs` | Reusable modal grid of selectable icons | `Open`, `Close`, `BuildGrid`, `SpawnCell` | `MainMenu` | Build UI procedurally without the cell prefab |
| `PlayerIcons.cs` | Static icon registry: load Resources/Icons, persist choices, fallback circle | `EnsureLoaded`, `GetPlayer1Icon/2Icon`, `SetPlayer1Icon/2Icon`, `GetFallbackSprite` | `MainMenu`, `TurnUI`, `HumanPlayer`, `IconPickerDialog` | Assume icons exist (fallback circle covers empty Resources/Icons) |
| `Shapes/IShapeDefinition.cs` | Shape-type interface | `Name`, `Points`, `Matches(boundary)` | `ShapeRecognizer` | — |
| `Shapes/SquareShape.cs` | Square (3 pts) | `Matches` | `ShapeRecognizer.definitions` | Register after Parallelogram |
| `Shapes/ParallelogramShape.cs` | Parallelogram (4 pts) | `Matches` | `ShapeRecognizer.definitions` | Register before Square |
| `Shapes/AcuteTriangleShape.cs` | Acute triangle (2 pts) + large isosceles-right | `Matches` | `ShapeRecognizer.definitions` | Register after RightTriangle |
| `Shapes/RightTriangleShape.cs` | Right triangle (1 pt), excludes large isosceles-right | `Matches` | `ShapeRecognizer.definitions` | Claim the large isosceles-right case |
| `Shapes/UnknownShape.cs` | Fallback; Matches always true (1 pt) | `Matches` | `ShapeRecognizer.definitions` | Be anywhere but last |
| `Players/IPlayer.cs` | Participant interface | `Id`, `Color`, `Icon`, `DisplayName`, `IsHuman` | `GameManager`, `HumanPlayer` | — |
| `Players/HumanPlayer.cs` | Only IPlayer impl; live icon from PlayerIcons | ctor `(id, color)`, `Icon` | `GameManager.Awake` | Cache the icon (must reflect live selection) |
| `Save/ISaveSystem.cs` | Key/value persistence interface | `SaveInt/LoadInt`, `SaveString/LoadString`, `HasKey`, `DeleteKey`, `Flush` | `SaveSystem`, `PlayerIcons` | — |
| `Save/PlayerPrefsSaveSystem.cs` | Default PlayerPrefs impl | delegates to `PlayerPrefs` | `SaveSystem.Current` default | — |
| `Save/SaveSystem.cs` | Static facade over ISaveSystem | `Current` | `PlayerIcons` | — |

**Total: 26 scripts** (11 root + 6 Shapes + 2 Players + 3 Save; plus GameManager/InputController/etc. counted above).
