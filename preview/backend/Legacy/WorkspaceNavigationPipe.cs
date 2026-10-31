using System.Diagnostics;
using System.IO.Pipes;
namespace RobloxLaunchHelper;

// Only navigation/refresh messages cross this channel. No paths or executable commands.
internal sealed class WorkspaceNavigationPipe : IDisposable
{
    private static string Name => Program.Product + "-Workspace-" + Program.Sid + "-" + Process.GetCurrentProcess().SessionId;
    private readonly CancellationTokenSource stopped = new();
    private readonly Task pump;
    internal WorkspaceNavigationPipe(Func<bool,Task> showUpdates) => pump = Task.Run(() => RunAsync(showUpdates));
    private async Task RunAsync(Func<bool,Task> show)
    {
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                using var server = NotificationWire.CreateServer(Name);
                await server.WaitForConnectionAsync(stopped.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token); deadline.CancelAfter(2000);
                if (!Native.GetNamedPipeClientProcessId(server.SafePipeHandle,out int pid) || !NotificationWire.IsPeer(Native.GetIdentity(pid))) continue;
                var bytes = new byte[1]; await server.ReadExactlyAsync(bytes,deadline.Token);
                if (bytes[0] > 1) continue;
                await show(bytes[0] == 1).WaitAsync(deadline.Token);
                await server.WriteAsync(new byte[]{1},deadline.Token);
            }
            catch (OperationCanceledException) { }
            catch { try { await Task.Delay(250,stopped.Token); } catch (OperationCanceledException) { } }
        }
    }
    internal static async Task<bool> SendAsync(bool passive)
    {
        try
        {
            using var deadline = new CancellationTokenSource(1500);
            using var client = new NamedPipeClientStream(".",Name,PipeDirection.InOut,PipeOptions.Asynchronous);
            await client.ConnectAsync(deadline.Token);
            if (!Native.GetNamedPipeServerProcessId(client.SafePipeHandle,out int pid) || !NotificationWire.IsPeer(Native.GetIdentity(pid))) return false;
            await client.WriteAsync(new byte[]{passive ? (byte)1 : (byte)0},deadline.Token);
            var ack = new byte[1]; await client.ReadExactlyAsync(ack,deadline.Token); return ack[0] == 1;
        }
        catch { return false; }
    }
    public void Dispose() => stopped.Cancel();
}
