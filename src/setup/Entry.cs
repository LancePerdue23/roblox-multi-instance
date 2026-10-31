using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using RobloxLaunchHelper;
using HelperProgram=RobloxLaunchHelper.Program;

namespace RobloxMultiInstance.Setup;

internal static class Entry
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        try
        {
            if(args.Contains("--uninstall")){using var uninstall=new UninstallForm();Application.Run(uninstall);return uninstall.Result;}
            if(args.FirstOrDefault()=="--maintenance-handoff")return MaintenanceHandoff(args);
            if(args.FirstOrDefault()=="--update-handoff")return Handoff(args);
            if(args.Contains("--apply"))
            {
                if(!HelperProgram.Elevated||Option(args,"--owner")!=HelperProgram.Sid)return 2;
                return Apply(args);
            }
            if(args.Contains("--inspect")){using var payload=OpenPayload();using var zip=new ZipArchive(payload,ZipArchiveMode.Read);Inspect(zip,Option(args,"--expect-edition"));return 0;}
            if(args.Contains("--silent"))return Elevate(args);
            using var form=new SetupForm(args);Application.Run(form);return form.Result;
        }
        catch(OperationCanceledException){return 1223;}
        catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){return 1223;}
        catch(Exception ex){if(!args.Contains("--silent")&&!args.Contains("--apply"))MessageBox.Show(ex is UserError?ex.Message:"Setup could not complete. Check setup logs and retry.","Roblox Multi Instance setup");Log.Write("edition-setup-failed",ex.GetType().Name,ex.HResult);return 1;}
    }
    internal static string? Option(string[] args,string key){int i=Array.IndexOf(args,key);if(i<0)return null;if(i+1>=args.Length)throw new ArgumentException();return args[i+1];}
    internal static Stream OpenPayload()=>Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")??throw new UserError("This setup build does not contain a payload. Build installers with Package.ps1.");
    internal static EditionManifest ReadManifest(ZipArchive zip)
    {
        var entry=zip.GetEntry("payload.json")??throw new InvalidDataException();if(entry.Length>1024*1024)throw new InvalidDataException();using var file=entry.Open();return JsonSerializer.Deserialize<EditionManifest>(file)??throw new InvalidDataException();
    }
    static void Inspect(ZipArchive zip,string? expected)
    {
        var manifest=ReadManifest(zip);Distribution.Validate(manifest);
        if(UpdateVersion.Parse(manifest.Version)!=Assembly.GetExecutingAssembly().GetName().Version)throw new InvalidDataException("Setup and payload versions differ.");
        if(expected is not null&&manifest.Edition!=expected)throw new InvalidDataException("Unexpected setup edition.");
        var files=manifest.Files.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries){if(!seen.Add(entry.FullName))throw new InvalidDataException();if(entry.FullName=="payload.json")continue;if(!files.TryGetValue(entry.FullName,out var file)||entry.Length!=file.Size)throw new InvalidDataException();using var stream=entry.Open();if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();}
        if(seen.Count!=files.Count+1)throw new InvalidDataException("Incomplete payload.");
    }
    internal static int Elevate(string[] args)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};
        foreach(string arg in args.Where(x=>x!="--silent"))start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--apply");start.ArgumentList.Add("--owner");start.ArgumentList.Add(HelperProgram.Sid);
        using var child=Process.Start(start)??throw new IOException();child.WaitForExit();return child.ExitCode;
    }
    static int Apply(string[] args)
    {
        string action=Option(args,"--action")??"setup";if(action is not ("setup" or "repair" or "recover"))throw new ArgumentException();
        using var input=OpenPayload();using var zip=new ZipArchive(input,ZipArchiveMode.Read);var manifest=ReadManifest(zip);Distribution.Validate(manifest);
        // Extract only the compiled manifest allowlist. Reject duplicate, unexpected, or oversized zip members.
        var expected=manifest.Files.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string staging=Path.Combine(HelperProgram.InstallRoot,"staging-"+HelperProgram.Sid+"-"+Guid.NewGuid().ToString("N"));
        Installation.PrepareProtectedDirectory(staging);
        try
        {
            foreach(var entry in zip.Entries)
            {
                if(!seen.Add(entry.FullName))throw new InvalidDataException("Duplicate payload member.");
                if(entry.FullName=="payload.json")continue;
                if(!expected.TryGetValue(entry.FullName,out var item)||entry.Length!=item.Size)throw new InvalidDataException("Unexpected payload member.");
                string target=Path.GetFullPath(Path.Combine(staging,item.Path));if(!target.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
                Installation.PrepareProtectedDirectory(Path.GetDirectoryName(target)!);using var source=entry.Open();using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);source.CopyTo(output);if(output.Length!=item.Size)throw new InvalidDataException();
            }
            Distribution.VerifyPayload(staging,manifest);
            Installation.InstallEdition(staging,manifest,!args.Contains("--disable-integration"),Option(args,"--player"),action);
            return 0;
        }
        finally
        {
            // Staging is generated beneath the protected install root and never follows external paths.
            string full=Path.GetFullPath(staging);if(Path.GetDirectoryName(full)==Path.GetFullPath(HelperProgram.InstallRoot)&&Directory.Exists(full)){UpdateFiles.RejectLinks(full);Directory.Delete(full,true);}
        }
    }
    static int Handoff(string[] args)
    {
        if(args.Length<6||HelperProgram.Elevated||!int.TryParse(args[1],out int pid)||!long.TryParse(args[2],out long created)||UpdateVersion.Parse(args[3]) is not Version version||!long.TryParse(args[4],out long size))return 2;
        using var parent=Process.GetProcessById(pid);
        var id=Native.GetIdentity(pid);string[] allowed=[HelperProgram.InstalledExe,Path.Combine(HelperProgram.InstallDirectory,"RobloxMultiInstance.exe"),Path.Combine(HelperProgram.InstallDirectory,"rmi.exe")];
        if(parent.StartTime.ToUniversalTime().Ticks!=created||id.Sid!=HelperProgram.Sid||id.Elevated||id.Session!=Process.GetCurrentProcess().SessionId||!allowed.Contains(id.Image,StringComparer.OrdinalIgnoreCase))return 2;
        using(var file=File.OpenRead(Environment.ProcessPath!)){if(file.Length!=size||!Convert.ToHexString(SHA256.HashData(file)).Equals(args[5],StringComparison.OrdinalIgnoreCase))return 2;}
        using(var payload=OpenPayload())using(var zip=new ZipArchive(payload,ZipArchiveMode.Read)){var manifest=ReadManifest(zip);Distribution.Validate(manifest);if(UpdateVersion.Parse(manifest.Version)!=version||Distribution.Installed()?.Edition!=manifest.Edition)return 2;}
        using(var ready=EventWaitHandle.OpenExisting(UpdateSetupHandoff.ReadyName(pid,created)))ready.Set();
        if(!parent.WaitForExit(30000))throw new UserError("The previous app has not closed. Close it and retry setup.");
        UpdateShutdownRequest.CloseManagers();
        var options=args.Skip(6).ToArray();
        if(options.Contains("--silent"))return Elevate(options);
        using var form=new SetupForm(options);Application.Run(form);return form.Result;
    }
    static int MaintenanceHandoff(string[] args)
    {
        if(args.Length<4||HelperProgram.Elevated||!int.TryParse(args[1],out int pid)||!long.TryParse(args[2],out long created)||args[3] is not ("setup" or "repair" or "recover"))return 2;
        using var parent=Process.GetProcessById(pid);var id=Native.GetIdentity(pid);
        if(parent.StartTime.ToUniversalTime().Ticks!=created||id.Sid!=HelperProgram.Sid||id.Elevated||id.Session!=Process.GetCurrentProcess().SessionId||!new[]{Path.Combine(HelperProgram.InstallDirectory,"RobloxMultiInstance.exe"),Path.Combine(HelperProgram.InstallDirectory,"rmi.exe")}.Contains(id.Image,StringComparer.OrdinalIgnoreCase))return 2;
        using(var payload=OpenPayload())using(var zip=new ZipArchive(payload,ZipArchiveMode.Read)){var manifest=ReadManifest(zip);Distribution.Validate(manifest);if(Distribution.Installed()?.Edition!=manifest.Edition)return 2;}
        using(var ready=EventWaitHandle.OpenExisting(UpdateSetupHandoff.ReadyName(pid,created)))ready.Set();
        if(!parent.WaitForExit(30000))throw new UserError("The previous app has not closed. Close it and retry setup.");
        UpdateShutdownRequest.CloseManagers();
        var options=new[]{"--action",args[3]}.Concat(args.Skip(4)).ToArray();
        if(options.Contains("--silent"))return Elevate(options);
        using var form=new SetupForm(options);Application.Run(form);return form.Result;
    }
}

internal sealed class UninstallForm : Form
{
    internal int Result=1223;
    bool running;
    internal UninstallForm()
    {
        Text="Uninstall Roblox Multi Instance";Width=650;Height=280;StartPosition=FormStartPosition.CenterScreen;
        var root=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(24),FlowDirection=FlowDirection.TopDown,WrapContents=false};
        root.Controls.Add(new Label{Text="Remove the installed app, helper, and owned browser integration? Roblox games will remain open.",MaximumSize=new(560,0),AutoSize=true});
        var data=new CheckBox{Text="Also permanently remove settings and launch history",AutoSize=true};root.Controls.Add(data);
        var status=new Label{Text="Settings and history are retained unless you select the option above.",MaximumSize=new(560,0),AutoSize=true};root.Controls.Add(status);
        var uninstall=new Button{Text="Uninstall",AutoSize=true};var close=new Button{Text="Cancel",AutoSize=true};var row=new FlowLayoutPanel{AutoSize=true};row.Controls.Add(uninstall);row.Controls.Add(close);root.Controls.Add(row);Controls.Add(root);
        close.Click+=(_,_)=>Close();
        uninstall.Click+=async (_,_)=>{
            if(running)return;running=true;uninstall.Enabled=false;close.Enabled=false;data.Enabled=false;
            try{
                UpdateShutdownRequest.CloseManagers();status.Text="Approve Windows administrator access to remove helper integration…";
                var result=await Distribution.RunInstalledAsync("uninstall",CancellationToken.None);Result=result.ExitCode;
                if(result.Outcome==OperationOutcome.Succeeded){if(data.Checked)Terminal.RemoveUserData();status.Text="Integration removed. Close this window to allow installed files to be removed.";uninstall.Visible=false;close.Text="Close";}else{status.Text=result.Message;uninstall.Text="Retry uninstall";}
            }
            catch(Exception ex){Result=1;status.Text=ex is UserError?ex.Message:"Uninstall did not complete. Check System health and retry.";}
            finally{running=false;uninstall.Enabled=true;close.Enabled=true;data.Enabled=true;}
        };
        FormClosing+=(_,e)=>{if(running)e.Cancel=true;};
    }
}

internal sealed class SetupForm : Form
{
    readonly CheckBox integration=new(){Text="Use helper for browser Play links",Checked=true,AutoSize=true};
    readonly ComboBox players=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=650};
    readonly Label status=new(){Text="Setup installs only after you select Install and approve Windows elevation.",AutoSize=true,MaximumSize=new(650,0)};
    readonly Button install=new(){Text="Install",AutoSize=true};
    readonly string[] args;
    bool running;
    internal int Result=1223;
    internal SetupForm(string[] args)
    {
        this.args=args;Text="Roblox Multi Instance setup";Width=730;Height=380;StartPosition=FormStartPosition.CenterScreen;
        var root=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(24),AutoScroll=true};
        using(var stream=Entry.OpenPayload())using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){var manifest=Entry.ReadManifest(zip);Distribution.Validate(manifest);root.Controls.Add(new Label{Text=(manifest.Edition=="Desktop"?"Roblox Multi Instance + helper":"Roblox Launch Helper")+" "+manifest.Version,AutoSize=true,Font=new Font(Font.FontFamily,16)});}
        bool existing=File.Exists(HelperProgram.ConfigPath);if(existing){try{integration.Checked=Installation.ReadConfig().BrowserIntegrationEnabled;}catch{}integration.Enabled=false;status.Text="Your existing settings, history, and browser integration preference will be preserved.";}
        var candidates=PlayerRecovery.InstalledPlayers().Where(p=>PlayerRecovery.Valid($"\"{p}\" \"%1\"")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();players.Items.AddRange(candidates);if(candidates.Length==1)players.SelectedIndex=0;
        root.Controls.Add(integration);root.Controls.Add(new Label{Text="Official Roblox player (choose when several are found):",AutoSize=true});root.Controls.Add(players);root.Controls.Add(status);
        var row=new FlowLayoutPanel{AutoSize=true};row.Controls.Add(install);var cancel=new Button{Text="Cancel",AutoSize=true};cancel.Click+=(_,_)=>Close();row.Controls.Add(cancel);root.Controls.Add(row);Controls.Add(root);
        install.Click+=async (_,_)=>{if(running)return;running=true;install.Enabled=false;cancel.Enabled=false;status.Text="Waiting for Windows administrator approval and setup verification…";try{var options=args.ToList();if(!integration.Checked)options.Add("--disable-integration");if(players.SelectedItem is string player){options.Add("--player");options.Add(player);}else if(candidates.Length>1)throw new UserError("Choose an official Roblox player before installing.");Result=await Task.Run(()=>Entry.Elevate(options.ToArray()));status.Text=Result==0?"Installation and normal-user verification completed.":Result==1223?"Administrator approval canceled. Select Retry setup when ready.":"Setup did not complete. Check setup logs and retry.";if(Result==0){cancel.Text="Close";install.Visible=false;}else install.Text="Retry setup";}catch(Exception ex){Result=ex is System.ComponentModel.Win32Exception native&&native.NativeErrorCode==1223?1223:1;status.Text=Result==1223?"Administrator approval canceled. Select Retry setup when ready.":ex is UserError?ex.Message:"Setup could not complete. Check setup logs and retry.";install.Text="Retry setup";}finally{running=false;install.Enabled=true;cancel.Enabled=true;}};
        FormClosing+=(_,e)=>{if(running)e.Cancel=true;};
    }
}
