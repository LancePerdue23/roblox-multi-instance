namespace RobloxLaunchHelper;
internal sealed class LayoutPreferences
{
    internal readonly LayoutStore Store;
    internal LayoutSettings Settings = LayoutSettings.Default;
    internal bool Available = true;
    internal event Action? Changed;
    internal LayoutPreferences(LayoutStore? store = null)
    {
        Store = store ?? new LayoutStore();
        try { Settings = Store.Load(); } catch { Available = false; }
    }
    internal void Save(LayoutSettings settings)
    {
        if (!Available) throw new InvalidDataException();
        Store.Save(settings); Settings = settings; Changed?.Invoke();
    }
    internal void Reset() { Store.Save(LayoutSettings.Default); Settings = LayoutSettings.Default; Available = true; Changed?.Invoke(); }
}
