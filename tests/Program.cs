using Microsoft.Win32;
using RobloxLaunchHelper;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

internal static class Tests
{
    private static readonly List<object> Results = [];
    private static int failed;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args.FirstOrDefault() == "--fixture") return Fixture.Run();
        if (args.FirstOrDefault() == "--inspect-roblox" && args.Length == 2) return Inspect(args[1]);
        bool integration = false;
        string? requestedReport = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--integration") integration = true;
            else if (args[i] == "--report" && i + 1 < args.Length) requestedReport = args[++i];
            else { Console.Error.WriteLine("Usage: tests [--integration] [--report path]"); return 2; }
        }

        ReliabilityTests.Run(Run);
        Run("Exact event name and type matching", () =>
        {
            Check(Native.IsTargetEvent("Event", @"\Sessions\1\BaseNamedObjects\ROBLOX_singletonEvent"));
            Check(!Native.IsTargetEvent("Mutant", @"\ROBLOX_singletonEvent"));
            Check(!Native.IsTargetEvent("Event", @"\ROBLOX_singletonEventExtra"));
            Check(!Native.IsTargetEvent("Event", @"\Other_ROBLOX_singletonEvent"));
            Check(!Native.IsTargetEvent("Event", @"\ROBLOX_SingletonEvent"));
            Check(!Native.IsTargetEvent("Event", @"\roblox_singletonevent"));
            Check(!Native.IsTargetEvent("Event", null));
        });
        Run("Launch arguments preserve quotes and metacharacters", () =>
        {
            // Synthetic injection payload, not a real authentication ticket.
            const string link = "roblox-player:1+launchmode:play+ticket:synthetic-fixture\" & calc.exe $(test_placeholder)";
            var start = Launch.BuildStart("\"C:\\Roblox\\RobloxPlayerBeta.exe\" \"%1\"", link);
            Check(!start.UseShellExecute && start.ArgumentList.Count == 1 && start.ArgumentList[0] == link);
            var desktop = Launch.BuildStart("\"C:\\Roblox\\RobloxPlayerBeta.exe\" %1", null);
            Check(desktop.ArgumentList.Count == 1 && desktop.ArgumentList[0] == "-app");
            Throws<InvalidOperationException>(() => Launch.BuildStart("cmd.exe /c %1", link));
            Throws<InvalidOperationException>(() => Launch.BuildStart("\"C:\\Roblox\\RobloxPlayerBeta.exe\" --arg=%1", link));
        });
        Run("URI scheme validation", () =>
        {
            Check(Launch.IsRobloxUri("roblox-player:test")); Check(Launch.IsRobloxUri("ROBLOX:test"));
            Check(!Launch.IsRobloxUri("https://roblox.com")); Check(!Launch.IsRobloxUri("--worker"));
        });
        Run("Peer authentication rejects wrong owner, session and elevation", () =>
        {
            var identity = new Identity(RobloxLaunchHelper.Program.Sid, Process.GetCurrentProcess().SessionId, true, RobloxLaunchHelper.Program.InstalledExe, 0);
            Check(Protocol.IsPeer(identity, true));
            Check(!Protocol.IsPeer(identity with { Sid = "S-1-5-18" }, true));
            Check(!Protocol.IsPeer(identity with { Session = identity.Session + 1 }, true));
            Check(!Protocol.IsPeer(identity with { Elevated = false }, true));
            Check(!Protocol.IsPeer(identity with { Image = @"C:\Untrusted.exe" }, true));
        });
        Run("Registry backup round trip preserves value kinds and nested keys", () =>
        {
            string root = @"Software\RobloxLaunchHelperTests-" + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(root);
                using (var original = key.CreateSubKey("Original"))
                {
                    original.SetValue("", "original");
                    original.SetValue("expand", "%LOCALAPPDATA%\\Roblox", RegistryValueKind.ExpandString);
                    original.SetValue("binary", new byte[] { 0, 1, 255 }, RegistryValueKind.Binary);
                    original.SetValue("multi", new[] { "a", "b" }, RegistryValueKind.MultiString);
                    original.SetValue("dword", -12, RegistryValueKind.DWord);
                    original.SetValue("qword", long.MaxValue, RegistryValueKind.QWord);
                    using var nested = original.CreateSubKey(@"shell\open\command"); nested.SetValue("", "command");
                }
                RegistryTree snapshot;
                using (var original = key.OpenSubKey("Original")!) snapshot = RegistryTree.Capture(original);
                var saved = JsonSerializer.Serialize(snapshot);
                Installation.RestoreTree(key, "Restored", JsonSerializer.Deserialize<RegistryTree>(saved));
                using var restored = key.OpenSubKey("Restored")!;
                Check(JsonSerializer.Serialize(RegistryTree.Capture(restored)) == saved);
                Installation.RestoreTree(key, "Restored", null);
                Check(key.OpenSubKey("Restored") is null);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
        });
        Run("Unsigned or non-Roblox executable is rejected", () =>
        {
            Check(!RobloxTrust.IsSignedRoblox(Environment.ProcessPath!));
        });
        if (integration) Run("Installed Roblox passes the Authenticode publisher check", () =>
        {
            string command = Installation.CurrentCommand("roblox-player") ?? throw new Exception("No Roblox registration");
            if (command.Contains("RobloxLaunchHelper.exe", StringComparison.OrdinalIgnoreCase))
                command = Installation.ReadConfig().Commands["roblox-player"];
            Check(RobloxTrust.IsSignedRoblox(Launch.BuildStart(command, null).FileName));
        });
        Run("Overlapping launches require distinct new handled processes", () =>
        {
            var existing = new ProcessKey(1, 1); var first = new ProcessKey(2, 2); var second = new ProcessKey(3, 3);
            var a = new PendingLaunch([existing]) { Started = true, Deadline = 100 };
            var b = new PendingLaunch([existing]) { Started = true, Deadline = 100 };
            var claimed = new HashSet<ProcessKey>();
            a.Update([existing], [existing], claimed, 1); Check(!a.Done.Task.IsCompleted);
            a.Update([existing, first], [existing, first], claimed, 2); Check(a.Done.Task.Result == "OK");
            b.Update([existing, first], [existing, first], claimed, 2); Check(!b.Done.Task.IsCompleted);
            b.Update([existing, first, second], [existing, first, second], claimed, 3); Check(b.Done.Task.Result == "OK");
            Check(claimed.SetEquals([first, second]));
        });
        Run("Launch timeout, start handshake, and exited-process handling", () =>
        {
            var key = new ProcessKey(2, 2);
            var request = new PendingLaunch([]) { Deadline = 100 };
            var claimed = new HashSet<ProcessKey>();
            request.Update([key], [key], claimed, 1); Check(!request.Done.Task.IsCompleted);
            request.Started = true;
            request.Update([], [key], claimed, 99); Check(!request.Done.Task.IsCompleted);
            request.Update([], [key], claimed, 100); Check(request.Done.Task.Result == "TIMEOUT");
        });
        Run("Pipe protocol rejects oversized messages and premature disconnect", () =>
        {
            using var oversized = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("012345678901234567890\n"));
            Throws<IOException>(() => Protocol.ReadAsync(oversized, default).GetAwaiter().GetResult());
            using var empty = new MemoryStream();
            Throws<IOException>(() => Protocol.ReadAsync(empty, default).GetAwaiter().GetResult());
        });
        if (integration) Run("Controlled process: close target; preserve lookalike, unrelated event, and same-name mutex", ControlledClosure);
        Run("Invalid process and process-exit handling", () =>
        {
            using var invalid = Native.OpenProcess(0x1040, false, int.MaxValue);
            Check(invalid.IsInvalid);
            Throws<System.ComponentModel.Win32Exception>(() => Native.GetIdentity(int.MaxValue));
        });
        DiagnosticsTests.Run(Run);
        LaunchNotificationTests.Run(Run);
        ActiveInstancesTests.Run(Run);
        BackgroundClientCleanupTests.Run(Run);
        LaunchStartupTests.Run(Run);
        WindowLayoutsTests.Run(Run);
        UpdateTests.Run(Run);
        RecoveryTests.Run(Run);

        string report = requestedReport ?? Path.Combine(AppContext.BaseDirectory, "test-results.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { time = DateTimeOffset.Now, integration, elevated = RobloxLaunchHelper.Program.Elevated, failures = failed, tests = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count - failed}/{Results.Count} tests passed. Report: {report}");
        if (!integration) Console.WriteLine("Integration tests omitted; use --integration with administrator access and Roblox installed.");
        return failed == 0 ? 0 : 1;
    }

    private static void ControlledClosure()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--fixture");
        using var process = Process.Start(start)!;
        try
        {
            string? ready = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (ready != "READY") throw new Exception("Fixture did not start: " + ready + " " + process.StandardError.ReadToEnd());
            using var handle = Native.OpenProcess(0x1040, false, process.Id);
            Check(!handle.IsInvalid);
            var identity = Native.GetIdentity(handle, process.Id);
            var key = new ProcessKey(process.Id, identity.Created);
            using var privilege = RobloxLaunchHelper.Program.Elevated ? Native.DebugPrivilege() : null;
            Check(Native.CloseTargetEvents(handle, key with { Created = key.Created + 1 }) == 0);
            int count = Native.CloseTargetEvents(handle, key);
            Check(count == 1, "Expected one target closure; got " + count + ". An elevated test run may be required for handle-table access.");
            process.StandardInput.WriteLine("CHECK"); process.StandardInput.Flush();
            Check(process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "TARGET_CLOSED_OTHERS_OPEN");
            Check(Native.CloseTargetEvents(handle, key) == 0);
            process.StandardInput.WriteLine("EXIT"); process.StandardInput.Flush();
            Check(process.WaitForExit(5000));
            Throws<System.ComponentModel.Win32Exception>(() => Native.GetIdentity(process.Id));
        }
        finally { if (!process.HasExited) process.Kill(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(Microsoft.Win32.SafeHandles.SafeProcessHandle source, nint handle, nint target, out nint copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    private static int Inspect(string report)
    {
        using var privilege = Native.DebugPrivilege();
        using var eventMarker = new EventWaitHandle(false, EventResetMode.ManualReset);
        using var mutexMarker = new Mutex();
        var snapshot = Native.Snapshot();
        ushort eventType = snapshot.Single(x => x.ProcessId == Environment.ProcessId && x.Handle == eventMarker.SafeWaitHandle.DangerousGetHandle()).Type;
        ushort mutexType = snapshot.Single(x => x.ProcessId == Environment.ProcessId && x.Handle == mutexMarker.SafeWaitHandle.DangerousGetHandle()).Type;
        var lines = new List<string>();
        foreach (var candidate in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (candidate)
            using (var process = Native.OpenProcess(0x1040, false, candidate.Id))
            {
                if (process.IsInvalid) { lines.Add($"PID {candidate.Id}: inaccessible"); continue; }
                int events = 0;
                foreach (var row in snapshot.Where(x => x.ProcessId == candidate.Id && (x.Type == eventType || x.Type == mutexType)))
                {
                    if (!DuplicateHandle(process, row.Handle, GetCurrentProcess(), out nint copy, 0, false, 2)) continue;
                    try
                    {
                        events++;
                        string? name = Native.ObjectString(copy, 1);
                        if (name?.Contains("singleton", StringComparison.OrdinalIgnoreCase) == true)
                            lines.Add($"PID {candidate.Id}: {Native.ObjectString(copy, 2)} {name}; objectVisible={row.Object != 0}");
                    }
                    finally { Native.CloseHandle(copy); }
                }
                lines.Add($"PID {candidate.Id}: inspected {events} event/mutex handles");
            }
        }
        File.WriteAllLines(report, lines); return 0;
    }

    private static void Run(string name, Action action)
    {
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { action(); Results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

internal static class Fixture
{
    [StructLayout(LayoutKind.Sequential)] private struct Unicode { public ushort Length, Maximum; public nint Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes
    { public int Length; public nint Root, Name; public uint Flags; public nint Security, Quality; }
    [DllImport("ntdll.dll")] private static extern int NtCreateDirectoryObject(out nint handle, uint access, ref Attributes attributes);
    [DllImport("ntdll.dll")] private static extern int NtCreateEvent(out nint handle, uint access, ref Attributes attributes, int type, [MarshalAs(UnmanagedType.U1)] bool initial);
    [DllImport("ntdll.dll")] private static extern int NtCreateMutant(out nint handle, uint access, ref Attributes attributes, [MarshalAs(UnmanagedType.U1)] bool initial);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(nint handle, out uint flags);

    private static nint Create(string name, int kind)
    {
        nint text = Marshal.StringToHGlobalUni(name), descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<Unicode>());
        try
        {
            Marshal.StructureToPtr(new Unicode { Length = (ushort)(name.Length * 2), Maximum = (ushort)((name.Length + 1) * 2), Buffer = text }, descriptor, false);
            var attributes = new Attributes { Length = Marshal.SizeOf<Attributes>(), Name = descriptor, Flags = 0x40 };
            nint handle;
            int status = kind switch
            {
                0 => NtCreateDirectoryObject(out handle, 0xF000F, ref attributes),
                1 => NtCreateEvent(out handle, 0x1F0003, ref attributes, 0, false),
                _ => NtCreateMutant(out handle, 0x1F0001, ref attributes, false)
            };
            if (status < 0) throw new Exception($"Native fixture object creation failed: 0x{status:X8}");
            return handle;
        }
        finally { Marshal.FreeHGlobal(descriptor); Marshal.FreeHGlobal(text); }
    }
    internal static int Run()
    {
        var handles = new List<nint>();
        try
        {
            int session = Process.GetCurrentProcess().SessionId;
            string objectRoot = session == 0 ? @"\BaseNamedObjects" : $@"\Sessions\{session}\BaseNamedObjects";
            string root = $@"{objectRoot}\RLH-Tests-{Guid.NewGuid():N}";
            // A private object-manager directory avoids touching the real Roblox singleton.
            handles.Add(Create(root, 0));
            nint target = Create(root + @"\ROBLOX_singletonEvent", 1); handles.Add(target);
            nint lookalike = Create(root + @"\ROBLOX_singletonEventExtra", 1); handles.Add(lookalike);
            nint unrelated = Create(root + @"\OtherEvent", 1); handles.Add(unrelated);
            handles.Add(Create(root + @"\Mutex", 0));
            nint mutex = Create(root + @"\Mutex\ROBLOX_singletonEvent", 2); handles.Add(mutex);
            Console.WriteLine("READY");
            while (Console.ReadLine() is { } command && command != "EXIT")
            {
                if (command == "CHECK") Console.WriteLine(!GetHandleInformation(target, out _) && GetHandleInformation(lookalike, out _) &&
                    GetHandleInformation(unrelated, out _) && GetHandleInformation(mutex, out _) ? "TARGET_CLOSED_OTHERS_OPEN" : "WRONG_HANDLES");
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { foreach (var handle in handles.AsEnumerable().Reverse()) if (GetHandleInformation(handle, out _)) Native.CloseHandle(handle); }
    }
}
