using System.Diagnostics;

namespace RobloxLaunchHelper;

internal sealed class NotificationSettingsPage : WorkspacePage
{
    internal NotificationSettingsPage(NotificationPreferences? preferences = null)
    {
        var store = preferences ?? new NotificationPreferences();
        Text = "Launch notifications"; MinimumSize = new(0, 300);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(14) };
        var progress = new CheckBox { Text = "Show launch progress", AutoSize = true, Checked = store.Settings.Progress };
        var completion = new CheckBox { Text = "Show confirmed helper completion", AutoSize = true, Checked = store.Settings.Completion };
        root.Controls.Add(progress); root.Controls.Add(completion);
        root.Controls.Add(new Label { AutoSize = true, MaximumSize = new(480, 0), Text = "Both options are off by default. Warnings and failures still appear.\n" + LaunchStatusText.Limit + "\nNotifications run temporarily; tray mode and startup are not enabled." });
        var error = new Label { AutoSize = true, MaximumSize = new(480, 0), Text = store.Available ? "" : "Saved notification settings are unavailable. Reset them explicitly to enable changes." };
        var save = new WorkspaceButton { Text = "Save", AutoSize = true, Enabled = store.Available };
        var reset = new WorkspaceButton { Text = "Reset saved settings…", AutoSize = true };
        save.Click += (_, _) =>
        {
            try { store.Save(new(1, progress.Checked, completion.Checked)); error.Text = "Notification preferences saved."; }
            catch { error.Text = "Settings could not be saved. Check folder access or reset saved settings."; }
        };
        reset.Click += async (_, _) =>
        {
            if (!await WorkspacePrompt.ConfirmAsync(this, "Replace saved notification settings with both options off?", "Reset notifications")) return;
            try { store.Save(new(), reset: true); progress.Checked = completion.Checked = false; save.Enabled = true; error.Text = "Settings reset."; }
            catch { error.Text = "Settings could not be reset. Check folder access."; }
        };
        var actions = new FlowLayoutPanel { AutoSize = true }; actions.Controls.Add(save); actions.Controls.Add(reset);
        root.Controls.Add(actions); root.Controls.Add(error); Controls.Add(root);
    }
}

internal sealed class LaunchProgressForm : Form
{
    private readonly TextBox text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Launch status" };
    internal Action? Dismissed;
    private bool closing;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80; return value; }
    }
    internal LaunchProgressForm()
    {
        Text = "Roblox launch status"; Size = new(530, 310); MinimumSize = new(390, 240);
        AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea; Location = new(area.Right - Width - 16, area.Bottom - Height - 16);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var diagnostics = new WorkspaceButton { Text = "Open Diagnostics", AutoSize = true };
        diagnostics.Click += (_, _) =>
        {
            try { var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; start.ArgumentList.Add("--diagnostics"); Process.Start(start)?.Dispose(); }
            catch { text.Text = "Diagnostics could not be opened. Open it from the Start menu."; }
        };
        var dismiss = new WorkspaceButton { Text = "Dismiss", AutoSize = true }; dismiss.Click += (_, _) => Close();
        buttons.Controls.Add(diagnostics); buttons.Controls.Add(dismiss); Controls.Add(text); Controls.Add(buttons);
        FormClosing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); Dismissed?.Invoke(); } };
    }
    internal void Present(string message) { if (text.Text != message) text.Text = message; if (!Visible) Show(); }
    internal void Shutdown() { closing = true; Close(); }
}

// A separate, on-demand UI loop. Never owns the worker or the manager/tray lifetime.
internal sealed class LaunchNotificationHost : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly LaunchNotificationState state = new(() => Environment.TickCount64);
    private readonly LaunchProgressForm progress = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly NotificationServer server;
    private readonly ICompletionNotification completion;
    private NotificationSettings settings = new();
    private long idleSince = Environment.TickCount64, lastSettings, lastExpiry, resultUntil;
    private string? warningText;
    private bool refreshing, disposed;
    internal LaunchNotificationHost(ICompletionNotification? completion = null)
    {
        this.completion = completion ?? new CompletionNotification();
        _ = dispatcher.Handle;
        settings = new NotificationPreferences().Settings;
        progress.Dismissed = () => { state.Dismiss(); warningText = null; resultUntil = 0; };
        this.completion.Clicked += () =>
        {
            try { var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; start.ArgumentList.Add("--diagnostics"); Process.Start(start)?.Dispose(); } catch { }
        };
        server = new NotificationServer(status =>
        {
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (disposed) { accepted.TrySetResult(false); return; }
                    bool ok = state.Accept(status, settings);
                    if (ok) idleSince = Environment.TickCount64;
                    accepted.TrySetResult(ok);
                });
            }
            catch { accepted.TrySetResult(false); }
            return accepted.Task;
        });
        timer.Tick += (_, _) => Tick(); timer.Start();
        ReloadSettings();
    }
    private async void ReloadSettings()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var next = await Task.Run(() => new NotificationPreferences().Settings);
            if (disposed) return;
            if (!next.Completion) completion.Hide();
            settings = next;
        }
        finally { refreshing = false; }
    }
    private static bool Alive(LaunchStatus status)
    {
        try { return Native.GetIdentity(status.OwnerPid).Created == status.OwnerCreated; }
        catch { return false; }
    }
    private void Tick()
    {
        long now = Environment.TickCount64;
        if (now - lastSettings >= 1000) { lastSettings = now; ReloadSettings(); }
        if (now - lastExpiry >= 1000) { lastExpiry = now; state.Expire(Alive, settings); }
        var popup = state.TakePopup(settings);
        if (popup is { } notice)
        {
            resultUntil = now + 10000; idleSince = now;
            if (notice.Warning) warningText = notice.Text;
            else if (settings.Completion)
            {
                completion.Show(notice.Text);
            }
        }
        bool showProgress = settings.Progress && state.ProgressReady;
        if (warningText is not null && now >= resultUntil) warningText = null;
        if (showProgress || warningText is not null)
        {
            var lines = new List<string>();
            if (warningText is not null) lines.Add(warningText);
            if (showProgress)
            {
                lines.Add($"{state.Active} launch attempts in progress");
                int number = 0;
                foreach (var status in state.Statuses) lines.Add($"Attempt {++number}: " + LaunchStatusText.Text(status.Outcome, status.Code, status.RobloxStarted));
            }
            if (state.Overflow > 0) lines.Add("Additional status updates could not be accepted. Review recent launches in Diagnostics.");
            lines.Add(LaunchStatusText.Limit); progress.Present(string.Join("\r\n\r\n", lines));
        }
        else progress.Hide();
        if (state.Active > 0 || state.HasPending) idleSince = now;
        if (state.Active == 0 && !state.HasPending && now - idleSince >= 10000 && now >= resultUntil) ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; timer.Stop(); timer.Dispose(); server.Dispose();
            completion.Dispose(); progress.Shutdown(); progress.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
    internal static int Run()
    {
        if (Program.Elevated) return 2;
        using var guard = new Mutex(false, @"Local\" + NotificationWire.Name);
        bool held;
        try { held = guard.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) return 0;
        try { Application.EnableVisualStyles(); using var host = new LaunchNotificationHost(); Application.Run(host); return 0; }
        finally { guard.ReleaseMutex(); }
    }
}
