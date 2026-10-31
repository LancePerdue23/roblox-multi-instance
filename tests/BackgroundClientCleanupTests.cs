using RobloxLaunchHelper;

internal static class BackgroundClientCleanupTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 23, 0, 0, DateTimeKind.Utc);
    private static readonly ProcessKey Key = new(42, Now.AddMinutes(-5).ToFileTimeUtc());
    private static Identity ValidIdentity => new("synthetic-owner", 7, false, @"C:\Synthetic\RobloxPlayerBeta.exe", Key.Created);
    private static void Check(bool value) { if (!value) throw new Exception("Background cleanup assertion failed."); }
    internal static void Run(Action<string, Action> run)
    {
        run("Automatic background cleanup requires continuous windowless observations and resets after gaps", () =>
        {
            var row = new ActiveInstance(Key, [], null, null, false);
            var snapshot = new InstanceSnapshot([row], 0);
            var tracker = new BackgroundCleanupTracker();
            for (int seconds = 0; seconds < 60; seconds += 5) Check(tracker.Observe(snapshot, seconds, Now).Length == 0);
            Check(tracker.Observe(snapshot, 60, Now).SequenceEqual(new[] { Key }));
            Check(tracker.Observe(snapshot, 65, Now).Length == 0); // No repeated stop on every poll.
            tracker.Observe(new([row with { Windows = [new(100, "fixture")] }], 0), 70, Now);
            Check(tracker.Observe(snapshot, 75, Now).Length == 0);
            Check(tracker.Observe(snapshot, 140, Now).Length == 0); // Long observation gap restarts grace.
            tracker.Observe(InstanceSnapshot.Failed, 145, Now);
            Check(tracker.Observe(snapshot, 150, Now).Length == 0);
            tracker.Observe(new([], 0), 155, Now);
            Check(tracker.Observe(snapshot, 160, Now).Length == 0);
            var replaced = new InstanceSnapshot([row with { Key = Key with { Created = Key.Created + 1 } }], 0);
            Check(tracker.Observe(replaced, 165, Now).Length == 0);
            Check(tracker.Observe(replaced, double.NaN, Now).Length == 0);
            var auxiliary = snapshot with { Instances = [row with { AnyVisibleWindow = true }] };
            Check(BackgroundClientCleanup.Candidates(auxiliary, Now).Length == 0);
            Check(tracker.Observe(auxiliary, 170, Now).Length == 0);
            Check(tracker.Observe(snapshot, 175, Now).Length == 0);
        });
        run("Automatic cleanup monitoring stops with its host without touching real clients", () =>
        {
            using var observed = new ManualResetEventSlim();
            var process = new FakeProcess();
            var monitor = new BackgroundClientMonitor(new EmptyCollector(observed), Service(process),
                @"Local\RLH-BackgroundCleanupTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Check(observed.Wait(TimeSpan.FromSeconds(5)));
                Check(monitor.StopAsync().Wait(TimeSpan.FromSeconds(5)));
                Check(process.Stops == 0);
                monitor.Dispose(); monitor.Dispose(); Check(monitor.StopAsync().IsCompleted);
            }
            finally { monitor.Dispose(); }
        });
        run("Background cleanup selects only older windowless clients from fresh snapshots", () =>
        {
            var row = new ActiveInstance(Key, [], null, null, false);
            var snapshot = new InstanceSnapshot([row, row, row with { Key = new(43, Now.AddSeconds(-59).ToFileTimeUtc()) },
                row with { Key = new(44, Key.Created), Windows = [new(100, "fixture")] }], 0);
            Check(BackgroundClientCleanup.Candidates(snapshot, Now).SequenceEqual(new[] { Key }));
            Check(BackgroundClientCleanup.Candidates(snapshot with { Available = false }, Now).Length == 0);
            Check(!BackgroundClientCleanup.OldEnough(Key with { Created = long.MaxValue }, Now));
        });
        run("Background cleanup binds identity and rechecks windows before stopping", () =>
        {
            foreach (var identity in new[] { ValidIdentity with { Created = Key.Created + 1 }, ValidIdentity with { Sid = "other" },
                ValidIdentity with { Session = 8 }, ValidIdentity with { Elevated = true }, ValidIdentity with { Image = @"C:\Synthetic\other.exe" } })
            {
                var process = new FakeProcess { Identity = identity };
                Check(Service(process).Execute(Key, default) == BackgroundCleanupResult.Skipped && process.Stops == 0 && process.Disposed);
            }
            var untrusted = new FakeProcess();
            Check(Service(untrusted, _ => false).Execute(Key, default) == BackgroundCleanupResult.Skipped && untrusted.Stops == 0);
            var visible = new FakeProcess { Visible = () => true };
            Check(Service(visible).Execute(Key, default) == BackgroundCleanupResult.Skipped && visible.Stops == 0);
            int reads = 0;
            var appeared = new FakeProcess { Visible = () => ++reads == 2 };
            Check(Service(appeared).Execute(Key, default) == BackgroundCleanupResult.Skipped && appeared.Stops == 0);
            var exited = new FakeProcess { IsAlive = false };
            Check(Service(exited).Execute(Key, default) == BackgroundCleanupResult.Skipped && exited.Stops == 0);
            var stopped = new FakeProcess();
            Check(Service(stopped).Execute(Key, default) == BackgroundCleanupResult.Stopped && stopped.Stops == 1 && stopped.Disposed);
            var denied = new FakeProcess { StopResult = false };
            Check(Service(denied).Execute(Key, default) == BackgroundCleanupResult.Unavailable);
            var pending = new FakeProcess { Exited = false };
            Check(Service(pending).Execute(Key, default) == BackgroundCleanupResult.ExitUnconfirmed);
        });
        run("Background cleanup respects cancellation and contains native failures", () =>
        {
            using var canceled = new CancellationTokenSource();
            var process = new FakeProcess();
            var service = Service(process, _ => { canceled.Cancel(); return true; });
            try { service.Execute(Key, canceled.Token); throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
            Check(process.Stops == 0 && process.Disposed);
            var failed = new BackgroundClientCleanup(_ => throw new IOException(), _ => true, "synthetic-owner", 7, () => Now);
            Check(failed.Execute(Key, default) == BackgroundCleanupResult.Unavailable);
        });
    }
    private static BackgroundClientCleanup Service(FakeProcess process, Func<string, bool>? trusted = null) =>
        new(_ => process, trusted ?? (_ => true), "synthetic-owner", 7, () => Now);
    private sealed class EmptyCollector(ManualResetEventSlim observed) : IInstanceCollector
    {
        public InstanceSnapshot Collect(CancellationToken token) { observed.Set(); return new([], 0); }
    }
    private sealed class FakeProcess : IBackgroundClientProcess
    {
        public Identity Identity { get; init; } = ValidIdentity;
        public bool IsAlive { get; init; } = true;
        internal Func<bool> Visible = () => false;
        internal int Stops;
        internal bool Disposed, StopResult = true, Exited = true;
        public bool HasVisibleWindow() => Visible();
        public bool Stop() { Stops++; return StopResult; }
        public bool WaitForExit(uint milliseconds) { Check(milliseconds == 2000); return Exited; }
        public long? CpuTime() => null;
        public ulong? WorkingSet() => null;
        public InstanceWindow[] Windows() => [];
        public bool OwnsWindow(nint window) => false;
        public void Restore(nint window) { }
        public bool Focus(nint window) => false;
        public bool Close(nint window) => false;
        public void Dispose() => Disposed = true;
    }
}
