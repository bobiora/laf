using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A single "+N" score number. One of these is spawned (from a prefab) every time a
// scorable shape is claimed — by default as a white "coin" (shadow + face + rim tinted with
// the player's color) with the digits on it. It appears at its rest position, drifts up, and
// fades out while scaling — then destroys itself. Timing, distances, scales and easing all come from
// a ScorePopupConfig asset. Instances are independent, so several can float on screen at
// once during a combo; nothing is queued or reused.
//
// This is UI: it lives under a Screen-Space Overlay Canvas and moves via
// RectTransform.anchoredPosition (NOT world space). Non-interactive — it never eats taps.
[DisallowMultipleComponent]
public class ScorePopup : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Label that shows the awarded points (e.g. +2). Auto-found on this object " +
             "or its children if left empty.")]
    [SerializeField] private TMP_Text pointsText;

    [Tooltip("CanvasGroup used to fade the number out. Added at runtime if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Coin (optional — children stretched over this object)")]
    [Tooltip("Soft disc drawn under the coin as its shadow.")]
    [SerializeField] private Image coinShadow;

    [Tooltip("White coin face behind the digits.")]
    [SerializeField] private Image coinFace;

    [Tooltip("Ring around the coin edge, tinted with the player's color.")]
    [SerializeField] private Image coinRim;

    private RectTransform rectTransform;
    private ScorePopupConfig config;
    private Action<ScorePopup> onFinished;

    void Awake()
    {
        rectTransform = (RectTransform)transform;

        if (pointsText == null) pointsText = GetComponentInChildren<TMP_Text>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Never steal input.
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    // Start the appear -> rise -> scale/fade animation. Called by ScorePopupUI right after
    // the instance is spawned and positioned. onFinished lets the spawner drop its reference.
    public void Play(int points, Color color, ScorePopupConfig config, Action<ScorePopup> onFinished)
    {
        this.config = config;
        this.onFinished = onFinished;

        if (pointsText != null)
        {
            pointsText.text = $"+{points}";
            pointsText.color = config.GetTextColor(color);
        }
        ApplyCoinLook(color);
        FitToContent();

        StartCoroutine(Animate(config));
    }

    // Update the displayed value in place, keeping the animation running. Used when a
    // single move closes two figures at once: the second award is folded into this number
    // (e.g. +2 becomes +4) instead of spawning a separate popup.
    public void SetPoints(int points)
    {
        if (pointsText != null) pointsText.text = $"+{points}";
        FitToContent();
    }

    // Colors + visibility of the coin layers (shadow, face, player-colored rim).
    private void ApplyCoinLook(Color playerColor)
    {
        bool show = config.ShowCoin;

        if (coinShadow != null)
        {
            coinShadow.gameObject.SetActive(show && config.ShadowColor.a > 0f);
            coinShadow.color = config.ShadowColor;
            coinShadow.rectTransform.anchoredPosition = config.ShadowOffsetPixels;
        }
        if (coinFace != null)
        {
            coinFace.gameObject.SetActive(show);
            coinFace.color = config.CoinColor;
        }
        if (coinRim != null)
        {
            coinRim.gameObject.SetActive(show);
            coinRim.color = config.GetRimColor(playerColor);
        }
    }

    // Size the container to its content, so the config's pivot (bottom / middle / top) refers
    // to what's visible rather than an arbitrary prefab size. With the coin: a square that fits
    // the digits plus padding (the coin children stretch over it). Without: the digits' box.
    // The pivot stays put while the rect resizes around it.
    private void FitToContent()
    {
        if (pointsText == null || rectTransform == null || config == null) return;

        pointsText.ForceMeshUpdate();
        Vector2 digits = pointsText.textBounds.size;
        if (digits.x <= 0f || digits.y <= 0f)
            digits = pointsText.GetPreferredValues(pointsText.text);

        Vector2 size = digits;
        if (config.ShowCoin)
        {
            float d = Mathf.Max(config.MinCoinDiameter,
                                Mathf.Max(digits.x, digits.y) + 2f * config.CoinPaddingPixels);
            size = new Vector2(d, d);
        }

        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
    }

    private IEnumerator Animate(ScorePopupConfig config)
    {
        // Snapshot the config so a value edited mid-flight can't make this popup jump.
        float total = config.Duration;
        float fadeDuration = config.FadeDuration;
        float scaleDuration = config.ScaleDuration;
        float fadeStart = total - fadeDuration;   // fade = last fadeDuration seconds
        float scaleStart = total - scaleDuration; // scale = last scaleDuration seconds
        float startScale = config.StartScale;
        float endScale = config.EndScale;

        // Appear immediately, fully visible, at the rest position set by the spawner.
        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0f, config.RiseDistancePixels);
        Vector3 baseScale = rectTransform.localScale;
        rectTransform.localScale = baseScale * startScale;
        canvasGroup.alpha = 1f;

        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / total);

            // Unclamped lerps let overshooting curves (bounce/elastic) go past the target.
            rectTransform.anchoredPosition =
                Vector2.LerpUnclamped(startPos, endPos, config.EvaluateRise(p));

            if (fadeDuration > 0f && t >= fadeStart)
                canvasGroup.alpha = 1f - config.EvaluateFade((t - fadeStart) / fadeDuration);

            if (scaleDuration > 0f && t >= scaleStart)
            {
                float k = config.EvaluateScale((t - scaleStart) / scaleDuration);
                rectTransform.localScale = baseScale * Mathf.LerpUnclamped(startScale, endScale, k);
            }

            yield return null;
        }

        rectTransform.anchoredPosition = endPos;
        rectTransform.localScale = baseScale * endScale;
        canvasGroup.alpha = 0f;

        onFinished?.Invoke(this);
        Destroy(gameObject);
    }
}
