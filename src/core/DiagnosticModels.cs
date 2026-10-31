namespace RobloxLaunchHelper;

internal enum CheckState { Healthy, Warning, Failed, Unknown, NotRun }
internal enum LaunchOutcome { InProgress, Confirmed, Warning, Failed, Incomplete }
internal enum LaunchSource { Desktop, Roblox, RobloxPlayer }
internal enum DiagnosticCode
{
    Ready, NotInstalled, InvalidConfig, WrongAccount, PlayerMissing, SignatureRejected, HelperVersionMismatch,
    ProtocolMismatch, TaskMismatch, PermissionsUnsafe, InspectionUnavailable,
    WorkerNotTested, WorkerReady, WorkerUnavailable, WorkerTimeout, CompletionUnconfirmed,
    LaunchStarting, LaunchConfirmed, ProcessStartFailed, LaunchFailed, Interrupted,
    HistoryUnavailable, LogsUnavailable,
    ProtocolChanged, ProtocolMissing, IntegrationDisabled, RecoveryPending,
    PlayerExited, LegacyInstallation
}
internal enum Remediation { None, Setup, Repair, ReinstallRoblox, Refresh, OpenLogs, ClearHistory }
internal sealed record HealthCheckResult(string Id, CheckState State, DiagnosticCode Code,
    DateTimeOffset ObservedUtc, Remediation Action = Remediation.None, int? ErrorCode = null);
internal sealed record HealthSnapshot(int SchemaVersion, string HelperVersion, string WindowsVersion,
    string Architecture, DateTimeOffset CollectedUtc, HealthCheckResult[] Checks, HealthCheckResult WorkerTest, string RobloxVersion = "unknown", bool? BrowserIntegrationEnabled = null)
{
    private IEnumerable<HealthCheckResult> Required => Checks.Where(c => c.Id is not ("history" or "logs"));
    internal CheckState Overall => Required.Any(c => c.State == CheckState.Failed) ? CheckState.Failed :
        Required.Any(c => c.State is CheckState.Unknown or CheckState.NotRun) ? CheckState.Unknown :
        Required.Any(c => c.State == CheckState.Warning) ? CheckState.Warning : CheckState.Healthy;
}
internal sealed record LaunchResult(int SchemaVersion, Guid AttemptId, DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc, long? DurationMs, LaunchSource Source, bool? RobloxStarted,
    LaunchOutcome Outcome, DiagnosticCode Code, int? ErrorCode, int OwnerPid, long OwnerCreated);

internal static class DiagnosticMessages
{
    internal static string Text(DiagnosticCode code) => code switch
    {
        DiagnosticCode.Ready => "Configuration check passed.",
        DiagnosticCode.NotInstalled => "Helper is not installed for this Windows account. Run Setup.",
        DiagnosticCode.InvalidConfig => "Installation settings are missing or invalid. Run Repair.",
        DiagnosticCode.WrongAccount => "Use the Windows account that owns this installation.",
        DiagnosticCode.HelperVersionMismatch => "The installed helper version differs from this executable. Run Setup/Repair from the newer executable.",
        DiagnosticCode.LegacyInstallation => "A legacy helper installation was found. Run the desktop or helper setup to upgrade explicitly; existing preferences and original protocol backups are preserved.",
        DiagnosticCode.PlayerMissing => "Roblox may have updated. Launch or reinstall official Roblox, then run Repair.",
        DiagnosticCode.SignatureRejected => "The saved Roblox player could not be trusted. Reinstall official Roblox, then run Repair.",
        DiagnosticCode.ProtocolMismatch => "Browser launch registration differs from the helper. Run Repair.",
        DiagnosticCode.ProtocolChanged => "Another installer changed this browser registration. Recover/Repair can capture its current official player and restore helper integration.",
        DiagnosticCode.ProtocolMissing => "This browser launch registration is missing. Run Recover/Repair, or reinstall official Roblox if no trusted player can be found.",
        DiagnosticCode.IntegrationDisabled => "Browser integration is intentionally disabled. Official browser launching is available; the desktop helper still works. Enable integration when ready.",
        DiagnosticCode.RecoveryPending => "An installation operation was interrupted. Run Recover/Repair to reconcile its journal before making further changes.",
        DiagnosticCode.TaskMismatch => "The on-demand task is missing, disabled, or configured differently. Run Repair.",
        DiagnosticCode.PermissionsUnsafe => "Installation or task permissions allow unsafe changes. Run Repair.",
        DiagnosticCode.InspectionUnavailable => "This check could not establish safety. Check access and refresh; use Setup/Repair for full verification.",
        DiagnosticCode.WorkerNotTested => "Worker execution has not been tested. Idle is normal; select Test worker to check startup.",
        DiagnosticCode.WorkerReady => "Authenticated worker responded. Idle is normal; no Roblox handles were inspected by this test.",
        DiagnosticCode.WorkerUnavailable => "Worker startup could not be confirmed. Run Repair and test again.",
        DiagnosticCode.WorkerTimeout => "Worker did not confirm singleton release within 60 seconds. Roblox was left running. Check diagnostics before opening another instance.",
        DiagnosticCode.CompletionUnconfirmed => "Roblox started; multi-instance readiness was not confirmed. Check diagnostics or run Repair.",
        DiagnosticCode.LaunchStarting => "Launch is in progress. Refresh to see its result.",
        DiagnosticCode.LaunchConfirmed => "Roblox started; singleton release confirmed. This does not verify sign-in or in-game success.",
        DiagnosticCode.ProcessStartFailed => "Roblox could not start. Launch official Roblox normally, then run Repair.",
        DiagnosticCode.PlayerExited => "Roblox closed during startup, so the new game could not open. A stuck Roblox background or tray process can cause this. Save your progress, close all Roblox games, then exit Roblox from the Windows system tray and try Play again. Existing games were left running.",
        DiagnosticCode.LaunchFailed => "Launch could not finish. Check diagnostics and run Repair.",
        DiagnosticCode.Interrupted => "The launcher exited without recording a final result. Completion is unknown.",
        DiagnosticCode.HistoryUnavailable => "Recent launch history is unavailable. Check folder access or Clear history to reset the store. Launch behavior is unaffected.",
        DiagnosticCode.LogsUnavailable => "One or more diagnostic log folders are unavailable. A partial report can still be exported.",
        _ => "Unknown diagnostic result."
    };
}
