using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// Owns the pointer input state machine for drawing lines. Supports two coexisting
// input styles that share the same game rules (GameManager.TryCommitLine):
//
//   * Drag (primary): press on a point A and drag. While the finger is held, the preview
//     line stretches toward the nearest valid adjacent point and SNAPS onto it when the
//     finger aims at it. The snapped point becomes the "current target". The target is
//     RE-TARGETABLE: dragging from A onto B, then onto C, keeps updating the target — the
//     last one hovered wins. The line A->target is committed ONLY on release (pointer up),
//     never mid-drag. Dragging the finger back onto the START point cancels the pending
//     connection (preview disappears); the player may then drag back out and aim again.
//   * Tap-tap (fallback): quick tap a point, then quick tap a neighbor.
//
// The "primary pointer" is a touch when the touchscreen is active, otherwise the mouse,
// so the same machine drives both mobile and desktop. Uses the new Input System only.
//
// State machine:
//   Idle     -> press over point P              => Pressed  (start = P, highlight P)
//   Pressed  -> drag beyond threshold           => Dragging (clear pending tap, preview)
//   Pressed  -> release (no drag)               => quick tap -> GameManager.OnPointClicked
//   Dragging -> aim at a legal neighbor          => snap: currentTarget = that neighbor
//   Dragging -> aim back at the START point      => cancel pending target, hide preview
//   Dragging -> release with a target            => commit start->target (this move only)
//   Dragging -> release with no/target==start    => cancel, no connection
//   any      -> isGameOver                       => reset to Idle
[DisallowMultipleComponent]
public class InputController : MonoBehaviour
{
    enum SwipeState { Idle, Pressed, Dragging }

    [Header("Tuning")]
    [Tooltip("Drag distance (screen pixels) beyond which a press becomes a drag.")]
    public float dragThresholdPixels = 10f;

    [Tooltip("How far along the start->target segment (0..1) the finger must aim before " +
             "the preview snaps onto that target. 0.9 = only in the last 10% before the target.")]
    public float swipeCommitProgress = 0.9f;

    [Tooltip("How far (world units) the finger may stray sideways from the start->target " +
             "line and still snap. Smaller = must aim more precisely at the target.")]
    public float swipeCommitPerpTolerance = 0.3f;

    private GameManager gm;

    private SwipeState state = SwipeState.Idle;

    // The point the current press/drag session started from. The committed line always
    // runs from here. (Named "start point" in the design; kept as originPoint in code.)
    private PointClick originPoint;

    // The point the drag is currently aimed at — the one that WILL be connected on release.
    // Sticky: it holds the last valid neighbor hovered and only changes when the finger
    // snaps onto a different neighbor, or is reset to null when the finger returns to the
    // start point (cancel). null = no pending connection.
    private PointClick currentTarget;

    private Vector2 pressScreenPos;       // screen position at press-down
    private PointClick highlightedTarget; // point currently showing the approach glow

    // Adjacent-but-illegal points already logged as skipped this session (avoid spam).
    private readonly HashSet<PointClick> skippedLogged = new HashSet<PointClick>();

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
            case SwipeState.Idle:     TickIdle(p);     break;
            case SwipeState.Pressed:  TickPressed(p);  break;
            case SwipeState.Dragging: TickDragging(p); break;
        }
    }

    // ---- State: Idle ----
    void TickIdle(Pointer p)
    {
        if (!p.pressedThisFrame) return;

        // Never let a drag start on top of UI (buttons like "Back to menu").
        if (IsPointerOverUI()) return;

        PointClick hit = gm.GetPointAtScreenPos(p.pos);
        if (hit == null) return; // press over empty space — no-op

        originPoint = hit;
        currentTarget = null;
        pressScreenPos = p.pos;
        state = SwipeState.Pressed;

        // Immediate feedback: start point uses the same highlight as a tap-tap selection.
        originPoint.SetSelected(true, gm.GetCurrentColor());
        Debug.Log($"[Swipe:Start] press on ({hit.gridX},{hit.gridY})");
    }

    // ---- State: Pressed (start point known, not yet dragging) ----
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
            // A drag has begun. Once dragging, any pending tap-tap selection is dropped
            // so the two flows never conflict.
            state = SwipeState.Dragging;
            currentTarget = null;
            gm.CancelSelection();
            originPoint.SetSelected(true, gm.GetCurrentColor()); // re-assert start highlight
            gm.ShowPreview();

            skippedLogged.Clear();
            UpdateDrag(p.pos);
        }
    }

    // ---- State: Dragging (preview active, choosing/re-choosing the target) ----
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
    // start highlight first; OnPointClicked re-applies it if the point becomes firstPoint.
    void HandleQuickTap()
    {
        PointClick origin = originPoint;
        state = SwipeState.Idle;
        originPoint = null;
        currentTarget = null;

        if (origin != null)
        {
            origin.SetSelected(false, Color.white);
            gm.OnPointClicked(origin);
        }
    }

    // ========================================================================
    //  DRAG UPDATE  —  pick / re-pick the target, or cancel back to the start
    // ------------------------------------------------------------------------
    //  Each drag frame does three things, in order:
    //
    //   1) CANCEL: if the finger is back over the START point, drop the pending
    //      target and hide the preview. The drag session stays alive, so the player
    //      can drag out again and aim at a (possibly different) target.
    //
    //   2) RE-TARGET: find the nearest valid adjacent point the finger is aiming at
    //      (progress >= swipeCommitProgress along start->point AND within the
    //      perpendicular tolerance — the same snap test used before). If one is found
    //      it becomes the new currentTarget. Aiming A -> B -> C simply overwrites it;
    //      the last one hovered is what gets committed on release.
    //
    //   3) PREVIEW: draw the preview line. With a currentTarget it locks solid onto
    //      that target (so the player can see exactly what release will connect);
    //      with none it trails the raw finger position (semi-transparent).
    //
    //  NOTHING is committed here — a line is only created on release (HandleDragRelease).
    // ========================================================================
    void UpdateDrag(Vector2 screenPos)
    {
        Vector3 worldPos = gm.ScreenToWorld(screenPos);
        Vector2 originPos = originPoint.transform.position;

        // --- 1) Cancel: finger returned onto the START point ---
        PointClick hovered = gm.GetPointAtScreenPos(screenPos);
        if (hovered == originPoint)
        {
            if (currentTarget != null)
                Debug.Log($"[Swipe:Cancel] back on start ({originPoint.gridX},{originPoint.gridY}), pending target dropped");
            SetTargetHighlight(null);
            currentTarget = null;
            gm.HidePreview(); // per design: preview disappears while on the start point
            return;
        }

        // --- 2) Re-target: nearest valid adjacent point the finger is aiming at ---
        List<PointClick> adjacent = gm.GetAdjacentPoints(originPoint);
        PointClick snapTarget = null;
        float bestSnapSq = float.MaxValue;

        foreach (PointClick pt in adjacent)
        {
            if (!PointInCommitZone(worldPos, originPos, pt.transform.position)) continue;

            if (gm.IsLegalMove(originPoint, pt))
            {
                // Prefer the target closest to the start (first one the finger reaches).
                float od = ((Vector2)pt.transform.position - originPos).sqrMagnitude;
                if (od < bestSnapSq) { bestSnapSq = od; snapTarget = pt; }
            }
            else if (skippedLogged.Add(pt))
            {
                Debug.Log($"[Swipe:Skip] ({pt.gridX},{pt.gridY}) not a legal target from ({originPoint.gridX},{originPoint.gridY})");
            }
        }

        if (snapTarget != null)
        {
            // Aiming at a valid neighbor -> it becomes the (sticky) current target.
            currentTarget = snapTarget;
        }

        // --- 3) Preview ---
        gm.ShowPreview();
        if (currentTarget != null)
        {
            // Lock the preview onto the point that release would connect.
            SetTargetHighlight(currentTarget);
            gm.UpdatePreview(originPos, currentTarget.transform.position, true);
        }
        else
        {
            // No target yet — trail the finger (semi-transparent).
            SetTargetHighlight(null);
            gm.UpdatePreview(originPos, worldPos, false);
        }
    }

    // True if finger position 'f' is inside the snap zone of target 'p' for start 'a':
    // progress along a->p is >= swipeCommitProgress AND perpendicular distance to the
    // a->p line is <= swipeCommitPerpTolerance.
    bool PointInCommitZone(Vector2 f, Vector2 a, Vector2 p)
    {
        Vector2 ap = p - a;
        float len = ap.magnitude;
        if (len < 1e-6f) return false;

        Vector2 dir = ap / len;
        Vector2 fa = f - a;
        float progress = Mathf.Clamp01(Vector2.Dot(fa, dir) / len);
        if (progress < swipeCommitProgress) return false;

        float perp = Mathf.Abs(fa.x * dir.y - fa.y * dir.x);
        return perp <= swipeCommitPerpTolerance;
    }

    // ---- Release: the ONLY place a line is committed ----
    // Commits start->currentTarget through the shared game logic. If there is no target,
    // or the finger came to rest back on the start point (currentTarget == null), nothing
    // is created — the whole action is cancelled.
    void HandleDragRelease()
    {
        PointClick origin = originPoint;
        PointClick target = currentTarget;

        // Clear all transient drag visuals first.
        SetTargetHighlight(null);
        if (origin != null) origin.SetSelected(false, Color.white);
        gm.HidePreview();

        state = SwipeState.Idle;
        originPoint = null;
        currentTarget = null;
        skippedLogged.Clear();

        // Cancel: nothing hovered, or target somehow equals the start point.
        if (origin == null || target == null || target == origin)
        {
            if (origin != null)
                Debug.Log($"[Swipe:Cancel] released with no target from ({origin.gridX},{origin.gridY})");
            return;
        }

        // Commit start->target. TryCommitLine runs the full shared pipeline: legality
        // check, draw, planar loop/shape detection, scoring, turn switch and end-game.
        // The finger is already up, so there is no chaining — the next line is a fresh drag.
        bool committed = gm.TryCommitLine(origin, target);
        Debug.Log(committed
            ? $"[Swipe:Commit] ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})"
            : $"[Swipe:Reject] ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})");
    }

    // Moves the approach glow to 'target' (or clears it when null).
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
        currentTarget = null;
        skippedLogged.Clear();
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

    // True when the pointer is over a UI element, so clicks on buttons don't start drags.
    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
