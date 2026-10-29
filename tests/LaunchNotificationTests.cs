using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RobloxLaunchHelper;

internal static class LaunchNotificationTests
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Launch notification assertion failed."); }
    private static LaunchStatus Status(LaunchOutcome outcome = LaunchOutcome.InProgress, int revision = 1) => new(Guid.NewGuid(), revision, 123, 456,
        outcome, outcome switch { LaunchOutcome.Confirmed => DiagnosticCode.LaunchConfirmed, LaunchOutcome.Warning => DiagnosticCode.WorkerTimeout,
            LaunchOutcome.Failed => DiagnosticCode.ProcessStartFailed, LaunchOutcome.Incomplete => DiagnosticCode.Interrupted, _ => DiagnosticCode.LaunchStarting },
        outcome is LaunchOutcome.Confirmed or LaunchOutcome.Warning ? true : null);
    private static void Store(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RLH-notifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); } finally { Directory.Delete(dir, true); }
    }
    internal static void Run(Action<string, Action> run)
    {
        run("Notification defaults and settings combinations round-trip; invalid data requires explicit reset", () => Store(dir =>
        {
            string path = Path.Combine(dir, "settings.json"); var preferences = new NotificationPreferences(path);
            Check(preferences.Available && !preferences.Settings.Progress && !preferences.Settings.Completion);
            foreach (bool progress in new[] { false, true }) foreach (bool completion in new[] { false, true })
            {
                preferences.Save(new(1, progress, completion)); preferences.Reload();
                Check(preferences.Settings.Progress == progress && preferences.Settings.Completion == completion);
            }
            foreach (string invalid in new[] { "{", "null", "{\"Version\":99}", "{\"RawLink\":\"synthetic-ticket\"}", new string('x', 4097) })
            {
                File.WriteAllText(path, invalid); preferences.Reload(); Check(!preferences.Available && preferences.Settings == new NotificationSettings());
                bool rejected = false; try { preferences.Save(new()); } catch (IOException) { rejected = true; }
                Check(rejected && File.ReadAllText(path) == invalid); preferences.Save(new(), reset: true); Check(preferences.Available);
            }
        }));
        run("Shared launch wording separates player startup, helper confirmation, unknown completion and failure", () =>
        {
            Check(LaunchStatusText.Text(LaunchOutcome.InProgress, DiagnosticCode.LaunchStarting, false) == "Starting Roblox…");
            Check(LaunchStatusText.Text(LaunchOutcome.InProgress, DiagnosticCode.LaunchStarting, true).Contains("player started"));
            Check(LaunchStatusText.Text(LaunchOutcome.Confirmed, DiagnosticCode.LaunchConfirmed, true).Contains("singleton release confirmed"));
            Check(LaunchStatusText.Text(LaunchOutcome.Warning, DiagnosticCode.WorkerUnavailable, true).Contains("completion unconfirmed"));
            Check(LaunchStatusText.Text(LaunchOutcome.Incomplete, DiagnosticCode.Interrupted, null).Contains("completion unknown"));
            Check(!LaunchStatusText.Text(LaunchOutcome.Failed, DiagnosticCode.LaunchFailed, null).Contains("could not start"));
            Check(!(Status(LaunchOutcome.Confirmed) with { RobloxStarted = false }).Valid);
        });
        run("Overlapping launches deduplicate revisions, reject regression and finish independently", () =>
        {
            long now = 0; var state = new LaunchNotificationState(() => now); var settings = new NotificationSettings(1, true, true);
            var a = Status(); var b = Status(); Check(state.Accept(a, settings) && state.Accept(b, settings));
            now = 749; Check(!state.ProgressReady); now = 750; Check(state.ProgressReady && state.Active == 2);
            var started = a with { Revision = 2, RobloxStarted = true }; Check(state.Accept(started, settings));
            Check(!state.Accept(a with { Revision = 3 }, settings));
            Check(state.Accept(b with { Revision = 2, Outcome = LaunchOutcome.Warning, Code = DiagnosticCode.WorkerTimeout, RobloxStarted = true }, settings));
            var done = started with { Revision = 3, Outcome = LaunchOutcome.Confirmed, Code = DiagnosticCode.LaunchConfirmed };
            Check(state.Accept(done, settings) && state.Accept(done, settings)); Check(state.Accept(a, settings)); Check(state.Active == 0);
            now = 2750; var popup = state.TakePopup(settings); Check(popup is { Warning: true } && popup.Value.Text.Contains("1 helper completions confirmed") && popup.Value.Text.Contains("1 completions unconfirmed"));
            Check(state.TakePopup(settings) is null);
        });
        run("Notification preference combinations preserve warnings while routine success stays silent by default", () =>
        {
            foreach (bool progress in new[] { false, true }) foreach (bool completion in new[] { false, true })
            {
                long now = 0; var state = new LaunchNotificationState(() => now); var settings = new NotificationSettings(1, progress, completion);
                state.Accept(Status(LaunchOutcome.Confirmed), settings); now = 2000;
                Check((state.TakePopup(settings) is not null) == completion);
                state.Accept(Status(LaunchOutcome.Failed), settings); now = 12000;
                Check(state.TakePopup(settings) is { Warning: true });
            }
        });
        run("Popup rate limiting produces a trailing summary during continuous launches and drops disabled successes", () =>
        {
            long now = 0; var state = new LaunchNotificationState(() => now); var settings = new NotificationSettings(1, true, true);
            state.Accept(Status(LaunchOutcome.Confirmed), settings); now = 2000; Check(state.TakePopup(settings) is not null);
            for (int i = 0; i < 9; i++) { now = 2100 + i * 1000; state.Accept(Status(LaunchOutcome.Confirmed), settings); Check(state.TakePopup(settings) is null); }
            var active = Status(); state.Accept(active, settings); now = 12000;
            var trailing = state.TakePopup(settings); Check(trailing is { Warning: false } && trailing.Value.Text.Contains("9 helper completions confirmed") && trailing.Value.Text.Contains("1 still in progress"));
            state.Accept(Status(LaunchOutcome.Confirmed), settings); now = 24000;
            Check(state.TakePopup(settings with { Completion = false }) is null && !state.HasPending);
        });
        run("Dismissal covers the active group; interruption and stale owners never become confirmed", () =>
        {
            long now = 0; var state = new LaunchNotificationState(() => now); var settings = new NotificationSettings(1, true, true);
            var a = Status() with { RobloxStarted = true }; state.Accept(a, settings); state.Dismiss(); state.Accept(Status(), settings);
            now = 2000; Check(!state.ProgressReady); state.Expire(_ => false, settings); Check(state.Active == 0);
            now = 4000; var popup = state.TakePopup(settings); Check(popup is { Warning: true } && popup.Value.Text.Contains("completion unknown") && !popup.Value.Text.Contains("confirmed"));
            state.Accept(Status(), settings); now = 4750; Check(state.ProgressReady);
            now = 125000; state.Expire(_ => true, settings); Check(state.Active == 0);
        });
        run("Reducer bounds capacity and recovers it during sustained activity", () =>
        {
            long now = 0; var state = new LaunchNotificationState(() => now); var settings = new NotificationSettings();
            for (int i = 0; i < 100; i++) Check(state.Accept(Status(LaunchOutcome.Confirmed), settings));
            Check(!state.Accept(Status(), settings) && state.Overflow == 1 && state.Statuses.Count() == 100);
            now = 15000; state.Expire(_ => true, settings); Check(state.Accept(Status(), settings));
        });
        run("Launch finalization publishes once, separates provisional errors and survives history loss", () => Store(dir =>
        {
            var messages = new List<LaunchStatus>();
            var publisher = new LaunchNotificationPublisher((status, _) => { lock (messages) messages.Add(status); return Task.FromResult(true); });
            var history = new DiagnosticHistory(dir, alive: _ => true);
            using var attempt = new LaunchAttempt(null, history, publisher);
            attempt.SetFallback(LaunchOutcome.Failed, DiagnosticCode.SignatureRejected); attempt.Started();
            attempt.SetFallback(LaunchOutcome.Warning, DiagnosticCode.CompletionUnconfirmed);
            Check(history.Clear()); // Cleared history never comes back; live final status still arrives.
            attempt.Complete(LaunchOutcome.Confirmed, DiagnosticCode.LaunchConfirmed);
            Check(attempt.FinishAsync().GetAwaiter().GetResult()); Check(attempt.FinishAsync().GetAwaiter().GetResult());
            lock (messages)
            {
                Check(messages.Count(m => m.Terminal) == 1 && messages.Last().Outcome == LaunchOutcome.Confirmed);
                Check(messages.Where(m => !m.Terminal).All(m => m.Outcome == LaunchOutcome.InProgress));
            }
            Check(history.Read(out var rows) && rows.Length == 0);
        }));
        run("Terminal notification preempts stalled progress delivery; delivery failure is acknowledged as failure", () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var publisher = new LaunchNotificationPublisher(async (status, token) =>
            {
                if (!status.Terminal) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                return status.Terminal;
            });
            publisher.Publish(Status()); entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Check(publisher.FinishAsync(Status(LaunchOutcome.Warning)).GetAwaiter().GetResult());
            using var failed = new LaunchNotificationPublisher((_, _) => throw new IOException());
            Check(!failed.FinishAsync(Status(LaunchOutcome.Failed)).GetAwaiter().GetResult());
        });
        run("Presentation wire rejects oversized, truncated, unknown and inconsistent fields without private data", () =>
        {
            using var valid = new MemoryStream(); var status = Status(LaunchOutcome.Confirmed);
            NotificationWire.WriteAsync(valid, status, default).GetAwaiter().GetResult(); valid.Position = 0;
            Check(NotificationWire.ReadAsync(valid, default).GetAwaiter().GetResult() == status);
            foreach (var bytes in new[] { BitConverter.GetBytes(2049), BitConverter.GetBytes(-1), new byte[] { 1 },
                Frame("{\"RawLink\":\"synthetic-ticket-canary\"}"), Frame(JsonSerializer.Serialize(status with { RobloxStarted = false })) })
            {
                bool rejected = false;
                try { NotificationWire.ReadAsync(new MemoryStream(bytes), default).GetAwaiter().GetResult(); } catch { rejected = true; }
                Check(rejected);
            }
            var identity = new Identity(RobloxLaunchHelper.Program.Sid, Process.GetCurrentProcess().SessionId, false, Environment.ProcessPath!, 1);
            Check(NotificationWire.IsPeer(identity)); Check(!NotificationWire.IsPeer(identity with { Elevated = true }));
            Check(!NotificationWire.IsPeer(identity with { Session = identity.Session + 1 }));
            Check(!NotificationWire.IsPeer(identity with { Sid = "S-1-5-18" })); Check(!NotificationWire.IsPeer(identity with { Image = "untrusted.exe" }));
            string json = JsonSerializer.Serialize(status);
            Check(!json.Contains("synthetic-ticket-canary") && !json.Contains("Image") && !json.Contains("Source") && !json.Contains("ErrorCode"));
        });
        run("Notification IPC authenticates actual peers, binds owner lifetime, acknowledges acceptance and drains", () =>
        {
            if (RobloxLaunchHelper.Program.Elevated) return; // Normal-token acceptance is also in the manual checklist.
            Task.Run(async () =>
            {
                string name = "RLH-notification-test-" + Guid.NewGuid().ToString("N");
                var state = new LaunchNotificationState(() => 0); object gate = new();
                using var server = new NotificationServer(status => { lock (gate) return Task.FromResult(state.Accept(status, new(1, true, true))); }, name);
                var status = Status(LaunchOutcome.Warning) with { OwnerPid = Environment.ProcessId, OwnerCreated = Native.GetIdentity(Environment.ProcessId).Created };
                async Task<string> Send(LaunchStatus value)
                {
                    using var timeout = new CancellationTokenSource(3000);
                    using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(timeout.Token);
                    Check(Native.GetNamedPipeServerProcessId(client.SafePipeHandle, out int pid) && NotificationWire.IsPeer(Native.GetIdentity(pid)));
                    await NotificationWire.WriteAsync(client, value, timeout.Token);
                    return await Protocol.ReadAsync(client, timeout.Token);
                }
                Check(await Send(status) == "ACCEPTED"); Check(await Send(status) == "ACCEPTED");
                lock (gate) Check(state.Statuses.Count() == 1);
                bool rejected = false;
                try { await Send(status with { AttemptId = Guid.NewGuid(), OwnerCreated = status.OwnerCreated + 1 }); } catch (IOException) { rejected = true; }
                Check(rejected);
                server.Dispose(); await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }).GetAwaiter().GetResult();
        });
        run("Notification settings and progress surfaces render with safe defaults and bounded text", () => Store(dir =>
        {
            using var settings = new NotificationSettingsForm(new NotificationPreferences(Path.Combine(dir, "settings.json")));
            settings.Show(); Application.DoEvents();
            var options = settings.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().ToArray();
            Check(options.Length == 2 && options.All(c => !c.Checked));
            using var image = new Bitmap(settings.Width, settings.Height); settings.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".build")); Directory.CreateDirectory(output);
            image.Save(Path.Combine(output, "notification-settings-preview.png")); settings.Close();
            using var progress = new LaunchProgressForm();
            progress.Present("1 helper completion confirmed; 1 completion unconfirmed; 2 still in progress.\r\n\r\n" + LaunchStatusText.Limit);
            Application.DoEvents();
            using var progressImage = new Bitmap(progress.Width, progress.Height); progress.DrawToBitmap(progressImage, new Rectangle(Point.Empty, progressImage.Size));
            progressImage.Save(Path.Combine(output, "launch-progress-preview.png"));
            bool dismissed = false; progress.Dismissed = () => dismissed = true; progress.Close();
            Check(dismissed && !progress.Visible && !progress.IsDisposed); progress.Shutdown();
        }));
        run("Passive completion requests no sound, discards stale balloons, preserves disclaimer and retracts on hide", () =>
        {
            var calls = new List<(uint Operation, CompletionNotification.IconData Data)>();
            using var notification = new CompletionNotification((uint operation, ref CompletionNotification.IconData data) =>
            { calls.Add((operation, data)); return true; });
            Check(notification.Show(new string('x', 1000)));
            var balloon = calls.Single(c => c.Operation == 1).Data;
            Check((balloon.InfoFlags & 0x10) != 0 && (balloon.InfoFlags & 0x80) != 0 && (balloon.Flags & 0x40) != 0);
            Check(balloon.Info.Length < 256 && balloon.Info.EndsWith(LaunchStatusText.Limit));
            notification.Hide(); Check(calls[^2].Data.Info == "" && calls[^1].Operation == 2);
        });
        run("Default success never requests a host; opting out mid-launch still delivers its final state", () =>
        {
            Task.Run(async () =>
            {
                int requests = 0;
                using (var silent = new LaunchNotificationPublisher(settings: () => new(), requestHost: () => requests++))
                    Check(!await silent.FinishAsync(Status(LaunchOutcome.Confirmed)) && requests == 0);
                if (RobloxLaunchHelper.Program.Elevated) return;
                string name = "RLH-optout-test-" + Guid.NewGuid().ToString("N");
                var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var messages = new List<LaunchStatus>();
                using var server = new NotificationServer(status =>
                {
                    lock (messages) messages.Add(status);
                    if (!status.Terminal) began.TrySetResult(); return Task.FromResult(true);
                }, name);
                var settings = new NotificationSettings(1, true, true);
                using var publisher = new LaunchNotificationPublisher(settings: () => settings, pipeName: name, requestHost: () => requests++);
                var status = Status() with { OwnerPid = Environment.ProcessId, OwnerCreated = Native.GetIdentity(Environment.ProcessId).Created };
                publisher.Publish(status); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
                int beforeOptOut = requests;
                settings = new();
                Check(await publisher.FinishAsync(status with { Revision = 3, Outcome = LaunchOutcome.Confirmed, Code = DiagnosticCode.LaunchConfirmed, RobloxStarted = true }));
                lock (messages) Check(messages.Count == 2 && messages[^1].Outcome == LaunchOutcome.Confirmed);
                Check(requests == beforeOptOut); server.Dispose(); await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }).GetAwaiter().GetResult();
        });
    }
    private static byte[] Frame(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json); return [.. BitConverter.GetBytes(bytes.Length), .. bytes];
    }
}
