using RobloxLaunchHelper;

internal static class LaunchStartupTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Launch startup assertion failed."); }
    private static readonly DateTime Now = new(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);
    private static ActiveInstance Row(int pid, int age = 120) =>
        new(new(pid, Now.AddSeconds(-age).ToFileTimeUtc()), [], null, null, false, false);

    internal static void Run(Action<string, Action> run)
    {
        run("Launch evidence preserves process identity and uncertainty without window titles",()=>
        {
            var row=Row(10) with {Windows=[new(1,"private title with ticket")],AnyVisibleWindow=true};
            string evidence=LaunchStartup.Evidence(new([row],2));
            Check(evidence.Contains("available=True;unavailable=2")&&evidence.Contains("10@2026-10-31T15:58:00.0000000Z/windows=1/visible=True")&&!evidence.Contains("private"));
            Check(LaunchStartup.Evidence(InstanceSnapshot.Failed).Contains("available=False"));
        });
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
        run("Startup exit preserves pending singleton cleanup and cannot be hidden by an early worker OK", () =>
        {
            foreach (bool pending in new[] {true,false})
            {
                long time = 0;
                var completion=new TaskCompletionSource();
                Task worker=pending?completion.Task:Task.CompletedTask;
                bool failed = LaunchStartup.ExitedAsync(new([], 0), () => !pending || time >= 1000,
                    _ => Task.FromResult(new InstanceSnapshot([], 0)), worker, default,
                    () => time, _ => { time += 500;if(time>=60000)completion.TrySetResult();return Task.CompletedTask; }).GetAwaiter().GetResult();
                Check(failed && time == (pending?60000:8000));
            }
        });
        run("Delayed bootstrap handoff keeps singleton observation alive until the worker completes", () =>
        {
            long time=0;var completion=new TaskCompletionSource();
            var baseline=new InstanceSnapshot([Row(10)],0);
            bool failed=LaunchStartup.ExitedAsync(baseline,()=>true,
                _=>Task.FromResult(time<12000?baseline:new InstanceSnapshot([Row(10),Row(20,0)],0)),
                completion.Task,default,()=>time,_=>{time+=500;if(time>=15000)completion.TrySetResult();return Task.CompletedTask;}).GetAwaiter().GetResult();
            Check(!failed&&time==15000);
        });
        run("Startup observer accepts healthy handoff, bounds success monitoring and honors cancellation", () =>
        {
            long time = 0;
            var baseline = new InstanceSnapshot([Row(10)], 0);
            bool failed = LaunchStartup.ExitedAsync(baseline, () => true,
                _ => Task.FromResult(time < 1000 ? baseline : new InstanceSnapshot([Row(10), Row(20, 0)], 0)),
                Task.CompletedTask, default, () => time,
                _ => { time += 500; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(!failed && time == 1000);
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
        run("Startup exit when the worker completes still receives the full handoff grace period", () =>
        {
            long time = 0;
            var worker=new TaskCompletionSource();
            bool failed = LaunchStartup.ExitedAsync(new([], 0), () => time >= 7500,
                _ => Task.FromResult(new InstanceSnapshot([], 0)), worker.Task, default,
                () => time, _ => { time += 500;if(time>=7500)worker.TrySetResult();return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(failed && time == 9500);
        });
        run("Healthy singleton confirmation has no fixed eight-second success delay",()=>
        {
            long time=0;var worker=new TaskCompletionSource();
            bool failed=LaunchStartup.ExitedAsync(new([],0),()=>false,_=>throw new Exception("A living starter does not need discovery"),worker.Task,default,
                ()=>time,_=>{time+=500;if(time>=3000)worker.TrySetResult();return Task.CompletedTask;}).GetAwaiter().GetResult();
            Check(!failed&&time==3000);
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
