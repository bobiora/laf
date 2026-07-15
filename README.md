# Dots and Figures

A two-player 2D Unity game — a twist on the classic Dots and Boxes with scoring based on the type of closed geometric shape.

## About the Game

Two players take turns connecting dots on a grid with lines. When a line closes a shape, it is recognized automatically and the player earns points based on the shape type.

| Shape | Points |
|-------|--------|
| Right triangle | 1 |
| Acute triangle | 2 |
| Square | 3 |
| Parallelogram | 4 |
| Other closed shapes | 1 |

**Turn rules:**
- Red — Player 1, Green — Player 2
- Close a shape — take another turn
- Don't close a shape — turn passes to your opponent
- The game ends when no free lines remain on the board

## Requirements

- [Unity 6000.3.16f1](https://unity.com/releases/editor/whats-new/6000.3.16f1) (or a compatible Unity 6 version)
- Universal Render Pipeline (URP)
- Mouse and touch input support (Input System)

## Getting Started

1. Clone the repository
2. Open the project folder in Unity Hub
3. Wait for assets to import
4. Open the scene `Assets/Scenes/MainMenu.unity`
5. Click Play, choose the board size, and start the game

## Building

Target platform: **Android** (minimum SDK 25). Build via **File → Build Profiles** in the Unity Editor.

## Project Structure

```
Assets/
├── Scenes/
│   ├── MainMenu.unity    # Main menu and board settings
│   └── Game.unity        # Game scene
├── Scripts/
│   ├── GameManager.cs    # Turn logic, lines, and scoring
│   ├── ShapeRecognizer.cs # Shape recognition
│   ├── BoardGenerator.cs  # Dot grid generation
│   ├── MainMenu.cs        # Main menu UI
│   ├── TurnUI.cs          # Turn and score display
│   ├── GameOverUI.cs      # Game over screen
│   ├── PointClick.cs      # Dot click handling
│   ├── BackToMenu.cs      # Return to menu
│   └── GameSettings.cs    # Board size settings
└── Prefabs/
    ├── Point.prefab
    └── Line.prefab
ProjectSettings/           # Unity project settings
Packages/                  # Dependencies (manifest.json)
```

## Git

The repository tracks `Assets/`, `ProjectSettings/`, and `Packages/`. Folders such as `Library/`, `Logs/`, `UserSettings/`, and build artifacts are excluded via `.gitignore`.

Recommended Unity settings for Git:

- **Edit → Project Settings → Editor → Version Control → Mode:** Visible Meta Files
- **Asset Serialization → Mode:** Force Text

## License

Specify a license before publishing the project.
