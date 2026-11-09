using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
namespace RobloxLaunchHelper;

internal sealed record PayloadFile(string Path,long Size,string Sha256);
internal sealed record EditionManifest(int Schema,string Edition,string Version,int Protocol,string OwnerSid,PayloadFile[] Files);
internal sealed record SetupRequest(string Edition,bool Integration,string? Player);
internal static class Distribution
{
    internal const int ProtocolVersion=1;
    internal static string MetadataPath=>Path.Combine(Program.InstallDirectory,"edition.json");
    internal static string RequestPath=>Path.Combine(Program.InstallDirectory,"setup-request.json");
    internal static string SetupName(string edition)=>edition switch { "Desktop"=>"RobloxMultiInstance-Setup.exe","Helper"=>"RobloxLaunchHelper-Setup.exe",_=>throw new InvalidDataException("Unknown edition.") };
    internal static void ValidateUpgrade(EditionManifest incoming,EditionManifest? previous,bool helperExists,string? helperVersion)
    {
        Validate(incoming);
        if(previous is not null&&previous.Edition!=incoming.Edition)throw new UserError("Uninstall the current edition before switching editions. Settings and history can be retained.");
        var candidate=UpdateVersion.Parse(incoming.Version)!;
        if(previous is not null&&candidate<UpdateVersion.Parse(previous.Version))throw new UserError("Downgrades are not supported. Uninstall the newer development installation first; settings and history can be retained.");
        if(helperExists)
        {
            var installed=UpdateVersion.Parse(helperVersion)??throw new UserError("The installed helper version could not be verified. Repair or uninstall the existing installation first.");
            if(candidate<installed)throw new UserError("Downgrades are not supported. Uninstall the newer development installation first; settings and history can be retained.");
        }
    }
    internal static bool UnsupportedProtocol()
    {
        if(!File.Exists(MetadataPath))return false;UpdateFiles.RejectLinks(MetadataPath);
        if(!DiagnosticAccess.ProtectedInstallationMetadata(new FileInfo(MetadataPath).GetAccessControl())||new FileInfo(MetadataPath).Length>1024*1024)throw new InvalidDataException();
        using var document=JsonDocument.Parse(File.ReadAllBytes(MetadataPath));
        return document.RootElement.TryGetProperty("Protocol",out var protocol)&&protocol.TryGetInt32(out int value)&&value!=ProtocolVersion;
    }
    internal static EditionManifest? Installed()
    {
        if(!File.Exists(MetadataPath))return null;
        UpdateFiles.RejectLinks(MetadataPath);
        if(!DiagnosticAccess.ProtectedInstallationMetadata(new FileInfo(MetadataPath).GetAccessControl()))throw new InvalidDataException("Installation metadata is not protected.");
        if(new FileInfo(MetadataPath).Length>1024*1024)throw new InvalidDataException();
        var data=JsonSerializer.Deserialize<EditionManifest>(File.ReadAllBytes(MetadataPath))??throw new InvalidDataException();
        Validate(data);if(data.OwnerSid!=Program.Sid)throw new InvalidDataException("Installation belongs to another Windows account.");return data;
    }
    internal static void Validate(EditionManifest data)
    {
        if(data.Schema!=1 || data.Edition is not ("Desktop" or "Helper") || UpdateVersion.Parse(data.Version) is null || data.Protocol!=ProtocolVersion || data.Files.Length is <1 or >4096)throw new InvalidDataException("Unsupported installation manifest.");
        if(data.Files.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=data.Files.Length)throw new InvalidDataException("Duplicate payload entry.");
        foreach(var file in data.Files)
        {
            if(string.IsNullOrWhiteSpace(file.Path)||file.Path.Contains('\\')||file.Path.Split('/').Any(x=>x is "" or "." or "..")||file.Path.Contains(':')||Path.IsPathRooted(file.Path)||file.Size<0||!System.Text.RegularExpressions.Regex.IsMatch(file.Sha256,"\\A[A-Fa-f0-9]{64}\\z"))throw new InvalidDataException("Invalid payload entry.");
            if(data.Edition=="Helper"&&(file.Path.StartsWith("web/",StringComparison.OrdinalIgnoreCase)||file.Path.Contains("Presentation",StringComparison.OrdinalIgnoreCase)||file.Path.Contains("WebView",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(file.Path).StartsWith("RobloxMultiInstance.",StringComparison.OrdinalIgnoreCase)&&!file.Path.Contains("Core",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Desktop files in helper edition.");
        }
        if(!data.Files.Any(x=>x.Path=="RobloxLaunchHelper.exe")||!data.Files.Any(x=>x.Path=="rmi.exe")||data.Edition=="Desktop"&&!data.Files.Any(x=>x.Path=="RobloxMultiInstance.exe"))throw new InvalidDataException("Required executable missing.");
    }
    internal static void VerifyPayload(string source,EditionManifest manifest)
    {
        Validate(manifest);UpdateFiles.RejectLinks(source);
        var actual=Directory.GetFiles(source,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(source,p).Replace('\\','/')).Where(p=>p!="payload.json").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if(!actual.SequenceEqual(manifest.Files.Select(x=>x.Path).Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Payload does not match its allowlist.");
        foreach(var item in manifest.Files){string file=Path.Combine(source,item.Path);UpdateFiles.RejectLinks(file);using var stream=File.OpenRead(file);if(stream.Length!=item.Size||!Convert.ToHexString(SHA256.HashData(stream)).Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Payload verification failed.");}
        foreach(string exe in manifest.Edition=="Desktop"?new[]{"RobloxMultiInstance.exe","RobloxLaunchHelper.exe","rmi.exe"}:new[]{"RobloxLaunchHelper.exe","rmi.exe"})
            if(UpdateVersion.Parse(FileVersionInfo.GetVersionInfo(Path.Combine(source,exe)).FileVersion)!=UpdateVersion.Parse(manifest.Version))throw new InvalidDataException("Payload executable versions differ.");
    }
    internal static async Task<OperationResult> RunInstalledAsync(string action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(Program.InstalledExe){UseShellExecute=true};start.ArgumentList.Add("--"+action);
        try {using var child=Process.Start(start)??throw new IOException();await child.WaitForExitAsync(CancellationToken.None);return OperationResult.FromExit(child.ExitCode);}
        catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){return OperationResult.FromExit(1223);}
    }
    internal static void RegisterCommands(bool remove)
    {
        string command=Path.Combine(Program.InstallDirectory,"rmi.exe"),desktop=Path.Combine(Program.InstallDirectory,"RobloxMultiInstance.exe");
        using var app=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\rmi.exe");
        if(app.GetValue("") is string existing&&!existing.Equals(command,StringComparison.OrdinalIgnoreCase))throw new UserError("The rmi command is owned by another application.");
        const string uninstall=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\RobloxMultiInstance";
        using var entry=Registry.CurrentUser.CreateSubKey(uninstall);
        if(entry.GetValue("InstallLocation") is string location&&!location.Equals(Program.InstallDirectory,StringComparison.OrdinalIgnoreCase))throw new UserError("The uninstall entry is owned by another application.");
        using var environment=Registry.CurrentUser.CreateSubKey("Environment");
        string userPath=environment.GetValue("Path","",RegistryValueOptions.DoNotExpandEnvironmentNames) as string??"";
        var paths=userPath.Length==0?[]:userPath.Split(';',StringSplitOptions.None).ToList();
        bool owned=app.GetValue("RmiOwnedPath") is int flag&&flag==1;
        bool changed=false;
        if(remove)
        {
            app.DeleteValue("",false);app.DeleteValue("Path",false);app.DeleteValue("RmiOwnedPath",false);
            if(owned){changed=paths.RemoveAll(p=>p.Trim().TrimEnd('\\').Equals(Program.InstallDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))>0;}
        }
        else
        {
            app.SetValue("",command);app.SetValue("Path",Program.InstallDirectory);
            if(!paths.Any(p=>p.Trim().TrimEnd('\\').Equals(Program.InstallDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))){paths.Add(Program.InstallDirectory);owned=true;changed=true;}
            app.SetValue("RmiOwnedPath",owned?1:0);
        }
        if(changed){environment.SetValue("Path",string.Join(';',paths),RegistryValueKind.ExpandString);SendMessageTimeout(new nint(0xffff),0x1a,0,"Environment",2,2000,out _);}
        if(remove){using var classes=Registry.CurrentUser.CreateSubKey(@"Software\Classes");BadgeFileAssociation.Apply(classes,desktop,true);}
        if(remove){foreach(var name in new[]{"DisplayName","DisplayVersion","InstallLocation","UninstallString","NoModify","NoRepair"})entry.DeleteValue(name,false);return;}
        var metadata=Installed()??throw new InvalidDataException();
        if(metadata.Edition=="Desktop"){using var classes=Registry.CurrentUser.CreateSubKey(@"Software\Classes");BadgeFileAssociation.Apply(classes,desktop,false);}
        entry.SetValue("DisplayName",metadata.Edition=="Desktop"?"Roblox Multi Instance":"Roblox Launch Helper");entry.SetValue("DisplayVersion",metadata.Version);entry.SetValue("InstallLocation",Program.InstallDirectory);entry.SetValue("UninstallString",$"\"{Path.Combine(Program.InstallDirectory,"Installer.exe")}\" --uninstall");entry.SetValue("NoModify",1);entry.SetValue("NoRepair",1);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint SendMessageTimeout(nint hwnd,uint message,nint parameter,string text,uint flags,uint timeout,out nint result);
}

internal static partial class Installation
{
    internal static void InstallEdition(string source,EditionManifest manifest,bool integration,string? player,string action="setup")
    {
        if(!Program.Elevated)throw new UnauthorizedAccessException();
        Distribution.VerifyPayload(source,manifest);
        SetupDiagnostics.Step(SetupStage.UpgradeValidation);
        var previous=Distribution.Installed();
        bool helperExists=File.Exists(Program.InstalledExe);
        Distribution.ValidateUpgrade(manifest,previous,helperExists,helperExists?FileVersionInfo.GetVersionInfo(Program.InstalledExe).FileVersion:null);
        if(!UpdateInstallReadiness.IsReady(Program.InstalledExe)||!UpdateInstallReadiness.IsReady(Path.Combine(Program.InstallDirectory,"RobloxMultiInstance.exe")))throw new UserError("The installed app or helper is busy. Close its windows and retry. Roblox games may stay open.");
        SetupDiagnostics.Step(SetupStage.InstallDirectoryProtection);
        ProtectDirectory(Program.InstallRoot,true);ProtectDirectory(Program.InstallDirectory,false);RejectReparseTree(Program.InstallDirectory);
        SetupDiagnostics.Step(SetupStage.InstallLock);
        using var gate=new Mutex(false,@"Global\RobloxLaunchHelper-Distribution-"+Program.Sid);bool held=false;
        try
        {
            try {held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}
            if(!held)throw new UserError("Another setup is running.");
            // Rollback material lives in a protected sibling directory and includes legacy configuration/backups.
            SetupDiagnostics.Step(SetupStage.RollbackBackup);
            string rollback=Path.Combine(Program.InstallRoot,"rollback-"+Program.Sid+"-"+Guid.NewGuid().ToString("N"));ProtectDirectory(rollback,false);
            var original=Directory.GetFiles(Program.InstallDirectory,"*",SearchOption.AllDirectories).ToArray();
            foreach(string file in original){string target=Path.Combine(rollback,Path.GetRelativePath(Program.InstallDirectory,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);ProtectFile(target);}
            try
            {
                SetupDiagnostics.Step(SetupStage.PayloadCopy);
                foreach(var item in manifest.Files){string target=Path.Combine(Program.InstallDirectory,item.Path);string directory=Path.GetDirectoryName(target)!;Directory.CreateDirectory(directory);ProtectDirectory(directory,false);if(!File.Exists(target)||!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))File.Copy(Path.Combine(source,item.Path),target,true);ProtectFile(target);}
                string pending=Path.Combine(Program.InstallDirectory,"uninstall.pending");if(File.Exists(pending))File.Delete(pending);
                SetupDiagnostics.Step(SetupStage.InstallerCopy);
                string installer=Path.Combine(Program.InstallDirectory,"Installer.exe");
                if(!string.Equals(Environment.ProcessPath,installer,StringComparison.OrdinalIgnoreCase))File.Copy(Environment.ProcessPath!,installer,true);
                ProtectFile(installer);
                var installerInfo=new FileInfo(installer);
                using var installerStream=File.OpenRead(installer);
                var installedManifest=manifest with {OwnerSid=Program.Sid,Files=[..manifest.Files,new("Installer.exe",installerInfo.Length,Convert.ToHexString(SHA256.HashData(installerStream)))]};
                SetupDiagnostics.Step(SetupStage.MetadataWrite);
                File.WriteAllBytes(Distribution.MetadataPath,JsonSerializer.SerializeToUtf8Bytes(installedManifest));ProtectFile(Distribution.MetadataPath);
                File.WriteAllBytes(Distribution.RequestPath,JsonSerializer.SerializeToUtf8Bytes(new SetupRequest(manifest.Edition,integration,player)));ProtectFile(Distribution.RequestPath);
                SetupDiagnostics.Step(SetupStage.VerificationTask);
                int exit=ScheduledWorker.RunCleanInstallerTask(action);
                Log.Write("edition-setup-verification-result","",exit);
                if(exit!=0)throw new UserError("Setup verification failed. Existing payload files were restored; check setup logs.");
                File.Delete(Distribution.RequestPath);
            }
            catch(Exception error)
            {
                SetupDiagnostics.Failure(error);
                SetupDiagnostics.Step(SetupStage.Rollback);
                foreach(var item in manifest.Files){string target=Path.Combine(Program.InstallDirectory,item.Path);if(File.Exists(target)&&!original.Contains(target,StringComparer.OrdinalIgnoreCase))File.Delete(target);}
                foreach(string file in Directory.GetFiles(rollback,"*",SearchOption.AllDirectories)){string target=Path.Combine(Program.InstallDirectory,Path.GetRelativePath(rollback,file));File.Copy(file,target,true);ProtectFile(target);}
                if(previous is null&&File.Exists(Distribution.MetadataPath))File.Delete(Distribution.MetadataPath);
                string installer=Path.Combine(Program.InstallDirectory,"Installer.exe");if(!original.Contains(installer,StringComparer.OrdinalIgnoreCase)&&File.Exists(installer))File.Delete(installer);
                if(File.Exists(Distribution.RequestPath))File.Delete(Distribution.RequestPath);
                throw;
            }
            // Keep rollback material for recovery; later accepted cleanup can remove it explicitly.
        }
        finally {if(held)gate.ReleaseMutex();}
    }
}
