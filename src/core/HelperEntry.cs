using System.Diagnostics;
namespace RobloxLaunchHelper;

internal static class HelperEntry
{
    internal static int Run(string[] args)
    {
        try
        {
            string mode=args.FirstOrDefault()??"";
            if(mode=="--worker"){if(args.Length!=1||!Program.Elevated||!Program.IsInstalledExecutable())return 2;_ = Installation.ReadConfig();return Worker.RunAsync().GetAwaiter().GetResult();}
            if(mode=="--verify")return Launch.VerifyAsync().GetAwaiter().GetResult();
            if(mode=="--verify-protocols"){if(args.Length!=1||Program.Elevated||!Program.IsInstalledExecutable())return 2;Installation.VerifyProtocolRegistration();return 0;}
            if(mode=="--shortcuts"){if(args.Length!=2||args[1] is not ("install" or "remove")||!Program.IsInstalledExecutable())return 2;Installation.Shortcuts(args[1]=="remove");return 0;}
            if(mode=="--admin-clean"){if(args.Length!=3||!Program.Elevated||args[2]!=Program.Sid||!Program.IsInstalledExecutable())return 2;SetupDiagnostics.Begin();SetupDiagnostics.Step(SetupStage.VerificationTask);Installation.Execute(args[1],args[2],silent:true);return 0;}
            if(mode=="--admin"){if(args.Length!=3||!Program.Elevated||args[2]!=Program.Sid)return 2;return Installation.ExecuteInWindowsContext(args[1],args[2]);}
            if(mode.StartsWith("--")&&Installation.IsAction(mode[2..]))
            {
                if(args.Length!=1)return 2;
                if(mode=="--uninstall"&&!Program.Elevated)UpdateShutdownRequest.CloseManagers();
                if(Program.Elevated)return Installation.ExecuteInWindowsContext(mode[2..],Program.Sid);
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};start.ArgumentList.Add("--admin");start.ArgumentList.Add(mode[2..]);start.ArgumentList.Add(Program.Sid);
                using var child=Process.Start(start)??throw new IOException();child.WaitForExit();return child.ExitCode;
            }
            if(mode is "" or "--diagnostics" or "--updates")
            {
                string desktop=Path.Combine(Program.InstallDirectory,"RobloxMultiInstance.exe");
                if(!File.Exists(desktop))return 2;
                Process.Start(new ProcessStartInfo(desktop){UseShellExecute=false})?.Dispose();return 0;
            }
            if(args.Length!=1||mode!="--launch-roblox"&&!Launch.IsRobloxUri(mode))return 2;
            return Launch.RunAsync(mode=="--launch-roblox"?null:mode).GetAwaiter().GetResult();
        }
        catch(OperationCanceledException){return 1223;}
        catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){return 1223;}
        catch(Exception ex){if(args.FirstOrDefault()=="--admin-clean")SetupDiagnostics.Failure(ex);Log.Write("operation-failed",ex.GetType().Name,ex.HResult);return 1;}
    }
}
