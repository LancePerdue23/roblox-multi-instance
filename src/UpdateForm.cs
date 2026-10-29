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

internal sealed class UpdateForm : Form
{
    private readonly Label heading = new() { Text = "Updates", AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label summary = new() { AutoSize = true, MaximumSize = new(390, 0), ForeColor = Color.FromArgb(180, 178, 174), Text = "Checking for updates…" };
    private readonly Button primary = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button later = new() { Text = "Later", AutoSize = true };
    private readonly Button check = new() { Text = "Check again", AutoSize = true };
    private readonly LinkLabel notes = new() { Text = "What’s new", AutoSize = true, Visible = false };
    private readonly LinkLabel page = new() { Text = "Release page", AutoSize = true };
    private readonly ProgressBar progress = new() { Width = 388, Height = 8, Visible = false };
    private readonly IUpdateDownloader downloader;
    private readonly IUpdateSetupHandoff handoff;
    private readonly bool passive;
    private readonly CancellationTokenSource closed = new();
    private CancellationTokenSource? downloadCancellation;
    private UpdateRelease? release;
    private DownloadedUpdate? downloaded;
    private Task operation = Task.CompletedTask;
    private bool finishing, disposed;
    internal bool Busy { get; private set; }
    internal event Func<Task>? CheckRequested;
    protected override bool ShowWithoutActivation => passive;
    internal UpdateForm(bool passive, IUpdateDownloader downloader, IUpdateSetupHandoff handoff)
    {
        this.passive = passive; this.downloader = downloader; this.handoff = handoff;
        Text = "Roblox Multi Instance — Updates"; ClientSize = new(440, 245); FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(19, 18, 16); ForeColor = Color.FromArgb(242, 240, 235); Font = new("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = !passive; StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = passive ? new(area.Right - Width - 18, area.Bottom - Height - 18) : new(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        var top = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        top.ColumnStyles.Add(new(SizeType.Percent, 100)); top.ColumnStyles.Add(new(SizeType.AutoSize));
        var close = new Button { Text = "×", Width = 28, Height = 26, FlatStyle = FlatStyle.Flat, AccessibleName = "Close update notice" };
        close.FlatAppearance.BorderSize = 0; close.Click += (_, _) => Close(); top.Controls.Add(heading); top.Controls.Add(close);
        var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        foreach (var link in new[] { notes, page }) { link.LinkColor = Color.FromArgb(237, 222, 184); link.ActiveLinkColor = Color.White; link.Margin = new(0, 4, 15, 8); links.Controls.Add(link); }
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        foreach (var button in new[] { primary, later, check })
        {
            button.FlatStyle = FlatStyle.Flat; button.Padding = new(8, 5, 8, 5); button.FlatAppearance.BorderColor = Color.FromArgb(55, 52, 47); actions.Controls.Add(button);
        }
        primary.BackColor = Color.FromArgb(237, 222, 184); primary.ForeColor = Color.Black;
        AcceptButton = primary; CancelButton = later;
        check.Visible = false;
        root.Controls.Add(top, 0, 0); root.Controls.Add(summary, 0, 1); root.Controls.Add(progress, 0, 2); root.Controls.Add(links, 0, 3); root.Controls.Add(actions, 0, 4); Controls.Add(root);
        primary.Click += async (_, _) => { if (!Busy) { operation = PrimaryAsync(); await operation; } };
        later.Click += (_, _) => { if (downloadCancellation is not null) downloadCancellation.Cancel(); else Close(); };
        check.Click += async (_, _) => { if (!Busy && CheckRequested is not null) await CheckRequested(); };
        notes.LinkClicked += (_, _) => ShowNotes();
        page.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo((release?.Page ?? UpdateUrls.Releases).AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
            catch { summary.Text = "The release page could not be opened. Try again after configuring your default browser."; }
        };
        FormClosing += async (_, e) =>
        {
            if (finishing) return;
            closed.Cancel(); downloadCancellation?.Cancel();
            if (!operation.IsCompleted)
            {
                e.Cancel = true;
                await operation;
                if (!IsDisposed) { finishing = true; Close(); }
            }
        };
    }
    internal void Checking()
    {
        ClearDownload(); release = null; Busy = true; primary.Enabled = false; check.Enabled = false; notes.Visible = false;
        summary.Text = "Checking for updates…";
    }
    internal void Message(string text)
    {
        heading.Text = "Updates"; summary.Text = text; Busy = false; primary.Text = "Check for updates"; primary.Enabled = true; check.Visible = false;
    }
    internal void Present(UpdateRelease value, Version installed)
    {
        ClearDownload(); release = value; Busy = false; heading.Text = "↓  Update available";
        summary.Text = $"A new version ({UpdateVersion.Label(value.Version)}) is available. You are currently on version {UpdateVersion.Label(installed)}." +
            (value.DownloadProblem is null ? "" : "\n" + value.DownloadProblem);
        primary.Text = "Download update"; primary.Enabled = value.Asset is not null; notes.Visible = true; check.Visible = true; check.Enabled = true;
    }
    private async Task PrimaryAsync()
    {
        if (release is null) { if (CheckRequested is not null) await CheckRequested(); return; }
        Busy = true; primary.Enabled = false; check.Enabled = false;
        downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
        try
        {
            if (downloaded is null)
            {
                progress.Visible = true; later.Text = "Cancel";
                var reporting = new Progress<long>(bytes =>
                {
                    if (disposed || closed.IsCancellationRequested || !Busy || downloaded is not null) return;
                    progress.Value = (int)Math.Min(100, bytes * 100 / release.Asset!.Size);
                    summary.Text = $"Downloading version {UpdateVersion.Label(release.Version)}… {progress.Value}%";
                });
                downloaded = await downloader.DownloadAsync(release, reporting, downloadCancellation.Token);
                if (closed.IsCancellationRequested) return;
                primary.Text = "Run setup";
                summary.Text = "Download verified. Run setup to close helper windows and request administrator approval. Roblox games will remain open.";
            }
            else
            {
                await downloader.VerifyAsync(downloaded.Executable, release, downloadCancellation.Token);
                await handoff.StartAsync(downloaded.Executable, release);
                finishing = true; Close();
            }
        }
        catch (OperationCanceledException) { if (!closed.IsCancellationRequested) summary.Text = "Download canceled. Select Download update to try again."; }
        catch (InvalidDataException)
        {
            ClearDownload(); primary.Text = "Download update";
            summary.Text = "The download did not match this release’s size, SHA-256, or executable version. Download it again; if this continues, try again after the publisher fixes the release.";
        }
        catch (UnauthorizedAccessException)
        {
            ClearDownload(); primary.Text = "Download update";
            summary.Text = "The update folder could not be accessed. Check folder permissions or security software, then retry.";
        }
        catch (Exception)
        {
            ClearDownload(); primary.Text = "Download update";
            summary.Text = "The update could not be downloaded, verified, or started. Check your connection and available disk space, then try again.";
        }
        finally
        {
            downloadCancellation.Dispose(); downloadCancellation = null;
            if (!disposed) { Busy = false; primary.Enabled = true; check.Enabled = true; later.Text = "Later"; progress.Visible = false; }
        }
    }
    private void ShowNotes()
    {
        if (release is null) return;
        var details = new Form { Text = release.Title + " — " + UpdateVersion.Label(release.Version), Size = new(650, 500), MinimumSize = new(400, 300), AutoScaleMode = AutoScaleMode.Dpi, StartPosition = FormStartPosition.CenterParent };
        details.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.IsNullOrWhiteSpace(release.Notes) ? "No release notes were provided." : release.Notes.Replace("\n", "\r\n"), AccessibleName = "Release notes" });
        details.FormClosed += (_, _) => details.Dispose(); details.Show(this);
    }
    private void ClearDownload() { downloaded?.Dispose(); downloaded = null; }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; closed.Cancel(); ClearDownload(); closed.Dispose(); }
        base.Dispose(disposing);
    }
}
