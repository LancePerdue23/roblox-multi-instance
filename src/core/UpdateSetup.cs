using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal interface IUpdateSetupHandoff { Task StartAsync(string executable, UpdateRelease release); }
internal sealed class UpdateSetupHandoff : IUpdateSetupHandoff
{
    internal static string ReadyName(int pid, long created) => @"Local\RobloxLaunchHelper-UpdateReady-" + Program.Sid + "-" + pid + "-" + created;
    public Task StartAsync(string executable,UpdateRelease release)=>StartAsync(executable,release,[]);
    internal async Task StartAsync(string executable, UpdateRelease release,IEnumerable<string> options)
    {
        var asset = release.Asset ?? throw new InvalidDataException();
        using var current = Process.GetCurrentProcess();
        long created = current.StartTime.ToUniversalTime().Ticks;
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, ReadyName(current.Id, created));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in new[] { "--update-handoff", current.Id.ToString(CultureInfo.InvariantCulture),
            created.ToString(CultureInfo.InvariantCulture), release.Version.ToString(),
            asset.Size.ToString(CultureInfo.InvariantCulture), asset.Sha256 }) start.ArgumentList.Add(argument);
        foreach(string option in options)start.ArgumentList.Add(option);
        using var child = Process.Start(start) ?? throw new IOException("Setup could not be started.");
        if (!await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(15)))) throw new IOException("Setup did not acknowledge the handoff.");
    }
    internal static async Task StartMaintenanceAsync(string executable,string action,IEnumerable<string>? options=null)
    {
        if(action is not ("setup" or "repair" or "recover"))throw new ArgumentException();
        using var current=Process.GetCurrentProcess();long created=current.StartTime.ToUniversalTime().Ticks;
        using var ready=new EventWaitHandle(false,EventResetMode.AutoReset,ReadyName(current.Id,created));
        var start=new ProcessStartInfo(executable){UseShellExecute=false};
        foreach(string arg in new[]{"--maintenance-handoff",current.Id.ToString(CultureInfo.InvariantCulture),created.ToString(CultureInfo.InvariantCulture),action}.Concat(options??[]))start.ArgumentList.Add(arg);
        using var child=Process.Start(start)??throw new IOException("Setup could not start.");
        if(!await Task.Run(()=>ready.WaitOne(TimeSpan.FromSeconds(15))))throw new IOException("Setup did not acknowledge the maintenance handoff.");
    }
}

// Managers expose only a graceful close request, never an installation or elevation command.
internal sealed class UpdateShutdownRequest : IDisposable
{
    private readonly EventWaitHandle request;
    private readonly System.Threading.Timer timer;
    private bool closing;
    private static string Name(int pid, long created) => @"Local\RobloxLaunchHelper-UpdateClose-" + Program.Sid + "-" + pid + "-" + created;
    internal UpdateShutdownRequest(Func<Task> close)
    {
        using var current = Process.GetCurrentProcess();
        request = new EventWaitHandle(false, EventResetMode.AutoReset, Name(current.Id, current.StartTime.ToUniversalTime().Ticks));
        timer = new System.Threading.Timer(async _ =>
        {
            if (closing || !request.WaitOne(0)) return;
            closing = true;
            try { await close(); } finally { closing = false; }
        },null,250,250);

    }
    internal static void CloseManagers()
    {
        foreach (var candidate in new[]{Program.Product,"RobloxMultiInstance"}.SelectMany(Process.GetProcessesByName))
        {
            using (candidate)
            {
                try
                {
                    var identity = Native.GetIdentity(candidate.Id);
                    if (identity.Sid != Program.Sid || identity.Elevated || identity.Session != Process.GetCurrentProcess().SessionId ||
                        !new[]{Program.InstalledExe,Path.Combine(Program.InstallDirectory,"RobloxMultiInstance.exe")}.Contains(identity.Image,StringComparer.OrdinalIgnoreCase)) continue;
                    using var signal = EventWaitHandle.OpenExisting(Name(candidate.Id, candidate.StartTime.ToUniversalTime().Ticks));
                    signal.Set();
                }
                catch { /* Workers and short-lived launchers have no close endpoint. */ }
            }
        }
    }
    public void Dispose() { timer.Dispose(); request.Dispose(); }
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
    internal static Func<string[],int>? Present;
    internal static int Run(string[] args)=>Present?.Invoke(args)??2;

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
