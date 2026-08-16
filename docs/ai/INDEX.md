# INDEX — Dots and Figures KB

## What the game is

- Two-player **local hot-seat** 2D game for Android.
- Players alternate drawing lines between adjacent dots on a grid.
- Closing a scorable polygon **claims** that region for the current player, fills it with their color, classifies its shape, and scores it.
- Closing at least one shape grants **another turn**.
- Game ends when **no legal move remains**; highest score wins (ties = draw).

## Stack

- **Engine:** Unity 6 (6000.3.16f1), URP 2D.
- **Input:** new Input System (`UnityEngine.InputSystem`) only — never `UnityEngine.Input`.
- **Text:** TextMeshPro (TMP).
- **Target:** Android (min SDK 25), touch + mouse via one code path.

## Scenes, prefabs, singletons

- **Scenes:** `Assets/Scenes/MainMenu.unity` (build 0) → `Assets/Scenes/Game.unity` (build 1). Loaded by name (`SceneManager.LoadScene("Game"/"MainMenu")`).
- **Prefabs:** `Assets/Prefabs/Point.prefab` (SpriteRenderer + CircleCollider2D + `PointClick`), `Line.prefab` (LineRenderer), `IconCell.prefab` (icon-picker cell).
- **Singletons/statics:** `GameManager.Instance`, `HapticFeedback.Instance`, static `GameSettings`, static `SaveSystem.Current`, static `PlayerIcons`.
- **Runtime-added components:** `GameManager` auto-adds `InputController` + `HapticFeedback`; `BoardGenerator` auto-adds `CameraFitter`. No scene wiring for these.

## KB file map — open when you need…

| File | Purpose |
|------|---------|
| `glossary.md` | one precise meaning per project term (Point, edge, face, claimed region, …) |
| `architecture.md` | component ownership + call graph + folder layout |
| `data-model.md` | exact runtime state, field-by-field; grid-space vs world-space |
| `mechanics.md` | rules as checkable invariants + shape/score table, each citing the enforcing method |
| `flows.md` | numbered call flows (drag, tap-tap, commit, face trace, scene boot) |
| `file-index.md` | every script: responsibility / key methods / called-by / must-not-do |
| `invariants-and-pitfalls.md` | NEVER / ALWAYS / WHY — what agents break |
| `ui-and-build.md` | scenes, canvases, sorting orders, Android + Input System notes |
| `project-kb.json` | machine-readable graph of entities, rules, flows, files |

## Source-of-truth order

1. `Assets/Scripts/**` — code is authoritative.
2. `CLAUDE.md` — short always-on brief.
3. `docs/ai/**` — this KB.
4. `.claude/skills/dots-and-figures/SKILL.md` — **partly stale**; predates `InputController`, `CameraFitter`, and the `Shapes/`, `Players/`, `Save/` layers.

If code disagrees with any doc, trust the code and correct the doc.
