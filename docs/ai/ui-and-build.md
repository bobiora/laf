# UI & Build

## Scenes

| Scene | Build index | Loaded by |
|-------|:--:|-----------|
| `Assets/Scenes/MainMenu.unity` | 0 | `SceneManager.LoadScene("MainMenu")` (`GameOverUI`, `BackToMenu`) |
| `Assets/Scenes/Game.unity` | 1 | `SceneManager.LoadScene("Game")` (`MainMenu.OnStartGameClicked`) |

Scenes are loaded **by name**, so both must be in Build Settings' scene list.

## Canvases & UI

- The dots are **world-space** objects rendered by the Main Camera. The UI (score/turn/menu, game-over panel) lives on a **separate Screen-Space Overlay Canvas** drawn on top. This is why `CameraFitter` moves the *camera*, not the canvas, to fit the board.
- The in-game Canvas is Constant-Pixel-Size, so UI occupies a fixed pixel band → `CameraFitter` reserves margins in **screen pixels** (top 200, bottom 150, sides 40) converted to world units.

### UI scripts

- `TurnUI` — score + turn text + player icons; updates only on change. **Labels hardcode "Red:"/"Green:".**
- `GameOverUI` — winner/draw panel; `Show(winner, color, score, draw)`; Restart reloads active scene, Menu loads MainMenu. `GameManager.gameOverUI` **must be assigned** in the inspector.
- `MainMenu` — width/height sliders (3–10), settings panel, icon-picker buttons, Start.
- `IconPickerDialog` — modal grid built from `Assets/Prefabs/IconCell.prefab`; icons from `PlayerIcons.AvailableIcons`.
- `BackToMenu` — `GoToMenu()` on a button.

## Prefabs

| Prefab | Contents |
|--------|----------|
| `Assets/Prefabs/Point.prefab` | SpriteRenderer + CircleCollider2D + `PointClick` |
| `Assets/Prefabs/Line.prefab` | LineRenderer (used for committed lines and the reused preview) |
| `Assets/Prefabs/IconCell.prefab` | icon-picker cell (UI Button + child Image) |

## Sorting orders (low → high)

| Order | Layer |
|:--:|-------|
| −1 | claimed region fills (`PolygonFill`) |
| 0 | committed lines (`GameManager.DrawLine`) |
| 5 | preview line (`GameManager.PreviewLine`) |
| 10 | dots (`BoardGenerator` sets on each Point's SpriteRenderer) |

Dots always render above lines.

## Icons

- Icons load from `Assets/Resources/Icons/` (must be under a `Resources` folder). Each Sprite PNG becomes a selectable icon. If none exist, `PlayerIcons.GetFallbackSprite()` generates a white circle at runtime.
- Choices persist via `SaveSystem.Current` (PlayerPrefs) keys `player1_icon`/`player2_icon`.

## Android / build

- **Target:** Android, minimum SDK **25**. Build via **File → Build Profiles**.
- **Unity:** 6000.3.16f1 (Unity 6), URP 2D.
- **Input System package is required** — the whole input path uses `UnityEngine.InputSystem`. Ensure "Active Input Handling" includes the new Input System.
- **Haptics:** `HapticFeedback` drives the native Android `Vibrator`/`VibratorManager`. It needs the `android.permission.VIBRATE` permission. The code references `Handheld.Vibrate` in a never-executed branch so Unity auto-adds VIBRATE to the built manifest as a safety net; add it explicitly if customizing the manifest. No-op in Editor / non-Android.

## Editor wiring notes (no code needed for these)

- `InputController`, `HapticFeedback`, `CameraFitter` are auto-added at runtime — do **not** require them in the scene.
- To edit haptic durations in the inspector, add `HapticFeedback` to the `GameManager` GameObject in `Game.unity`.
- `GameManager.gameOverUI` and `GameManager.linePrefab` must be assigned in `Game.unity`.
- `TurnUI` icon Image slots and `MainMenu` slider/panel/icon references are inspector-wired.
