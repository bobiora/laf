using System;
using System.Collections;
using UnityEngine;
using TMPro;

// A single "+N" score number. One of these is spawned (from a prefab) every time a
// scorable shape is claimed. It appears at its rest position, drifts up, and fades out
// while scaling — then destroys itself. Timing, distances, scales and easing all come from
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

    private RectTransform rectTransform;
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
        this.onFinished = onFinished;

        if (pointsText != null)
        {
            pointsText.text = $"+{points}";
            pointsText.color = color;
        }
        FitHeightToText();

        StartCoroutine(Animate(config));
    }

    // Update the displayed value in place, keeping the animation running. Used when a
    // single move closes two figures at once: the second award is folded into this number
    // (e.g. +2 becomes +4) instead of spawning a separate popup.
    public void SetPoints(int points)
    {
        if (pointsText != null) pointsText.text = $"+{points}";
        FitHeightToText();
    }

    // Make the container exactly as tall as the rendered number, so the config's pivot
    // (bottom / middle / top) refers to the visible digits rather than an arbitrary prefab
    // height. The pivot stays put while the rect resizes around it.
    private void FitHeightToText()
    {
        if (pointsText == null || rectTransform == null) return;
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, pointsText.preferredHeight);
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
