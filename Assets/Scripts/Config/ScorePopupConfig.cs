using UnityEngine;

// Editor-tunable look & feel of the "+N" score popup: WHERE the number appears relative to
// the shape that was just closed, and HOW it animates (rise, scale, fade, speed, easing).
//
// Lives as a ScriptableObject asset so it can be tuned from the Inspector — including live
// in Play Mode: every new popup reads the current values, and changes made while playing are
// kept (it's an asset, not a scene object). The asset's inspector has a "Play test popup"
// button (Play Mode only) to preview a change without closing a shape.
//
// Wiring: ScorePopupUI references a ScorePopupConfig. If none is assigned it loads one from
// Resources ("ScorePopupConfig"); if that is missing too, the built-in defaults below are
// used, so the popup always works.
[CreateAssetMenu(
    fileName = "ScorePopupConfig",
    menuName = "Dots and Figures/Score Popup Config",
    order = 1)]
public class ScorePopupConfig : ScriptableObject
{
    [Header("Position")]
    [Tooltip("Horizontal attach point of the number on the shape center: 0 = its left edge, " +
             "0.5 = its middle, 1 = its right edge.")]
    [Range(0f, 1f)]
    [SerializeField] private float pivotX = 0.5f;

    [Tooltip("Vertical attach point of the number on the shape center: 0 = its bottom edge " +
             "(the number grows up out of the center), 0.5 = centered on it, 1 = its top edge.")]
    [Range(0f, 1f)]
    [SerializeField] private float pivotY = 0f;

    [Tooltip("Extra shift from the shape center, in UI pixels (canvas units). +X = right, " +
             "+Y = up.")]
    [SerializeField] private Vector2 offsetPixels = Vector2.zero;

    [Header("Timing")]
    [Tooltip("Playback speed of the whole animation. 1 = the durations below as written, " +
             "1.5 = everything 1.5x faster, 0.5 = twice as slow.")]
    [Range(0.1f, 5f)]
    [SerializeField] private float speed = 1.5f;

    [Tooltip("Total length of the animation in seconds (at speed 1). The number rises for " +
             "this whole time, then disappears.")]
    [Min(0.01f)]
    [SerializeField] private float duration = 0.8f;

    [Tooltip("Seconds of fade-out at the END of the animation (at speed 1). 0 = no fade.")]
    [Min(0f)]
    [SerializeField] private float fadeDuration = 0.2f;

    [Tooltip("Seconds of scaling at the END of the animation (at speed 1). Equal to Duration " +
             "(or more) = scales from the very first frame; smaller = starts scaling later.")]
    [Min(0f)]
    [SerializeField] private float scaleDuration = 0.8f;

    [Header("Rise")]
    [Tooltip("How far the number drifts upward over the animation, in UI pixels. " +
             "Negative = drifts down.")]
    [SerializeField] private float riseDistancePixels = 80f;

    [Tooltip("Easing of the rise: X = animation progress (0..1), Y = share of the rise " +
             "distance covered (0..1). Values above 1 overshoot.")]
    [SerializeField] private AnimationCurve riseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Scale")]
    [Tooltip("Size of the number when it appears (1 = prefab size).")]
    [Min(0f)]
    [SerializeField] private float startScale = 1f;

    [Tooltip("Size of the number at the end of the animation (1 = no change, 0.5 = half).")]
    [Min(0f)]
    [SerializeField] private float endScale = 0.5f;

    [Tooltip("Easing of the scale: X = scaling progress (0..1), Y = 0 at Start Scale, " +
             "1 at End Scale. Values outside 0..1 overshoot (e.g. a bounce).")]
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Fade")]
    [Tooltip("Easing of the fade: X = fade progress (0..1), Y = 0 fully visible, " +
             "1 fully transparent.")]
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Coin")]
    [Tooltip("Show the number on a white coin (disc + colored rim + shadow). Off = bare number.")]
    [SerializeField] private bool showCoin = true;

    [Tooltip("Free space between the digits and the coin edge, in UI pixels. The coin grows " +
             "with the number (+12 gets a wider coin than +2).")]
    [Min(0f)]
    [SerializeField] private float coinPaddingPixels = 40f;

    [Tooltip("Smallest coin diameter in UI pixels, so short numbers don't get a tiny coin.")]
    [Min(0f)]
    [SerializeField] private float minCoinDiameter = 150f;

    [Tooltip("Color of the coin face.")]
    [SerializeField] private Color coinColor = Color.white;

    [Tooltip("Tint the coin rim with the scoring player's color. Off = use Rim Color.")]
    [SerializeField] private bool rimUsesPlayerColor = true;

    [Tooltip("Rim color when Rim Uses Player Color is off.")]
    [SerializeField] private Color rimColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [Tooltip("Tint the digits with the scoring player's color. Off = use Text Color.")]
    [SerializeField] private bool textUsesPlayerColor = true;

    [Tooltip("Digit color when Text Uses Player Color is off.")]
    [SerializeField] private Color textColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [Tooltip("Color (and transparency) of the shadow under the coin. Alpha 0 = no shadow.")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.25f);

    [Tooltip("Shadow shift from the coin, in UI pixels. -Y = below.")]
    [SerializeField] private Vector2 shadowOffsetPixels = new Vector2(0f, -8f);

    // ---- Position --------------------------------------------------------------------

    public Vector2 Pivot => new Vector2(pivotX, pivotY);
    public Vector2 OffsetPixels => offsetPixels;

    // ---- Timing (already divided by speed — these are real seconds) --------------------

    float SafeSpeed => Mathf.Max(0.01f, speed);
    public float Duration => Mathf.Max(0.0001f, duration / SafeSpeed);
    public float FadeDuration => Mathf.Clamp(fadeDuration / SafeSpeed, 0f, Duration);
    public float ScaleDuration => Mathf.Clamp(scaleDuration / SafeSpeed, 0f, Duration);

    // ---- Rise / scale / fade -----------------------------------------------------------

    public float RiseDistancePixels => riseDistancePixels;
    public float StartScale => startScale;
    public float EndScale => endScale;

    public float EvaluateRise(float t) => Evaluate(riseCurve, t);
    public float EvaluateScale(float t) => Evaluate(scaleCurve, t);
    public float EvaluateFade(float t) => Evaluate(fadeCurve, t);

    // ---- Coin --------------------------------------------------------------------------

    public bool ShowCoin => showCoin;
    public float CoinPaddingPixels => coinPaddingPixels;
    public float MinCoinDiameter => minCoinDiameter;
    public Color CoinColor => coinColor;
    public Color ShadowColor => shadowColor;
    public Vector2 ShadowOffsetPixels => shadowOffsetPixels;

    public Color GetRimColor(Color playerColor) =>
        rimUsesPlayerColor ? new Color(playerColor.r, playerColor.g, playerColor.b, 1f) : rimColor;

    public Color GetTextColor(Color playerColor) => textUsesPlayerColor ? playerColor : textColor;

    // A missing/empty curve behaves as linear, so a cleared curve never freezes the popup.
    static float Evaluate(AnimationCurve curve, float t)
    {
        t = Mathf.Clamp01(t);
        return curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
    }

    // Built-in defaults (the field initializers above) for when no asset exists.
    public static ScorePopupConfig CreateDefault()
    {
        ScorePopupConfig cfg = CreateInstance<ScorePopupConfig>();
        cfg.name = "ScorePopupConfig (built-in defaults)";
        cfg.hideFlags = HideFlags.DontSave;
        return cfg;
    }
}
