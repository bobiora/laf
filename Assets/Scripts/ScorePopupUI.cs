using System.Collections.Generic;
using UnityEngine;

// Spawner for "+N" score numbers. GameManager calls Show(points, color, worldPosition) the
// moment a scorable shape is filled and scored in CommitLine; each call INSTANTLY spawns a
// fresh ScorePopup instance that animates on its own (appear -> rise -> fade -> self-destroy).
//
// The number appears at the CENTER of the shape that was just closed: GameManager passes the
// shape's world-space center, which we project onto this UI's rect so the "+N" pops out of
// the figure itself rather than from a fixed spot at the top of the screen.
//
// Position and animation are tuned in a ScorePopupConfig asset (Resources/ScorePopupConfig),
// not here — see that class. Values are read per popup, so edits apply live in Play Mode.
//
// Nothing is queued and nothing waits: closing another figure right away (extra turn, or
// two faces on one line) spawns another number immediately, so several may float at once
// during a combo. One number per scored shape, so every close is visible.
//
// This lives on a Screen-Space Overlay Canvas child in the Game scene and is referenced from
// GameManager. See the editor checklist for scene/prefab setup.
[DisallowMultipleComponent]
public class ScorePopupUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Prefab (Assets/Prefabs/) with a ScorePopup component. One is spawned per " +
             "scored shape.")]
    [SerializeField] private ScorePopup popupPrefab;

    [Tooltip("Position + animation settings for the numbers. If empty, loads " +
             "Resources/ScorePopupConfig; if that is missing, built-in defaults are used.")]
    [SerializeField] private ScorePopupConfig config;

    [Tooltip("Parent the spawned numbers appear under. Defaults to this object's " +
             "RectTransform. Each number is repositioned to the closed shape's center, so " +
             "the parent's own anchor/rest position only matters as a fallback.")]
    [SerializeField] private RectTransform spawnParent;

    [Tooltip("Camera used to project the shape's world center to screen space. Defaults to " +
             "Camera.main, which is the board camera this game uses.")]
    [SerializeField] private Camera worldCamera;

    // The config actually in use (assigned, Resources, or built-in defaults).
    public ScorePopupConfig Config => ResolveConfig();

    // Live instances, so HideImmediately() can clear them all on game over / scene unload.
    private readonly List<ScorePopup> active = new List<ScorePopup>();

    // Same-move combining: a single line can close two figures, each firing its own Show()
    // in the SAME frame. Those are summed into one number (+total) rather than two popups.
    // A Show() in a later frame (the extra-turn chain: close -> another turn -> close)
    // starts a fresh number.
    private ScorePopup currentFramePopup;
    private int currentFramePoints;
    private int lastShowFrame = -1;

    // Canvas this UI lives under, cached so we can project world -> UI correctly for both
    // Screen-Space Overlay and Screen-Space Camera setups.
    private Canvas canvas;

    void Awake()
    {
        if (spawnParent == null) spawnParent = transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();
    }

    private ScorePopupConfig ResolveConfig()
    {
        if (config == null) config = Resources.Load<ScorePopupConfig>("ScorePopupConfig");
        if (config == null) config = ScorePopupConfig.CreateDefault();
        return config;
    }

    // Spawn a new "+points" number tinted with the scoring player's color, positioned at the
    // just-closed shape's world center, and start its animation immediately. Never queues,
    // never waits on a previous popup.
    public void Show(int points, Color color, Vector3 worldPosition)
    {
        if (popupPrefab == null)
        {
            Debug.LogError("ScorePopupUI.popupPrefab is not assigned! Drag the ScorePopup " +
                           "prefab into the popupPrefab field in the inspector.");
            return;
        }
        if (!isActiveAndEnabled) return;

        // Two figures on one move (same frame): fold the second award into the number
        // already spawned this frame so the player sees one combined +total. It stays at
        // the first shape's center (where the popup already is).
        if (Time.frameCount == lastShowFrame && currentFramePopup != null)
        {
            currentFramePoints += points;
            currentFramePopup.SetPoints(currentFramePoints);
            return;
        }

        currentFramePoints = points;
        lastShowFrame = Time.frameCount;

        ScorePopupConfig cfg = ResolveConfig();

        ScorePopup popup = Instantiate(popupPrefab, spawnParent);
        active.Add(popup);
        currentFramePopup = popup;

        PositionAtWorld((RectTransform)popup.transform, worldPosition, cfg);

        popup.Play(points, color, cfg, OnPopupFinished);
    }

    // Preview for tuning: spawns a "+N" over the middle of the board without closing a shape.
    // Called by the ScorePopupConfig inspector's "Play test popup" button (Play Mode only).
    public void ShowPreview(int points = 2)
    {
        BoardGenerator board = FindFirstObjectByType<BoardGenerator>();
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        Vector3 center = board != null ? board.GridBounds.center
                       : cam != null ? cam.transform.position
                       : Vector3.zero;

        Color color = GameManager.Instance != null ? GameManager.Instance.GetCurrentColor()
                                                   : Color.white;

        lastShowFrame = -1; // never fold a preview into another popup
        Show(points, color, center);
    }

    // Place a spawned popup so its config pivot (by default the bottom-center of the number)
    // sits on the given world position (the shape's center), then apply the config offset.
    // We go through the screen point, then onto the plane of this UI's rect, so it works
    // whether the canvas renders in Screen-Space Overlay or Screen-Space Camera. Anchor
    // independent: setting rectTransform.position moves the pivot however the prefab is anchored.
    private void PositionAtWorld(RectTransform rt, Vector3 worldPosition, ScorePopupConfig cfg)
    {
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null || spawnParent == null) return; // keep the prefab's rest position

        rt.pivot = cfg.Pivot;

        Vector2 screenPoint = cam.WorldToScreenPoint(worldPosition);

        // For Overlay the canvas camera must be null; otherwise it's the canvas world camera.
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                spawnParent, screenPoint, uiCam, out Vector3 worldPoint))
        {
            rt.position = worldPoint;
            rt.anchoredPosition += cfg.OffsetPixels;
        }
    }

    // Destroy every live number. Called on game over so nothing lingers over the end panel.
    public void HideImmediately()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i] != null) Destroy(active[i].gameObject);
        }
        active.Clear();
        currentFramePopup = null;
        lastShowFrame = -1;
    }

    void OnDisable()
    {
        HideImmediately();
    }

    // A popup removes itself from tracking when its animation completes (it self-destroys).
    private void OnPopupFinished(ScorePopup popup)
    {
        active.Remove(popup);
        if (currentFramePopup == popup) currentFramePopup = null;
    }
}
