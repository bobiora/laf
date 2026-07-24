// Abstraction over persistent key/value storage. Decouples game code from
// UnityEngine.PlayerPrefs so the backing store can be swapped (e.g. a cloud save)
// without touching call sites.
public interface ISaveSystem
{
    void SaveInt(string key, int value);
    int LoadInt(string key, int defaultValue);
    void SaveString(string key, string value);
    string LoadString(string key, string defaultValue);
    bool HasKey(string key);
    void DeleteKey(string key);
    void Flush(); // for systems that batch writes
}
