using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxLaunchHelper;

internal sealed record NotificationSettings(int Version = 1, bool Progress = false, bool Completion = false);

internal sealed class NotificationPreferences
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "launch-notifications.json");
    private readonly string path;
    internal NotificationSettings Settings { get; private set; } = new();
    internal bool Available { get; private set; } = true;
    internal NotificationPreferences(string? path = null) { this.path = path ?? DefaultPath; Reload(); }
    private void CheckPath()
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException();
    }
    internal void Reload()
    {
        try
        {
            CheckPath();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4096) throw new IOException();
            // Parse a bounded snapshot even if another writer grows the file after the length check.
            byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            var settings = JsonSerializer.Deserialize<NotificationSettings>(bytes, NotificationWire.Json);
            if (settings is null || settings.Version != 1) throw new IOException();
            Settings = settings; Available = true;
        }
        catch (FileNotFoundException) { Settings = new(); Available = true; }
        catch (DirectoryNotFoundException) { Settings = new(); Available = true; }
        catch { Settings = new(); Available = false; }
    }
    internal void Save(NotificationSettings settings, bool reset = false)
    {
        Reload();
        if ((!Available && !reset) || settings.Version != 1) throw new IOException();
        CheckPath(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Settings = settings; Available = true;
    }
}

// Only these typed fields cross the presentation endpoint. No raw launch input or error text.
internal sealed record LaunchStatus(Guid AttemptId, int Revision, int OwnerPid, long OwnerCreated,
    LaunchOutcome Outcome, DiagnosticCode Code, bool? RobloxStarted)
{
    internal bool Terminal => Outcome != LaunchOutcome.InProgress;
    internal bool Warning => Outcome is LaunchOutcome.Warning or LaunchOutcome.Failed or LaunchOutcome.Incomplete;
    internal static LaunchStatus From(LaunchResult result, int revision) => new(result.AttemptId, revision,
        result.OwnerPid, new DateTime(result.OwnerCreated, DateTimeKind.Utc).ToFileTimeUtc(), result.Outcome, result.Code, result.RobloxStarted);
    internal bool Valid => AttemptId != Guid.Empty && Revision > 0 && OwnerPid > 0 && OwnerCreated > 0 &&
        Enum.IsDefined(Outcome) && Enum.IsDefined(Code) && (Outcome switch
        {
            LaunchOutcome.InProgress => Code == DiagnosticCode.LaunchStarting,
            LaunchOutcome.Confirmed => Code == DiagnosticCode.LaunchConfirmed && RobloxStarted == true,
            LaunchOutcome.Warning => RobloxStarted == true && Code is DiagnosticCode.WorkerUnavailable or DiagnosticCode.WorkerTimeout or DiagnosticCode.CompletionUnconfirmed,
            LaunchOutcome.Failed => Code is DiagnosticCode.LaunchFailed or DiagnosticCode.InvalidConfig or DiagnosticCode.WrongAccount or DiagnosticCode.PlayerMissing or DiagnosticCode.SignatureRejected or DiagnosticCode.ProcessStartFailed,
            LaunchOutcome.Incomplete => Code == DiagnosticCode.Interrupted && RobloxStarted is null,
            _ => false
        });
}

internal static class LaunchStatusText
{
    internal const string Limit = "Helper status does not verify account sign-in or joining a game.";
    internal static string Text(LaunchOutcome outcome, DiagnosticCode code, bool? started) => outcome switch
    {
        LaunchOutcome.InProgress => started == true ? "Roblox player started; waiting for helper confirmation…" : "Starting Roblox…",
        LaunchOutcome.Confirmed => "Helper complete — singleton release confirmed.",
        LaunchOutcome.Warning => "Roblox player started; helper completion unconfirmed. " + DiagnosticMessages.Text(code),
        LaunchOutcome.Incomplete => "Launcher interrupted; completion unknown.",
        _ => "Launch failed. " + DiagnosticMessages.Text(code)
    };
}

// UI-independent reducer: all mutation occurs on the host UI thread, with a monotonic clock.
internal sealed class LaunchNotificationState(Func<long> clock)
{
    private sealed record Entry(LaunchStatus Status, long Seen);
    private readonly Dictionary<Guid, Entry> entries = [];
    private long? progressSince, pendingSince;
    private long lastPopup = long.MinValue / 2;
    private int pendingConfirmed, pendingWarnings, pendingFailed, pendingUnknown;
    private readonly HashSet<DiagnosticCode> reasons = [];
    internal int Overflow { get; private set; }
    internal bool Dismissed { get; private set; }
    internal int Active => entries.Values.Count(e => !e.Status.Terminal);
    internal bool HasPending => pendingSince is not null;
    internal bool ProgressReady => !Dismissed && progressSince is long start && clock() - start >= 750 && Active > 0;
    internal IEnumerable<LaunchStatus> Statuses => entries.Values.Select(e => e.Status);
    internal void Dismiss() => Dismissed = true;
    internal bool Accept(LaunchStatus status, NotificationSettings settings)
    {
        if (!status.Valid) return false;
        if (entries.TryGetValue(status.AttemptId, out var old))
        {
            if (old.Status.OwnerPid != status.OwnerPid || old.Status.OwnerCreated != status.OwnerCreated) return false;
            if (old.Status.Terminal || old.Status.Revision >= status.Revision) return true;
            if (old.Status.RobloxStarted == true && status.RobloxStarted != true && status.Outcome != LaunchOutcome.Incomplete) return false;
        }
        else if (entries.Count >= 100) { Overflow = Math.Min(1000000, Overflow + 1); return false; }
        int before = Active;
        entries[status.AttemptId] = new(status, clock());
        if (before == 0 && Active > 0) { progressSince = clock(); Dismissed = false; }
        if (Active == 0) progressSince = null;
        if (status.Terminal && (status.Warning || settings.Completion))
        {
            pendingSince ??= clock();
            if (status.Outcome == LaunchOutcome.Confirmed) pendingConfirmed++;
            else if (status.Outcome == LaunchOutcome.Warning) pendingWarnings++;
            else if (status.Outcome == LaunchOutcome.Failed) pendingFailed++;
            else pendingUnknown++;
            if (status.Warning) reasons.Add(status.Code);
        }
        return true;
    }
    internal void Expire(Func<LaunchStatus, bool> alive, NotificationSettings settings)
    {
        // Delivery retries last two seconds. Keep tombstones well beyond that window,
        // but release capacity during a sustained stream of independent launches.
        foreach (var key in entries.Where(e => e.Value.Status.Terminal && clock() - e.Value.Seen >= 15000).Select(e => e.Key).ToArray())
            entries.Remove(key);
        foreach (var entry in entries.Values.Where(e => !e.Status.Terminal).ToArray())
            if (clock() - entry.Seen >= 120000 || (clock() - entry.Seen >= 2000 && !alive(entry.Status)))
                Accept(entry.Status with { Revision = entry.Status.Revision + 1, Outcome = LaunchOutcome.Incomplete,
                    Code = DiagnosticCode.Interrupted, RobloxStarted = null }, settings);
    }
    internal (string Text, bool Warning)? TakePopup(NotificationSettings settings)
    {
        if (!settings.Completion) pendingConfirmed = 0;
        if (pendingConfirmed + pendingWarnings + pendingFailed + pendingUnknown == 0) { pendingSince = null; return null; }
        if (pendingSince is not long since || clock() - since < 2000 || clock() - lastPopup < 10000) return null;
        bool warning = pendingWarnings + pendingFailed + pendingUnknown > 0;
        var parts = new List<string>();
        if (pendingConfirmed > 0) parts.Add($"{pendingConfirmed} helper completions confirmed");
        if (pendingWarnings > 0) parts.Add($"{pendingWarnings} completions unconfirmed");
        if (pendingFailed > 0) parts.Add($"{pendingFailed} launches failed");
        if (pendingUnknown > 0) parts.Add($"{pendingUnknown} interrupted; completion unknown");
        if (Active > 0) parts.Add($"{Active} still in progress");
        string text = string.Join("; ", parts) + ".";
        if (warning) text += "\n" + string.Join("\n", reasons.Select(DiagnosticMessages.Text));
        pendingConfirmed = pendingWarnings = pendingFailed = pendingUnknown = 0; reasons.Clear();
        pendingSince = null; lastPopup = clock();
        return (text, warning);
    }
}
