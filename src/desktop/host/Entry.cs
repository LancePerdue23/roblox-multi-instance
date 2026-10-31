using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RobloxMultiInstance.Desktop;

internal static class Entry
{
    [STAThread]
    public static int Main(string[] args)
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string name = "RobloxMultiInstance-" + sid + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (Array.IndexOf(args, "--smoke") >= 0) name += "-smoke";
        using var owner = new Mutex(false, @"Local\" + name);
        bool acquired;
        try { acquired = owner.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out); pipe.Connect(1500); pipe.WriteByte(1); }
            catch { MessageBox.Show("The preview is already running. Open it from its taskbar or tray icon.", "Roblox Multi Instance"); }
            return 0;
        }
        using var stop = new CancellationTokenSource();
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(Array.IndexOf(args, "--smoke") >= 0);
            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await pipe.WaitForConnectionAsync(stop.Token);
                        byte[] message = new byte[1];
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        deadline.CancelAfter(1500);
                        if (await pipe.ReadAsync(message, deadline.Token) == 1 && message[0] == 1)
                            _ = app.Dispatcher.BeginInvoke(window.Reopen);
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { }
                }
            });
            return app.Run(window);
        }
        finally { stop.Cancel(); owner.ReleaseMutex(); }
    }
}
