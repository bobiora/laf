using System;
using System.Collections;
using UnityEngine;
using TMPro;

// A single "+N" score number. One of these is spawned (from a prefab) every time a
// scorable shape is claimed. It appears at its rest position, drifts up a few pixels, and
// fades out — then destroys itself. Instances are independent, so several can float on
// screen at once during a combo; nothing is queued or reused.
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

    // Start the appear -> rise -> fade animation. Called by ScorePopupUI right after the
    // instance is spawned. onFinished lets the spawner drop its reference when we're done.
    public void Play(int points, Color color, float riseDistancePixels,
                     float riseDuration, float fadeDuration, Action<ScorePopup> onFinished)
    {
        this.onFinished = onFinished;

        if (pointsText != null)
        {
            pointsText.text = $"+{points}";
            pointsText.color = color;
        }

        StartCoroutine(Animate(riseDistancePixels, riseDuration, fadeDuration));
    }

    private IEnumerator Animate(float riseDistancePixels, float riseDuration, float fadeDuration)
    {
        // Appear immediately, fully visible, at the rest position set by the prefab/spawn.
        canvasGroup.alpha = 1f;
        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0f, riseDistancePixels);

        float total = Mathf.Max(riseDuration, 0.0001f);
        // Fade overlaps the tail of the rise (its second half by default), so the number is
        // still moving as it dissolves rather than sitting still.
        float fadeStart = Mathf.Max(0f, total - Mathf.Max(0f, fadeDuration));

        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / total);

            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, p);

            if (t >= fadeStart && fadeDuration > 0f)
                canvasGroup.alpha = Mathf.Clamp01(1f - (t - fadeStart) / fadeDuration);

            yield return null;
        }

        rectTransform.anchoredPosition = endPos;
        canvasGroup.alpha = 0f;

        onFinished?.Invoke(this);
        Destroy(gameObject);
    }
}
