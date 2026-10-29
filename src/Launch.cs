using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal static class ShellAssociations
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint first, nint second);

    internal static void NotifyChanged() => SHChangeNotify(0x08000000, 0x1000, 0, 0); // ASSOCCHANGED, IDLIST | FLUSH.
}

internal static class Launch
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);

    internal static bool IsRobloxUri(string value) => value.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase);

    internal static string[] SplitCommand(string command)
    {
        nint argv = CommandLineToArgvW(command, out int count);
        if (argv == 0) throw new InvalidOperationException("The saved Roblox launch command is invalid. Run Repair.");
        try
        {
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            return result;
        }
        finally { LocalFree(argv); }
    }

    internal static ProcessStartInfo BuildStart(string command, string? uri)
    {
        string[] words = SplitCommand(command);
        if (words.Length == 0 || !Path.IsPathFullyQualified(words[0]) ||
            !string.Equals(Path.GetFileName(words[0]), "RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved launch target is not the Roblox player. Run Repair.");
        var result = new ProcessStartInfo(words[0]) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(words[0])! };
        bool inserted = false;
        foreach (string word in words.Skip(1))
        {
            if (word is "%1" or "%L" or "%l" or "%*")
            {
                if (uri is not null) result.ArgumentList.Add(uri);
                inserted = true;
            }
            else
            {
                if (word.Contains('%')) throw new InvalidOperationException("Unsupported launch placeholder. Run Repair.");
                result.ArgumentList.Add(word);
            }
        }
        if (uri is not null && !inserted) result.ArgumentList.Add(uri);
        if (uri is null && result.ArgumentList.Count == 0) result.ArgumentList.Add("-app");
        return result;
    }

    internal static async Task<NamedPipeClientStream> ConnectAsync()
    {
        var deadline = Environment.TickCount64 + 12000;
        long lastRequest = 0;
        while (Environment.TickCount64 < deadline)
        {
            var client = new NamedPipeClientStream(".", Program.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            try
            {
                await client.ConnectAsync(350);
                Protocol.VerifyServer(client);
                return client;
            }
            catch (TimeoutException) { client.Dispose(); }
            catch (IOException) { client.Dispose(); }
            catch { client.Dispose(); throw; }
            if (Environment.TickCount64 - lastRequest >= 1000) { ScheduledWorker.Start(); lastRequest = Environment.TickCount64; }
            await Task.Delay(100);
        }
        throw new UserError("The administrator helper did not start. Run Repair from the Start menu. Roblox can still launch normally.");
    }

    internal static async Task<int> VerifyAsync()
    {
        Log.Write("probe-start", "elevated=" + Program.Elevated + ";installed=" + Program.IsInstalledExecutable());
        if (Program.Elevated || !Program.IsInstalledExecutable()) return 2;
        try
        {
            Installation.VerifyReadOnly();
            ScheduledWorker.VerifyNoWriteAccess();
            using var pipe = await ConnectAsync();
            using var timeout = new CancellationTokenSource(15000);
            await Protocol.WriteAsync(pipe, "PROBE", timeout.Token);
            bool ok = await Protocol.ReadAsync(pipe, timeout.Token) == "OK";
            Log.Write("probe-complete", "", ok ? 0 : 3);
            return ok ? 0 : 3;
        }
        catch (Exception e) { Log.Write("setup-probe-failed", e.GetType().Name, e.HResult); return 1; }
    }

    internal static async Task<int> RunAsync(string? uri)
    {
        Log.Write("launch-requested", uri is null ? "desktop" : uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) ? "roblox-player" : "roblox", Environment.ProcessId);
        if (Program.Elevated) throw new UserError("Launch Roblox normally, without Run as administrator. Only setup and the handle helper need administrator access.");
        var config = Installation.ReadConfig();
        if (config.OwnerSid != Program.Sid) throw new UserError("This installation belongs to another Windows account.");
        string scheme = uri?.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) != false ? "roblox-player" : "roblox";
        if (!config.Commands.TryGetValue(scheme, out string? command)) command = config.Commands["roblox-player"];
        var start = BuildStart(command, uri);
        if (!File.Exists(start.FileName)) throw new UserError("Roblox's saved executable is missing, usually after an update. Launch or reinstall Roblox normally, then run Roblox Launch Helper Repair.");
        if (!RobloxTrust.IsSignedRoblox(start.FileName)) throw new UserError("The saved Roblox executable did not pass its Roblox signature check. Repair or reinstall Roblox before trying again.");
        NamedPipeClientStream? pipe = null;
        string? helperError = null;
        using var timeout = new CancellationTokenSource(90000);
        try
        {
            try
            {
                pipe = await ConnectAsync();
                await Protocol.WriteAsync(pipe, "LAUNCH", timeout.Token);
                if (await Protocol.ReadAsync(pipe, timeout.Token) != "READY") throw new IOException();
            }
            catch (Exception e)
            {
                helperError = "The administrator helper was unavailable. Run Repair before opening another instance.";
                Log.Write("worker-unavailable", e.GetType().Name, e.HResult);
                pipe?.Dispose(); pipe = null;
            }
            using var game = Process.Start(start) ?? throw new UserError("Roblox could not start. Run Repair.");
            Log.Write("roblox-launched", "", game.Id);
            if (pipe is not null)
            {
                try
                {
                    await Protocol.WriteAsync(pipe, "STARTED", timeout.Token);
                    string result = await Protocol.ReadAsync(pipe, timeout.Token);
                    if (result != "OK") helperError = "The singleton event could not be confirmed closed within 60 seconds. Roblox was left running. Check diagnostics before opening another instance.";
                }
                catch (Exception e)
                {
                    helperError = "The helper could not confirm completion. Roblox was left running. Check diagnostics or run Repair.";
                    Log.Write("worker-result-failed", e.GetType().Name, e.HResult);
                }
            }
        }
        finally { pipe?.Dispose(); }
        if (helperError is not null)
        {
            Log.Write("launch-warning");
            MessageBox.Show(helperError + "\n\nDiagnostics: " + Log.DirectoryPath + "\nWorker logs: " + Path.Combine(Program.InstallDirectory, "logs"),
                "Roblox Launch Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        return 0;
    }
}

internal static class ScheduledWorker
{
    private static dynamic Service()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect(); return service;
    }

    internal static void Start()
    {
        dynamic service = Service();
        dynamic root = service.GetFolder("\\");
        dynamic task = root.GetTask(Program.TaskName);
        // No task arguments supplied by the launcher; the registered action is fixed.
        task.RunEx(null, 4, Process.GetCurrentProcess().SessionId, null);
    }

    internal static void Register()
    {
        dynamic service = Service();
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Closes only the Roblox singleton event on demand; no startup trigger.";
        task.Principal.UserId = Program.Sid;
        task.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN, no saved credentials.
        task.Principal.RunLevel = 1;
        task.Settings.Enabled = true;
        task.Settings.AllowDemandStart = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 0; // Parallel across sessions; session-local worker guard.
        task.Settings.Hidden = false;
        dynamic action = task.Actions.Create(0);
        action.Path = Program.InstalledExe;
        action.Arguments = "--worker";
        action.WorkingDirectory = Program.InstallDirectory;
        string sddl = $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;{Program.Sid})";
        dynamic root = service.GetFolder("\\");
        // Keep Task Scheduler from adding a broader ACE for the task principal.
        root.RegisterTaskDefinition(Program.TaskName, task, 6 | 0x10, Program.Sid, null, 3, sddl);
    }

    internal static void Remove()
    {
        dynamic service = Service();
        dynamic root = service.GetFolder("\\");
        try { dynamic task = root.GetTask(Program.TaskName); task.Stop(0); root.DeleteTask(Program.TaskName, 0); }
        catch (COMException e) when ((uint)e.HResult is 0x80070002 or 0x8004130F) { }
    }

    internal static void VerifyNoWriteAccess()
    {
        dynamic service = Service();
        dynamic root = service.GetFolder("\\");
        dynamic task = root.GetTask(Program.TaskName);
        string descriptor = task.GetSecurityDescriptor(7);
        try { task.SetSecurityDescriptor(descriptor, 0x10); }
        catch (UnauthorizedAccessException) { return; }
        catch (COMException e) when ((uint)e.HResult == 0x80070005) { return; }
        throw new InvalidOperationException("The task permissions allow modification without elevation.");
    }

    internal static int RunLimitedSetupTask(string arguments, TimeSpan timeout)
    {
        if (arguments is not ("--verify" or "--verify-protocols" or "--shortcuts install" or "--shortcuts remove")) throw new InvalidOperationException("Unsupported setup probe.");
        dynamic service = Service();
        dynamic root = service.GetFolder("\\");
        string name = Program.TaskName + "-SetupProbe";
        dynamic definition = service.NewTask(0);
        definition.Principal.UserId = Program.Sid;
        definition.Principal.LogonType = 3;
        definition.Principal.RunLevel = 0; // TASK_RUNLEVEL_LUA, actual filtered user token.
        definition.Settings.Enabled = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT1M";
        dynamic action = definition.Actions.Create(0);
        action.Path = Program.InstalledExe;
        action.Arguments = arguments;
        action.WorkingDirectory = Program.InstallDirectory;
        string sddl = $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;{Program.Sid})";
        dynamic task = root.RegisterTaskDefinition(name, definition, 6 | 0x10, Program.Sid, null, 3, sddl);
        try
        {
            dynamic running = task.RunEx(null, 4, Process.GetCurrentProcess().SessionId, null);
            long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(100);
                int state = task.State;
                int result = task.LastTaskResult;
                if (state == 3 && result != 0x41303 && result != 0x41301)
                {
                    Log.Write("limited-probe-result", "", result);
                    return result;
                }
            }
            running.Stop();
            return 1460;
        }
        finally { root.DeleteTask(name, 0); }
    }

    internal static int RunCleanInstallerTask(string actionName)
    {
        if (!Program.Elevated || actionName is not ("setup" or "repair" or "uninstall"))
            throw new InvalidOperationException("Unsupported installer task action.");
        dynamic service = Service();
        dynamic root = service.GetFolder("\\");
        string name = Program.TaskName + "-Installer-" + Guid.NewGuid().ToString("N");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Temporary explicit installer action; removed on completion.";
        definition.Principal.UserId = Program.Sid;
        definition.Principal.LogonType = 3;
        definition.Principal.RunLevel = 1;
        definition.Settings.Enabled = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        dynamic action = definition.Actions.Create(0);
        action.Path = Program.InstalledExe;
        action.Arguments = "--admin-clean " + actionName + " " + Program.Sid;
        action.WorkingDirectory = Program.InstallDirectory;
        // Only elevated administrators and SYSTEM can run/change this temporary
        // privileged installer task. Unlike the worker, it grants no user ACE.
        string sddl = "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)";
        dynamic task = root.RegisterTaskDefinition(name, definition, 6 | 0x10, Program.Sid, null, 3, sddl);
        try
        {
            task.RunEx(null, 4, Process.GetCurrentProcess().SessionId, null);
            while (true)
            {
                Thread.Sleep(200);
                int state = task.State;
                int result = task.LastTaskResult;
                if (state == 3 && result != 0x41303 && result != 0x41301) return result;
            }
        }
        finally { root.DeleteTask(name, 0); }
    }
}
