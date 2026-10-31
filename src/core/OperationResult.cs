namespace RobloxLaunchHelper;

public enum OperationOutcome { Succeeded, Warning, PartiallySucceeded, Failed, Canceled, Unknown }
public sealed record OperationItem(string Id, OperationOutcome Outcome, string Code, string Message);
public sealed record OperationResult(OperationOutcome Outcome, string Code, string Message, OperationItem[]? Items = null)
{
    public int ExitCode => Code=="INVALID_USAGE"?2:Outcome switch { OperationOutcome.Succeeded => 0, OperationOutcome.Warning or OperationOutcome.PartiallySucceeded => 3, OperationOutcome.Unknown => 4, OperationOutcome.Canceled => 1223, _ => 1 };
    public static OperationResult FromExit(int exit, bool launch = false) => exit switch
    {
        0 => new(OperationOutcome.Succeeded, "COMPLETED", launch ? "Roblox started; helper completed. Sign-in and game readiness are not verified." : "Operation completed."),
        1223 => new(OperationOutcome.Canceled, "CANCELED", "Operation canceled."),
        3 => new(OperationOutcome.Warning, "WARNING", "Operation completed with a warning. Check Activity or System health."),
        4 => new(OperationOutcome.Unknown, "UNCONFIRMED", "Completion could not be confirmed. Check System health before retrying."),
        _ when launch => new(OperationOutcome.Unknown, "LEGACY_LAUNCH_UNCONFIRMED", "Launch was unsuccessful or unconfirmed. Check Activity for the helper result before retrying."),
        _ => new(OperationOutcome.Failed, "HELPER_FAILED", "The helper operation did not complete. Check System health.")
    };
    internal static OperationResult ForAction(InstanceActionResult result) => result switch
    {
        InstanceActionResult.Focused => new(OperationOutcome.Succeeded, "FOCUSED", "Window focused."),
        InstanceActionResult.CloseRequested => new(OperationOutcome.Succeeded, "CLOSE_REQUESTED", "Close requested."),
        InstanceActionResult.FocusDenied => new(OperationOutcome.Warning, "FOCUS_DENIED", "Windows denied foreground focus. Select the game from the taskbar."),
        InstanceActionResult.Changed => new(OperationOutcome.Warning, "WINDOW_CHANGED", "The selected window changed or exited. Refresh Instances."),
        _ => new(OperationOutcome.Failed, "WINDOW_UNAVAILABLE", "The window action could not complete. Refresh Instances.")
    };
    internal static OperationResult ForCleanup(IEnumerable<BackgroundCleanupResult> results)
    {
        var items = results.Select((r,i) => new OperationItem(i.ToString(), r switch {
            BackgroundCleanupResult.Stopped or BackgroundCleanupResult.Skipped => OperationOutcome.Succeeded,
            BackgroundCleanupResult.ExitUnconfirmed => OperationOutcome.Unknown, _ => OperationOutcome.Failed }, r.ToString(), r.ToString())).ToArray();
        int stopped=items.Count(x=>x.Code=="Stopped"), skipped=items.Count(x=>x.Code=="Skipped"), failed=items.Count(x=>x.Outcome==OperationOutcome.Failed), unknown=items.Count(x=>x.Outcome==OperationOutcome.Unknown);
        var outcome=failed+unknown==0 ? OperationOutcome.Succeeded : stopped>0 ? OperationOutcome.PartiallySucceeded : unknown>0 ? OperationOutcome.Unknown : OperationOutcome.Failed;
        return new(outcome,"CLEANUP_RESULT",$"Background cleanup: {stopped} stopped, {skipped} skipped, {failed} failed, {unknown} unconfirmed.",items);
    }
}
