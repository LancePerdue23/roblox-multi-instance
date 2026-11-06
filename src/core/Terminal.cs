using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RobloxLaunchHelper;

internal static class Terminal
{
    internal const string Help="""
        rmi — Roblox Multi Instance
        menu (guided interactive CLI; also opens when rmi runs without arguments in a terminal)
        launch [Roblox URI]
        setup|repair|recover [--installer PATH] [--player PATH] [--disable-integration] [--yes]
        uninstall [--remove-data] [--yes]
        status | diagnostics | worker-test
        integration on|off [--yes]
        settings show|reset|set SCHEME [--yes]
        history list|clear|export PATH [--yes]
        report export PATH
        telemetry status|on|off|clear|send [--yes]
        updates check|download|install [--player PATH] [--yes]
        Global: --json, --help
        Fresh setup enables browser Play integration unless --disable-integration is given.
        Existing integration preferences are preserved. Specify --player when discovery is ambiguous.
        Exit codes: 0 success, 1 failure, 2 usage, 3 warning/partial, 4 unknown, 1223 canceled.
        """;
    static readonly JsonSerializerOptions json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}};
    internal static bool ValidCommand(string[] args)=>args.Length>0&&new[]{"launch","setup","repair","recover","uninstall","status","diagnostics","worker-test","integration","settings","history","report","updates","telemetry"}.Contains(args[0]);
    internal static async Task<int> RunAsync(string[] args)
    {
        if(args.Length==1&&args[0]=="menu"||args.Length==0&&!Console.IsInputRedirected&&!Console.IsOutputRedirected)
            return await TerminalMenu.RunAsync();
        bool machine=args.Contains("--json");args=args.Where(x=>x!="--json").ToArray();
        if(args.Length==0||args.Contains("--help")||args[0]=="help"){Console.Out.WriteLine(Help);return 0;}
        if(!ValidCommand(args)){var invalid=new OperationResult(OperationOutcome.Failed,"INVALID_USAGE","Unknown command. Use rmi --help.");(machine?Console.Out:Console.Error).WriteLine(machine?JsonSerializer.Serialize(invalid,json):invalid.Message);return 2;}
        using var cancellation=new CancellationTokenSource();
        ConsoleCancelEventHandler handler=(_,e)=>{e.Cancel=true;cancellation.Cancel();};Console.CancelKeyPress+=handler;
        try
        {
            object data=await ExecuteAsync(args,cancellation.Token);
            var result=data as OperationResult;
            var output=result?.Outcome is OperationOutcome.Failed or OperationOutcome.Unknown?Console.Error:Console.Out;
            if(machine)Console.Out.WriteLine(JsonSerializer.Serialize(data,json));else if(result is not null)output.WriteLine(result.Message);else Console.Out.WriteLine(TerminalMenu.Format(data));
            return result?.ExitCode??0;
        }
        catch(ArgumentException ex){if(machine)Console.Out.WriteLine(JsonSerializer.Serialize(new OperationResult(OperationOutcome.Failed,"INVALID_USAGE",ex.Message),json));else Console.Error.WriteLine(ex.Message+" Use rmi --help.");return 2;}
        catch(OperationCanceledException){var result=new OperationResult(OperationOutcome.Canceled,"CANCELED","Operation canceled. Any game already started remains running.");(machine?Console.Out:Console.Error).WriteLine(machine?JsonSerializer.Serialize(result,json):result.Message);return 1223;}
        catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){var result=new OperationResult(OperationOutcome.Canceled,"CANCELED","Administrator approval canceled.");(machine?Console.Out:Console.Error).WriteLine(machine?JsonSerializer.Serialize(result,json):result.Message);return 1223;}
        catch(Exception ex){Log.Write("terminal-failed",ex.GetType().Name,ex.HResult);var result=new OperationResult(OperationOutcome.Failed,"OPERATION_FAILED",ex is UserError?ex.Message:"The operation could not complete. Run rmi diagnostics.");(machine?Console.Out:Console.Error).WriteLine(machine?JsonSerializer.Serialize(result,json):result.Message);return 1;}
        finally {Console.CancelKeyPress-=handler;}
    }
    static string? Option(string[] args,string name){int index=Array.IndexOf(args,name);if(index<0)return null;if(index+1>=args.Length||args[index+1].StartsWith("--"))throw new ArgumentException("Missing value for "+name);return args[index+1];}
    static void Confirm(string[] args,string message)
    {
        if(args.Contains("--yes"))return;
        if(Console.IsInputRedirected)throw new ArgumentException("Confirmation required. Review the action and supply --yes for redirected input.");
        Console.Error.Write(message+" [y/N] ");if(!string.Equals(Console.ReadLine(),"y",StringComparison.OrdinalIgnoreCase))throw new OperationCanceledException();
    }
    internal static async Task<object> ExecuteAsync(string[] args,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();ValidateArguments(args);var operands=new List<string>();for(int i=1;i<args.Length;i++){if(args[i] is "--installer" or "--player"){i++;continue;}if(!args[i].StartsWith("--"))operands.Add(args[i]);}string command=args[0],sub=operands.FirstOrDefault()??"";
        switch(command)
        {
            case "launch": if(sub!=""&&!Launch.IsRobloxUri(sub))throw new ArgumentException("Expected a Roblox URI.");return await Launch.RunResultAsync(sub==""?null:sub,token);
            case "status": case "diagnostics":
                var health=DiagnosticChecks.Collect();return new { readiness=HelperReadiness.Evaluate(health,DiagnosticChecks.NotTested,File.Exists(Program.InstalledExe)),health };
            case "worker-test":
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(35000);using var pipe=await Launch.ConnectAsync(deadline.Token);await Protocol.WriteAsync(pipe,"PROBE",deadline.Token);bool ready=await Protocol.ReadAsync(pipe,deadline.Token)=="OK";return new OperationResult(ready?OperationOutcome.Succeeded:OperationOutcome.Failed,ready?"WORKER_READY":"WORKER_FAILED",ready?"Authenticated worker responded. Game readiness was not tested.":"Worker probe failed.");}
            case "integration":if(sub is not ("on" or "off"))throw new ArgumentException("Specify integration on or off.");Confirm(args,"Change browser Play integration?");await BrowserIntegration.SetAsync(sub=="on",token);return new OperationResult(OperationOutcome.Succeeded,"INTEGRATION_CHANGED","Browser integration updated.");
            case "setup": case "repair": case "recover":
                Confirm(args,"Run "+command+"? Windows will request administrator approval.");
                string installer=Option(args,"--installer")??Path.Combine(AppContext.BaseDirectory,"Installer.exe");
                if(!File.Exists(installer)){string bundled=Path.Combine(AppContext.BaseDirectory,Distribution.SetupName("Desktop"));installer=File.Exists(bundled)?bundled:throw new UserError("Supply --installer with the setup executable for your chosen edition.");}
                var start=new ProcessStartInfo(installer){UseShellExecute=true};foreach(string arg in new[]{"--silent","--action",command})start.ArgumentList.Add(arg);
                foreach(string opt in new[]{"--player"})if(Option(args,opt) is string value){start.ArgumentList.Add(opt);start.ArgumentList.Add(value);}
                if(args.Contains("--disable-integration"))start.ArgumentList.Add("--disable-integration");
                if(string.Equals(Environment.ProcessPath,Path.Combine(Program.InstallDirectory,"rmi.exe"),StringComparison.OrdinalIgnoreCase)){
                    var options=start.ArgumentList.Skip(3).Prepend("--silent").ToArray();
                    await UpdateSetupHandoff.StartMaintenanceAsync(installer,command,options);return new OperationResult(OperationOutcome.Warning,"SETUP_HANDOFF","Setup accepted the request. This terminal will exit so setup can proceed.");
                }
                using(var child=Process.Start(start)??throw new IOException()){await child.WaitForExitAsync(CancellationToken.None);return OperationResult.FromExit(child.ExitCode);}
            case "uninstall":
                Confirm(args,args.Contains("--remove-data")?"Uninstall and permanently remove this Windows account's app settings and history?":"Uninstall and retain settings/history?");
                var uninstall=await Distribution.RunInstalledAsync("uninstall",token);
                if(uninstall.Outcome==OperationOutcome.Succeeded&&args.Contains("--remove-data"))RemoveUserData();return uninstall;
            case "settings":
                string preferenceRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Distribution.Installed()?.Edition=="Desktop"?"RobloxMultiInstance":Program.Product);
                var appearance=new AppearancePreferences(Path.Combine(preferenceRoot,"appearance.json"));
                if(sub=="show")return new { scheme=appearance.Scheme.ToString(),appearance.Available,browserIntegration=Installation.ReadConfig().BrowserIntegrationEnabled };
                Confirm(args,"Change application settings?");
                if(sub=="reset"){appearance.Save(ColorScheme.Dark,true);new LayoutStore(Path.Combine(preferenceRoot,"window-layouts.json")).Save(LayoutSettings.Default);}
                else if(sub=="set"&&Enum.TryParse<ColorScheme>(operands.ElementAtOrDefault(1),out var scheme)&&Enum.IsDefined(scheme))appearance.Save(scheme);
                else throw new ArgumentException("Use settings show, reset, or set SCHEME.");
                return new OperationResult(OperationOutcome.Succeeded,"SETTINGS_SAVED","Settings saved.");
            case "history":
                var history=new DiagnosticHistory();bool available=history.Read(out var records);
                if(sub=="list")return new { available,records };
                if(sub=="clear"){Confirm(args,"Clear launch history?");if(!history.Clear())throw new IOException();return new OperationResult(OperationOutcome.Succeeded,"HISTORY_CLEARED","History cleared.");}
                if(sub=="export"&&operands.ElementAtOrDefault(1) is string historyPath){if(!available)throw new UserError("History is unavailable.");WriteNew(historyPath,JsonSerializer.SerializeToUtf8Bytes(records,json));return new OperationResult(OperationOutcome.Succeeded,"HISTORY_EXPORTED","History exported.");}
                throw new ArgumentException("Use history list, clear, or export PATH.");
            case "report":
                if(sub!="export"||operands.ElementAtOrDefault(1) is not string reportPath)throw new ArgumentException("Use report export PATH.");
                var store=new DiagnosticHistory();bool readable=store.Read(out var launches);if(File.Exists(reportPath))throw new ArgumentException("Destination already exists.");DiagnosticReports.Write(Path.GetFullPath(reportPath),DiagnosticReports.Build(DiagnosticChecks.Collect(),launches,readable));return new OperationResult(OperationOutcome.Succeeded,"REPORT_EXPORTED","Troubleshooting report exported.");
            case "telemetry":
                var telemetry=TelemetryService.Default;
                if(sub=="status")return telemetry.Status();
                if(sub=="off")return RequireTelemetrySaved(telemetry.SetEnabled(false));
                if(sub=="clear")return RequireTelemetrySaved(telemetry.Clear());
                if(sub=="on"){Confirm(args,TelemetryService.Disclosure+" Enable optional reporting?");return RequireTelemetrySaved(telemetry.SetEnabled(true));}
                if(sub=="send"){
                    var before=telemetry.Status();
                    if(!before.Enabled)throw new UserError("Optional reporting is off. Enable it explicitly with rmi telemetry on first.");
                    if(!before.Configured)throw new UserError("This build has no reporting endpoint configured. No summary was sent.");
                    var sent=await telemetry.SendAsync(token);
                    if(sent.QueuedErrors>0)return new OperationResult(OperationOutcome.Warning,"TELEMETRY_PENDING",sent.LastResult??"Some errors remain queued.");
                    return sent;
                }
                throw new ArgumentException("Use telemetry status, on, off, clear, or send.");
            case "updates":
                if(sub is not ("check" or "download" or "install"))throw new ArgumentException("Use updates check, download, or install.");
                using(var http=UpdateHost.CreateClient())
                {
                    var release=await new GitHubReleaseSource(http).LatestAsync(token);
                    if(release is null)return new OperationResult(OperationOutcome.Succeeded,"NO_RELEASE","No update release is available.");
                    if(sub=="check")return new { version=release.Version.ToString(),release.Notes,release.DownloadProblem,page=release.Page.ToString() };
                    if(Distribution.Installed() is null)throw new UserError("Upgrade this legacy installation explicitly with setup --installer PATH. Choose the desktop or helper edition from "+release.Page);
                    if(new UpdateVersionProvider().Current() is Version installed&&release.Version<=installed)return new OperationResult(OperationOutcome.Succeeded,"UP_TO_DATE","The installed edition is up to date.");
                    if(release.Asset is null)throw new UserError(release.DownloadProblem+" "+release.Page);
                    if(sub=="install")Confirm(args,"Download and install this edition's update? The terminal will exit before setup.");
                    using var downloaded=await new UpdateDownloader(http,UpdateFiles.Staging).DownloadAsync(release,null,token);
                    if(sub=="download")return new { executable=downloaded.Executable,version=release.Version.ToString(),message="Verified download retained. Use updates install to install the current verified release." };
                    var options=new List<string>{"--silent"};if(Option(args,"--player") is string player){options.Add("--player");options.Add(player);}
                    await new UpdateSetupHandoff().StartAsync(downloaded.Executable,release,options);return new OperationResult(OperationOutcome.Warning,"SETUP_HANDOFF","Setup accepted the handoff. This terminal will exit; run rmi status afterward to verify installation. Setup errors are retained in setup logs.");
                }
            default:throw new ArgumentException("Unknown command.");
        }
    }
    internal static void ValidateArguments(string[] args)
    {
        if(!ValidCommand(args))throw new ArgumentException("Unknown command.");
        var positional=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int i=1;i<args.Length;i++)
        {
            string value=args[i];if(!value.StartsWith("--")){positional.Add(value);continue;}
            bool allowed=value switch {"--yes"=>args[0] is "setup" or "repair" or "recover" or "uninstall" or "integration" or "settings" or "history" or "updates" or "telemetry","--player"=>args[0] is "setup" or "repair" or "recover" or "updates","--installer" or "--disable-integration"=>args[0] is "setup" or "repair" or "recover","--remove-data"=>args[0]=="uninstall",_=>false};
            if(!allowed||!seen.Add(value))throw new ArgumentException("Unsupported or duplicate option: "+value);
            if(value is "--installer" or "--player"){if(++i>=args.Length||args[i].StartsWith("--"))throw new ArgumentException("Missing value for "+value);}
        }
        int count=positional.Count;
        bool valid=args[0] switch {
            "launch"=>count<=1,
            "setup" or "repair" or "recover" or "uninstall" or "status" or "diagnostics" or "worker-test"=>count==0,
            "integration" or "updates"=>count==1,
            "telemetry"=>count==1&&positional[0] is "status" or "on" or "off" or "clear" or "send",
            "settings"=>count==1&&positional[0] is "show" or "reset"||count==2&&positional[0]=="set",
            "history"=>count==1&&positional[0] is "list" or "clear"||count==2&&positional[0]=="export",
            "report"=>count==2&&positional[0]=="export",_=>false };
        if(!valid)throw new ArgumentException("Invalid command arguments.");
    }
    static void WriteNew(string path,byte[] bytes){using var file=new FileStream(Path.GetFullPath(path),FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(bytes);}
    static TelemetryStatus RequireTelemetrySaved(TelemetryStatus state)
    {
        if(!state.Available)throw new UserError(state.LastResult??"The reporting setting could not be saved. Check telemetry status and retry.");
        return state;
    }
    internal static void RemoveUserData()
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(string name in new[]{Program.Product,"RobloxMultiInstance"}){string target=Path.GetFullPath(Path.Combine(local,name));if(Path.GetDirectoryName(target)!=Path.GetFullPath(local))throw new IOException();if(Directory.Exists(target)){UpdateFiles.RejectLinks(target);foreach(string entry in Directory.EnumerateFileSystemEntries(target,"*",SearchOption.AllDirectories))UpdateFiles.RejectLinks(entry);Directory.Delete(target,true);}}
    }
}
