using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// Owns the pointer input state machine for drawing lines. Supports two coexisting
// input styles that share the same game rules (GameManager.TryCommitLine):
//
//   * Swipe (primary): press on a point A and drag. As soon as the finger's swept path
//     crosses a legal adjacent target, the line A->target commits AUTOMATICALLY — the
//     player does not have to release on the point. If that commit keeps the same
//     player's turn (a shape was closed) and the finger is still down, a new swipe
//     session chains from the committed point.
//   * Tap-tap (fallback): quick tap a point, then quick tap a neighbor.
//
// The "primary pointer" is a touch when the touchscreen is active, otherwise the mouse,
// so the same machine drives both mobile and desktop. Uses the new Input System only.
//
// State machine:
//   Idle     -> press over point P             => Pressed  (origin = P, highlight P)
//   Pressed  -> drag beyond threshold           => Dragging (clear pending tap, preview)
//   Pressed  -> release (no drag)               => quick tap -> GameManager.OnPointClicked
//   Dragging -> swept path enters a legal target => auto-commit; chain or Locked
//   Dragging -> release with no commit          => cancel, hide preview -> Idle
//   Locked   -> release                         => Idle (finger held after a non-chaining
//                                                  commit; ignores input until lifted)
//   any      -> isGameOver                      => reset to Idle
[DisallowMultipleComponent]
public class InputController : MonoBehaviour
{
    enum SwipeState { Idle, Pressed, Dragging, Locked }

    [Header("Tuning")]
    [Tooltip("Drag distance (screen pixels) beyond which a press becomes a swipe.")]
    public float dragThresholdPixels = 10f;

    [Tooltip("How far along the origin->target segment (0..1) the finger must travel " +
             "before an auto-commit engages. 0.9 = only in the last 10% before the target.")]
    public float swipeCommitProgress = 0.9f;

    [Tooltip("How far (world units) the finger may stray sideways from the origin->target " +
             "line and still auto-commit. Smaller = must aim more precisely at the target.")]
    public float swipeCommitPerpTolerance = 0.3f;

    private GameManager gm;

    private SwipeState state = SwipeState.Idle;
    private PointClick originPoint;      // where the current press/swipe session started
    private Vector2 pressScreenPos;      // screen position at press-down
    private Vector3 lastWorldPos;        // finger world position last drag frame (segment start)
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
            case SwipeState.Locked:   TickLocked(p);   break;
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

            skippedLogged.Clear();
            // Seed the swept-segment with the current finger position so the first frame
            // tests a zero-length segment (no false commit from a stale lastWorldPos).
            lastWorldPos = gm.ScreenToWorld(p.pos);
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

    // ---- State: Locked (finger still down after a non-chaining commit) ----
    void TickLocked(Pointer p)
    {
        // Ignore all movement; a fresh touch is required for the next move.
        if (p.releasedThisFrame || !p.isDown)
            state = SwipeState.Idle;
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

    // ========================================================================
    //  SWEPT-SEGMENT AUTO-COMMIT  ("90% progress with perpendicular tolerance")
    // ------------------------------------------------------------------------
    //  A line origin(A)->target(P) commits only when the finger has travelled
    //  most of the way to P and is aimed roughly at it. For a finger position F
    //  we measure two things relative to the A->P segment:
    //
    //    progress = clamp01( dot(F - A, dir) / |A->P| )   // 0 at A, 1 at P
    //    perp     = |cross(F - A, dir)|                    // sideways offset from the line
    //
    //  Commit requires  progress >= swipeCommitProgress (0.9)  AND
    //                   perp <= swipeCommitPerpTolerance (0.3) AND the move is legal.
    //  So the trigger zone is a small slab hugging the last 10% before P, not a
    //  wide circle — the player keeps control until they clearly commit.
    //
    //  We do not only test where the finger IS; we test the SEGMENT it swept this
    //  frame (lastWorldPos -> currentWorldPos). If ANY point along that segment
    //  satisfies both conditions, we commit — this is what preserves the "fast
    //  flick straight through the target" feel while keeping the zone tight.
    //
    //  When several legal targets qualify at once (diagonals), we pick the one
    //  CLOSEST TO THE ORIGIN (first the finger reaches). Adjacent-but-illegal
    //  points that are swept are logged once as [Swipe:Skip] and ignored.
    //
    //  The preview snap uses the SAME threshold: the preview only turns solid /
    //  highlights a target once the current finger position itself clears the 0.9
    //  progress + perpendicular test, so the visuals never promise a snap that has
    //  not engaged. (In practice that coincides with the commit frame, so before
    //  commit the preview simply follows the finger.)
    // ========================================================================
    void UpdateDrag(Vector2 screenPos)
    {
        Vector3 worldPos = gm.ScreenToWorld(screenPos);
        Vector2 segA = lastWorldPos;
        Vector2 segB = worldPos;

        List<PointClick> adjacent = gm.GetAdjacentPoints(originPoint);
        Vector2 originPos = originPoint.transform.position;

        // --- 1) Auto-commit: swept segment enters the progress+perpendicular zone ---
        PointClick commitTarget = null;
        float bestOriginDistSq = float.MaxValue;

        foreach (PointClick pt in adjacent)
        {
            Vector2 ptPos = pt.transform.position;
            if (!SegmentEntersCommitZone(segA, segB, originPos, ptPos)) continue;

            if (gm.IsLegalMove(originPoint, pt))
            {
                // Prefer the target closest to the origin (first one the finger reaches).
                float od = (ptPos - originPos).sqrMagnitude;
                if (od < bestOriginDistSq) { bestOriginDistSq = od; commitTarget = pt; }
            }
            else if (skippedLogged.Add(pt))
            {
                Debug.Log($"[Swipe:Skip] ({pt.gridX},{pt.gridY}) not a legal target from ({originPoint.gridX},{originPoint.gridY})");
            }
        }

        if (commitTarget != null)
        {
            AutoCommit(commitTarget, worldPos);
            return;
        }

        // --- 2) No commit: preview snaps solid ONLY if the current finger position
        //        itself clears the same 0.9-progress + perpendicular test. ---
        PointClick snapTarget = null;
        float bestSnapSq = float.MaxValue;
        foreach (PointClick pt in adjacent)
        {
            if (!gm.IsLegalMove(originPoint, pt)) continue;
            if (!PointInCommitZone(worldPos, originPos, pt.transform.position)) continue;
            float od = ((Vector2)pt.transform.position - originPos).sqrMagnitude;
            if (od < bestSnapSq) { bestSnapSq = od; snapTarget = pt; }
        }

        if (snapTarget != null)
        {
            SetTargetHighlight(snapTarget);
            gm.UpdatePreview(originPos, snapTarget.transform.position, true);
        }
        else
        {
            SetTargetHighlight(null);
            gm.UpdatePreview(originPos, worldPos, false);
        }

        lastWorldPos = worldPos;
    }

    // True if finger position 'f' is inside the commit zone of target 'p' for origin 'a':
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

    // True if ANY point along the swept segment [s0, s1] satisfies the commit zone of
    // target 'p' for origin 'a'. Solved analytically: the two commit conditions are each
    // linear in the segment parameter s, giving an s-interval; the segment enters the zone
    // iff the intersection of those intervals with [0, 1] is non-empty. Exact for fast
    // flicks (no sampling gaps).
    bool SegmentEntersCommitZone(Vector2 s0, Vector2 s1, Vector2 a, Vector2 p)
    {
        Vector2 ap = p - a;
        float len = ap.magnitude;
        if (len < 1e-6f) return false;
        Vector2 dir = ap / len;

        // along(s) = along0 + s*alongD  (progress condition: along >= progress*len)
        float along0 = Vector2.Dot(s0 - a, dir);
        float alongD = Vector2.Dot(s1 - s0, dir);
        // perp(s) = perp0 + s*perpD (signed; condition: |perp| <= tol)
        float perp0 = (s0 - a).x * dir.y - (s0 - a).y * dir.x;
        float perpD = (s1 - s0).x * dir.y - (s1 - s0).y * dir.x;

        float threshold = swipeCommitProgress * len;
        float tol = swipeCommitPerpTolerance;

        float lo = 0f, hi = 1f;

        // Progress: along0 + s*alongD >= threshold
        if (Mathf.Abs(alongD) < 1e-9f)
        {
            if (along0 < threshold) return false;
        }
        else
        {
            float sBound = (threshold - along0) / alongD;
            if (alongD > 0f) lo = Mathf.Max(lo, sBound);
            else hi = Mathf.Min(hi, sBound);
        }
        if (lo > hi) return false;

        // Perpendicular: -tol <= perp0 + s*perpD <= tol
        if (Mathf.Abs(perpD) < 1e-9f)
        {
            if (Mathf.Abs(perp0) > tol) return false;
        }
        else
        {
            float s1Bound = (tol - perp0) / perpD;
            float s2Bound = (-tol - perp0) / perpD;
            lo = Mathf.Max(lo, Mathf.Min(s1Bound, s2Bound));
            hi = Mathf.Min(hi, Mathf.Max(s1Bound, s2Bound));
        }

        return lo <= hi;
    }

    // Commits origin->target via the shared game logic, then either chains a new swipe
    // session (same player continued) or locks until the finger is released.
    void AutoCommit(PointClick target, Vector3 worldPos)
    {
        PointClick origin = originPoint;
        int playerBefore = gm.currentPlayer;

        // Clear this session's transient visuals before committing.
        SetTargetHighlight(null);
        origin.SetSelected(false, Color.white);

        bool committed = gm.TryCommitLine(origin, target);
        if (!committed)
        {
            // Defensive: legality was verified this same frame, so this should not happen.
            Debug.Log($"[Swipe:Cancel] auto-commit rejected ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})");
            gm.HidePreview();
            state = SwipeState.Locked;
            originPoint = null;
            return;
        }

        Debug.Log($"[Swipe:AutoCommit] ({origin.gridX},{origin.gridY})->({target.gridX},{target.gridY})");

        // The commit may have ended the game.
        if (gm.isGameOver)
        {
            gm.HidePreview();
            state = SwipeState.Locked;
            originPoint = null;
            return;
        }

        // Same player still to move => a shape was closed => allow a chained swipe.
        if (gm.currentPlayer == playerBefore)
        {
            originPoint = target;
            skippedLogged.Clear();
            lastWorldPos = worldPos; // continue the swept path from the current finger pos
            target.SetSelected(true, gm.GetCurrentColor());
            gm.UpdatePreview(target.transform.position, worldPos, false);
            Debug.Log($"[Swipe:Chain] continue from ({target.gridX},{target.gridY})");
            // state stays Dragging
        }
        else
        {
            // Turn passed to the other player — no chaining; wait for a fresh touch.
            gm.HidePreview();
            state = SwipeState.Locked;
            originPoint = null;
        }
    }

    // Release while dragging with no auto-commit having happened => silent cancel.
    void HandleDragRelease()
    {
        PointClick origin = originPoint;

        SetTargetHighlight(null);
        gm.HidePreview();
        if (origin != null) origin.SetSelected(false, Color.white);

        state = SwipeState.Idle;
        originPoint = null;
        skippedLogged.Clear();

        if (origin != null)
            Debug.Log($"[Swipe:Cancel] released with no commit from ({origin.gridX},{origin.gridY})");
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

    // True when the pointer is over a UI element, so clicks on buttons don't start swipes.
    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
