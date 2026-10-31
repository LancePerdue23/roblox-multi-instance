namespace RobloxLaunchHelper;
internal interface IHotkeyPlatform
{
    bool Register(int id, Keys keys);
    void Unregister(int id);
}
internal sealed class HotkeyRegistrations(IHotkeyPlatform platform) : IDisposable
{
    private readonly Dictionary<int, string> registered = [];
    internal string[] Set(Dictionary<string, int> shortcuts, bool enabled)
    {
        Dispose();
        if (!enabled) return [];
        if (shortcuts.Values.Any(k => !ShortcutPolicy.Valid(k)) || shortcuts.Values.Distinct().Count() != shortcuts.Count)
            return ["Invalid or duplicate shortcut bindings."];
        var conflicts = new List<string>(); int id = 1;
        foreach (var (command, keys) in shortcuts)
        {
            if (platform.Register(id, (Keys)keys)) registered[id] = command;
            else conflicts.Add(ShortcutPolicy.Label(command) + ": shortcut is unavailable; choose another combination.");
            id++;
        }
        return conflicts.ToArray();
    }
    internal string? Command(int id) => registered.GetValueOrDefault(id);
    public void Dispose() { foreach (int id in registered.Keys) platform.Unregister(id); registered.Clear(); }
}
