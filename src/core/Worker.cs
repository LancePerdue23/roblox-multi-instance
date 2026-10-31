using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RobloxLaunchHelper;

internal static class Protocol
{
    internal static async Task WriteAsync(Stream stream, string message, CancellationToken token)
    {
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(message + "\n"), token);
        await stream.FlushAsync(token);
    }
    internal static async Task<string> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[1];
        var text = new System.Text.StringBuilder();
        while (text.Length < 16)
        {
            if (await stream.ReadAsync(buffer, token) == 0) throw new IOException("Helper connection closed.");
            if (buffer[0] == 10) return text.ToString();
            if (buffer[0] < 32 || buffer[0] > 126) throw new IOException("Invalid helper response.");
            text.Append((char)buffer[0]);
        }
        throw new IOException("Helper message too long.");
    }

    internal static bool IsPeer(Identity identity, bool server) => identity.Sid == Program.Sid &&
        identity.Session == Process.GetCurrentProcess().SessionId &&
        (!server || (identity.Elevated && string.Equals(identity.Image, Program.InstalledExe, StringComparison.OrdinalIgnoreCase)));

    internal static void VerifyServer(NamedPipeClientStream pipe)
    {
        if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out int pid) || !IsPeer(Native.GetIdentity(pid), true))
            throw new IOException("Worker identity verification failed.");
    }

    internal static void VerifyClient(NamedPipeServerStream pipe)
    {
        if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out int pid) || !IsPeer(Native.GetIdentity(pid), false))
            throw new IOException("Launcher identity verification failed.");
    }
}

internal sealed class ScanService
{
    private readonly Dictionary<ProcessKey, bool> trust = new();
    private readonly HashSet<ProcessKey> handled = [];
    internal HashSet<ProcessKey> HandledSnapshot() { lock (handled) return [.. handled]; }
    private readonly SemaphoreSlim gate = new(1);
    internal static HashSet<ProcessKey> CurrentProcesses()
    {
        var keys = new HashSet<ProcessKey>();
        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (process)
            {
                try { var id = Native.GetIdentity(process.Id); keys.Add(new(process.Id, id.Created)); }
                catch { /* Processes can disappear during enumeration. */ }
            }
        }
        return keys;
    }

    internal async Task<HashSet<ProcessKey>> ScanAsync()
    {
        await gate.WaitAsync();
        try { return await Task.Run(Scan); }
        finally { gate.Release(); }
    }

    private HashSet<ProcessKey> Scan()
    {
        var alive = new HashSet<ProcessKey>();
        foreach (var candidate in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (candidate)
            {
                int pid = candidate.Id;
                try
                {
                    using var process = Native.OpenProcess(0x1040, false, pid);
                    if (process.IsInvalid) { Log.Write("process-access-denied", "", pid); continue; }
                    var identity = Native.GetIdentity(process, pid);
                    var key = new ProcessKey(pid, identity.Created);
                    if (identity.Sid != Program.Sid || identity.Session != Process.GetCurrentProcess().SessionId) continue;
                    alive.Add(key);
                    if (!trust.TryGetValue(key, out bool trusted))
                    {
                        trusted = RobloxTrust.IsSignedRoblox(identity.Image);
                        trust[key] = trusted;
                        if (!trusted) Log.Write("roblox-signature-rejected", "", pid);
                    }
                    if (!trusted) continue;
                    int count = Native.CloseTargetEvents(process, key);
                    if (count > 0) { lock (handled) handled.Add(key); Log.Write("singleton-closed", "", pid); }
                }
                catch (Exception e) { Log.Write("scan-process-failed", e.GetType().Name, pid); }
            }
        }
        foreach (var key in trust.Keys.Where(x => !alive.Contains(x)).ToArray()) trust.Remove(key);
        return alive;
    }
}

internal sealed class PendingLaunch(HashSet<ProcessKey> baseline)
{
    internal HashSet<ProcessKey> Baseline = baseline;
    internal long Deadline = Environment.TickCount64 + 60000;
    internal bool Started;
    internal TaskCompletionSource<string> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Update(HashSet<ProcessKey> alive, HashSet<ProcessKey> handled, HashSet<ProcessKey> claimed, long now)
    {
        if (!Started || Done.Task.IsCompleted) return;
        var match = alive.FirstOrDefault(x => handled.Contains(x) && !Baseline.Contains(x) && !claimed.Contains(x));
        if (match != default) { claimed.Add(match); Done.TrySetResult("OK"); }
        else if (now >= Deadline) Done.TrySetResult("TIMEOUT");
    }
}

internal static class Worker
{
    internal static async Task<int> RunAsync()
    {
        // Presence handles avoid thread-affine mutex ownership across asynchronous awaits.
        using var presence = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\" + Program.PipeName + "-present", out bool created);
        if (!created) return 0;
        using var debugPrivilege = Native.DebugPrivilege();
        using var cancel = new CancellationTokenSource();
        var requests = new List<PendingLaunch>();
        var clients = new List<Task>();
        var claimed = new HashSet<ProcessKey>();
        var scan = new ScanService();
        object gate = new();
        long lastActivity = Environment.TickCount64;
        bool received = false;
        int activeClients = 0;
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(Program.Sid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        NamedPipeServerStream CreatePipe(bool first) => NamedPipeServerStreamAcl.Create(Program.PipeName, PipeDirection.InOut, 16,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : 0), 1024, 1024, security);

        async Task ClientAsync(NamedPipeServerStream pipe)
        {
            Interlocked.Increment(ref activeClients);
            try
            {
                using (pipe)
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    Protocol.VerifyClient(pipe);
                    string command;
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                    {
                        handshake.CancelAfter(5000);
                        command = await Protocol.ReadAsync(pipe, handshake.Token);
                    }
                    lock (gate) { received = true; lastActivity = Environment.TickCount64; }
                    if (command == "PROBE") { await Protocol.WriteAsync(pipe, "OK", timeout.Token); return; }
                    if (command is "INTEGRATION-ON" or "INTEGRATION-OFF")
                    {
                        // Only fixed settings are accepted; never accept installer actions or paths over IPC.
                        string result = "OK";
                        try { await Task.Run(() => Installation.SetBrowserIntegration(command == "INTEGRATION-ON")); }
                        catch (Exception e)
                        {
                            Log.Write("integration-change-failed", e.GetType().Name, e.HResult);
                            result = "FAILED";
                        }
                        await Protocol.WriteAsync(pipe, result, timeout.Token);
                        return;
                    }
                    if (command != "LAUNCH") return;
                    // Clear any leftover singleton before the next Roblox process starts.
                    await scan.ScanAsync();
                    var request = new PendingLaunch(ScanService.CurrentProcesses());
                    lock (gate) requests.Add(request);
                    try
                    {
                        await Protocol.WriteAsync(pipe, "READY", timeout.Token);
                        string started;
                        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                        {
                            handshake.CancelAfter(10000);
                            started = await Protocol.ReadAsync(pipe, handshake.Token);
                        }
                        if (started != "STARTED") return;
                        lock (gate) { request.Started = true; request.Deadline = Environment.TickCount64 + 60000; }
                        await Protocol.WriteAsync(pipe, await request.Done.Task.WaitAsync(timeout.Token), timeout.Token);
                    }
                    finally
                    {
                        lock (gate) { requests.Remove(request); lastActivity = Environment.TickCount64; }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Write("worker-client-failed", e.GetType().Name); }
            finally { Interlocked.Decrement(ref activeClients); }
        }

        async Task ListenAsync()
        {
            NamedPipeServerStream? listener = CreatePipe(true);
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    await listener.WaitForConnectionAsync(cancel.Token);
                    var connected = listener;
                    listener = null;
                    try { listener = CreatePipe(false); }
                    catch { connected.Dispose(); throw; }
                    lock (gate) clients.Add(ClientAsync(connected));
                }
            }
            catch (OperationCanceledException) { }
            finally { listener?.Dispose(); }
        }

        Log.Write("worker-started");
        Task listening = ListenAsync();
        try
        {
            while (true)
            {
                await Task.Delay(250);
                if (listening.IsFaulted) await listening;
                bool hasRequests;
                lock (gate) hasRequests = requests.Count > 0;
                HashSet<ProcessKey> alive = hasRequests ? await scan.ScanAsync() : [];
                var handled = scan.HandledSnapshot();
                lock (gate)
                {
                    foreach (var request in requests)
                        request.Update(alive, handled, claimed, Environment.TickCount64);
                    clients.RemoveAll(x => x.IsCompleted);
                    int idleLimit = received ? 2000 : 10000;
                    if (requests.Count == 0 && Volatile.Read(ref activeClients) == 0 && Environment.TickCount64 - lastActivity >= idleLimit) break;
                }
            }
        }
        finally
        {
            cancel.Cancel();
            try { await listening; } catch (OperationCanceledException) { }
            Task[] remaining; lock (gate) remaining = clients.ToArray();
            await Task.WhenAll(remaining);
            Log.Write("worker-exited");
        }
        return 0;
    }
}
