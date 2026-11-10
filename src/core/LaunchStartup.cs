using System.Diagnostics;

namespace RobloxLaunchHelper;

// A player may hand off to another process. Existing games must never mask an
// unsuccessful new launch, and an inaccessible snapshot is not evidence of exit.
internal sealed class LaunchStartupState(IEnumerable<ProcessKey> baseline)
{
    private readonly HashSet<ProcessKey> existing = [.. baseline];
    private long? emptySince;
    internal bool ExitPending => emptySince is not null;
    internal bool Exited(InstanceSnapshot snapshot, bool starterExited, long milliseconds)
    {
        if (!starterExited || !snapshot.Available || snapshot.Unavailable != 0 ||
            snapshot.Instances.Any(x => !existing.Contains(x.Key)))
        {
            emptySince = null;
            return false;
        }
        emptySince ??= milliseconds;
        return milliseconds - emptySince >= 2000;
    }
}

internal static class LaunchStartup
{
    internal static IInstanceCollector Collector() => new InstanceCollector(new InstancePlatform(),
        Program.Sid, Process.GetCurrentProcess().SessionId, collectMetrics: false);

    // Only old, consistently windowless clients qualify. If any client is visible
    // or cannot be inspected, leave everything alone. Execute revalidates identity,
    // signature, ownership, elevation, liveness and visibility on the same handle.
    internal static ProcessKey[] StaleBackgrounds(InstanceSnapshot before, InstanceSnapshot after, DateTime now)
    {
        static bool Safe(InstanceSnapshot value) => value.Available && value.Unavailable == 0 &&
            value.Instances.All(x => x.Windows.Length == 0 && x.AnyVisibleWindow == false);
        if (!Safe(before) || !Safe(after)) return [];
        return BackgroundClientCleanup.Candidates(before, now)
            .Intersect(BackgroundClientCleanup.Candidates(after, now)).ToArray();
    }

    internal static async Task<InstanceSnapshot> PrepareAsync(IInstanceCollector collector, CancellationToken token)
    {
        var before = await Task.Run(() => collector.Collect(token), token);
        Log.Write("launch-baseline", Evidence(before));
        if (StaleBackgrounds(before, before, DateTime.UtcNow).Length == 0) return before;
        await Task.Delay(500, token);
        var after = await Task.Run(() => collector.Collect(token), token);
        var cleanup = new BackgroundClientCleanup(pid => new InstanceProcess(pid, allowStop: true),
            RobloxTrust.IsSignedRoblox, Program.Sid, Process.GetCurrentProcess().SessionId);
        foreach (var key in StaleBackgrounds(before, after, DateTime.UtcNow))
        {
            var result = await Task.Run(() => cleanup.Execute(key, token), token);
            Log.Write("launch-background-cleanup", result.ToString(), key.Id);
        }
        return await Task.Run(() => collector.Collect(token), token);
    }

    // Process identity/visibility only: never include window titles, URIs or authentication arguments.
    internal static string Evidence(InstanceSnapshot snapshot) =>
        $"available={snapshot.Available};unavailable={snapshot.Unavailable};clients="+
        string.Join(',',snapshot.Instances.Select(x=>$"{x.Key.Id}@{DateTime.FromFileTimeUtc(x.Key.Created):O}/windows={x.Windows.Length}/visible={x.AnyVisibleWindow}"));

    // Preserve the worker's bounded scan through startup gaps. Successful cleanup
    // need not wait for a fixed observation floor; confirmation concerns the
    // singleton, not a guarantee that the player survives later gameplay.
    internal static Task<bool> ExitedAsync(Process starter, IInstanceCollector collector,
        InstanceSnapshot baseline, Task workerResult, CancellationToken token)
        => ExitedAsync(baseline, () => starter.HasExited,
            ct => Task.Run(() => collector.Collect(ct), ct), workerResult, token);

    internal static async Task<bool> ExitedAsync(InstanceSnapshot baseline, Func<bool> starterExited,
        Func<CancellationToken, Task<InstanceSnapshot>> collect, Task workerResult, CancellationToken token,
        Func<long>? milliseconds = null, Func<CancellationToken, Task>? delay = null)
    {
        if (!baseline.Available || baseline.Unavailable != 0) return false;
        var state = new LaunchStartupState(baseline.Instances.Select(x => x.Key));
        var elapsed = Stopwatch.StartNew();
        milliseconds ??= () => elapsed.ElapsedMilliseconds;
        delay ??= ct => Task.Delay(500, ct);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            bool exited;
            try { exited = starterExited(); }
            catch { return false; }
            if (exited)
            {
                var snapshot = await collect(token);
                // A bootstrapper can disappear well before the replacement player
                // appears. Keep the worker's bounded scan alive until its result;
                // a temporary process gap must not cancel singleton cleanup.
                if (state.Exited(snapshot, true, milliseconds()) && workerResult.IsCompleted && milliseconds() >= 8000) return true;
            }
            if(workerResult.IsCompleted && !state.ExitPending)return false;
            await delay(token);
        }
    }
}
