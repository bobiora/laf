# Data Model — exact runtime state

## Space convention

- **Grid space** = integer `(gridX, gridY)`. Source of truth for adjacency, face detection, area membership, dedup.
- **World space** = `transform.position` (float). Used only for rendering + segment-intersection / point-in-polygon math.

## `GameManager` (`Assets/Scripts/GameManager.cs`)

| Field | Type | Meaning |
|-------|------|---------|
| `Instance` | `static GameManager` | singleton |
| `players` | `IPlayer[2]` | index 0 = player 1, 1 = player 2; built in `Awake` from `player1Color`/`player2Color` |
| `scores` | `int[2]` | per-player score; exposed as `player1Score`/`player2Score` |
| `currentPlayerIndex` | `int` (0/1) | active player; exposed as `currentPlayer` (1/2) |
| `edges` | `List<(PointClick,PointClick)>` | all committed edges (ordered pair as drawn) |
| `edgeSet` | `HashSet<(int,int,int,int)>` | O(1) existence via `EdgeKey` (sorted grid-coord pair) |
| `adjacency` | `Dictionary<PointClick,List<PointClick>>` | incident-edge neighbors, for face tracing |
| `cachedPoints` | `PointClick[]` | all dots, scanned once (`EnsureBoardCached`) |
| `gridLookup` | `Dictionary<(int,int),PointClick>` | `(gridX,gridY)` → dot, O(1) |
| `cachedLegalA/B` | `PointClick` | last known-legal move; fast-path for end-game check |
| `closedShapes` | `List<List<PointClick>>` | faces already scored (compared normalized) — never score twice |
| `claimedRegions` | `List<ClaimedRegion>` | filled regions; lines can't cross their interior |
| `firstPoint` | `PointClick` | current tap-tap selection (null = none) |
| `allLines` | `List<GameObject>` | instantiated line visuals |
| `lastClickFrame` | `int` | one click processed per frame guard |
| `previewLine` | `LineRenderer` | single reused preview (sorting order 5); `PreviewLine` getter |
| `isGameOver` | `bool` | true after `EndGame` |
| `debugEndGame` | `bool` (default false) | gates verbose end-game/face/diag logs |
| `linePrefab` | `GameObject` | inspector; `Line.prefab` |
| `gameOverUI` | `GameOverUI` | inspector; must be assigned |
| `player1Color`/`player2Color` | `Color` (red/green) | inspector; fed to `HumanPlayer` |

### `ClaimedRegion` (nested class)

| Field | Type | Meaning |
|-------|------|---------|
| `boundaryPoints` | `List<PointClick>` | perimeter order (grid identity) |
| `boundaryWorld` | `List<Vector2>` | same vertices in world space |
| `owner` | `int` (1/2) | claiming player |
| `visual` | `GameObject` | the `PolygonFill` mesh object |

### Edge representation

- Stored twice: `edges` (list of ordered `PointClick` pairs, for crossing tests via world positions) **and** `edgeSet` (normalized coord key, for O(1) existence) **and** `adjacency` (for neighbor walks). `RegisterEdge` keeps them consistent.
- `EdgeKey(a,b)` sorts by `(gridX, then gridY)` so `a-b == b-a`.

### Face representation

- A face is an ordered `List<PointClick>` (grid identity). Normalized for comparison by `NormalizeFace` (canonical start vertex + lexicographically smaller direction). Compared with `FacesEqual`.

## `PointClick` (`Assets/Scripts/PointClick.cs`)

| Field | Type | Meaning |
|-------|------|---------|
| `gridX`, `gridY` | `int` | grid coordinates (set by `BoardGenerator`) |
| `isSelected` | `bool` | selection visual state |
| `baseScale` | `Vector3` | captured local scale; highlight scales ×1.2 |

## `GameSettings` (`Assets/Scripts/GameSettings.cs`)

| Field | Type | Default | Range |
|-------|------|---------|-------|
| `BoardWidth` | `static int` | 4 | 3–10 (slider bounds in `MainMenu.unity`) |
| `BoardHeight` | `static int` | 4 | 3–10 |

## `IPlayer` / players (`Assets/Scripts/Players/`)

- `IPlayer`: `Id` (1/2), `Color`, `Icon` (Sprite, may be null), `DisplayName`, `IsHuman`.
- `HumanPlayer`: only impl. `Icon` pulled live from `PlayerIcons.GetPlayer{1,2}Icon()` each access (not cached).
- `GameManager` keys everything off `scores[]`/`currentPlayerIndex`; adding an `AIPlayer` needs no other state change (marked `// TODO: AIPlayer`).

## `PlayerIcons` state (`Assets/Scripts/PlayerIcons.cs`)

- `AvailableIcons` (`Sprite[]`) loaded from `Resources/Icons`; `Player1IconIndex`/`Player2IconIndex` persisted via `SaveSystem.Current` keys `player1_icon`/`player2_icon`.

## `HapticFeedback` durations (`Assets/Scripts/HapticFeedback.cs`)

- `lineConnectMilliseconds` (20), `shapeCreatedMilliseconds` (120), `hapticsEnabled` (true), `logHaptics` (false). Clamped `>= 0` via properties.
