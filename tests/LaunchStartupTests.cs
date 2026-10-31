using RobloxLaunchHelper;

internal static class LaunchStartupTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Launch startup assertion failed."); }
    private static readonly DateTime Now = new(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);
    private static ActiveInstance Row(int pid, int age = 120) =>
        new(new(pid, Now.AddSeconds(-age).ToFileTimeUtc()), [], null, null, false, false);

    internal static void Run(Action<string, Action> run)
    {
        run("Prelaunch cleanup preserves games, recent clients, changed identities and uncertain snapshots", () =>
        {
            var row = Row(10);
            var snapshot = new InstanceSnapshot([row], 0);
            Check(LaunchStartup.StaleBackgrounds(snapshot, snapshot, Now).SequenceEqual(new[] { row.Key }));
            foreach (var unsafeSnapshot in new[] {
                InstanceSnapshot.Failed, snapshot with { Unavailable = 1 },
                new([row with { AnyVisibleWindow = true }], 0),
                new([row with { AnyVisibleWindow = null }], 0),
                new([row with { Windows = [new(1, "minimized game")] }], 0),
                new([row, Row(20) with { AnyVisibleWindow = true }], 0),
                new([Row(10, 59)], 0), new([Row(10, 121)], 0), new([], 0) })
            {
                Check(LaunchStartup.StaleBackgrounds(snapshot, unsafeSnapshot, Now).Length == 0);
                Check(LaunchStartup.StaleBackgrounds(unsafeSnapshot, snapshot, Now).Length == 0);
            }
        });
        run("Startup exit detection excludes existing games and allows process handoff and inspection gaps", () =>
        {
            var old = Row(10); var next = Row(20, 0);
            var baseline = new InstanceSnapshot([old], 0);
            var state = new LaunchStartupState([old.Key]);
            Check(!state.Exited(baseline, false, 0));
            Check(!state.Exited(baseline, true, 500));
            Check(!state.Exited(baseline, true, 2499));
            Check(state.Exited(baseline, true, 2500));
            Check(!state.Exited(new([old, next], 0), true, 2600));
            Check(!state.Exited(baseline, true, 2700));
            Check(!state.Exited(baseline with { Unavailable = 1 }, true, 4700));
            Check(!state.Exited(baseline, true, 4800));
            Check(!state.Exited(InstanceSnapshot.Failed, true, 6800));
            Check(!state.Exited(baseline, true, 6900));
            Check(state.Exited(baseline, true, 8900));
        });
        run("Startup crash ends pending worker wait quickly and cannot be hidden by an early worker OK", () =>
        {
            foreach (Task worker in new[] { new TaskCompletionSource().Task, Task.CompletedTask })
            {
                long time = 0;
                bool failed = LaunchStartup.ExitedAsync(new([], 0), () => time >= 1000,
                    _ => Task.FromResult(new InstanceSnapshot([], 0)), worker, default,
                    () => time, _ => { time += 500; return Task.CompletedTask; }).GetAwaiter().GetResult();
                Check(failed && time == 3000);
            }
        });
        run("Startup observer accepts healthy handoff, bounds success monitoring and honors cancellation", () =>
        {
            long time = 0;
            var baseline = new InstanceSnapshot([Row(10)], 0);
            bool failed = LaunchStartup.ExitedAsync(baseline, () => true,
                _ => Task.FromResult(time < 1000 ? baseline : new InstanceSnapshot([Row(10), Row(20, 0)], 0)),
                Task.CompletedTask, default, () => time,
                _ => { time += 500; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(!failed && time == 8000);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try
            {
                LaunchStartup.ExitedAsync(baseline, () => true, _ => throw new Exception("Must not collect"),
                    Task.CompletedTask, canceled.Token).GetAwaiter().GetResult();
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
            Check(!LaunchStartup.ExitedAsync(InstanceSnapshot.Failed, () => throw new Exception("Must not inspect"),
                _ => throw new Exception("Must not collect"), Task.CompletedTask, default).GetAwaiter().GetResult());
        });
        run("Startup exit near the monitoring deadline still receives the full handoff grace period", () =>
        {
            long time = 0;
            bool failed = LaunchStartup.ExitedAsync(new([], 0), () => time >= 7500,
                _ => Task.FromResult(new InstanceSnapshot([], 0)), Task.CompletedTask, default,
                () => time, _ => { time += 500; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(failed && time == 9500);
        });
        run("Startup exit failure always requests a popup with progress and completion turned off", () =>
        {
            long time = 0;
            var state = new LaunchNotificationState(() => time);
            var status = new LaunchStatus(Guid.NewGuid(), 3, 123, 456, LaunchOutcome.Failed, DiagnosticCode.PlayerExited, true);
            Check(status.Valid && status.Warning);
            Check(state.Accept(status, new())); time = 2000;
            var popup = state.TakePopup(new());
            Check(popup is { Warning: true } && popup.Value.Text.Contains("Roblox closed during startup") &&
                popup.Value.Text.Contains("system tray"));
        });
    }
}
