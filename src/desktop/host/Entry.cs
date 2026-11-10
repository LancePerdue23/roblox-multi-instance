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
        RobloxLaunchHelper.UpdateCredentialMigration.RemoveOwnedCredential();
        int result=Run(args);
        if(result!=72)return result;
        // Run has released the single-instance mutex before launching the replacement.
        try {
            var restart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,ArgumentList={"--safe-rendering"}};
            if(BadgeActivation.Parse(args) is string claim){restart.ArgumentList.Add("--claim-badge");restart.ArgumentList.Add(claim);}
            System.Diagnostics.Process.Start(restart)?.Dispose();return 0;
        }
        catch { MessageBox.Show("Restart failed. Reopen the app with --safe-rendering to retry.","Roblox Multi Instance");return 1; }
    }
    static int Run(string[] args)
    {
        if(Array.IndexOf(args,"--launch-notifications")>=0)return RobloxLaunchHelper.LaunchNotificationHost.Run();
        // Record entry before backend construction/cleanup, without activation links or tickets.
        using(var current=System.Diagnostics.Process.GetCurrentProcess())
            RobloxLaunchHelper.Log.Write("desktop-entry", "createdUtc="+current.StartTime.ToUniversalTime().ToString("O"),current.Id);
        string? badgePath;try{badgePath=BadgeActivation.Parse(args);}catch(Exception ex){MessageBox.Show(ex.Message,"Recognition file");return 1;}
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
        if(Array.IndexOf(args,"--supporter-preview")>=0)name+="-supporter-preview";
        using var owner = new Mutex(false, @"Local\" + name);
        bool acquired;
        try { acquired = owner.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out); pipe.Connect(1500); if(badgePath is not null)BadgeActivation.Write(pipe,badgePath);else pipe.WriteByte(updates?(byte)2:(byte)1); }
            catch { MessageBox.Show("The preview is already running. Open it from its taskbar or tray icon.", "Roblox Multi Instance"); }
            return 0;
        }
        using var stop = new CancellationTokenSource();
        try
        {
            bool safeRendering=Array.IndexOf(args,"--safe-rendering")>=0;
            if(safeRendering)System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(Array.IndexOf(args, "--smoke") >= 0,safeRendering,Array.IndexOf(args,"--supporter-preview")>=0);
            if(updates)window.RequestUpdates();
            if(badgePath is not null)window.RequestBadgeClaim(badgePath);
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
                        if (await pipe.ReadAsync(message, deadline.Token) == 1)
                        {
                            string? claim=message[0]==3?await BadgeActivation.ReadAsync(pipe,deadline.Token):null;
                            if(message[0] is 1 or 2 or 3)
                                _ = app.Dispatcher.BeginInvoke(()=>{window.Reopen();if(message[0]==2)window.RequestUpdates();if(claim is not null)window.RequestBadgeClaim(claim);});
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { }
                    catch (ArgumentException) { }
                }
            });
            int result=app.Run(window);return window.RestartInSafeRendering?72:result;
        }
        finally { stop.Cancel(); owner.ReleaseMutex(); }
    }
}
