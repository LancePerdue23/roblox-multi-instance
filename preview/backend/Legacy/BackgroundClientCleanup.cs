namespace RobloxLaunchHelper;

internal interface IBackgroundClientProcess : IInstanceProcess
{
    bool HasVisibleWindow();
    bool Stop();
    bool WaitForExit(uint milliseconds);
}

internal enum BackgroundCleanupResult { Stopped, Skipped, Unavailable, ExitUnconfirmed }

internal sealed class BackgroundClientCleanup(Func<int, IBackgroundClientProcess> open,
    Func<string, bool> trusted, string sid, int session, Func<DateTime>? utcNow = null)
{
    private readonly Func<DateTime> now = utcNow ?? (() => DateTime.UtcNow);
    internal static bool OldEnough(ProcessKey key, DateTime utcNow)
    {
        try { return utcNow - DateTime.FromFileTimeUtc(key.Created) >= TimeSpan.FromMinutes(1); }
        catch { return false; }
    }
    internal static ProcessKey[] Candidates(InstanceSnapshot snapshot, DateTime utcNow) => !snapshot.Available ? [] :
        snapshot.Instances.Where(client => client.Windows.Length == 0 && client.AnyVisibleWindow != true && OldEnough(client.Key, utcNow))
            .Select(client => client.Key).Distinct().ToArray();

    internal BackgroundCleanupResult Execute(ProcessKey key, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!OldEnough(key, now())) return BackgroundCleanupResult.Skipped;
            using var process = open(key.Id);
            var id = process.Identity;
            if (id.Created != key.Created || !InstancePolicy.Matches(id, sid, session) || id.Elevated || !process.IsAlive)
                return BackgroundCleanupResult.Skipped;
            token.ThrowIfCancellationRequested();
            if (!trusted(id.Image) || process.HasVisibleWindow()) return BackgroundCleanupResult.Skipped;
            token.ThrowIfCancellationRequested();
            // Repeat after potentially slow signature/window checks. Termination uses this same process handle.
            if (!process.IsAlive || process.HasVisibleWindow()) return BackgroundCleanupResult.Skipped;
            token.ThrowIfCancellationRequested();
            if (!process.Stop()) return BackgroundCleanupResult.Unavailable;
            return process.WaitForExit(2000) ? BackgroundCleanupResult.Stopped : BackgroundCleanupResult.ExitUnconfirmed;
        }
        catch (OperationCanceledException) { throw; }
        catch { return BackgroundCleanupResult.Unavailable; }
    }
}

// A missed or unavailable observation restarts the grace period. No process identity is persisted.
internal sealed class BackgroundCleanupTracker
{
    private readonly Dictionary<ProcessKey, double> since = [];
    private double? lastObservation;
    internal ProcessKey[] Observe(InstanceSnapshot snapshot, double seconds, DateTime utcNow)
    {
        if (!double.IsFinite(seconds) || !snapshot.Available)
        { since.Clear(); lastObservation = null; return []; }
        if (lastObservation is double previous && (seconds < previous || seconds - previous > 30)) since.Clear();
        lastObservation = seconds;
        var eligible = snapshot.Instances.Where(client => client.Windows.Length == 0 && client.AnyVisibleWindow != true)
            .Select(client => client.Key).ToHashSet();
        foreach (var key in since.Keys.Where(key => !eligible.Contains(key)).ToArray()) since.Remove(key);
        var ready = new List<ProcessKey>();
        foreach (var key in eligible)
        {
            if (!since.TryGetValue(key, out double started)) since[key] = seconds;
            else if (seconds - started >= 60 && BackgroundClientCleanup.OldEnough(key, utcNow))
            { ready.Add(key); since[key] = seconds; }
        }
        return ready.ToArray();
    }
}

// One dedicated thread holds the session mutex: mutex ownership must not migrate across await boundaries.
internal sealed class BackgroundClientMonitor : IDisposable
{
    private readonly CancellationTokenSource stopped = new();
    private int disposed;
    internal Task Completion { get; }
    internal volatile bool OwnsMutex;
    internal BackgroundClientMonitor(IInstanceCollector collector, BackgroundClientCleanup cleanup, string mutexName)
    {
        var token = stopped.Token;
        Completion = Task.Factory.StartNew(() =>
        {
            try
            {
                using var mutex = new Mutex(false, mutexName);
                bool held = false;
                try
                {
                    while (!token.IsCancellationRequested && !held)
                    {
                        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held && token.WaitHandle.WaitOne(5000)) return;
                    }
                    if (!held) return; OwnsMutex = true;
                    var tracker = new BackgroundCleanupTracker();
                    while (!token.IsCancellationRequested)
                    {
                        InstanceSnapshot snapshot;
                        try { snapshot = collector.Collect(token); }
                        catch (OperationCanceledException) { return; }
                        catch { snapshot = InstanceSnapshot.Failed; }
                        token.ThrowIfCancellationRequested();
                        double seconds = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                        foreach (var key in tracker.Observe(snapshot, seconds, DateTime.UtcNow)) cleanup.Execute(key, token);
                        if (token.WaitHandle.WaitOne(5000)) return;
                    }
                }
                finally { OwnsMutex = false; if (held) mutex.ReleaseMutex(); }
            }
            catch (OperationCanceledException) { }
            catch { /* Cleanup being unavailable never blocks app or game startup. */ }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    internal Task StopAsync() { try { stopped.Cancel(); } catch(ObjectDisposedException) { } return Completion; }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        stopped.Cancel();
        _ = Completion.ContinueWith(_ => stopped.Dispose(), TaskScheduler.Default);
    }
}

