using System.Diagnostics;

namespace RobloxLaunchHelper;

// Transient UI data only: deliberately absent from diagnostic history/report models.
internal sealed record InstanceWindow(nint Handle, string Title);
internal sealed record ActiveInstance(ProcessKey Key, InstanceWindow[] Windows, double? CpuPercent,
    ulong? WorkingSet, bool CpuAvailable, bool? AnyVisibleWindow = null, string? Username = null);
internal sealed record InstanceSnapshot(ActiveInstance[] Instances, int Unavailable, bool Available = true)
{
    internal static readonly InstanceSnapshot Failed = new([], 0, false);
}
internal readonly record struct InstanceTarget(ProcessKey Key, nint Window);
internal enum InstanceAction { Focus, Close }
internal enum InstanceActionResult { Focused, FocusDenied, CloseRequested, CloseFailed, Changed, Unavailable }

internal interface IInstanceProcess : IDisposable
{
    Identity Identity { get; }
    bool IsAlive { get; }
    long? CpuTime(); // 100 ns, cumulative kernel + user
    ulong? WorkingSet();
    InstanceWindow[] Windows();
    bool OwnsWindow(nint window);
    void Restore(nint window);
    bool Focus(nint window);
    bool Close(nint window);
}
internal interface IInstancePlatform
{
    int[] Candidates();
    IInstanceProcess Open(int pid);
    bool Trusted(string image);
    int ProcessorCount { get; }
}
internal interface IInstanceCollector
{
    InstanceSnapshot Collect(CancellationToken token);
}

internal static class InstancePolicy
{
    internal static bool Matches(Identity id, string sid, int session) => id.Sid == sid && id.Session == session &&
        string.Equals(Path.GetFileName(id.Image), "RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase);
    internal static DateTime Started(ProcessKey key) => DateTime.FromFileTimeUtc(key.Created).ToLocalTime();
    internal static bool Contains(InstanceSnapshot snapshot, InstanceTarget target) => snapshot.Available &&
        snapshot.Instances.Any(x => x.Key == target.Key && x.Windows.Any(w => w.Handle == target.Window));
}

internal sealed class InstanceCollector(IInstancePlatform platform, string sid, int session,
    Func<double>? seconds = null, bool collectMetrics = true) : IInstanceCollector
{
    private readonly Dictionary<ProcessKey, string> trust = [];
    private readonly InstanceAccounts accounts = new();
    private readonly Dictionary<ProcessKey, (long Cpu, double At)> samples = [];
    private readonly Func<double> clock = seconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    public InstanceSnapshot Collect(CancellationToken token)
    {
        var rows = new List<ActiveInstance>();
        var seen = new HashSet<ProcessKey>();
        int unavailable = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            var candidates = platform.Candidates();
            foreach (int pid in candidates)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var process = platform.Open(pid);
                    var id = process.Identity;
                    var key = new ProcessKey(pid, id.Created);
                    if (!InstancePolicy.Matches(id, sid, session)) continue;
                    token.ThrowIfCancellationRequested();
                    if (!trust.TryGetValue(key, out string? image) || image != id.Image)
                    {
                        if (!platform.Trusted(id.Image)) { unavailable++; continue; }
                        trust[key] = id.Image;
                    }
                    token.ThrowIfCancellationRequested();
                    if (!process.IsAlive) continue;
                    token.ThrowIfCancellationRequested();
                    long? cpu = collectMetrics ? process.CpuTime() : null;
                    double at = clock();
                    token.ThrowIfCancellationRequested();
                    ulong? memory = collectMetrics ? process.WorkingSet() : null;
                    token.ThrowIfCancellationRequested();
                    var windows = process.Windows();
                    token.ThrowIfCancellationRequested();
                    bool? anyVisibleWindow = !collectMetrics && process is IBackgroundClientProcess background
                        ? background.HasVisibleWindow() : null;
                    token.ThrowIfCancellationRequested();
                    if (!process.IsAlive) continue;
                    token.ThrowIfCancellationRequested();
                    double? percent = null;
                    if (cpu is long current && current >= 0)
                    {
                        if (samples.TryGetValue(key, out var previous))
                            percent = CalculateCpu(previous.Cpu, current, at - previous.At, platform.ProcessorCount);
                        samples[key] = (current, at);
                    }
                    else samples.Remove(key);
                    seen.Add(key);
                    string? username = collectMetrics && platform is InstancePlatform ? accounts.Resolve(key,token) : null;
                    token.ThrowIfCancellationRequested();
                    if (!process.IsAlive) continue;
                    rows.Add(new(key, windows, percent, memory, cpu is >= 0, anyVisibleWindow,username));
                }
                catch (OperationCanceledException) { throw; }
                catch { unavailable++; } // No arbitrary exception text or candidate identity enters logs.
            }
            foreach (var key in trust.Keys.Where(k => !seen.Contains(k)).ToArray()) trust.Remove(key);
            foreach (var key in samples.Keys.Where(k => !seen.Contains(k)).ToArray()) samples.Remove(key);
            accounts.Retain(seen);
            return new(rows.OrderBy(x => x.Key.Created).ThenBy(x => x.Key.Id).ToArray(), unavailable);
        }
        catch (OperationCanceledException) { trust.Clear(); samples.Clear(); throw; }
        catch { trust.Clear(); samples.Clear(); return InstanceSnapshot.Failed; }
    }

    internal static double? CalculateCpu(long previous, long current, double elapsed, int processors)
    {
        if (previous < 0 || current < previous || elapsed <= 0 || !double.IsFinite(elapsed) || processors <= 0) return null;
        return Math.Clamp(100 * ((double)current - previous) / TimeSpan.TicksPerSecond / elapsed / processors, 0, 100);
    }
}

internal sealed class InstanceActions(IInstancePlatform platform, string sid, int session)
{
    internal InstanceActionResult Execute(InstanceTarget target, InstanceAction action, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var process = platform.Open(target.Key.Id);
            var id = process.Identity;
            if (id.Created != target.Key.Created || !InstancePolicy.Matches(id, sid, session)) return InstanceActionResult.Changed;
            token.ThrowIfCancellationRequested();
            if (!platform.Trusted(id.Image)) return InstanceActionResult.Changed;
            token.ThrowIfCancellationRequested();
            if (!process.IsAlive || !process.OwnsWindow(target.Window)) return InstanceActionResult.Changed;
            token.ThrowIfCancellationRequested();
            if (action == InstanceAction.Focus)
            {
                process.Restore(target.Window);
                token.ThrowIfCancellationRequested();
                if (!process.IsAlive || !process.OwnsWindow(target.Window)) return InstanceActionResult.Changed;
                token.ThrowIfCancellationRequested();
                return process.Focus(target.Window) ? InstanceActionResult.Focused : InstanceActionResult.FocusDenied;
            }
            token.ThrowIfCancellationRequested();
            return process.Close(target.Window) ? InstanceActionResult.CloseRequested : InstanceActionResult.CloseFailed;
        }
        catch (OperationCanceledException) { throw; }
        catch { return InstanceActionResult.Unavailable; }
    }
}

// UI-thread owned. A new generation awaits the preceding one, including a synchronous
// native call that cancellation cannot interrupt. No overlapping collectors or late UI results.
internal sealed class InstanceRefreshController(Func<IInstanceCollector> factory, Action<InstanceSnapshot> present,
    Func<CancellationToken, Task>? delay = null) : IDisposable
{
    private CancellationTokenSource? cancellation;
    private Task tail = Task.CompletedTask;
    private bool active, disposed;
    private int generation;
    internal Task Completion => tail;
    internal void SetActive(bool value)
    {
        if (disposed || active == value) return;
        active = value;
        generation++;
        cancellation?.Cancel();
        if (!value) return;
        var source = new CancellationTokenSource();
        cancellation = source;
        tail = RunAsync(tail, source, generation);
    }
    private async Task RunAsync(Task previous, CancellationTokenSource source, int version)
    {
        // Yield also makes SetActive safe with a synchronous fake collector/delay.
        await Task.Yield();
        try
        {
            await previous;
            source.Token.ThrowIfCancellationRequested();
            var collector = factory(); // Fresh trust and CPU baselines after every pause.
            while (true)
            {
                var result = await Task.Run(() => collector.Collect(source.Token), source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (disposed || version != generation) return;
                present(result);
                await (delay?.Invoke(source.Token) ?? Task.Delay(TimeSpan.FromSeconds(2), source.Token));
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!disposed && version == generation) present(InstanceSnapshot.Failed); }
        finally
        {
            if (ReferenceEquals(cancellation, source)) cancellation = null;
            source.Dispose();
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        SetActive(false);
        disposed = true;
    }
}
