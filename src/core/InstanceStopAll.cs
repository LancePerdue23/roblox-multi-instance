namespace RobloxLaunchHelper;

// Only explicit, confirmed Stop All uses this path. Never enumerate again after confirmation.
internal sealed class InstanceStopAll(Func<int, IBackgroundClientProcess> open,
    Func<string, bool> trusted, string sid, int session)
{
    internal OperationResult Execute(IEnumerable<ProcessKey> targets, CancellationToken token)
    {
        var items = new List<OperationItem>();
        foreach (var key in targets.Distinct())
        {
            token.ThrowIfCancellationRequested();
            OperationOutcome outcome;
            string code;
            try
            {
                using var process = open(key.Id);
                var id = process.Identity;
                if (id.Created != key.Created || !InstancePolicy.Matches(id, sid, session) || id.Elevated ||
                    !process.IsAlive || !trusted(id.Image))
                { outcome = OperationOutcome.Warning; code = "Skipped"; }
                else
                {
                    token.ThrowIfCancellationRequested();
                    // Terminate the same held handle we verified, including visible clients.
                    // WM_CLOSE can launch Roblox's tray replacement and affect other games.
                    if (!process.IsAlive) { outcome = OperationOutcome.Warning; code = "Skipped"; }
                    else if (!process.Stop()) { outcome = OperationOutcome.Failed; code = "Failed"; }
                    else if (!process.WaitForExit(2000)) { outcome = OperationOutcome.Unknown; code = "Unconfirmed"; }
                    else { outcome = OperationOutcome.Succeeded; code = "Stopped"; }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { outcome = OperationOutcome.Failed; code = "Failed"; }
            items.Add(new(key.Id.ToString(), outcome, code, code));
        }
        int stopped = items.Count(x => x.Code == "Stopped"), skipped = items.Count(x => x.Code == "Skipped"),
            failed = items.Count(x => x.Code == "Failed"), unknown = items.Count(x => x.Code == "Unconfirmed");
        var result = stopped == items.Count ? OperationOutcome.Succeeded : stopped > 0 ? OperationOutcome.PartiallySucceeded :
            unknown > 0 ? OperationOutcome.Unknown : failed > 0 ? OperationOutcome.Failed : OperationOutcome.Warning;
        return new(result, "STOP_ALL_RESULT", $"Stop all: {stopped} stopped, {skipped} skipped, {failed} failed, {unknown} unconfirmed.", items.ToArray());
    }
}
