using Microsoft.Win32;
using System.Text.Json;

namespace RobloxLaunchHelper;

internal enum ColorScheme { Dark, Light, WarmNeutral, FollowWindows, Purple, Ocean, Forest, Rose }
internal sealed class AppearancePreferences
{
    private readonly string path;
    internal ColorScheme Scheme { get; private set; }
    internal bool Available { get; private set; } = true;
    internal AppearancePreferences(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "appearance.json");
        try
        {
            if (!File.Exists(this.path)) return;
            var value = JsonSerializer.Deserialize<AppearanceSetting>(File.ReadAllText(this.path));
            if (value is null || value.Version != 1 || !Enum.IsDefined(value.Scheme)) throw new InvalidDataException();
            Scheme = value.Scheme;
        }
        catch { Available = false; }
    }
    internal void Save(ColorScheme scheme, bool reset = false)
    {
        if ((!Available && !reset) || !Enum.IsDefined(scheme)) throw new InvalidDataException();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new AppearanceSetting(1, scheme))); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Scheme = scheme; Available = true;
    }
    private sealed record AppearanceSetting(int Version, ColorScheme Scheme);
}
