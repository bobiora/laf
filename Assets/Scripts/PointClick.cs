using UnityEngine;

// A single grid point. Passive by design: it no longer reads input on its own — the
// input state machine (InputController) owns press/drag/release detection and does the
// hit-testing (see GameManager.GetPointAtScreenPos). This component just holds the grid
// coordinates and manages its own visual state (selection color + target highlight).
public class PointClick : MonoBehaviour
{
    public int gridX;
    public int gridY;
    public bool isSelected = false;

    private SpriteRenderer sr;
    private Color defaultColor = Color.white;

    // Base local scale captured at load, so the swipe target "glow" (scale-up) can be
    // applied and reverted without drift.
    private Vector3 baseScale;
    private bool highlighted = false;
    private const float HighlightScale = 1.2f;

    void Awake()
    {
        baseScale = transform.localScale;
    }

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = defaultColor;
    }

    // Selection color (origin / firstPoint highlight). Same visual used by tap-tap and
    // by the swipe origin.
    public void SetSelected(bool selected, Color color)
    {
        isSelected = selected;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = isSelected ? color : Color.white;
    }

    // Target "release here to confirm" highlight during a swipe: scale the point up a
    // little. Reverts to the base scale when turned off.
    public void SetHighlighted(bool on)
    {
        if (highlighted == on) return;
        highlighted = on;
        transform.localScale = on ? baseScale * HighlightScale : baseScale;
    }
}
