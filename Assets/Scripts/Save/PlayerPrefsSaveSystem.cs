using UnityEngine;

// Default ISaveSystem implementation backed by UnityEngine.PlayerPrefs. Behavior is a
// 1:1 delegation, so keys, defaults, and timing match the previous direct usage.
public class PlayerPrefsSaveSystem : ISaveSystem
{
    public void SaveInt(string key, int value) => PlayerPrefs.SetInt(key, value);

    public int LoadInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);

    public void SaveString(string key, string value) => PlayerPrefs.SetString(key, value);

    public string LoadString(string key, string defaultValue) => PlayerPrefs.GetString(key, defaultValue);

    public bool HasKey(string key) => PlayerPrefs.HasKey(key);

    public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);

    public void Flush() => PlayerPrefs.Save();
}
