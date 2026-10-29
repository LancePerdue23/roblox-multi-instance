using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RobloxLaunchHelper;

internal static class DiagnosticChecks
{
    internal static HealthCheckResult Result(string id, CheckState state, DiagnosticCode code, Remediation action = Remediation.None, int? error = null)
        => new(id, state, code, DateTimeOffset.UtcNow, action, error);
    internal static HealthCheckResult NotTested => Result("worker", CheckState.NotRun, DiagnosticCode.WorkerNotTested);
    internal static HealthCheckResult Inspect(string id, Func<HealthCheckResult> check)
    {
        try { return check(); }
        catch (Exception e) { return Result(id, CheckState.Unknown, DiagnosticCode.InspectionUnavailable, Remediation.Refresh, e.HResult); }
    }
    internal static bool ProtocolMatches(string? command) => string.Equals(command, Installation.WrapperCommand, StringComparison.OrdinalIgnoreCase);
    internal static HealthCheckResult CheckProtocol(string id, string? command, bool enabled, Func<string, bool>? exists = null, Func<string, bool>? trusted = null)
    {
        if (ProtocolMatches(command)) return Result(id, enabled ? CheckState.Healthy : CheckState.Failed,
            enabled ? DiagnosticCode.Ready : DiagnosticCode.ProtocolMismatch, enabled ? Remediation.None : Remediation.Repair);
        if (string.IsNullOrWhiteSpace(command)) return Result(id, CheckState.Failed, DiagnosticCode.ProtocolMissing, Remediation.Repair);
        var player = CheckPlayer(id, command, exists, trusted);
        if (enabled) return player.State == CheckState.Healthy
            ? Result(id, CheckState.Failed, DiagnosticCode.ProtocolChanged, Remediation.Repair) : player;
        return player.State == CheckState.Healthy ? Result(id, CheckState.Healthy, DiagnosticCode.IntegrationDisabled) : player;
    }
    internal static DiagnosticCode ConfigStatus(InstallConfig? config)
    {
        if (config is null || config.Version != 1 || config.Commands is null || Installation.Schemes.Any(s => !config.Commands.TryGetValue(s, out var command) || string.IsNullOrWhiteSpace(command))) return DiagnosticCode.InvalidConfig;
        return config.OwnerSid == Program.Sid ? DiagnosticCode.Ready : DiagnosticCode.WrongAccount;
    }
    internal static HealthCheckResult CheckPlayer(string id, string command, Func<string, bool>? exists = null, Func<string, bool>? trusted = null)
    {
        ProcessStartInfo start;
        try { start = Launch.BuildStart(command, null); }
        catch { return Result(id, CheckState.Failed, DiagnosticCode.InvalidConfig, Remediation.Repair); }
        if (!(exists ?? File.Exists)(start.FileName)) return Result(id, CheckState.Failed, DiagnosticCode.PlayerMissing, Remediation.ReinstallRoblox);
        if (!(trusted ?? RobloxTrust.IsSignedRoblox)(start.FileName)) return Result(id, CheckState.Failed, DiagnosticCode.SignatureRejected, Remediation.ReinstallRoblox);
        return Result(id, CheckState.Healthy, DiagnosticCode.Ready);
    }
    internal static HealthSnapshot Collect(HealthCheckResult? worker = null)
    {
        var checks = new List<HealthCheckResult>();
        InstallConfig? config = null;
        string playerVersion = "unknown";
        checks.Add(Inspect("installation", () =>
        {
            if (!Directory.Exists(Program.InstallDirectory)) return Result("installation", CheckState.Failed, DiagnosticCode.NotInstalled, Remediation.Setup);
            if (!File.Exists(Program.ConfigPath) || new FileInfo(Program.ConfigPath).Length > 1024 * 1024)
                return Result("installation", CheckState.Failed, DiagnosticCode.InvalidConfig, Remediation.Repair);
            try { config = JsonSerializer.Deserialize<InstallConfig>(File.ReadAllText(Program.ConfigPath)); }
            catch (JsonException) { return Result("installation", CheckState.Failed, DiagnosticCode.InvalidConfig, Remediation.Repair); }
            var code = ConfigStatus(config);
            if (code != DiagnosticCode.Ready) { config = null; return Result("installation", CheckState.Failed, code, code == DiagnosticCode.InvalidConfig ? Remediation.Repair : Remediation.None); }
            return config!.PendingPreviousEnabled is not null
                ? Result("installation", CheckState.Failed, DiagnosticCode.RecoveryPending, Remediation.Repair)
                : Result("installation", CheckState.Healthy, DiagnosticCode.Ready);
        }));
        checks.Add(Inspect("helper", () =>
        {
            if (!File.Exists(Program.InstalledExe)) return Result("helper", CheckState.Failed, DiagnosticCode.NotInstalled, Remediation.Setup);
            string? installed = FileVersionInfo.GetVersionInfo(Program.InstalledExe).FileVersion;
            string? running = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion;
            return Result("helper", installed == running ? CheckState.Healthy : CheckState.Warning,
                installed == running ? DiagnosticCode.Ready : DiagnosticCode.HelperVersionMismatch, installed == running ? Remediation.None : Remediation.Repair);
        }));
        foreach (string scheme in Installation.Schemes)
        {
            string id = "player-" + scheme;
            checks.Add(Inspect(id, () =>
            {
                if (config is null || !config.Commands.TryGetValue(scheme, out var command)) return Result(id, CheckState.Unknown, DiagnosticCode.InvalidConfig, Remediation.Repair);
                var result = CheckPlayer(id, command);
                if (result.State == CheckState.Healthy) playerVersion = FileVersionInfo.GetVersionInfo(Launch.BuildStart(command, null).FileName).FileVersion ?? "unknown";
                return result;
            }));
            checks.Add(Inspect("protocol-" + scheme, () =>
            {
                if (config is null) return Result("protocol-" + scheme, CheckState.Unknown, DiagnosticCode.InvalidConfig, Remediation.Repair);
                return CheckProtocol("protocol-" + scheme, Installation.CurrentCommand(scheme), config.BrowserIntegrationEnabled);
            }));
        }
        checks.Add(Inspect("task", InspectTask));
        checks.Add(Inspect("file-permissions", () =>
        {
            if (!Directory.Exists(Program.InstallDirectory)) return Result("file-permissions", CheckState.Unknown, DiagnosticCode.NotInstalled, Remediation.Setup);
            var dir = new DirectoryInfo(Program.InstallDirectory);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return Result("file-permissions", CheckState.Failed, DiagnosticCode.PermissionsUnsafe, Remediation.Repair);
            if (!DiagnosticAccess.Protected(dir.GetAccessControl())) return Result("file-permissions", CheckState.Failed, DiagnosticCode.PermissionsUnsafe, Remediation.Repair);
            foreach (string path in Directory.EnumerateFiles(Program.InstallDirectory))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || !DiagnosticAccess.Protected(new FileInfo(path).GetAccessControl()))
                    return Result("file-permissions", CheckState.Failed, DiagnosticCode.PermissionsUnsafe, Remediation.Repair);
            }
            return Result("file-permissions", CheckState.Healthy, DiagnosticCode.Ready);
        }));
        bool historyOk = new DiagnosticHistory().Read(out _);
        checks.Add(Result("history", historyOk ? CheckState.Healthy : CheckState.Warning, historyOk ? DiagnosticCode.Ready : DiagnosticCode.HistoryUnavailable, historyOk ? Remediation.None : Remediation.ClearHistory));
        bool logsOk = DiagnosticReports.LogDirectories.All(Directory.Exists);
        checks.Add(Result("logs", logsOk ? CheckState.Healthy : CheckState.Warning, logsOk ? DiagnosticCode.Ready : DiagnosticCode.LogsUnavailable, Remediation.OpenLogs));
        return new(1, typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown", Environment.OSVersion.Version.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(), DateTimeOffset.UtcNow, checks.ToArray(), worker ?? NotTested, playerVersion, config?.BrowserIntegrationEnabled);
    }
    private static HealthCheckResult InspectTask()
    {
        dynamic? service = null, root = null, task = null, definition = null, actions = null, action = null, principal = null, settings = null, triggers = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            service.Connect(); root = service.GetFolder("\\");
            try { task = root.GetTask(Program.TaskName); }
            catch (COMException e) when ((uint)e.HResult is 0x80070002 or 0x8004130F) { return Result("task", CheckState.Failed, DiagnosticCode.TaskMismatch, Remediation.Repair); }
            definition = task.Definition; actions = definition.Actions; principal = definition.Principal; settings = definition.Settings; triggers = definition.Triggers;
            bool valid = actions.Count == 1;
            if (valid)
            {
                action = actions.Item(1);
                valid = action.Type == 0 && string.Equals((string)action.Path, Program.InstalledExe, StringComparison.OrdinalIgnoreCase) &&
                    (string)action.Arguments == "--worker" && string.Equals((string)action.WorkingDirectory, Program.InstallDirectory, StringComparison.OrdinalIgnoreCase);
            }
            valid &= (string)principal.UserId == Program.Sid && principal.LogonType == 3 && principal.RunLevel == 1 &&
                (bool)settings.Enabled && (bool)settings.AllowDemandStart && triggers.Count == 0;
            if (!valid) return Result("task", CheckState.Failed, DiagnosticCode.TaskMismatch, Remediation.Repair);
            var descriptor = new RawSecurityDescriptor((string)task.GetSecurityDescriptor(7));
            return DiagnosticAccess.Protected(descriptor) ? Result("task", CheckState.Healthy, DiagnosticCode.Ready) : Result("task", CheckState.Failed, DiagnosticCode.PermissionsUnsafe, Remediation.Repair);
        }
        finally
        {
            foreach (object? com in new object?[] { triggers, settings, principal, action, actions, definition, task, root, service })
                if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
    }
    internal static async Task<HealthCheckResult> TestWorkerAsync(CancellationToken token)
    {
        if (Program.Elevated || !Program.IsInstalledExecutable()) return Result("worker", CheckState.Unknown, DiagnosticCode.InspectionUnavailable, Remediation.Repair);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromSeconds(35));
            using var pipe = await Launch.ConnectAsync(limit.Token);
            await Protocol.WriteAsync(pipe, "PROBE", limit.Token);
            if (await Protocol.ReadAsync(pipe, limit.Token) != "OK") return Result("worker", CheckState.Failed, DiagnosticCode.WorkerUnavailable, Remediation.Repair);
            return Result("worker", CheckState.Healthy, DiagnosticCode.WorkerReady);
        }
        catch (Exception e) { return Result("worker", CheckState.Failed, DiagnosticCode.WorkerUnavailable, Remediation.Repair, e.HResult); }
    }
}

// Evaluate this normal user's effective rights, rather than trying a write or changing an ACL.
internal static class DiagnosticAccess
{
    [StructLayout(LayoutKind.Sequential)] private struct Mapping { public uint Read, Write, Execute, All; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateToken(nint existing, int level, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AccessCheck(byte[] descriptor, SafeAccessTokenHandle token, uint desired,
        ref Mapping mapping, nint privileges, ref uint length, out uint granted, [MarshalAs(UnmanagedType.Bool)] out bool status);
    internal static bool Protected(FileSystemSecurity security) => Protected(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0));
    internal static bool Protected(RawSecurityDescriptor descriptor)
    {
        if (Program.Elevated) throw new InvalidOperationException("Normal token required.");
        if (descriptor.Owner?.Value != "S-1-5-32-544") return false;
        // A NULL/missing DACL grants unrestricted access. Reject it explicitly.
        if (descriptor.DiscretionaryAcl is null) return false;
        byte[] bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
        using var identity = WindowsIdentity.GetCurrent();
        if (!DuplicateToken(identity.Token, 2, out var duplicate)) throw new System.ComponentModel.Win32Exception();
        using (duplicate)
        {
            var mapping = new Mapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A9, All = 0x1F01FF };
            uint length = 1024; nint privileges = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!AccessCheck(bytes, duplicate, 0x02000000, ref mapping, privileges, ref length, out uint granted, out bool status)) throw new System.ComponentModel.Win32Exception();
                if (!status) throw new UnauthorizedAccessException();
                return (granted & 0xD0156) == 0; // write/append/delete-child/attributes, DELETE, WRITE_DAC, WRITE_OWNER
            }
            finally { Marshal.FreeHGlobal(privileges); }
        }
    }
}
