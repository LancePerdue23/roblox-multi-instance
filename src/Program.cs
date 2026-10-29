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

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        try
        {
            string mode = args.FirstOrDefault() ?? "";
            if (mode == "--launch-notifications")
            {
                if (args.Length != 1) return 2;
                return LaunchNotificationHost.Run();
            }
            if (mode == "--worker")
            {
                if (args.Length != 1 || !Elevated || !IsInstalledExecutable()) return 2;
                var config = Installation.ReadConfig();
                if (config.OwnerSid != Sid) return 2;
                return Worker.RunAsync().GetAwaiter().GetResult();
            }
            if (mode == "--verify") return Launch.VerifyAsync().GetAwaiter().GetResult();
            if (mode == "--verify-protocols")
            {
                if (args.Length != 1 || Elevated || !IsInstalledExecutable()) return 2;
                Installation.VerifyProtocolRegistration();
                Log.Write("protocol-registration-verified");
                return 0;
            }
            if (mode == "--shortcuts")
            {
                if (args.Length != 2 || args[1] is not ("install" or "remove") || !IsInstalledExecutable()) return 2;
                Installation.Shortcuts(args[1] == "remove");
                return 0;
            }
            if (mode is "--setup" or "--repair" or "--recover" or "--uninstall" or "--enable-browser-integration" or "--disable-browser-integration")
            {
                if (args.Length != 1) throw new InvalidOperationException("Unexpected setup argument.");
                string action = mode[2..];
                if (!Elevated)
                {
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    start.ArgumentList.Add("--admin"); start.ArgumentList.Add(action); start.ArgumentList.Add(Sid);
                    using var child = Process.Start(start)!;
                    child.WaitForExit();
                    return child.ExitCode;
                }
                return Installation.ExecuteInWindowsContext(action, Sid);
            }
            if (mode == "--admin")
            {
                if (args.Length != 3 || !Elevated || args[2] != Sid)
                    throw new InvalidOperationException("Use an administrator account and approve setup with that same account.");
                return Installation.ExecuteInWindowsContext(args[1], args[2]);
            }
            if (mode == "--admin-clean")
            {
                if (args.Length != 3 || !Elevated || args[2] != Sid || !IsInstalledExecutable()) return 2;
                Installation.Execute(args[1], args[2]);
                return 0;
            }
            if (mode == "--diagnostics")
            {
                if (args.Length != 1) throw new UserError("Unexpected diagnostics argument.");
                if (Elevated) throw new UserError("Open Diagnostics normally, without Run as administrator, so checks use your normal Windows permissions.");
                Application.EnableVisualStyles();
                Application.Run(new DiagnosticsForm());
                return 0;
            }
            if (args.Length > 1 || (args.Length == 1 && !Launch.IsRobloxUri(args[0])))
                throw new InvalidOperationException("Expected a Roblox launch link or --setup, --repair, --recover, --uninstall, --diagnostics, --enable-browser-integration or --disable-browser-integration.");
            if (!File.Exists(ConfigPath))
            {
                if (args.Length != 0) throw new InvalidOperationException("Run the helper with --setup first.");
                return Main(["--setup"]);
            }
            return Launch.RunAsync(args.FirstOrDefault()).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { return 1223; }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { return 1223; }
        catch (Exception e)
        {
            Log.Write("operation-failed", e.GetType().Name + (e is System.ComponentModel.Win32Exception native ? ":win32=" + native.NativeErrorCode : ""), e.HResult);
            if (args.FirstOrDefault() is not ("--worker" or "--verify" or "--verify-protocols" or "--launch-notifications"))
                MessageBox.Show(e is UserError ? e.Message : FriendlyError(e), "Roblox Launch Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    internal static string FriendlyError(Exception e) => e is InvalidOperationException ? e.Message :
        e is System.ComponentModel.Win32Exception native ? $"The operation could not finish (Windows error {native.NativeErrorCode}: {native.Message}).\n\nDiagnostics: {Log.DirectoryPath}" :
        $"The operation could not finish ({e.GetType().Name}, 0x{e.HResult:X8}).\n\nDiagnostics: {Log.DirectoryPath}";

    internal static bool IsInstalledExecutable() => string.Equals(Path.GetFullPath(Environment.ProcessPath!), InstalledExe, StringComparison.OrdinalIgnoreCase);
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
