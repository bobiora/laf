using UnityEngine;

// Central haptic (vibration) feedback for the game. Two events matter:
//   * A line connects onto another point (a move commits)  -> a SHORT buzz.
//   * A shape (figure) is created                           -> a LONGER buzz.
//
// Durations are expressed in MILLISECONDS and exposed as serialized inspector
// fields (and public properties) so they can be tuned easily without touching code.
//
// The vibration is driven through the native Android Vibrator service, which — unlike
// Unity's Handheld.Vibrate() — supports an exact duration. On API 26+ it uses
// VibrationEffect.createOneShot; on older devices it falls back to the (deprecated)
// long-milliseconds vibrate call. On non-Android platforms this is a safe no-op.
//
// Wiring: GameManager auto-adds this component in Awake if none is present, so haptics
// work with zero manual setup. To EDIT the durations, add HapticFeedback to the
// GameManager GameObject in the Game scene — its millisecond fields then show in the
// inspector. Android also needs the VIBRATE permission (see the editor checklist).
[DisallowMultipleComponent]
public class HapticFeedback : MonoBehaviour
{
    public static HapticFeedback Instance { get; private set; }

    [Header("Vibration durations (milliseconds)")]
    [Tooltip("Short buzz when a line reaches another point and connects (a move commits).")]
    [SerializeField] private int lineConnectMilliseconds = 20;

    [Tooltip("Longer buzz when a shape (figure) is created and claimed.")]
    [SerializeField] private int shapeCreatedMilliseconds = 120;

    [Header("General")]
    [Tooltip("Master switch — turn all haptic feedback on or off.")]
    [SerializeField] private bool hapticsEnabled = true;

    [Tooltip("Log vibration init/calls to the console (visible in Android logcat). " +
             "Handy while debugging; turn off for shipping.")]
    [SerializeField] private bool logHaptics = false;

    // Public getters/setters (clamped to >= 0) so other systems or a settings UI can
    // read/adjust the durations at runtime.
    public int LineConnectMilliseconds
    {
        get => lineConnectMilliseconds;
        set => lineConnectMilliseconds = Mathf.Max(0, value);
    }

    public int ShapeCreatedMilliseconds
    {
        get => shapeCreatedMilliseconds;
        set => shapeCreatedMilliseconds = Mathf.Max(0, value);
    }

    public bool HapticsEnabled
    {
        get => hapticsEnabled;
        set => hapticsEnabled = value;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject vibrator;
    private AndroidJavaClass vibrationEffectClass;
    private int androidApiLevel;
    private bool hasVibrator;
#endif

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        InitPlatform();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Convenience entry points used by GameManager.
    public void VibrateLineConnected() => Vibrate(lineConnectMilliseconds);
    public void VibrateShapeCreated() => Vibrate(shapeCreatedMilliseconds);

    // Vibrate for an arbitrary duration in milliseconds. No-op when haptics are disabled
    // or the duration is not positive. A new vibrate call replaces any ongoing one, so a
    // shape buzz that follows a line buzz on the same move simply takes over.
    public void Vibrate(int milliseconds)
    {
        if (!hapticsEnabled || milliseconds <= 0) return;
        VibratePlatform(milliseconds);
    }

    // ---- Platform layer ----

    void InitPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                androidApiLevel = version.GetStatic<int>("SDK_INT");

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                // API 31+ (Android 12): getSystemService("vibrator") is deprecated and no-ops
                // on some OEM builds. Use VibratorManager.getDefaultVibrator() instead.
                if (androidApiLevel >= 31)
                {
                    using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                    {
                        if (manager != null)
                            vibrator = manager.Call<AndroidJavaObject>("getDefaultVibrator");
                    }
                }

                // Older devices (or a null VibratorManager) fall back to the legacy service.
                if (vibrator == null)
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            hasVibrator = vibrator != null && vibrator.Call<bool>("hasVibrator");

            if (androidApiLevel >= 26)
                vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");

            if (logHaptics)
                Debug.Log($"[Haptics] init: api={androidApiLevel}, path={(androidApiLevel >= 31 ? "VibratorManager" : "legacy")}, hasVibrator={hasVibrator}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Haptics] Failed to initialize Android vibrator: {e}");
            hasVibrator = false;
        }
#endif
    }

    void VibratePlatform(int milliseconds)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (logHaptics) Debug.Log($"[Haptics] Vibrate({milliseconds}ms) hasVibrator={hasVibrator}");
        if (!hasVibrator || vibrator == null) return;
        try
        {
            if (androidApiLevel >= 26 && vibrationEffectClass != null)
            {
                // VibrationEffect.DEFAULT_AMPLITUDE == -1 (let the system pick a strength).
                const int DEFAULT_AMPLITUDE = -1;
                using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                           "createOneShot", (long)milliseconds, DEFAULT_AMPLITUDE))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                // Deprecated on API 26+, but the only option on older devices.
                vibrator.Call("vibrate", (long)milliseconds);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Haptics] Vibrate failed: {e.Message}");
        }

        // Never executed (frameCount is never negative), but referencing Handheld.Vibrate
        // in compiled Android code makes Unity auto-add the android.permission.VIBRATE
        // permission to the built manifest — a safety net in case it isn't added manually.
        if (Time.frameCount < 0) Handheld.Vibrate();
#else
        // Editor and non-Android platforms: nothing to do. Log only in development builds.
        if (Debug.isDebugBuild)
            Debug.Log($"[Haptics] Vibrate {milliseconds} ms (no-op on this platform)");
#endif
    }
}
