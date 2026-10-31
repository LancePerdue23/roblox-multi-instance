using System.Diagnostics;
using System.Net.Http;

namespace RobloxLaunchHelper;

internal static class UpdateHost
{
    private static string Key => @"Local\RobloxLaunchHelper-Updates-" + Program.Sid + "-" + Process.GetCurrentProcess().SessionId;
    internal static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal static void TryStartAutomatic()
    {
        if (Program.Elevated) return;
        try
        {
            if (new WindowsBootIdentity().Read() is not { } boot ||
                !new UpdateAttemptStore(UpdateFiles.Root, @"Global\RobloxLaunchHelper-UpdateAttempt-" + Program.Sid).TryClaim(boot)) return;
            Start("--check-updates");
        }
        catch { /* Update checking must never prevent startup. */ }
    }
    private static void Start(string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add(mode); Process.Start(start)?.Dispose();
    }
    internal static void OpenManual() => Start("--updates");
    internal static int Run(bool automatic)
    {
        if (Program.Elevated) return 2;
        using var mutex = new Mutex(false, Key);
        bool held;
        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held)
        {
            if (!automatic) { try { using var signal = EventWaitHandle.OpenExisting(Key + "-Show"); signal.Set(); } catch { } }
            return 0;
        }
        try
        {
            Application.EnableVisualStyles();
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Key + "-Show");
            using var http = CreateClient();
            using var host = new UpdateApplicationContext(automatic, signal, new GitHubReleaseSource(http), new UpdateVersionProvider(),
                new UpdateDownloader(http, UpdateFiles.Staging), new UpdateSetupHandoff());
            Application.Run(host); return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }
}

internal sealed class UpdateApplicationContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly CancellationTokenSource closed = new();
    private readonly EventWaitHandle signal;
    private readonly IReleaseSource source;
    private readonly IUpdateVersionProvider versions;
    private readonly IUpdateDownloader downloader;
    private readonly IUpdateSetupHandoff handoff;
    private UpdateForm? form;
    private bool automatic, checking, disposed;
    internal UpdateApplicationContext(bool automatic, EventWaitHandle signal, IReleaseSource source, IUpdateVersionProvider versions,
        IUpdateDownloader downloader, IUpdateSetupHandoff handoff)
    {
        this.automatic = automatic; this.signal = signal; this.source = source; this.versions = versions; this.downloader = downloader; this.handoff = handoff;
        _ = dispatcher.Handle;
        timer.Tick += (_, _) => { if (signal.WaitOne(0)) { this.automatic = false; ShowForm(); form!.Activate(); } };
        timer.Start();
        dispatcher.BeginInvoke(async () =>
        {
            await Task.Run(() => UpdateFiles.Prune());
            if (!disposed) await CheckAsync();
        });
    }
    private void ShowForm()
    {
        if (form is null)
        {
            form = new UpdateForm(automatic, downloader, handoff);
            form.CheckRequested += async () => await CheckAsync();
            form.FormClosing += (_, _) => closed.Cancel();
            form.FormClosed += (_, _) => { closed.Cancel(); ExitThread(); };
            form.Show();
        }
        else if (!form.Visible) form.Show();
    }
    private async Task CheckAsync()
    {
        if (checking || form?.Busy == true) return;
        checking = true;
        if (!automatic) { ShowForm(); form!.Checking(); }
        try
        {
            var version = versions.Current();
            if (version is null) throw new UserError("The installed version could not be read. Check the installation in Diagnostics and try again.");
            var release = await source.LatestAsync(closed.Token);
            if (disposed || closed.IsCancellationRequested) return;
            if (release is not null && release.Version > version)
            {
                if (await WorkspaceNavigationPipe.SendAsync(true)) { ExitThread(); return; }
                ShowForm(); form!.Present(release, version);
            }
            else if (automatic) ExitThread();
            else form!.Message(release is null ? "No supported stable release is currently available." : $"You’re up to date (version {UpdateVersion.Label(version)}).");
        }
        catch (Exception e)
        {
            if (disposed || closed.IsCancellationRequested) return;
            if (automatic) ExitThread();
            else form!.Message(e is UserError ? e.Message : "Couldn’t check for updates. Check your connection or try again later. GitHub may be temporarily unavailable or limiting requests.");
        }
        finally { checking = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; closed.Cancel(); timer.Dispose(); form?.Dispose(); dispatcher.Dispose(); closed.Dispose();
        }
        base.Dispose(disposing);
    }
}

// A passive notice is the only update UI outside the workspace. All actions navigate inside it.
internal sealed class UpdateForm : Form
{
    private readonly Label summary = new() { AutoSize = true, MaximumSize = new(380,0) };
    protected override bool ShowWithoutActivation => true;
    internal UpdateForm(bool passive, IUpdateDownloader downloader, IUpdateSetupHandoff handoff)
    {
        Text = "Roblox Multi Instance — Update available"; ClientSize = new(430,190);
        FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; Font = new("Segoe UI",10);
        var area = Screen.PrimaryScreen!.WorkingArea; Location = new(area.Right-Width-18,area.Bottom-Height-18);
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        root.Controls.Add(new Label { Text = "Update available", AutoSize = true, Font = new("Segoe UI",14,FontStyle.Bold) }); root.Controls.Add(summary);
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(WorkspaceControls.Button("View update",()=> { UpdateHost.OpenManual(); Close(); }));
        actions.Controls.Add(WorkspaceControls.Button("Later",Close)); root.Controls.Add(actions); Controls.Add(root);
        ThemePalette.Resolve(new AppearancePreferences().Scheme).Apply(this);
    }
    internal bool Busy => false;
    internal event Func<Task>? CheckRequested { add { } remove { } }
    internal void Checking() { }
    internal void Message(string text) => summary.Text = text;
    internal void Present(UpdateRelease release, Version installed)
        => summary.Text = $"Version {UpdateVersion.Label(release.Version)} is available. Installed: {UpdateVersion.Label(installed)}. Open System → Updates to review and download.";
}
