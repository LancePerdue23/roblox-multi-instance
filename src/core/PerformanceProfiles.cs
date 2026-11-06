using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

internal sealed record PerformanceProfile(string Id, string Name, string Description, string CpuPriority, int? FpsCap, int? GraphicsQuality, string EcoQos = "unchanged",
    string TextureQuality = "unchanged", bool MinimumMsaa = false, bool DisableGrass = false, bool ReducedGeometry = false, bool GraySky = false, string WindowBehavior = "unchanged");
internal sealed record PerformanceProfiles(int Version, string SelectedProfileId, PerformanceProfile[] Profiles, bool Enabled = false, bool FocusBoost = true);

// Performance-only data. Unknown fields (including credentials, paths, and launch arguments) are refused.
internal sealed class PerformanceProfileConflictException() : InvalidOperationException("Performance profiles changed in another manager. Reload profiles before saving.");
internal sealed class PerformanceProfileStore(string path)
{
    internal const int MaxBytes = 32 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    static readonly Regex Id = new("^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant);
    internal static PerformanceProfiles Defaults => new(2, "full-quality", [
        new("full-quality", "Roblox defaults", "Restore settings owned by this app. Preserve your original Roblox preferences.", "unchanged", null, null),
        new("balanced", "Balanced", "Lower background scheduling priority; request lighter graphics and 30 FPS on launch.", "belowNormal", 30, 1, "off", "reduced", true),
        new("many-accounts", "Maximum instances", "Background efficiency mode and small minimized windows; request minimum visuals and 10 FPS.", "belowNormal", 10, 1, "on", "lowest", true, true, true, true, "smallMinimized")]);
    Mutex OpenMutex() => new(false, @"Local\RobloxMultiInstance-PerformanceProfiles-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))));
    internal PerformanceProfiles Load(out bool available) => Load(out available,out _);
    internal PerformanceProfiles Load(out bool available,out string revision)
    {
        available = false; revision = "";
        try
        {
            using var mutex = OpenMutex();
            bool held = false;
            try
            {
                try { held = mutex.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { held = true; }
                if (!held) return Defaults;
                var state = Read(); available = true; revision = Revision(state); return state;
            }
            finally { if (held) mutex.ReleaseMutex(); }
        }
        catch { return Defaults; }
    }
    PerformanceProfiles Read()
    {
        if (!File.Exists(path)) return Defaults;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Performance profile file is too large.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return Parse(reader.ReadToEnd());
    }
    internal static PerformanceProfiles Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new ArgumentException("Performance profile import exceeds 32 KB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            var state = JsonSerializer.Deserialize<PerformanceProfiles>(json, Json) ?? throw new ArgumentException("Missing performance profiles.");
            Validate(state); return state.Version == 1 ? state with { Enabled = false } : state;
        }
        catch (JsonException) { throw new ArgumentException("Invalid performance-only profile format. Unknown fields are not accepted."); }
    }
    static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate profile fields are not accepted.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    internal static void Validate(PerformanceProfiles state)
    {
        if (state.Version is not (1 or 2) || state.Profiles is null || state.Profiles.Length is < 1 or > 20 || state.SelectedProfileId is null || state.Version == 1 && state.Enabled)
            throw new ArgumentException("Use version 1 or 2 with one to twenty performance profiles. Legacy profiles cannot activate automatically.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in state.Profiles)
        {
            if (profile is null || profile.Id is null || !Id.IsMatch(profile.Id) || !ids.Add(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 60 || profile.Name.Any(char.IsControl) ||
                profile.Description is null || profile.Description.Length > 240 || profile.Description.Any(char.IsControl) ||
                profile.EcoQos is not ("unchanged" or "on" or "off") ||
                profile.CpuPriority is not ("unchanged" or "normal" or "belowNormal" or "idle") ||
                profile.FpsCap is < 10 or > 1000 || state.Version == 2 && profile.FpsCap is > 240 || profile.GraphicsQuality is < 1 or > 10 ||
                profile.TextureQuality is not ("unchanged" or "reduced" or "lowest") || profile.WindowBehavior is not ("unchanged" or "smallMinimized"))
                throw new ArgumentException("Invalid performance profile values or duplicate profile ID.");
        }
        if (!ids.Contains(state.SelectedProfileId)) throw new ArgumentException("Select an existing performance profile.");
    }
    internal static string Export(PerformanceProfiles state) { Validate(state); return JsonSerializer.Serialize(state, Json); }
    internal static string Revision(PerformanceProfiles state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Export(state))));
    internal void Save(PerformanceProfiles state, CancellationToken token,string expectedRevision)
    {
        string json = Export(state);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new ArgumentException("Performance profiles exceed 32 KB.");
        using var mutex = OpenMutex();
        bool held = false;
        string? temporary = null;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("Performance profiles are busy in another manager.");
            token.ThrowIfCancellationRequested();
            // Never overwrite a corrupt or future-version original, even after a successful import preview.
            if (!string.Equals(Revision(Read()),expectedRevision,StringComparison.Ordinal)) throw new PerformanceProfileConflictException();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true); temporary = null;
        }
        finally
        {
            if (temporary is not null) try { File.Delete(temporary); } catch { }
            if (held) mutex.ReleaseMutex();
        }
    }
}
