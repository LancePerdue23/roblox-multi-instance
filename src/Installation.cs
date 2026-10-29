using Microsoft.Win32;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RobloxLaunchHelper;

internal sealed record StoredValue(string Name, RegistryValueKind Kind, JsonElement Data);
internal sealed class RegistryTree
{
    public List<StoredValue> Values { get; set; } = [];
    public Dictionary<string, RegistryTree> Children { get; set; } = [];
    internal static RegistryTree Capture(RegistryKey key)
    {
        var tree = new RegistryTree();
        foreach (string name in key.GetValueNames())
        {
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            tree.Values.Add(new(name, key.GetValueKind(name), JsonSerializer.SerializeToElement(value)));
        }
        foreach (string name in key.GetSubKeyNames()) { using var child = key.OpenSubKey(name)!; tree.Children.Add(name, Capture(child)); }
        return tree;
    }
    internal void Restore(RegistryKey key)
    {
        foreach (var value in Values)
        {
            object data = value.Kind switch
            {
                RegistryValueKind.Binary or RegistryValueKind.None => value.Data.GetBytesFromBase64(),
                RegistryValueKind.DWord => value.Data.GetInt32(),
                RegistryValueKind.QWord => value.Data.GetInt64(),
                RegistryValueKind.MultiString => value.Data.Deserialize<string[]>()!,
                _ => value.Data.GetString()!
            };
            key.SetValue(value.Name, data, value.Kind);
        }
        foreach (var child in Children) { using var subkey = key.CreateSubKey(child.Key); child.Value.Restore(subkey); }
    }
}

internal sealed class InstallConfig
{
    public int Version { get; set; } = 1;
    public string OwnerSid { get; set; } = "";
    public Dictionary<string, string> Commands { get; set; } = [];
    public Dictionary<string, RegistryTree?> Backups { get; set; } = [];
    public bool BrowserIntegrationEnabled { get; set; } = true;
    public Dictionary<string, RegistryTree?> OriginalBackups { get; set; } = [];
    public Dictionary<string, RegistryTree> WrittenProtocols { get; set; } = [];
    public List<ProtocolEdit> PendingChanges { get; set; } = [];
    public bool? PendingPreviousEnabled { get; set; }
    public Dictionary<string, string?> ExpectedProtocols { get; set; } = [];
}

internal static partial class Installation
{
    internal static readonly string[] Schemes = ["roblox-player", "roblox"];
    internal static string WrapperCommand => $"\"{Program.InstalledExe}\" \"%1\"";
    internal static string ProtocolPath(string scheme) => @"Software\Classes\" + scheme;
    internal static InstallConfig ReadConfig()
    {
        var config = JsonSerializer.Deserialize<InstallConfig>(File.ReadAllText(Program.ConfigPath)) ?? throw new InvalidOperationException("Installation settings are missing. Run Repair.");
        if (config.Version != 1 || config.OwnerSid != Program.Sid) throw new InvalidOperationException("Installation settings do not match this account.");
        return config;
    }

    internal static string? CurrentCommand(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProtocolPath(scheme) + @"\shell\open\command");
        if (key?.GetValue("") is { } value) return value as string ?? "";
        using var machine = Registry.LocalMachine.OpenSubKey(ProtocolPath(scheme) + @"\shell\open\command");
        return machine?.GetValue("") as string;
    }
    internal static void VerifyReadOnly()
    {
        foreach (string file in Directory.EnumerateFiles(Program.InstallDirectory))
        {
            bool denied = false;
            try { using var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
            catch (UnauthorizedAccessException) { denied = true; }
            if (!denied) throw new InvalidOperationException("Installed files are writable without elevation.");
            string? owner = new FileInfo(file).GetAccessControl().GetOwner(typeof(SecurityIdentifier))?.Value;
            if (owner != "S-1-5-32-544") throw new InvalidOperationException("Installed file ownership is not protected.");
        }
        string probe = Path.Combine(Program.InstallDirectory, "permission-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("The installation directory is writable without elevation.");
    }
    private static bool IsWrapper(string? command) => DiagnosticChecks.ProtocolMatches(command);

    internal static int ExecuteInWindowsContext(string action, string owner)
    {
        if (!Program.Elevated || owner != Program.Sid || !IsAction(action))
            throw new InvalidOperationException("Invalid installer action or Windows account.");
        if (action != "setup" && !File.Exists(Program.ConfigPath))
            throw new UserError("No installation was found for this Windows account.");
        // A Windows-scheduled process sees the same user registry as Explorer/Chrome,
        // including when setup was started from an environment with an isolated hive.
        ProtectDirectory(Program.InstallRoot, true);
        ProtectDirectory(Program.InstallDirectory, false);
        RejectReparseTree(Program.InstallDirectory);
        CopyPayload();
        foreach (string file in Directory.EnumerateFiles(Program.InstallDirectory)) ProtectFile(file);
        Log.Write("setup-context", "dispatch-windows-process");
        int result = ScheduledWorker.RunCleanInstallerTask(action);
        if (result == 1223) return result;
        if (result != 0) throw new UserError("Setup did not finish successfully. Check the installed logs and setup logs for details.");
        return result;
    }

    private static void ProtectDirectory(string path, bool root)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The installation directory must not be a junction or symbolic link.");
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(root ? new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null) : new SecurityIdentifier(Program.Sid),
            FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void RejectReparseTree(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Installation files must not contain symbolic links or junctions.");
            if ((attributes & FileAttributes.Directory) != 0) RejectReparseTree(path);
        }
    }

    private static void CopyPayload()
    {
        if (Program.IsInstalledExecutable()) return;
        string source = AppContext.BaseDirectory;
        foreach (string path in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(path);
            if (name != Program.Product + ".exe" && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                name != Program.Product + ".runtimeconfig.json" && name != Program.Product + ".deps.json") continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Payload files must not be links.");
            string target = Path.Combine(Program.InstallDirectory, name);
            // Existing installed binaries can be locked by the launcher. Avoid unnecessary overwrites.
            if (File.Exists(target) && System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target)))) continue;
            File.Copy(path, target, true);
            ProtectFile(target);
        }
    }

    private static void ProtectFile(string path)
    {
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Program.Sid), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    internal static void RestoreTree(RegistryKey parent, string name, RegistryTree? tree)
    {
        parent.DeleteSubKeyTree(name, false);
        if (tree is not null) { using var restored = parent.CreateSubKey(name); tree.Restore(restored); }
    }
    private static void Uninstall()
    {
        if (!File.Exists(Program.ConfigPath)) throw new UserError("No installation was found for this Windows account.");
        _ = ReadConfig();
        ScheduledWorker.Remove();
        ShellAssociations.NotifyChanged();
        RunUnelevated("--shortcuts remove", TimeSpan.FromSeconds(15));
        ScheduleCleanup();
        MessageBox.Show("The task and launch integration have been removed. Program files will be removed after this window closes.", "Roblox Launch Helper");
    }

    private static void ScheduleCleanup()
    {
        string directory = Path.GetFullPath(Program.InstallDirectory);
        if (Path.GetDirectoryName(directory) != Path.GetFullPath(Program.InstallRoot) || Path.GetFileName(directory) != Program.Sid)
            throw new InvalidOperationException("Invalid cleanup target.");
        RejectReparseTree(directory);
        // A hidden, short-lived cleanup process waits for this executable to exit.
        string safeDirectory = directory.Replace("'", "''");
        string script = $"$ErrorActionPreference='Stop'; Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            $"$target='{safeDirectory}'; for ($attempt=0; $attempt -lt 20; $attempt++) {{ if (-not (Test-Path -LiteralPath $target)) {{ break }}; " +
            "if ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { break }; " +
            "try { Remove-Item -LiteralPath $target -Recurse -Force; break } catch { Start-Sleep -Milliseconds 500 } }";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        Process.Start(start)?.Dispose();
    }

    internal static void Shortcuts(bool remove)
    {
        if (Program.Elevated) throw new InvalidOperationException("Shortcut creation must run without elevation.");
        string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Roblox Launch Helper");
        string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Roblox Multi Instance.lnk");
        if (remove)
        {
            foreach (string path in new[] { desktop, Path.Combine(menu, "Repair.lnk"), Path.Combine(menu, "Uninstall.lnk"), Path.Combine(menu, "Diagnostics.lnk") })
                if (File.Exists(path) && ShortcutTargetsUs(path)) File.Delete(path);
            if (Directory.Exists(menu) && !Directory.EnumerateFileSystemEntries(menu).Any()) Directory.Delete(menu);
            return;
        }
        Directory.CreateDirectory(menu);
        SaveShortcut(desktop, "");
        SaveShortcut(Path.Combine(menu, "Repair.lnk"), "--repair");
        SaveShortcut(Path.Combine(menu, "Uninstall.lnk"), "--uninstall");
        SaveShortcut(Path.Combine(menu, "Diagnostics.lnk"), "--diagnostics");
    }
    private static bool ShortcutTargetsUs(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        return string.Equals((string)shortcut.TargetPath, Program.InstalledExe, StringComparison.OrdinalIgnoreCase);
    }
    private static void SaveShortcut(string path, string args)
    {
        if (File.Exists(path) && !ShortcutTargetsUs(path)) throw new InvalidOperationException("A shortcut with this name already belongs to another application.");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = Program.InstalledExe; shortcut.Arguments = args;
        shortcut.WorkingDirectory = Program.InstallDirectory; shortcut.Description = "Launch Roblox and close its singleton event automatically";
        shortcut.Save();
    }

    private static int RunUnelevated(string arguments, TimeSpan timeout)
    {
        return ScheduledWorker.RunLimitedSetupTask(arguments, timeout);
    }
}
