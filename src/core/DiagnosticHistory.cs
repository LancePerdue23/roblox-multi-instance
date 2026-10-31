using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobloxLaunchHelper;

internal sealed class DiagnosticHistory
{
    internal const int MaxRecords = 100, MaxBytes = 512 * 1024, MaxRecordBytes = 4096;
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "diagnostics");
    private readonly string directory;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<LaunchResult, bool> alive;
    private readonly string mutexName;
    private readonly Action<Exception>? onFailure;
    internal DiagnosticHistory(string? directory = null, Func<DateTimeOffset>? now = null, Func<LaunchResult, bool>? alive = null, Action<Exception>? onFailure = null)
    {
        this.directory = Path.GetFullPath(directory ?? DefaultDirectory);
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.alive = alive ?? IsAlive;
        this.onFailure = onFailure;
        // Global namespace serializes concurrent interactive sessions for this user's store.
        mutexName = @"Global\RobloxLaunchHelper-History-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.directory.ToUpperInvariant())));
    }
    private static bool IsAlive(LaunchResult record)
    {
        try { using var p = Process.GetProcessById(record.OwnerPid); return p.StartTime.ToUniversalTime().Ticks == record.OwnerCreated; }
        catch { return false; }
    }
    private void RejectLinks()
    {
        for (string? path = directory; path is not null; path = Path.GetDirectoryName(path))
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected diagnostic directory.");
        foreach (string name in new[] { "history.json", "history.tmp" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected diagnostic file.");
        }
    }
    internal static bool Valid(LaunchResult r) => r.SchemaVersion == 1 && r.AttemptId != Guid.Empty &&
        Enum.IsDefined(r.Source) && Enum.IsDefined(r.Outcome) && Enum.IsDefined(r.Code) &&
        r.StartedUtc != default && (r.EndedUtc is null || r.EndedUtc >= r.StartedUtc) &&
        (r.DurationMs is null || r.DurationMs >= 0) && JsonSerializer.SerializeToUtf8Bytes(r).Length <= MaxRecordBytes;
    private List<LaunchResult> Load()
    {
        RejectLinks();
        string path = Path.Combine(directory, "history.json");
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new IOException("Diagnostic history is oversized.");
        var records = JsonSerializer.Deserialize<List<LaunchResult>>(stream) ?? throw new IOException("Invalid history.");
        if (records.Count > MaxRecords || records.Any(r => r is null || !Valid(r)) || records.Select(r => r.AttemptId).Distinct().Count() != records.Count)
            throw new IOException("Unsupported or invalid history.");
        return records;
    }
    private List<LaunchResult> Trim(IEnumerable<LaunchResult> records) => records
        .Where(r => r.StartedUtc >= now().AddDays(-30) && r.StartedUtc <= now().AddMinutes(5))
        .OrderByDescending(r => r.StartedUtc).ThenBy(r => r.AttemptId).Take(MaxRecords).ToList();
    private void Save(List<LaunchResult> records)
    {
        RejectLinks();
        Directory.CreateDirectory(directory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(records);
        while (bytes.Length > MaxBytes && records.Count > 0) { records.RemoveAt(records.Count - 1); bytes = JsonSerializer.SerializeToUtf8Bytes(records); }
        string temp = Path.Combine(directory, "history.tmp");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, Path.Combine(directory, "history.json"), true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private bool WithLock(Action action)
    {
        try
        {
            using var mutex = new Mutex(false, mutexName);
            bool held;
            try { held = mutex.WaitOne(100); } catch (AbandonedMutexException) { held = true; }
            if (!held) return false;
            try { action(); return true; } finally { mutex.ReleaseMutex(); }
        }
        catch (Exception e)
        {
            try { onFailure?.Invoke(e); } catch { }
            return false;
        } // Diagnostic storage must never change launch behavior.
    }
    internal bool Read(out LaunchResult[] records)
    {
        LaunchResult[] snapshot = [];
        bool ok = WithLock(() =>
        {
            var loaded = Load();
            var trimmed = Trim(loaded).Select(r => r.Outcome == LaunchOutcome.InProgress && !alive(r)
                ? r with { Outcome = LaunchOutcome.Incomplete, Code = DiagnosticCode.Interrupted, RobloxStarted = null, OwnerPid = 0, OwnerCreated = 0 } : r).ToList();
            if (!loaded.SequenceEqual(trimmed)) Save(trimmed);
            snapshot = trimmed.ToArray();
        });
        records = snapshot; return ok;
    }
    internal bool Begin(LaunchResult result) => Valid(result) && WithLock(() =>
    {
        var records = Trim(Load());
        records.RemoveAll(r => r.AttemptId == result.AttemptId);
        records.Add(result); Save(Trim(records));
    });
    internal bool Finish(LaunchResult result) => Valid(result) && WithLock(() =>
    {
        var records = Trim(Load());
        int index = records.FindIndex(r => r.AttemptId == result.AttemptId);
        if (index >= 0 && records[index].Outcome == LaunchOutcome.InProgress) records[index] = result with { OwnerPid = 0, OwnerCreated = 0 };
        Save(records); // A cleared or evicted attempt is never recreated.
    });
    internal bool Clear() => WithLock(() => Save([]));
}

internal sealed class LaunchAttempt : IDisposable
{
    internal bool PlayerStarted=>result.RobloxStarted==true;
    internal OperationResult ToOperationResult()=>new(result.Outcome switch {LaunchOutcome.Confirmed=>OperationOutcome.Succeeded,LaunchOutcome.Warning=>OperationOutcome.Warning,LaunchOutcome.Failed=>OperationOutcome.Failed,_=>OperationOutcome.Unknown},result.Code.ToString(),DiagnosticMessages.Text(result.Code));
    private readonly DiagnosticHistory history;
    private readonly LaunchNotificationPublisher publisher;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private LaunchResult result;
    private LaunchOutcome fallbackOutcome = LaunchOutcome.Failed;
    private DiagnosticCode fallbackCode = DiagnosticCode.LaunchFailed;
    private Task<bool>? finished;
    internal LaunchAttempt(string? uri, DiagnosticHistory? history = null, LaunchNotificationPublisher? publisher = null)
    {
        this.history = history ?? new(); this.publisher = publisher ?? new();
        using var p = Process.GetCurrentProcess();
        result = new(1, Guid.NewGuid(), DateTimeOffset.UtcNow, null, null,
            uri is null ? LaunchSource.Desktop : uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) ? LaunchSource.RobloxPlayer : LaunchSource.Roblox,
            null, LaunchOutcome.InProgress, DiagnosticCode.LaunchStarting, null, p.Id, p.StartTime.ToUniversalTime().Ticks);
        this.history.Begin(result);
        this.publisher.Publish(LaunchStatus.From(result, 1));
    }
    internal void Started()
    {
        if (finished is not null) return;
        result = result with { RobloxStarted = true };
        publisher.Publish(LaunchStatus.From(result, 2));
    }
    internal void Error(int code) => result = result with { ErrorCode = code };
    internal void SetFallback(LaunchOutcome outcome, DiagnosticCode code) { fallbackOutcome = outcome; fallbackCode = code; }
    internal void Complete(LaunchOutcome outcome, DiagnosticCode code, int? error = null)
    {
        if (finished is null) result = result with { Outcome = outcome, Code = code, ErrorCode = error ?? result.ErrorCode };
    }
    internal Task<bool> FinishAsync() => finished ??= FinishCoreAsync();
    private Task<bool> FinishCoreAsync()
    {
        if (result.Outcome == LaunchOutcome.InProgress) result = result with { Outcome = fallbackOutcome, Code = fallbackCode };
        result = result with { EndedUtc = DateTimeOffset.UtcNow < result.StartedUtc ? result.StartedUtc : DateTimeOffset.UtcNow,
            DurationMs = elapsed.ElapsedMilliseconds, RobloxStarted = result.RobloxStarted ?? false };
        history.Finish(result);
        return publisher.FinishAsync(LaunchStatus.From(result, 3));
    }
    public void Dispose()
    {
        // All normal and exception paths await final delivery before disposal.
        _ = FinishAsync();
    }
}
