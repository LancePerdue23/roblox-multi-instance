using RobloxLaunchHelper;
using System.Diagnostics;
using System.Text;

internal static class ActiveInstancesTests
{
    private const string Sid = "S-1-5-21-synthetic";
    private const int Session = 7;
    private static readonly long Created = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    private static Identity Id => new(Sid, Session, false, @"C:\Synthetic\RobloxPlayerBeta.exe", Created);
    private static void Check(bool condition) { if (!condition) throw new Exception("Active-instance assertion failed."); }
    private static T Only<T>(IEnumerable<T> items) => items.Single();
    private static InstanceTarget Target => new(new(42, Created), 100);

    internal static void Run(Action<string, Action> run)
    {
        run("Active instances require owner, handle session, basename and publisher; trust is per lifetime", () =>
        {
            var platform = new FakePlatform();
            var collector = new InstanceCollector(platform, Sid, Session);
            foreach (var identity in new[] { Id with { Sid = "other" }, Id with { Session = 8 }, Id with { Image = @"C:\Synthetic\Other.exe" } })
            {
                platform.Identity = identity;
                Check(collector.Collect(default).Instances.Length == 0);
            }
            Check(platform.Verifications == 0);
            platform.Identity = Id; platform.Trust = false;
            Check(collector.Collect(default).Instances.Length == 0);
            platform.Trust = true;
            Check(collector.Collect(default).Instances.Length == 1);
            int verified = platform.Verifications;
            collector.Collect(default); Check(platform.Verifications == verified);
            platform.Identity = Id with { Created = Created + 1 };
            collector.Collect(default); Check(platform.Verifications == verified + 1);
            Check(platform.Opened == platform.Disposed);
        });
        run("CPU uses monotonic deltas and total processor capacity; PID reuse and failure reset samples", () =>
        {
            var platform = new FakePlatform { Cpu = 10_000_000 };
            double now = 1;
            var collector = new InstanceCollector(platform, Sid, Session, () => now);
            Check(Only(collector.Collect(default).Instances).CpuPercent is null);
            now += 2; platform.Cpu += 20_000_000;
            Check(Only(collector.Collect(default).Instances).CpuPercent == 25); // 1 CPU over 4 logical CPUs
            platform.Identity = Id with { Created = Created + 1 }; now++;
            Check(Only(collector.Collect(default).Instances).CpuPercent is null);
            platform.Cpu = null; now++;
            var missing = Only(collector.Collect(default).Instances);
            Check(!missing.CpuAvailable && missing.CpuPercent is null && missing.WorkingSet == 1048576);
            platform.Cpu = 100; now++;
            Check(Only(collector.Collect(default).Instances).CpuPercent is null);
            Check(InstanceCollector.CalculateCpu(100, 50, 1, 4) is null);
            Check(InstanceCollector.CalculateCpu(0, 100, 0, 4) is null);
            Check(InstanceCollector.CalculateCpu(0, 100, double.NaN, 4) is null);
            Check(InstanceCollector.CalculateCpu(0, 100, 1, 0) is null);
            Check(InstanceCollector.CalculateCpu(0, 100_000_000, 1, 1) == 100);
            Check(InstancePolicy.Started(new(42, Created)).ToUniversalTime().ToFileTimeUtc() == Created);
        });
        run("Discovery prunes exits and failed identities, distinguishes failed enumeration, and disposes handles", () =>
        {
            var p = new FakePlatform(); var collector = new InstanceCollector(p, Sid, Session);
            collector.Collect(default); int verified = p.Verifications;
            p.CandidateIds = []; Check(collector.Collect(default).Instances.Length == 0);
            p.CandidateIds = [42]; collector.Collect(default); Check(p.Verifications == verified + 1);
            p.OpenError = true; var unavailable = collector.Collect(default);
            Check(unavailable.Available && unavailable.Instances.Length == 0 && unavailable.Unavailable == 1);
            p.OpenError = false; collector.Collect(default); Check(p.Verifications == verified + 2);
            p.EnumerationError = true; Check(!collector.Collect(default).Available);
            p.EnumerationError = false; collector.Collect(default); Check(p.Verifications == verified + 3);
            foreach (string stage in new[] { "identity", "alive", "cpu", "memory", "windows" })
            {
                p.ErrorStage = stage;
                Check(collector.Collect(default).Instances.Length == 0);
                Check(p.Opened == p.Disposed);
            }
            p.ErrorStage = null;
            p.Alive = () => false; Check(collector.Collect(default).Instances.Length == 0);
            int reads = 0; p.Alive = () => ++reads == 1;
            Check(collector.Collect(default).Instances.Length == 0); // Exit after metrics/window reads.
            Check(p.Opened == p.Disposed);
        });
        run("Discovery retains windowless clients and separate same-title windows without duplicating metrics", () =>
        {
            var p = new FakePlatform { WindowList = [] }; var c = new InstanceCollector(p, Sid, Session);
            Check(Only(c.Collect(default).Instances).Windows.Length == 0);
            p.WindowList = [new(100, "same"), new(101, "same"), new(102, "")];
            var row = Only(c.Collect(default).Instances);
            Check(row.Windows.Length == 3 && row.WorkingSet == 1048576);
            Check(InstancePolicy.Contains(new([row], 0), Target));
            Check(!InstancePolicy.Contains(new([row], 0), Target with { Key = new(42, Created + 1) }));
            Check(!InstancePolicy.Contains(new([row], 0), Target with { Window = 999 }));
            p.Cpu = null; p.Memory = null;
            row = Only(c.Collect(default).Instances);
            Check(row.WorkingSet is null && !row.CpuAvailable && InstancePolicy.Contains(new([row], 0), Target));
        });
        run("Cancellation after trust validation performs no later sampling and disposes process handles", () =>
        {
            var p = new FakePlatform(); using var cancel = new CancellationTokenSource();
            p.OnTrust = cancel.Cancel;
            try { new InstanceCollector(p, Sid, Session).Collect(cancel.Token); throw new Exception("Cancellation expected"); }
            catch (OperationCanceledException) { }
            Check(p.CounterReads == 0 && p.Opened == p.Disposed);
        });
        run("Cancellation at identity and sampling boundaries prevents later reads or another candidate", () =>
        {
            string[] stages = ["identity", "alive", "cpu", "memory", "windows"];
            foreach (string stage in stages)
            {
                using var cancel = new CancellationTokenSource();
                var p = new FakePlatform { CandidateIds = [42, 43] };
                var reads = new List<string>();
                p.OnRead = current => { reads.Add(current); if (current == stage) cancel.Cancel(); };
                try { new InstanceCollector(p, Sid, Session).Collect(cancel.Token); throw new Exception("Cancellation expected"); }
                catch (OperationCanceledException) { }
                Check(p.Opened == 1 && p.Disposed == 1 && reads.Last() == stage);
            }
        });
        run("Actions revalidate identity, publisher and HWND after confirmation; never retarget a reused PID", () =>
        {
            var p = new FakePlatform(); var actions = new InstanceActions(p, Sid, Session);
            foreach (var identity in new[] { Id with { Created = Created + 1 }, Id with { Sid = "other" },
                Id with { Session = 8 }, Id with { Image = @"C:\Other.exe" } })
            {
                p.Identity = identity;
                Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.Changed);
            }
            p.Identity = Id; p.Trust = false;
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.Changed);
            p.Trust = true; p.WindowList = [new(101, "same caption")];
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.Changed);
            p.WindowList = [new(100, "changed title")]; p.Alive = () => false;
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.Changed);
            Check(p.CloseCalls == 0 && p.FocusCalls == 0 && p.Opened == p.Disposed);
        });
        run("Actions handle window changes during restore, foreground denial, unconfirmed close and delayed exit", () =>
        {
            var p = new FakePlatform(); var actions = new InstanceActions(p, Sid, Session);
            p.OnRestore = () => p.WindowList = [];
            Check(actions.Execute(Target, InstanceAction.Focus, default) == InstanceActionResult.Changed && p.FocusCalls == 0);
            p.WindowList = [new(100, "fixture")]; p.OnRestore = null; p.FocusResult = false;
            Check(actions.Execute(Target, InstanceAction.Focus, default) == InstanceActionResult.FocusDenied);
            p.CloseResult = false;
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.CloseFailed && p.CloseCalls == 1);
            p.CloseResult = true;
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.CloseRequested && p.Alive());
            p.OpenError = true;
            Check(actions.Execute(Target, InstanceAction.Close, default) == InstanceActionResult.Unavailable);
            Check(p.CloseCalls == 2 && p.Opened == p.Disposed);
        });
        run("Canceled action cannot send a window request after a slow trust check", () =>
        {
            var p = new FakePlatform(); using var cancel = new CancellationTokenSource(); p.OnTrust = cancel.Cancel;
            try { new InstanceActions(p, Sid, Session).Execute(Target, InstanceAction.Close, cancel.Token); throw new Exception("Cancellation expected"); }
            catch (OperationCanceledException) { }
            Check(p.CloseCalls == 0 && p.Opened == p.Disposed);
        });
        run("Refresh cancellation drains native work, discards late results and serializes reopened generations", () => WithoutContext(async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            int factories = 0, presentations = 0, concurrent = 0, maximum = 0;
            var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var controller = new InstanceRefreshController(() =>
            {
                int number = Interlocked.Increment(ref factories);
                return new FakeCollector(_ =>
                {
                    int active = Interlocked.Increment(ref concurrent); maximum = Math.Max(maximum, active);
                    try
                    {
                        if (number == 1) { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); }
                        return new([], 0);
                    }
                    finally { Interlocked.Decrement(ref concurrent); }
                });
            }, _ => Interlocked.Increment(ref presentations), async token =>
            {
                delayEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
            });
            controller.SetActive(true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.SetActive(false); controller.SetActive(true);
            Check(factories == 1 && presentations == 0);
            release.Set();
            await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.SetActive(false); await controller.Completion;
            Check(factories == 2 && maximum == 1 && presentations == 1);
            controller.Dispose(); controller.SetActive(true); Check(factories == 2);
        }));
        run("Refresh waits for completion before delaying, contains collector errors, and stops scheduling while closed", () => WithoutContext(async () =>
        {
            int calls = 0; var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var controller = new InstanceRefreshController(() => new FakeCollector(_ =>
            {
                Interlocked.Increment(ref calls); throw new UnauthorizedAccessException("synthetic-private");
            }), snapshot => { Check(!snapshot.Available); delayed.TrySetResult(); });
            controller.SetActive(true);
            await delayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.SetActive(false); await controller.Completion; Check(calls == 1);
        }));
        run("Refresh interval is awaited after collection and cancellation removes the pending tick", () => WithoutContext(async () =>
        {
            int collections = 0, presentations = 0;
            var delaying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var controller = new InstanceRefreshController(() => new FakeCollector(_ =>
            {
                Interlocked.Increment(ref collections); return new([], 0);
            }), _ => Interlocked.Increment(ref presentations), async token =>
            {
                delaying.TrySetResult(); await tick.Task.WaitAsync(token);
            });
            controller.SetActive(true); await delaying.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(collections == 1 && presentations == 1);
            controller.SetActive(false); await controller.Completion;
            tick.TrySetResult(); Check(collections == 1 && presentations == 1);
        }));
        run("Native instance queries bind creation, owner, session, CPU and memory to the opened process", () =>
        {
            using var current = Process.GetCurrentProcess();
            using var process = new InstanceProcess(current.Id);
            Check(process.Identity.Sid == RobloxLaunchHelper.Program.Sid && process.Identity.Session == current.SessionId);
            Check(process.Identity.Created == current.StartTime.ToUniversalTime().ToFileTimeUtc());
            using var handle = Native.OpenProcess(InstanceNative.QueryLimitedInformation, false, current.Id);
            // Session must come from the opened token even if a caller's PID lookup is stale.
            Check(Native.GetIdentity(handle, int.MaxValue).Session == current.SessionId);
            Check(process.IsAlive && process.CpuTime() is >= 0 && process.WorkingSet() is > 0);
            Check(new InstancePlatform().ProcessorCount > 0);
        });
        run("Native window discovery accepts a minimized fixture and rejects hidden and child windows", () =>
        {
            using var fixture = new Form { Text = "Synthetic native window fixture", Size = new(320, 160) };
            using var child = new Button { Text = "Synthetic child", Dock = DockStyle.Fill };
            fixture.Controls.Add(child); fixture.Show();
            using var process = new InstanceProcess(Environment.ProcessId);
            nint window = fixture.Handle;
            Check(process.OwnsWindow(window) && !process.OwnsWindow(child.Handle));
            Check(process.Windows().Any(w => w.Handle == window && w.Title == fixture.Text));
            fixture.WindowState = FormWindowState.Minimized;
            Check(process.OwnsWindow(window) && process.Windows().Any(w => w.Handle == window));
            fixture.Hide(); Check(!process.OwnsWindow(window));
            fixture.Close(); Check(!process.OwnsWindow(window));
        });
        run("Active panel preserves process/window selection and invalidates removed/reused windows; exports exclude transient data", Render);
    }

    private static void WithoutContext(Func<Task> test)
    {
        var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null);
        try { test().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void Render()
    {
        const string caption = "synthetic-private-caption-cookie=secret";
        using var form = new ActiveInstancesForm(false, new FakePlatform());
        form.Show(); form.PerformLayout();
        _ = form.Handle;
        var list = Descendants(form).OfType<ListView>().Single(); _ = list.Handle;
        var picker = Descendants(form).OfType<ComboBox>().Single();
        var first = new ActiveInstance(new(42, Created), [new(100, caption)], 12.5, 1048576, true);
        var second = first with { Key = new(43, Created + 1), Windows = [new(200, ""), new(201, "")] };
        form.Present(new([first, second], 1)); list.Items[0].Selected = true;
        Check(form.SelectedTarget == Target);
        form.Present(new([second, first], 0)); Check(form.SelectedTarget == Target);
        form.Present(new([second, first with { Windows = [new(101, caption)] }], 0));
        Check(form.SelectedTarget is null); // Never redirect a selection when an HWND disappears.
        form.Present(new([second, first with { Windows = [new(101, caption)] }], 0));
        Check(form.SelectedTarget is null); // Nor on subsequent refreshes without a new user selection.
        list.Items[1].Selected = false; list.Items[0].Selected = true;
        Check(form.SelectedTarget is null && picker.Items.Count == 2); // Multiple windows require explicit choice.
        picker.SelectedIndex = 1; Check(form.SelectedTarget == new InstanceTarget(second.Key, 201));
        form.Present(InstanceSnapshot.Failed);
        Check(list.Items[0].SubItems[5].Text == "Stale");
        form.Present(new([second with { Key = new(43, Created + 2) }], 0)); Check(form.SelectedTarget is null);
        form.Present(new([first, second, first with { Key = new(44, Created + 3), Windows = [], CpuPercent = null,
            CpuAvailable = false, WorkingSet = null }], 1));
        Check(list.Visible && list.Width > 500 && list.Height > 100);
        form.CreateControl();
        string output = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".build");
        Directory.CreateDirectory(output);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "active-instances-preview.png"));
        }
        form.Size = form.MinimumSize;
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "active-instances-narrow.png"));
        }
        form.Scale(new SizeF(1.5f, 1.5f)); form.PerformLayout();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "active-instances-150.png"));
        }
        var health = new HealthSnapshot(1, "1.1.0", "10.0", "X64", DateTimeOffset.UtcNow, [], DiagnosticChecks.NotTested);
        var report = DiagnosticReports.Build(health, [], true, []);
        Check(!Encoding.UTF8.GetString(report.Json).Contains(caption));
    }
    private sealed class FakeCollector(Func<CancellationToken, InstanceSnapshot> collect) : IInstanceCollector
    {
        public InstanceSnapshot Collect(CancellationToken token) => collect(token);
    }
    private sealed class FakePlatform : IInstancePlatform
    {
        internal Identity Identity = Id;
        internal int[] CandidateIds = [42];
        internal bool Trust = true, OpenError, EnumerationError, FocusResult = true, CloseResult = true;
        internal string? ErrorStage;
        internal int Opened, Disposed, Verifications, CounterReads, CloseCalls, FocusCalls;
        internal long? Cpu = 0;
        internal ulong? Memory = 1048576;
        internal Func<bool> Alive = () => true;
        internal Action? OnTrust, OnRestore;
        internal Action<string>? OnRead;
        internal InstanceWindow[] WindowList = [new(100, "fixture")];
        public int[] Candidates() => EnumerationError ? throw new IOException() : CandidateIds;
        public IInstanceProcess Open(int pid)
        {
            if (OpenError) throw new UnauthorizedAccessException();
            Opened++; return new FakeProcess(this);
        }
        public bool Trusted(string image) { Verifications++; OnTrust?.Invoke(); return Trust; }
        public int ProcessorCount => 4;
        private sealed class FakeProcess(FakePlatform owner) : IInstanceProcess
        {
            private void Fail(string stage)
            {
                owner.OnRead?.Invoke(stage);
                if (owner.ErrorStage == stage) throw new IOException("synthetic-private");
            }
            public Identity Identity { get { Fail("identity"); return owner.Identity; } }
            public bool IsAlive { get { Fail("alive"); return owner.Alive(); } }
            public long? CpuTime() { Fail("cpu"); owner.CounterReads++; return owner.Cpu; }
            public ulong? WorkingSet() { Fail("memory"); return owner.Memory; }
            public InstanceWindow[] Windows() { Fail("windows"); return owner.WindowList; }
            public bool OwnsWindow(nint window) => owner.WindowList.Any(w => w.Handle == window);
            public void Restore(nint window) => owner.OnRestore?.Invoke();
            public bool Focus(nint window) { owner.FocusCalls++; return owner.FocusResult; }
            public bool Close(nint window) { owner.CloseCalls++; return owner.CloseResult; }
            public void Dispose() => owner.Disposed++;
        }
    }
}
