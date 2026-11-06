using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxLaunchHelper;

public sealed record TelemetryStatus(bool Enabled, bool Available, bool Configured, int QueuedErrors, string? LastResult);

/// <summary>Optional technical error counters. This service never accepts arbitrary text or account data.</summary>
public sealed class TelemetryService : IDisposable
{
    public static TelemetryService Default { get; } = new();
    public const string Disclosure = "Optional: share app versions and technical error codes to help improve Roblox Multi Instance. Off by default. No usernames, credentials, cookies, game details, personal files, or persistent device identifiers are sent. Change this anytime in Settings or rmi telemetry off. Reports send automatically while the desktop app is open; helper-only users can send manually with rmi telemetry send. The hosting provider processes connection information such as your IP address. Previously received aggregate counts cannot be individually removed.";
    public const int MaxQueueBytes = 256 * 1024;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    static readonly HashSet<string> Codes = Enum.GetNames<DiagnosticCode>().Where(x => x is not ("Ready" or "LaunchStarting" or "LaunchConfirmed" or "WorkerReady" or "WorkerNotTested")).Concat(new[] { "OPERATION_FAILED", "HELPER_VERSION_MISMATCH", "ACCOUNT_SIGNIN_FAILED", "ACCOUNT_SESSION_EXPIRED", "ACCOUNT_LAUNCH_FAILED", "BATCH_LAUNCH_FAILED", "LAYOUT_FAILED" }).ToHashSet(StringComparer.Ordinal);
    readonly string directory, mutexName;
    readonly HttpClient http;
    readonly Uri? endpoint;
    readonly Func<DateTimeOffset> now;
    readonly object cancellationGate = new();
    readonly SemaphoreSlim sending = new(1, 1);
    CancellationTokenSource? active;
    string? lastResult;
    internal Action<string,string> AtomicCommit { private get; init; } = (source,target)=>File.Move(source,target,true);
    public TelemetryService(string? directory = null, HttpMessageHandler? handler = null, Uri? endpoint = null, Func<DateTimeOffset>? now = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "telemetry"));
        mutexName = @"Global\RobloxLaunchHelper-Telemetry-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.directory.ToUpperInvariant())));
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        string? configured = typeof(TelemetryService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "TelemetryEndpoint")?.Value;
        this.endpoint = endpoint ?? (Uri.TryCreate(configured, UriKind.Absolute, out var parsed) ? parsed : null);
        if (this.endpoint is not null && (this.endpoint.Scheme != "https" || !string.IsNullOrEmpty(this.endpoint.UserInfo) || !string.IsNullOrEmpty(this.endpoint.Query) || !string.IsNullOrEmpty(this.endpoint.Fragment)))
        {
            if (endpoint is not null) throw new ArgumentException("Telemetry requires a fixed HTTPS endpoint.");
            this.endpoint = null; // Invalid build configuration never blocks launching the app or a game.
        }
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(5) };
    }
    sealed record Consent(int Version = 1, bool Enabled = false, string Epoch = "");
    sealed record Counter(string Day, string AppVersion, string HelperVersion, string WindowsVersion, string Architecture, string Source, string Code, int? ErrorCode, int Count);
    sealed record Batch(int SchemaVersion, string BatchId, Counter[] Errors);
    sealed record QueueState(int Version, string Epoch, Counter[] Counters, Batch? Pending, string BudgetDay = "", int BudgetCount = 0);
    string PathFor(string name) => Path.Combine(directory, name);
    void RejectLinks()
    {
        for (string? p = directory; p is not null; p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException();
        if (Directory.Exists(directory)) foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException();
    }
    T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, mutexName);
        bool held;
        try { held = mutex.WaitOne(250); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Telemetry storage busy.");
        try { RejectLinks(); return action(); } finally { mutex.ReleaseMutex(); }
    }
    T Read<T>(string name, int limit)
    {
        using var stream = new FileStream(PathFor(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > limit) throw new IOException();
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new IOException();
    }
    Consent ReadConsent()
    {
        if (!File.Exists(PathFor("consent.json"))) return new();
        var consent = Read<Consent>("consent.json", 4096);
        if (consent.Version != 1 || (consent.Enabled && !Guid.TryParseExact(consent.Epoch, "N", out _))) throw new IOException();
        return consent;
    }
    void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(directory); RejectLinks();
        string temp = PathFor(Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, Json)); Commit(temp, PathFor(name)); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void Commit(string temporary,string target)
    {
        // Retry only replacement of the same staged bytes. Never replay a counter
        // increment, consent transition or HTTP upload after an uncertain outcome.
        for(int attempt=0;;attempt++)
        {
            try { AtomicCommit(temporary,target);return; }
            catch(UnauthorizedAccessException) when(attempt<3) { }
            catch(IOException error) when(attempt<3&&(error.HResult&0xffff) is 32 or 33) { }
            Thread.Sleep(10<<attempt);
            RejectLinks();
        }
    }
    bool Valid(Counter c) => DateOnly.TryParseExact(c.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= DateOnly.FromDateTime(now().UtcDateTime) && date >= DateOnly.FromDateTime(now().UtcDateTime.AddDays(-6)) && Version.TryParse(c.AppVersion, out _) && c.AppVersion.Length <= 24 && Version.TryParse(c.HelperVersion, out _) && c.HelperVersion.Length <= 24 && c.WindowsVersion is "Windows10" or "Windows11" or "Other" && c.Architecture is "X64" or "X86" or "Arm64" or "Arm" && c.Source is "Desktop" or "Roblox" or "RobloxPlayer" && Codes.Contains(c.Code) && c.Count is > 0 and <= 1000;
    QueueState ReadQueue(Consent consent)
    {
        var empty = new QueueState(1, consent.Epoch, [], null);
        if (!File.Exists(PathFor("queue.json"))) return empty;
        var queue = Read<QueueState>("queue.json", MaxQueueBytes);
        if (queue.Version != 1 || queue.Counters is null || queue.Counters.Length > 256 || (queue.Pending is not null && (queue.Pending.SchemaVersion != 1 || !Guid.TryParseExact(queue.Pending.BatchId, "N", out _) || queue.Pending.Errors is null || queue.Pending.Errors.Length > 20))) throw new IOException();
        if (queue.Epoch != consent.Epoch) return empty;
        // Expired counters and unsent batches disappear locally rather than being replayed.
        return queue with { Counters = queue.Counters.Where(Valid).ToArray(), Pending = queue.Pending is null || queue.Pending.Errors.Any(c => !Valid(c)) ? null : queue.Pending };
    }
    void WriteQueue(QueueState queue)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(queue, Json).Length > MaxQueueBytes) throw new IOException();
        Write("queue.json", queue);
    }
    static Batch? PrepareBatch(Counter[] counters)
    {
        var selected = new List<Counter>(); int count = 0;
        foreach (var counter in counters)
        {
            if (selected.Count == 20 || count + counter.Count > 1000) break;
            selected.Add(counter); count += counter.Count;
        }
        return selected.Count == 0 ? null : new Batch(1, Guid.NewGuid().ToString("N"), selected.ToArray());
    }
    TelemetryStatus Snapshot()
    {
        var consent = ReadConsent();
        var queue = consent.Enabled ? ReadQueue(consent) : null;
        int count = queue is null ? 0 : queue.Counters.Concat(queue.Pending?.Errors ?? []).Sum(c => c.Count);
        return new(consent.Enabled, true, endpoint is not null, count, lastResult);
    }
    public TelemetryStatus Status()
    {
        try { return Locked(Snapshot); } catch { return new(false, false, endpoint is not null, 0, "Storage unavailable; reporting is off."); }
    }
    public TelemetryStatus SetEnabled(bool enabled)
    {
        lock (cancellationGate) active?.Cancel();
        try
        {
            return Locked(() =>
            {
                // Explicitly choosing a setting can repair malformed storage. Each choice invalidates old batches.
                var consent = new Consent(1, false, Guid.NewGuid().ToString("N"));
                // Disable durably first; enabling is the final commit after queue initialization succeeds.
                Write("consent.json", consent); WriteQueue(new(1, consent.Epoch, [], null));
                if (enabled) Write("consent.json", consent with { Enabled = true });
                lastResult = null;
                return Snapshot();
            });
        }
        catch { return Status() with { Available = false, LastResult = "Could not save reporting preference. Check the reported current state and retry." }; }
    }
    public TelemetryStatus Clear()
    {
        lock (cancellationGate) active?.Cancel();
        try { return Locked(() => { var consent = ReadConsent(); WriteQueue(new(1, consent.Epoch, [], null)); return Snapshot(); }); }
        catch { return Status() with { Available = false, LastResult = "Could not clear queued reports. Check storage access and retry." }; }
    }
    internal void Record(DiagnosticCode code, LaunchSource source, int? nativeError = null) => RecordCore(code.ToString(), source.ToString(), nativeError);
    public void RecordOperation(string code, int? nativeError = null) => RecordCore(code, "Desktop", nativeError);
    void RecordCore(string code, string source, int? nativeError)
    {
        if (!Codes.Contains(code) || source is not ("Desktop" or "Roblox" or "RobloxPlayer")) return;
        try
        {
            Locked(() =>
            {
                var consent = ReadConsent(); if (!consent.Enabled) return false;
                var queue = ReadQueue(consent); string day = now().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                int budget = queue.BudgetDay == day ? queue.BudgetCount : 0;
                if (budget >= 1000) return false;
                string version = typeof(TelemetryService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                var counter = new Counter(day, version, version, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Windows11" : OperatingSystem.IsWindowsVersionAtLeast(10) ? "Windows10" : "Other", RuntimeInformation.ProcessArchitecture.ToString(), source, code, nativeError, 1);
                var values = queue.Counters.ToList(); int index = values.FindIndex(c => c with { Count = 1 } == counter);
                if (index >= 0) values[index] = values[index] with { Count = values[index].Count + 1 };
                else if (values.Count < 256) values.Add(counter);
                WriteQueue(queue with { Counters = values.ToArray(), BudgetDay = day, BudgetCount = budget + 1 }); return true;
            });
        }
        catch { /* Optional diagnostics must not change normal operation. */ }
    }
    public async Task<TelemetryStatus> SendAsync(CancellationToken token = default)
    {
        if (!await sending.WaitAsync(0, token)) return Status();
        CancellationTokenSource? deadline = null;
        try
        {
            if (endpoint is null) { lastResult = "Reporting endpoint is not configured."; return Status(); }
            var state = Locked(() => { var c = ReadConsent(); if (!c.Enabled) return (Consent: c, Batch: (Batch?)null); var q = ReadQueue(c); var batch = q.Pending ?? PrepareBatch(q.Counters); if (batch is not null && q.Pending is null) WriteQueue(q with { Pending = batch, Counters = q.Counters.Skip(batch.Errors.Length).ToArray() }); return (Consent: c, Batch: batch); });
            if (state.Batch is null) return Status();
            Directory.CreateDirectory(directory); RejectLinks();
            // A file lease works across async continuations and separate helper/desktop processes.
            using var lease = new FileStream(PathFor("uploader.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            lock (cancellationGate) active = deadline;
            if (!Locked(() => { var c = ReadConsent(); return c.Enabled && c.Epoch == state.Consent.Epoch; })) return Status();
            using var watcher = new Timer(_ => { try { if (!Locked(() => { var c = ReadConsent(); return c.Enabled && c.Epoch == state.Consent.Epoch; })) deadline.Cancel(); } catch { try { deadline.Cancel(); } catch (ObjectDisposedException) { } } }, null, 100, 100);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(state.Batch, Json)) };
            request.Content.Headers.ContentType = new("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.IsSuccessStatusCode)
            {
                Locked(() => { var c = ReadConsent(); if (c.Enabled && c.Epoch == state.Consent.Epoch) { var q = ReadQueue(c); if (q.Pending?.BatchId == state.Batch.BatchId) WriteQueue(q with { Pending = null }); } return true; });
                lastResult = "Error summary sent.";
            }
            else lastResult = "Reporting unavailable; summary retained for a later attempt.";
        }
        catch (OperationCanceledException) { lastResult = "Reporting canceled."; }
        catch { lastResult = "Reporting unavailable; summary retained for a later attempt."; }
        finally { lock (cancellationGate) { if (active == deadline) active = null; } deadline?.Dispose(); sending.Release(); }
        return Status();
    }
    public void Dispose() { lock (cancellationGate) active?.Cancel(); http.Dispose(); }
}
