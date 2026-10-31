using System.Text.Json;
using RobloxLaunchHelper;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;

namespace RobloxMultiInstance;

internal static class PreferenceMigration
{
    internal static string Copy(string source, string destination)
    {
        var warnings = new List<string>();
        foreach (string name in new[] { "appearance.json", "window-layouts.json" })
        {
            string input = Path.Combine(source, name), output = Path.Combine(destination, name);
            if (File.Exists(output) || !File.Exists(input)) continue;
            string? temporary = null;
            try
            {
                UpdateFiles.RejectLinks(input);
                byte[] bytes = File.ReadAllBytes(input);
                if (bytes.Length > 262144) throw new InvalidDataException();
                if (name == "appearance.json")
                {
                    using var value = JsonDocument.Parse(bytes);
                    if (value.RootElement.GetProperty("Version").GetInt32() != 1 ||
                        !Enum.IsDefined((ColorScheme)value.RootElement.GetProperty("Scheme").GetInt32()))
                        throw new InvalidDataException();
                }
                else LayoutStore.Validate(JsonSerializer.Deserialize<LayoutSettings>(bytes) ?? throw new InvalidDataException());
                UpdateFiles.EnsureDirectory(destination);
                temporary = Path.Combine(destination, Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, output, overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                warnings.Add($"Could not import Preview {name}. The original is preserved; reset this setting explicitly to use defaults.");
            }
            finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        }
        return string.Join(" ", warnings);
    }
}
