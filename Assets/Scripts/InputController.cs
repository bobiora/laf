using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// Owns the pointer input state machine for drawing lines. Supports two coexisting
// input styles that share the same game rules (GameManager.TryCommitLine):
//
//   * Swipe (primary): press on a point, drag to an adjacent point, release to commit.
//   * Tap-tap (fallback): quick tap a point, then quick tap a neighbor.
//
// The "primary pointer" is a touch when the touchscreen is active, otherwise the mouse,
// so the same machine drives both mobile and desktop. Uses the new Input System only.
//
// State machine:
//   Idle     -> press over point P            => Pressed (origin = P, highlight P)
//   Pressed  -> drag beyond threshold          => Dragging (clear pending tap, show preview)
//   Pressed  -> release (no drag)              => quick tap -> GameManager.OnPointClicked
//   Dragging -> release over valid target Q    => commit P->Q, hide preview -> Idle
//   Dragging -> release elsewhere/invalid      => cancel, hide preview -> Idle
//   any      -> isGameOver                     => reset to Idle
[DisallowMultipleComponent]
public class InputController : MonoBehaviour
{
    enum SwipeState { Idle, Pressed, Dragging }

    [Tooltip("Drag distance (screen pixels) beyond which a press becomes a swipe.")]
    public float dragThresholdPixels = 10f;

    [Tooltip("World-space radius for snapping the preview line onto a nearby point.")]
    public float snapRadiusWorld = 0.5f;

    private GameManager gm;

    private SwipeState state = SwipeState.Idle;
    private PointClick originPoint;      // where the current press/swipe started
    private Vector2 pressScreenPos;      // screen position at press-down

    private PointClick currentHover;     // nearest point within snap radius (valid or not)
    private PointClick snappedTarget;    // valid target currently snapped to (or null)
    private PointClick highlightedTarget; // point currently showing the target glow

    // Snapshot of the primary pointer for a single frame.
    struct Pointer
    {
        public bool isDown;
        public bool pressedThisFrame;
        public bool releasedThisFrame;
        public Vector2 pos;
    }

    void Update()
    {
        if (gm == null) gm = GameManager.Instance;
        if (gm == null) return;

        // Game over: ignore all input and make sure nothing is left highlighted.
        if (gm.isGameOver)
        {
            if (state != SwipeState.Idle) ResetSwipe();
            return;
        }

        Pointer p = ReadPointer();

        switch (state)
        {
            case SwipeState.Idle:
                TickIdle(p);
                break;
            case SwipeState.Pressed:
                TickPressed(p);
                break;
            case SwipeState.Dragging:
                TickDragging(p);
                break;
        }
    }

    // ---- State: Idle ----
    void TickIdle(Pointer p)
    {
        if (!p.pressedThisFrame) return;

        // Never let a swipe start on top of UI (buttons like "Back to menu").
        if (IsPointerOverUI()) return;

        PointClick hit = gm.GetPointAtScreenPos(p.pos);
        if (hit == null) return; // press over empty space — no-op

        originPoint = hit;
        pressScreenPos = p.pos;
        state = SwipeState.Pressed;

        // Immediate feedback: origin uses the same highlight as a tap-tap selection.
        originPoint.SetSelected(true, gm.GetCurrentColor());
        Debug.Log($"[Swipe:Start] press on ({hit.gridX},{hit.gridY})");
    }

    // ---- State: Pressed (origin known, not yet dragging) ----
    void TickPressed(Pointer p)
    {
        if (p.releasedThisFrame || !p.isDown)
        {
            // Released without dragging => quick tap. Hand off to the tap-tap flow.
            HandleQuickTap();
            return;
        }

        if ((p.pos - pressScreenPos).magnitude > dragThresholdPixels)
        {
            // A swipe has begun. Once dragging, any pending tap-tap selection is dropped
            // so the two flows never conflict.
            state = SwipeState.Dragging;
            gm.CancelSelection();
            originPoint.SetSelected(true, gm.GetCurrentColor()); // re-assert origin highlight
            gm.ShowPreview();
            UpdateDrag(p.pos);
        }
    }

    // ---- State: Dragging (preview active) ----
    void TickDragging(Pointer p)
    {
        if (p.releasedThisFrame || !p.isDown)
        {
            HandleDragRelease();
            return;
        }
        UpdateDrag(p.pos);
    }

    // Quick tap: convert the press into the existing tap-tap flow. Clear our transient
    // origin highlight first; OnPointClicked re-applies it if the point becomes firstPoint.
    void HandleQuickTap()
    {
        PointClick origin = originPoint;
        state = SwipeState.Idle;
        originPoint = null;

        if (origin != null)
        {
            origin.SetSelected(false, Color.white);
            gm.OnPointClicked(origin);
        }
    }

    // Recomputes the preview endpoint, snapping and target glow, each drag frame.
    void UpdateDrag(Vector2 screenPos)
    {
        Vector3 worldPos = gm.ScreenToWorld(screenPos);
        PointClick hover = gm.GetNearestPointWithinRadius(worldPos, snapRadiusWorld);
        currentHover = hover;

        bool valid = hover != null && hover != originPoint && gm.IsLegalMove(originPoint, hover);
        if (valid)
        {
            if (snappedTarget != hover)
                Debug.Log($"[Swipe:Snap] snap to ({hover.gridX},{hover.gridY})");
            snappedTarget = hover;
            SetTargetHighlight(hover);
            gm.UpdatePreview(originPoint.transform.position, hover.transform.position, true);
        }
        else
        {
            snappedTarget = null;
            SetTargetHighlight(null);
            gm.UpdatePreview(originPoint.transform.position, worldPos, false);
        }
    }

    // Release while dragging: commit onto a valid target, or cancel. Invalid targets are
    // reported with the same [Reject:*] prefixes as tap-tap (via TryCommitLine).
    void HandleDragRelease()
    {
        PointClick origin = originPoint;
        PointClick snapped = snappedTarget;
        PointClick hover = currentHover;

        // Tear down swipe visuals first.
        SetTargetHighlight(null);
        gm.HidePreview();
        if (origin != null) origin.SetSelected(false, Color.white);

        state = SwipeState.Idle;
        originPoint = null;
        snappedTarget = null;
        currentHover = null;

        if (origin == null) return;

        if (snapped != null)
        {
            // Snapped onto a valid neighbor — commit via the shared game logic.
            if (gm.TryCommitLine(origin, snapped))
                Debug.Log($"[Swipe:Commit] ({origin.gridX},{origin.gridY})->({snapped.gridX},{snapped.gridY})");
            else
                Debug.Log($"[Swipe:Cancel] commit rejected ({origin.gridX},{origin.gridY})->({snapped.gridX},{snapped.gridY})");
            return;
        }

        if (hover == null || hover == origin)
        {
            // Released over empty space or back on the origin — plain cancel, no turn switch.
            Debug.Log($"[Swipe:Cancel] no valid target from ({origin.gridX},{origin.gridY})");
            return;
        }

        // Released over some other point that is not a valid target: attempt the commit so
        // the exact [Reject:*] reason is logged, then note the swipe was cancelled.
        gm.TryCommitLine(origin, hover);
        Debug.Log($"[Swipe:Cancel] invalid target ({hover.gridX},{hover.gridY}) from ({origin.gridX},{origin.gridY})");
    }

    // Moves the target glow to 'target' (or clears it when null).
    void SetTargetHighlight(PointClick target)
    {
        if (highlightedTarget == target) return;
        if (highlightedTarget != null) highlightedTarget.SetHighlighted(false);
        highlightedTarget = target;
        if (highlightedTarget != null) highlightedTarget.SetHighlighted(true);
    }

    // Fully resets the machine (used on game over or as a safety net).
    void ResetSwipe()
    {
        if (originPoint != null) originPoint.SetSelected(false, Color.white);
        SetTargetHighlight(null);
        gm.HidePreview();
        state = SwipeState.Idle;
        originPoint = null;
        snappedTarget = null;
        currentHover = null;
    }

    // Reads the primary pointer for this frame: an active touch takes priority over the
    // mouse, so mobile and desktop share one code path.
    Pointer ReadPointer()
    {
        var result = new Pointer();

        Touchscreen ts = Touchscreen.current;
        if (ts != null)
        {
            var touch = ts.primaryTouch;
            bool down = touch.press.isPressed;
            bool released = touch.press.wasReleasedThisFrame;
            if (down || released)
            {
                result.isDown = down;
                result.pressedThisFrame = touch.press.wasPressedThisFrame;
                result.releasedThisFrame = released;
                result.pos = touch.position.ReadValue();
                return result;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            result.isDown = mouse.leftButton.isPressed;
            result.pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
            result.releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
            result.pos = mouse.position.ReadValue();
        }
        return result;
    }

    // True when the pointer is over a UI element, so clicks on buttons don't start swipes.
    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
