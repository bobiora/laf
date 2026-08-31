using System.Collections.Generic;
using UnityEngine;

// Spawner for "+N" score numbers. GameManager calls Show(points, color) the moment a
// scorable shape is filled and scored in CommitLine; each call INSTANTLY spawns a fresh
// ScorePopup instance that animates on its own (appear -> rise -> fade -> self-destroy).
//
// Nothing is queued and nothing waits: closing another figure right away (extra turn, or
// two faces on one line) spawns another number immediately, so several may float at once
// during a combo. One number per scored shape, so every close is visible.
//
// This lives on a Screen-Space Overlay Canvas child in the Game scene (in the top band
// CameraFitter reserves) and is referenced from GameManager. See the editor checklist for
// scene/prefab setup.
[DisallowMultipleComponent]
public class ScorePopupUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Prefab (Assets/Prefabs/) with a ScorePopup component. One is spawned per " +
             "scored shape.")]
    [SerializeField] private ScorePopup popupPrefab;

    [Tooltip("Parent the spawned numbers appear under. Defaults to this object's " +
             "RectTransform so they inherit its top-center anchor / rest position.")]
    [SerializeField] private RectTransform spawnParent;

    [Header("Animation")]
    [Tooltip("How far each number drifts upward, in UI pixels (anchoredPosition).")]
    [SerializeField] private float riseDistancePixels = 20f;

    [Tooltip("Seconds for the upward drift.")]
    [SerializeField] private float riseDuration = 0.8f;

    [Tooltip("Seconds for the fade-out. Overlaps the tail of the rise.")]
    [SerializeField] private float fadeDuration = 0.4f;

    // Public accessors (clamped) so a settings UI or other systems can tune at runtime.
    public float RiseDistancePixels
    {
        get => riseDistancePixels;
        set => riseDistancePixels = Mathf.Max(0f, value);
    }

    // Live instances, so HideImmediately() can clear them all on game over / scene unload.
    private readonly List<ScorePopup> active = new List<ScorePopup>();

    // Same-move combining: a single line can close two figures, each firing its own Show()
    // in the SAME frame. Those are summed into one number (+total) rather than two popups.
    // A Show() in a later frame (the extra-turn chain: close -> another turn -> close)
    // starts a fresh number.
    private ScorePopup currentFramePopup;
    private int currentFramePoints;
    private int lastShowFrame = -1;

    void Awake()
    {
        if (spawnParent == null) spawnParent = transform as RectTransform;
    }

    // Spawn a new "+points" number tinted with the scoring player's color and start its
    // animation immediately. Never queues, never waits on a previous popup.
    public void Show(int points, Color color)
    {
        if (popupPrefab == null)
        {
            Debug.LogError("ScorePopupUI.popupPrefab is not assigned! Drag the ScorePopup " +
                           "prefab into the popupPrefab field in the inspector.");
            return;
        }
        if (!isActiveAndEnabled) return;

        // Two figures on one move (same frame): fold the second award into the number
        // already spawned this frame so the player sees one combined +total.
        if (Time.frameCount == lastShowFrame && currentFramePopup != null)
        {
            currentFramePoints += points;
            currentFramePopup.SetPoints(currentFramePoints);
            return;
        }

        currentFramePoints = points;
        lastShowFrame = Time.frameCount;

        ScorePopup popup = Instantiate(popupPrefab, spawnParent);
        active.Add(popup);
        currentFramePopup = popup;
        popup.Play(points, color, riseDistancePixels, riseDuration, fadeDuration, OnPopupFinished);
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
