using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RobloxLaunchHelper.Tests")]
namespace RobloxLaunchHelper;
internal static class Program
{
    internal const string Product = "RobloxLaunchHelper";
    internal static string Sid => WindowsIdentity.GetCurrent().User!.Value;
    internal static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Product);
    internal static string InstallDirectory => Path.Combine(InstallRoot, Sid);
    internal static string InstalledExe => Path.Combine(InstallDirectory, Product + ".exe");
    internal static string ConfigPath => Path.Combine(InstallDirectory, "installation.json");
    internal static string TaskName => Product + "-" + Sid;
    internal static string PipeName => Product + "-" + Sid + "-" + Process.GetCurrentProcess().SessionId;
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };


    internal static int Main(string[] args) { UpdateCredentialMigration.RemoveOwnedCredential(); return HelperEntry.Run(args); }
    internal enum StartupMode { Setup, Dashboard, Roblox }

    // Administrative and internal modes are handled before this user-facing routing.
    internal static StartupMode SelectStartup(string[] args, bool installed)
    {
        if (args.Length == 0) return installed ? StartupMode.Dashboard : StartupMode.Setup;
        if (args.Length == 1 && args[0] == "--diagnostics") return StartupMode.Dashboard;
        if (args.Length != 1 || (args[0] != "--launch-roblox" && !Launch.IsRobloxUri(args[0])))
            throw new InvalidOperationException("Expected a Roblox launch link or --setup, --repair, --recover, --uninstall, --diagnostics, --launch-roblox, --enable-browser-integration or --disable-browser-integration.");
        if (!installed) throw new InvalidOperationException("Run the helper with --setup first.");
        return StartupMode.Roblox;
    }


    internal static bool IsInstalledExecutable()=>string.Equals(Path.GetFullPath(Environment.ProcessPath!),InstalledExe,StringComparison.OrdinalIgnoreCase);
}
internal sealed class UserError(string message) : Exception(message);

internal static class Log
{
    // Privileged code never follows a user-writable log path.
    internal static string DirectoryPath => Program.Elevated
        ? Program.IsInstalledExecutable() ? Path.Combine(Program.InstallDirectory, "logs") : Path.Combine(Program.InstallRoot, "setup-logs")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "logs");
    private static readonly object Gate = new();
    internal static void Write(string status, string detail = "", int number = 0)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "helper.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {status} {detail} {number}\n");
            }
        }
        catch { /* Logging must not break game launch or worker cleanup. */ }
    }
}
