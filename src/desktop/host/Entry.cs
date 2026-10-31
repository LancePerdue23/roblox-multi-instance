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
        if(Array.IndexOf(args,"--launch-notifications")>=0)return RobloxLaunchHelper.LaunchNotificationHost.Run();
        bool updates=Array.IndexOf(args,"--updates")>=0;
        if(Array.IndexOf(args,"--check-updates")>=0)
        {
            try
            {
                using var http=RobloxLaunchHelper.UpdateHost.CreateClient();using var timeout=new CancellationTokenSource(30000);
                var release=new RobloxLaunchHelper.GitHubReleaseSource(http).LatestAsync(timeout.Token).GetAwaiter().GetResult();
                var installed=new RobloxLaunchHelper.UpdateVersionProvider().Current();
                if(release is null||installed is null||release.Version<=installed)return 0;
                updates=true;
            }
            catch{return 0;}
        }
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string name = "RobloxMultiInstance-" + sid + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (Array.IndexOf(args, "--smoke") >= 0) name += "-smoke";
        using var owner = new Mutex(false, @"Local\" + name);
        bool acquired;
        try { acquired = owner.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out); pipe.Connect(1500); pipe.WriteByte(updates?(byte)2:(byte)1); }
            catch { MessageBox.Show("The preview is already running. Open it from its taskbar or tray icon.", "Roblox Multi Instance"); }
            return 0;
        }
        using var stop = new CancellationTokenSource();
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(Array.IndexOf(args, "--smoke") >= 0);
            if(updates)window.RequestUpdates();
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
                        if (await pipe.ReadAsync(message, deadline.Token) == 1 && message[0] is 1 or 2)
                            _ = app.Dispatcher.BeginInvoke(()=>{window.Reopen();if(message[0]==2)window.RequestUpdates();});
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
