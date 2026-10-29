using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using RobloxLaunchHelper;
using CheckState = RobloxLaunchHelper.CheckState;

internal static class DiagnosticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static void Check(bool condition) { if (!condition) throw new Exception("Diagnostic assertion failed."); }
    private static LaunchResult Record(DateTimeOffset? started = null) => new(1, Guid.NewGuid(), started ?? Now, null, null,
        LaunchSource.Desktop, false, LaunchOutcome.InProgress, DiagnosticCode.LaunchStarting, null, 123, 456);
    private static void Store(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RLH-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); } finally { Directory.Delete(dir, true); }
    }
    private static HealthSnapshot Snapshot(params HealthCheckResult[] checks) => new(1, "1.0.1.0", "10.0.12345.0", "X64", Now, checks, DiagnosticChecks.NotTested);
    internal static void Run(Action<string, Action> run)
    {
        run("Dashboard startup separates app opening, setup and explicit Roblox launches", () =>
        {
            Check(Program.SelectStartup([], true) == Program.StartupMode.Dashboard);
            Check(Program.SelectStartup([], false) == Program.StartupMode.Setup);
            foreach (bool installed in new[] { false, true })
                Check(Program.SelectStartup(["--diagnostics"], installed) == Program.StartupMode.Dashboard);
            foreach (string argument in new[] { "--launch-roblox", "roblox:test", "ROBLOX-PLAYER:synthetic" })
            {
                Check(Program.SelectStartup([argument], true) == Program.StartupMode.Roblox);
                Reject([argument], false);
            }
            foreach (string[] arguments in new string[][] { ["--diagnostics", "extra"], ["--launch-roblox", "extra"],
                ["roblox:test", "extra"], ["--unknown"], ["https://www.roblox.com"], [""] })
                Reject(arguments, true);
            static void Reject(string[] arguments, bool installed)
            {
                try { Program.SelectStartup(arguments, installed); }
                catch (InvalidOperationException) { return; }
                throw new Exception("Invalid startup arguments were accepted.");
            }
        });
        run("Dashboard launch dispatch stays responsive and recovers after startup failure", () =>
        {
            int starts = 0, errors = 0;
            var pending = new TaskCompletionSource();
            using var form = new DiagnosticsForm(false, launchProcess: (start, token) =>
            {
                starts++;
                Check(start.FileName == Environment.ProcessPath && !start.UseShellExecute && string.IsNullOrEmpty(start.Verb));
                Check(start.ArgumentList.SequenceEqual(new[] { "--launch-roblox" }));
                if (starts == 1) throw new IOException("synthetic startup failure");
                return pending.Task.WaitAsync(token);
            }, launchError: _ => errors++) { ShowInTaskbar = false, Opacity = 0 };
            form.Show();
            var panel = form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().Single();
            var button = panel.Controls.OfType<Button>().Single(b => b.AccessibleName == "Launch Roblox");
            button.PerformClick();
            Check(starts == 1 && errors == 1 && button.Enabled);
            var launch = form.LaunchRobloxAsync();
            Check(starts == 2 && !button.Enabled);
            Check(panel.Controls.OfType<Button>().Single(b => b.Text.Contains("Check again")).Enabled);
            Check(form.LaunchRobloxAsync().IsCompletedSuccessfully && starts == 2);
            pending.SetResult();
            Pump(launch);
            Check(button.Enabled && errors == 1);
            form.Close();
        });
        run("Closing Diagnostics cancels only the launch wait", () =>
        {
            var child = new TaskCompletionSource();
            CancellationToken waitToken = default;
            int errors = 0;
            using var form = new DiagnosticsForm(false, launchProcess: (_, token) =>
            {
                waitToken = token;
                return child.Task.WaitAsync(token);
            }, launchError: _ => errors++) { ShowInTaskbar = false, Opacity = 0 };
            form.Show();
            var launch = form.LaunchRobloxAsync();
            form.Close();
            Pump(launch);
            Check(waitToken.IsCancellationRequested && !child.Task.IsCompleted && errors == 0);
            child.SetResult();
        });
        run("Diagnostic severity, safe inspection failures and remediation catalog", () =>
        {
            var healthy = DiagnosticChecks.Result("task", CheckState.Healthy, DiagnosticCode.Ready);
            var unknown = healthy with { State = CheckState.Unknown };
            var failed = healthy with { State = CheckState.Failed };
            Check(Snapshot(healthy).Overall == CheckState.Healthy);
            Check(Snapshot(healthy, unknown).Overall == CheckState.Unknown);
            Check(Snapshot(unknown, failed).Overall == CheckState.Failed);
            Check(Snapshot(healthy, failed with { Id = "history" }).Overall == CheckState.Healthy);
            Check(Snapshot(healthy).WorkerTest.State == CheckState.NotRun);
            var denied = DiagnosticChecks.Inspect("task", () => throw new UnauthorizedAccessException("private-path-canary"));
            Check(denied.State == CheckState.Unknown && denied.Code == DiagnosticCode.InspectionUnavailable && denied.Action == Remediation.Refresh);
            foreach (var code in Enum.GetValues<DiagnosticCode>()) Check(DiagnosticMessages.Text(code) != "Unknown diagnostic result.");
            Check(DiagnosticChecks.ProtocolMatches(Installation.WrapperCommand));
            Check(!DiagnosticChecks.ProtocolMatches("private-command-canary"));
        });
        run("Diagnostic configuration and player failures have specific actionable results", () =>
        {
            Check(DiagnosticChecks.ConfigStatus(null) == DiagnosticCode.InvalidConfig);
            Check(DiagnosticChecks.ConfigStatus(new InstallConfig()) == DiagnosticCode.InvalidConfig);
            var config = new InstallConfig { OwnerSid = RobloxLaunchHelper.Program.Sid, Commands = Installation.Schemes.ToDictionary(s => s, _ => "\"C:\\Roblox\\RobloxPlayerBeta.exe\" %1") };
            Check(DiagnosticChecks.ConfigStatus(config) == DiagnosticCode.Ready);
            config.Version = 999; Check(DiagnosticChecks.ConfigStatus(config) == DiagnosticCode.InvalidConfig); config.Version = 1;
            config.OwnerSid = "S-1-5-18"; Check(DiagnosticChecks.ConfigStatus(config) == DiagnosticCode.WrongAccount);
            Check(DiagnosticChecks.CheckPlayer("player-roblox", "cmd.exe /c secret", _ => true, _ => true).Code == DiagnosticCode.InvalidConfig);
            string command = config.Commands["roblox-player"];
            var missing = DiagnosticChecks.CheckPlayer("player-roblox-player", command, _ => false, _ => throw new Exception("Must not test a missing file"));
            Check(missing.State == CheckState.Failed && missing.Code == DiagnosticCode.PlayerMissing && missing.Action == Remediation.ReinstallRoblox);
            Check(DiagnosticChecks.CheckPlayer("player-roblox-player", command, _ => true, _ => false).Code == DiagnosticCode.SignatureRejected);
            Check(DiagnosticChecks.CheckPlayer("player-roblox-player", command, _ => true, _ => true).State == CheckState.Healthy);
        });
        run("History finalizes once and does not recreate cleared attempts", () => Store(dir =>
        {
            var history = new DiagnosticHistory(dir, () => Now, _ => true);
            var record = Record(); Check(history.Begin(record));
            var final = record with { Outcome = LaunchOutcome.Confirmed, Code = DiagnosticCode.LaunchConfirmed, RobloxStarted = true, EndedUtc = Now.AddSeconds(1), DurationMs = 1000 };
            Check(history.Finish(final)); Check(history.Finish(final with { Outcome = LaunchOutcome.Failed }));
            Check(history.Read(out var rows) && rows.Length == 1 && rows[0].Outcome == LaunchOutcome.Confirmed && rows[0].OwnerPid == 0);
            Check(history.Clear()); Check(history.Finish(final)); Check(history.Read(out rows) && rows.Length == 0);
        }));
        run("History enforces age, count and byte bounds and preserves distinct attempts", () => Store(dir =>
        {
            Exception? failure = null;
            var history = new DiagnosticHistory(dir, () => Now, _ => true, e => failure = e);
            void Begin(LaunchResult record)
            {
                for (int retry = 0; retry < 10; retry++)
                {
                    if (history.Begin(record)) return;
                    Thread.Sleep(10); // The production store deliberately drops writes on transient I/O failures.
                }
                throw new Exception("Repeated history write failure", failure);
            }
            Begin(Record(Now.AddDays(-31)));
            for (int i = 0; i < 105; i++) Begin(Record(Now.AddSeconds(-i)));
            Check(history.Read(out var rows) && rows.Length == 100 && rows.All(r => r.StartedUtc >= Now.AddDays(-30)));
            Check(new FileInfo(Path.Combine(dir, "history.json")).Length <= DiagnosticHistory.MaxBytes);
            var evicted = Record(Now.AddDays(-2)); Check(history.Begin(evicted)); Check(history.Finish(evicted with { Outcome = LaunchOutcome.Confirmed }));
            Check(history.Read(out rows) && !rows.Any(r => r.AttemptId == evicted.AttemptId));
            Check(!history.Begin(Record() with { SchemaVersion = 99 }));
        }));
        run("History detects interruption without inventing a successful completion", () => Store(dir =>
        {
            var history = new DiagnosticHistory(dir, () => Now, _ => false);
            Check(history.Begin(Record())); Check(history.Read(out var rows));
            Check(rows.Single().Outcome == LaunchOutcome.Incomplete && rows[0].Code == DiagnosticCode.Interrupted && rows[0].EndedUtc is null && rows[0].RobloxStarted is null);
        }));
        run("History corruption, unsupported schema and oversized input fail safely; clear recovers", () => Store(dir =>
        {
            var history = new DiagnosticHistory(dir, () => Now);
            string path = Path.Combine(dir, "history.json");
            foreach (string invalid in new[] { "{broken", "null", "[null]", JsonSerializer.Serialize(new[] { Record() with { SchemaVersion = 5 } }), new string('x', DiagnosticHistory.MaxBytes + 1) })
            {
                File.WriteAllText(path, invalid); Check(!history.Read(out _)); Check(!history.Begin(Record()));
                Check(history.Clear()); Check(history.Read(out var rows) && rows.Length == 0);
            }
        }));
        run("History concurrent writers retain successful writes and bounded snapshots", () => Store(dir =>
        {
            var records = Enumerable.Range(0, 16).Select(_ => Record()).ToArray();
            var results = new bool[records.Length];
            Parallel.For(0, records.Length, i => results[i] = new DiagnosticHistory(dir, () => Now, _ => true).Begin(records[i]));
            var history = new DiagnosticHistory(dir, () => Now, _ => true);
            Check(history.Read(out var rows)); Check(rows.Length == results.Count(r => r));
            Check(rows.Select(r => r.AttemptId).Distinct().Count() == rows.Length);
        }));
        run("History rejects redirected file and directory storage", () => Store(dir =>
        {
            string target = Path.Combine(dir, "target"); Directory.CreateDirectory(target);
            string link = Path.Combine(dir, "link");
            // Junctions do not require developer mode or elevation.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/c", "mklink", "/J", link, target } })!;
            process.WaitForExit(); Check(process.ExitCode == 0);
            try { Check(!new DiagnosticHistory(link, () => Now).Begin(Record())); Check(!File.Exists(Path.Combine(target, "history.json"))); }
            finally { Directory.Delete(link); }
        }));
        run("Read-only effective ACL inspection recognizes protected and writable paths", () =>
        {
            if (RobloxLaunchHelper.Program.Elevated) return; // Checks require a filtered token; covered manually if this test host is elevated.
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            var safe = new RawSecurityDescriptor($"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;{sid})");
            var writable = new RawSecurityDescriptor($"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{sid})");
            Check(DiagnosticAccess.Protected(safe)); Check(!DiagnosticAccess.Protected(writable));
            Check(!DiagnosticAccess.Protected(new RawSecurityDescriptor("O:BAG:BAD:NO_ACCESS_CONTROL")));
        });
        run("Report serialization rejects privacy canaries including unknown fields and log details", () => Store(dir =>
        {
            const string canary = @"C:\Users\PrivateCanary\S-1-5-21-private roblox-player:ticket:secret https://private.invalid cookie=secret --command private-account";
            File.WriteAllText(Path.Combine(dir, "helper.log"), $"{Now:O} launch-requested {canary}\n{Now:O} unknown-canary {canary}\n{Now:O} setup-rollback-stack {canary}\n{Now:O} singleton-closed {canary}\n");
            var snapshot = Snapshot(DiagnosticChecks.Result(canary, CheckState.Unknown, DiagnosticCode.InspectionUnavailable)) with { HelperVersion = canary, WindowsVersion = canary, Architecture = canary, RobloxVersion = canary };
            var record = Record();
            var package = DiagnosticReports.Build(snapshot, [record], true, [(canary, dir)]);
            string json = Encoding.UTF8.GetString(package.Json);
            foreach (string secret in new[] { "PrivateCanary", "private-account", "cookie=", "secret", "S-1-5-21", "private.invalid", "OwnerPid", "OwnerCreated", "AttemptId", record.AttemptId.ToString() })
                Check(!json.Contains(secret) && !package.Summary.Contains(secret));
            Check(package.EventCount == 2 && package.LaunchCount == 1);
            Check(json.Contains("launch-requested") && !json.Contains("unknown-canary"));
            string output = Path.Combine(dir, "report.zip"); DiagnosticReports.Write(output, package);
            using var zip = ZipFile.OpenRead(output);
            Check(zip.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "diagnostics.json", "summary.txt" }));
            Check(zip.Entries.Sum(e => e.Length) <= DiagnosticReports.MaxReportBytes);
            foreach (var entry in zip.Entries) { using var reader = new StreamReader(entry.Open()); Check(!reader.ReadToEnd().Contains("PrivateCanary")); }
        }));
        run("Report log input and event output stay bounded; partial export remains valid", () => Store(dir =>
        {
            File.WriteAllText(Path.Combine(dir, "helper.log"), string.Concat(Enumerable.Repeat($"{Now:O} launch-requested {new string('x', 1000)}\n", 600)));
            var package = DiagnosticReports.Build(Snapshot(), [], false, [("launcher", dir), ("worker", Path.Combine(dir, "missing"))]);
            Check(package.EventCount == 200);
            string json = Encoding.UTF8.GetString(package.Json);
            Check(json.Contains("log-truncated") && json.Contains("history-unavailable") && json.Contains("worker-log-unavailable"));
            bool threw = false;
            try { DiagnosticReports.Write(Path.Combine(dir, "too-large.zip"), new("", new byte[DiagnosticReports.MaxReportBytes + 1], 0, 0)); } catch (IOException) { threw = true; }
            Check(threw && !File.Exists(Path.Combine(dir, "too-large.zip")));
            Check(!Directory.EnumerateFiles(dir, "*.tmp").Any());
        }));
        run("Canceled worker test cannot start a scheduled task", () =>
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            bool threw = false;
            try { using var pipe = Launch.ConnectAsync(canceled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { threw = true; }
            Check(threw);
        });
        run("Dashboard renders health, worker test and recent results without executing checks", () =>
        {
            Application.EnableVisualStyles();
            using var form = new DiagnosticsForm(false) { ShowInTaskbar = false, Opacity = 0 };
            var snapshot = Snapshot(
                DiagnosticChecks.Result("installation", CheckState.Healthy, DiagnosticCode.Ready),
                DiagnosticChecks.Result("protocol-roblox-player", CheckState.Failed, DiagnosticCode.ProtocolMismatch, Remediation.Repair),
                DiagnosticChecks.Result("history", CheckState.Healthy, DiagnosticCode.Ready)) with { BrowserIntegrationEnabled = false };
            form.Present(snapshot, [Record() with { Outcome = LaunchOutcome.Confirmed, Code = DiagnosticCode.LaunchConfirmed, RobloxStarted = true },
                Record(Now.AddMinutes(-1)) with { Outcome = LaunchOutcome.Warning, Code = DiagnosticCode.WorkerTimeout, RobloxStarted = true }], true);
            form.Show(); form.PerformLayout();
            var lists = form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<ListView>().ToArray();
            Check(lists[0].Items.Count == 4 && lists[1].Items.Count == 2);
            var toggle = form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>()
                .Single(b => b.Text.Contains("browser integration"));
            Check(toggle.Enabled && toggle.Text == "Enable browser integration");
            form.Present(snapshot with { BrowserIntegrationEnabled = true }, [], true);
            Check(toggle.Enabled && toggle.Text == "Disable browser integration");
            string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".build")); Directory.CreateDirectory(output);
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, "dashboard-preview.png"));
            form.Scale(new SizeF(1.5f, 1.5f));
            using var scaled = new Bitmap(form.Width, form.Height); form.DrawToBitmap(scaled, new Rectangle(Point.Empty, form.Size));
            scaled.Save(Path.Combine(output, "dashboard-preview-150.png"));
        });
    }
    private static void Pump(Task task)
    {
        var deadline = Stopwatch.StartNew();
        while (!task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(40))
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
