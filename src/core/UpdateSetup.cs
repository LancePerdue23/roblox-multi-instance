using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal interface IUpdateSetupHandoff { Task StartAsync(string executable, UpdateRelease release); }
internal sealed class UpdateSetupHandoff : IUpdateSetupHandoff
{
    internal static string ReadyName(int pid, long created) => @"Local\RobloxLaunchHelper-UpdateReady-" + Program.Sid + "-" + pid + "-" + created;
    public async Task StartAsync(string executable, UpdateRelease release)
    {
        var asset = release.Asset ?? throw new InvalidDataException();
        using var current = Process.GetCurrentProcess();
        long created = current.StartTime.ToUniversalTime().Ticks;
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, ReadyName(current.Id, created));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in new[] { "--update-handoff", current.Id.ToString(CultureInfo.InvariantCulture),
            created.ToString(CultureInfo.InvariantCulture), release.Version.ToString(),
            asset.Size.ToString(CultureInfo.InvariantCulture), asset.Sha256 }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Setup could not be started.");
        if (!await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(15)))) throw new IOException("Setup did not acknowledge the handoff.");
    }
}

// Managers expose only a graceful close request, never an installation or elevation command.
internal sealed class UpdateShutdownRequest : IDisposable
{
    private readonly EventWaitHandle request;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private bool closing;
    private static string Name(int pid, long created) => @"Local\RobloxLaunchHelper-UpdateClose-" + Program.Sid + "-" + pid + "-" + created;
    internal UpdateShutdownRequest(Func<Task> close)
    {
        using var current = Process.GetCurrentProcess();
        request = new EventWaitHandle(false, EventResetMode.AutoReset, Name(current.Id, current.StartTime.ToUniversalTime().Ticks));
        timer.Tick += async (_, _) =>
        {
            if (closing || !request.WaitOne(0)) return;
            closing = true;
            try { await close(); } finally { closing = false; }
        };
        timer.Start();
    }
    internal static void CloseManagers()
    {
        foreach (var candidate in Process.GetProcessesByName(Program.Product))
        {
            using (candidate)
            {
                try
                {
                    var identity = Native.GetIdentity(candidate.Id);
                    if (identity.Sid != Program.Sid || identity.Elevated || identity.Session != Process.GetCurrentProcess().SessionId ||
                        !string.Equals(identity.Image, Program.InstalledExe, StringComparison.OrdinalIgnoreCase)) continue;
                    using var signal = EventWaitHandle.OpenExisting(Name(candidate.Id, candidate.StartTime.ToUniversalTime().Ticks));
                    signal.Set();
                }
                catch { /* Workers and short-lived launchers have no close endpoint. */ }
            }
        }
    }
    public void Dispose() { timer.Stop(); timer.Dispose(); request.Dispose(); }
}

internal static class UpdateInstallReadiness
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { internal int Pid; internal System.Runtime.InteropServices.ComTypes.FILETIME Started; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        internal UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Application;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
        internal int Type;
        internal uint Status, Session;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, int flags, string key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint appCount, nint apps, uint serviceCount, nint services);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? processes, ref uint reasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    internal static bool IsReady(string executable)
    {
        if (!File.Exists(executable)) return true;
        if (RmStartSession(out uint session, 0, Guid.NewGuid().ToString("N")) != 0) return false;
        try
        {
            if (RmRegisterResources(session, 1, [executable], 0, 0, 0, 0) != 0) return false;
            uint count = 0, reasons = 0;
            int result = RmGetList(session, out uint needed, ref count, null, ref reasons);
            // No shutdown/restart API is ever called. A later file-lock race is still handled by setup.
            return result == 0 && needed == 0;
        }
        finally { RmEndSession(session); }
    }
}

internal static class UpdateSetup
{
    internal static string? ResultMessage(int exitCode, Version? installed, Version expected) => exitCode switch
    {
        1223 => "Administrator approval was canceled. The downloaded update is retained; select Retry setup when ready.",
        not 0 => "Setup did not complete. Check setup logs, then select Retry setup.",
        _ when installed != expected => "Setup finished, but the installed version could not be confirmed. Retry setup or check Diagnostics.",
        _ => null
    };
    internal static bool IsStagedExecutable(string executable, string staging)
    {
        try
        {
            string full = Path.GetFullPath(executable);
            string? directory = Path.GetDirectoryName(full);
            return Path.GetFileName(full) == Program.Product + ".exe" && directory is not null &&
                Guid.TryParseExact(Path.GetFileName(directory), "N", out _) &&
                string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    internal static bool SameParent(Process parent, long created)
    {
        try
        {
            var identity = Native.GetIdentity(parent.Id);
            return !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == created && identity.Sid == Program.Sid &&
                !identity.Elevated && identity.Session == Process.GetCurrentProcess().SessionId &&
                string.Equals(Path.GetFileName(identity.Image), Program.Product + ".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    internal static int Run(string[] args)
    {
        if (Program.Elevated || args.Length != 6 || !int.TryParse(args[1], out int pid) || !long.TryParse(args[2], out long created) ||
            UpdateVersion.Parse(args[3]) is not { } version || !long.TryParse(args[4], out long size) || size is <= 0 or > UpdateFiles.MaxDownload ||
            !System.Text.RegularExpressions.Regex.IsMatch(args[5], @"\A[A-Fa-f0-9]{64}\z") || !IsStagedExecutable(Environment.ProcessPath!, UpdateFiles.Staging)) return 2;
        using var parent = Process.GetProcessById(pid);
        if (!SameParent(parent, created)) return 2;
        using var guard = new Mutex(false, @"Global\RobloxLaunchHelper-UpdateSetup-" + Program.Sid);
        bool held;
        try { held = guard.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { MessageBox.Show("Another update setup is already open. Finish or close that window before retrying.", "Update setup"); return 0; }
        try
        {
            using (var ready = EventWaitHandle.OpenExisting(UpdateSetupHandoff.ReadyName(pid, created))) ready.Set();
            Application.EnableVisualStyles();
            using var form = new UpdateSetupForm(parent, version, size, args[5]);
            Application.Run(form); return 0;
        }
        finally { guard.ReleaseMutex(); }
    }
}

internal interface IUpdateSetupOperations : IDisposable
{
    Task WaitForParentAsync(CancellationToken token);
    Task VerifyAsync(CancellationToken token);
    void CloseManagers();
    Task<bool> ReadyAsync(CancellationToken token);
    Task<int> InstallAsync(CancellationToken token);
    Version? InstalledVersion();
}
internal sealed class UpdateSetupOperations(Process parent, Version version, long size, string digest) : IUpdateSetupOperations
{
    private FileStream? lease;
    public Task WaitForParentAsync(CancellationToken token) => parent.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
    public async Task VerifyAsync(CancellationToken token)
    {
        lease ??= new FileStream(Path.Combine(AppContext.BaseDirectory, "lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var http = UpdateHost.CreateClient();
        var release = new UpdateRelease(version, "", "", UpdateUrls.Releases, new(Program.Product + ".exe", UpdateUrls.Releases, size, digest), null);
        await new UpdateDownloader(http, UpdateFiles.Staging).VerifyAsync(Environment.ProcessPath!, release, token);
    }
    public void CloseManagers() => UpdateShutdownRequest.CloseManagers();
    public Task<bool> ReadyAsync(CancellationToken token) => Task.Run(() => UpdateInstallReadiness.IsReady(Program.InstalledExe), token);
    public async Task<int> InstallAsync(CancellationToken token)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }; start.ArgumentList.Add("--setup");
        using var setup = Process.Start(start) ?? throw new IOException();
        await setup.WaitForExitAsync(token); return setup.ExitCode;
    }
    public Version? InstalledVersion() => File.Exists(Program.InstalledExe) ? new UpdateVersionProvider().Current() : null;
    public void Dispose() => lease?.Dispose();
}
internal static class UpdateSetupFlow
{
    internal static async Task RunAsync(IUpdateSetupOperations operations, Version version, Action<string> status, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        status("Waiting for the previous app window to close…");
        await operations.WaitForParentAsync(token);
        await operations.VerifyAsync(token);
        operations.CloseManagers();
        status("Closing helper windows and waiting for active helper operations… Roblox games will remain open.");
        bool ready = false;
        for (int i = 0; i < 20; i++)
        {
            token.ThrowIfCancellationRequested();
            ready = await operations.ReadyAsync(token);
            if (ready) break;
            await delay(TimeSpan.FromMilliseconds(500), token);
        }
        if (!ready) throw new UserError("Another helper operation is still using the installed executable. Close helper windows, wait for current launches to finish, then select Retry setup. Roblox games can stay open.");
        status("Approve the Windows administrator prompt to install this update.");
        int result = await operations.InstallAsync(token);
        if (UpdateSetup.ResultMessage(result, operations.InstalledVersion(), version) is { } error) throw new UserError(error);
    }
}

internal sealed class UpdateSetupForm : Form
{
    private readonly Version version;
    private readonly IUpdateSetupOperations operations;
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(470, 0) };
    private readonly Button retry = new() { Text = "Retry setup", AutoSize = true, Visible = false };
    private readonly Button open = new() { Text = "Open app", AutoSize = true, Visible = false };
    private readonly CancellationTokenSource closed = new();
    private bool running;
    internal UpdateSetupForm(Process parent, Version version, long size, string digest)
    {
        this.version = version; operations = new UpdateSetupOperations(parent, version, size, digest);
        Text = "Roblox Multi Instance — Update setup"; ClientSize = new(510, 230); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        layout.Controls.Add(status); layout.Controls.Add(retry); layout.Controls.Add(open); Controls.Add(layout);
        retry.Click += async (_, _) => await InstallAsync();
        open.Click += (_, _) =>
        {
            try { var start = new ProcessStartInfo(Program.InstalledExe) { UseShellExecute = false }; start.ArgumentList.Add("--diagnostics"); Process.Start(start)?.Dispose(); Close(); }
            catch { status.Text = "The app could not be opened. Use Diagnostics in the Start menu."; }
        };
        Shown += async (_, _) => await InstallAsync();
        FormClosing += (_, e) => { if (running) { e.Cancel = true; return; } closed.Cancel(); };
    }
    private async Task InstallAsync()
    {
        if (running) return;
        running = true; retry.Visible = false;
        try
        {
            await UpdateSetupFlow.RunAsync(operations, version, text => status.Text = text, closed.Token);
            status.Text = $"Updated to version {UpdateVersion.Label(version)}."; open.Visible = true;
        }
        catch (UserError e) { status.Text = e.Message; retry.Visible = true; }
        catch (Exception) { status.Text = "Update setup could not finish. The downloaded update is retained. Close other helper windows and try again."; retry.Visible = true; }
        finally { running = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { closed.Cancel(); closed.Dispose(); operations.Dispose(); }
        base.Dispose(disposing);
    }
}
