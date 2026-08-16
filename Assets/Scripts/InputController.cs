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

    [Tooltip("The finger must aim within this many degrees of a neighbor's direction (from " +
             "the start point) for the preview to snap onto it. Neighbors are 45 degrees " +
             "apart, so keep this below ~44. Direction-based snapping (instead of requiring " +
             "the finger to physically reach the tiny target dot) is what makes drawing " +
             "reliable on a zoomed-out 10x10 board where the dots are only a few pixels wide.")]
    public float snapAngleDegrees = 38f;

    [Tooltip("Dead-zone around the start point, as a fraction of the distance to the nearest " +
             "neighbor. Inside it the drag shows no target; dragging back into it cancels a " +
             "pending target (the design's 'return to start = cancel').")]
    public float startDeadZoneFraction = 0.35f;

    [Tooltip("Verbose input logging ([Swipe:*]). OPT-IN — keep off for shipping so it does " +
             "not hitch on device. Independent of GameManager.debugEndGame.")]
    public bool debugInput = false;

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
        if (debugInput) Debug.Log($"[Swipe:Start] press on ({hit.gridX},{hit.gridY})");
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
            HandleDragRelease(p.pos);
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
    //   1) CANCEL: if the finger is back inside the start-point dead-zone, drop the
    //      pending target and hide the preview. The drag session stays alive, so the
    //      player can drag out again and aim at a (possibly different) target.
    //
    //   2) RE-TARGET by AIM DIRECTION: pick the legal adjacent point whose direction
    //      from the start best matches the finger's drag direction (within
    //      snapAngleDegrees). This does not require the finger to reach the target's
    //      collider, so a diagonal snaps reliably even when dots are only a few pixels
    //      apart on a zoomed-out 10x10 board. Aiming A -> B -> C overwrites the target;
    //      the last one aimed at is what gets committed on release.
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
        Vector2 offset = (Vector2)worldPos - originPos;
        float dist = offset.magnitude;

        List<PointClick> adjacent = gm.GetAdjacentPoints(originPoint);

        // Dead-zone radius = a fraction of the distance to the nearest neighbor. Using the
        // NEAREST neighbor (an orthogonal one) keeps the zone small enough that it never
        // reaches out to a diagonal target.
        float nearestAdjSq = float.MaxValue;
        foreach (PointClick pt in adjacent)
        {
            float dSq = ((Vector2)pt.transform.position - originPos).sqrMagnitude;
            if (dSq < nearestAdjSq) nearestAdjSq = dSq;
        }
        float deadZone = Mathf.Sqrt(Mathf.Max(nearestAdjSq, 1e-6f)) * startDeadZoneFraction;

        // --- 1) Cancel: finger essentially back on the START point (inside the dead-zone) ---
        // Replaces the old "nearest collider center == start" test, which — with dot colliders
        // (radius 1.4) larger than the spacing (1.2) — treated the whole first half of every
        // drag as "on the start point" and kept hiding the preview.
        if (dist < deadZone)
        {
            if (currentTarget != null && debugInput)
                Debug.Log($"[Swipe:Cancel] back on start ({originPoint.gridX},{originPoint.gridY}), pending target dropped");
            SetTargetHighlight(null);
            currentTarget = null;
            gm.HidePreview();
            return;
        }

        // --- 2) Re-target by AIM DIRECTION, not by reaching the target ---
        // Pick the legal neighbor whose direction (start->neighbor) best matches the finger's
        // drag direction, provided the finger is aiming within snapAngleDegrees of it. This
        // commits a diagonal as soon as the finger clearly heads that way, even on a tiny
        // zoomed-out board where the finger never lands on the target's small collider.
        Vector2 dir = offset / dist;
        float minDot = Mathf.Cos(snapAngleDegrees * Mathf.Deg2Rad);
        PointClick snapTarget = null;
        float bestDot = minDot;

        foreach (PointClick pt in adjacent)
        {
            Vector2 pd = (Vector2)pt.transform.position - originPos;
            float pl = pd.magnitude;
            if (pl < 1e-6f) continue;
            float aim = Vector2.Dot(dir, pd / pl);
            if (aim < minDot) continue; // finger not aimed at this neighbor

            if (gm.IsLegalMove(originPoint, pt))
            {
                if (aim > bestDot) { bestDot = aim; snapTarget = pt; }
            }
            else if (debugInput && skippedLogged.Add(pt))
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

    // ---- Release: the ONLY place a line is committed ----
    // Commits start->target through the shared game logic. The target is the sticky
    // currentTarget picked during the drag; if aim-based snapping never latched a target
    // (e.g. a very short flick on a tiny board), currentTarget stays null even though the
    // finger is released right on a valid neighbor. To avoid dropping that clearly-intended
    // line, we fall
    // back to the point UNDER the finger at release: if it is a legal neighbor of the start
    // point we commit to it. This does NOT loosen any rule (TryCommitLine still validates)
    // and preserves cancel-on-start (releasing on the start point commits nothing).
    void HandleDragRelease(Vector2 releaseScreenPos)
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

        if (origin == null)
            return;

        // Fallback: no sticky target, but the finger may be released on a legal neighbor.
        if (target == null || target == origin)
            target = ResolveReleaseTarget(origin, releaseScreenPos);

        // Cancel: still nothing (released on empty space or back on the start point).
        if (target == null || target == origin)
        {
            if (debugInput)
                Debug.Log($"[Swipe:Cancel] released with no target from ({origin.gridX},{origin.gridY})");
            return;
        }

        // Commit start->target. TryCommitLine runs the full shared pipeline: legality
        // check, draw, planar loop/shape detection, scoring, turn switch and end-game.
        // The finger is already up, so there is no chaining — the next line is a fresh drag.
        bool committed = gm.TryCommitLine(origin, target);
        if (debugInput)
            Debug.Log(committed
                ? $"[Swipe:Commit] ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})"
                : $"[Swipe:Reject] ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})");
    }

    // Resolves the intended target from the release position when no target snapped during
    // the drag. Returns the point under the finger if it is a legal neighbor of 'origin';
    // otherwise null (so releasing on the start point or empty space still cancels).
    PointClick ResolveReleaseTarget(PointClick origin, Vector2 releaseScreenPos)
    {
        PointClick hovered = gm.GetPointAtScreenPos(releaseScreenPos);
        if (hovered == null || hovered == origin) return null;
        return gm.IsLegalMove(origin, hovered) ? hovered : null;
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
