using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace RobloxLaunchHelper;

internal static class NotificationWire
{
    internal static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static string Name => Program.Product + "-Notifications-" + Program.Sid + "-" + Process.GetCurrentProcess().SessionId;
    internal static bool IsPeer(Identity peer) => peer.Sid == Program.Sid && !peer.Elevated &&
        peer.Session == Process.GetCurrentProcess().SessionId &&
        string.Equals(peer.Image, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    internal static async Task WriteAsync(Stream stream, LaunchStatus status, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(status);
        if (bytes.Length > 2048) throw new IOException();
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
    }
    internal static async Task<LaunchStatus> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int size = BitConverter.ToInt32(header);
        if (size is < 1 or > 2048) throw new IOException();
        byte[] bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, token);
        var status = JsonSerializer.Deserialize<LaunchStatus>(bytes, Json) ?? throw new IOException();
        if (!status.Valid) throw new IOException();
        return status;
    }
    internal static NamedPipeServerStream CreateServer(string? name = null)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(Program.Sid), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name ?? Name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
    }
}

// One-slot queue coalesces progress; terminal publication replaces any queued progress.
internal sealed class LaunchNotificationPublisher : IDisposable
{
    private readonly Channel<LaunchStatus> queue = Channel.CreateBounded<LaunchStatus>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource stopped = new();
    private readonly CancellationTokenSource progressStopped = new();
    private readonly TaskCompletionSource<bool> terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<LaunchStatus, CancellationToken, Task<bool>> send;
    private readonly Func<NotificationSettings> getSettings;
    private readonly string pipeName;
    private readonly Action requestHost;
    private readonly Task pump;
    private int disposed;
    private bool hasConnected;
    internal LaunchNotificationPublisher(Func<LaunchStatus, CancellationToken, Task<bool>>? send = null,
        Func<NotificationSettings>? settings = null, string? pipeName = null, Action? requestHost = null)
    {
        getSettings = settings ?? (() => new NotificationPreferences().Settings);
        this.pipeName = pipeName ?? NotificationWire.Name;
        this.requestHost = requestHost ?? (() =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--launch-notifications"); Process.Start(start)?.Dispose();
        });
        this.send = send ?? SendAsync; pump = Task.Run(PumpAsync);
    }
    internal void Publish(LaunchStatus status) => queue.Writer.TryWrite(status);
    internal async Task<bool> FinishAsync(LaunchStatus status)
    {
        progressStopped.Cancel();
        Publish(status); queue.Writer.TryComplete();
        try { return await terminal.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch { return false; }
        finally { Dispose(); }
    }
    private async Task PumpAsync()
    {
        try
        {
            await foreach (var status in queue.Reader.ReadAllAsync(stopped.Token))
            {
                bool accepted;
                try
                {
                    using var sending = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token,
                        status.Terminal ? CancellationToken.None : progressStopped.Token);
                    accepted = await send(status, sending.Token);
                }
                catch { accepted = false; }
                if (status.Terminal) { terminal.TrySetResult(accepted); return; }
            }
        }
        catch { terminal.TrySetResult(false); }
    }
    private async Task<bool> SendAsync(LaunchStatus status, CancellationToken token)
    {
        var settings = getSettings();
        if (!status.Warning && !settings.Progress && !settings.Completion && !hasConnected) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(2000);
        bool hostRequested = false;
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
                // Connection retries run in this background pump, never before player startup.
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); connect.CancelAfter(100);
                await pipe.ConnectAsync(connect.Token);
                if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out int pid) || !NotificationWire.IsPeer(Native.GetIdentity(pid))) return false;
                // Even if terminal priority cancels the ACK read, the host may already
                // have accepted progress. It still needs final cleanup after opt-out.
                hasConnected = true;
                await NotificationWire.WriteAsync(pipe, status, deadline.Token);
                // ACK means the host reducer accepted responsibility, not merely bytes queued.
                bool accepted = await Protocol.ReadAsync(pipe, deadline.Token) == "ACCEPTED";
                return accepted;
            }
            catch when (!deadline.IsCancellationRequested)
            {
                if (!hostRequested)
                {
                    // A host that saw progress still needs the final state after opt-out,
                    // but opting out must not start a replacement notification process.
                    if (!status.Warning && !settings.Progress && !settings.Completion) return false;
                    hostRequested = true;
                    try
                    {
                        requestHost();
                    }
                    catch { return false; }
                }
                await Task.Delay(50, deadline.Token);
            }
        }
        return false;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopped.Cancel(); queue.Writer.TryComplete();
        // Dispose cancellation resources only after pending I/O has observed cancellation.
        _ = pump.ContinueWith(_ => { stopped.Dispose(); progressStopped.Dispose(); }, TaskScheduler.Default);
    }
}

internal sealed class NotificationServer : IDisposable
{
    private readonly CancellationTokenSource stopped = new();
    private readonly Task[] listeners;
    private int disposed;
    internal Task Completion => Task.WhenAll(listeners);
    internal NotificationServer(Func<LaunchStatus, Task<bool>> accept, string? name = null)
        => listeners = Enumerable.Range(0, 4).Select(_ => Task.Run(() => ListenAsync(accept, name))).ToArray();
    private async Task ListenAsync(Func<LaunchStatus, Task<bool>> accept, string? name)
    {
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                using var pipe = NotificationWire.CreateServer(name);
                await pipe.WaitForConnectionAsync(stopped.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token); timeout.CancelAfter(2000);
                if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out int pid)) continue;
                var peer = Native.GetIdentity(pid);
                if (!NotificationWire.IsPeer(peer)) continue;
                var status = await NotificationWire.ReadAsync(pipe, timeout.Token);
                if (status.OwnerPid != pid || status.OwnerCreated != peer.Created) continue;
                bool accepted = await accept(status).WaitAsync(timeout.Token);
                await Protocol.WriteAsync(pipe, accepted ? "ACCEPTED" : "REJECTED", timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch { if (!stopped.IsCancellationRequested) await Task.Delay(100); }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopped.Cancel();
        _ = Task.WhenAll(listeners).ContinueWith(_ => stopped.Dispose(), TaskScheduler.Default);
    }
}
