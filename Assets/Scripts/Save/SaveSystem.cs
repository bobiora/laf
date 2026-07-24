// Static facade exposing the active ISaveSystem implementation. Assign a different
// implementation to Current at startup (e.g. a CloudSaveSystem) to swap the backing
// store without touching any call site.
public static class SaveSystem
{
    public static ISaveSystem Current { get; set; } = new PlayerPrefsSaveSystem();
}
