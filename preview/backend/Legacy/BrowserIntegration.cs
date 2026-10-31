namespace RobloxLaunchHelper;

internal static class BrowserIntegration
{
    internal static async Task SetAsync(bool enabled, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var pipe = await Launch.ConnectAsync(timeout.Token);
        await Protocol.WriteAsync(pipe, enabled ? "INTEGRATION-ON" : "INTEGRATION-OFF", timeout.Token);
        string result;
        try { result = await Protocol.ReadAsync(pipe, timeout.Token); }
        catch (IOException)
        {
            throw new UserError("The installed helper could not change browser integration. Update it using Recover/Repair, then try again.");
        }
        if (result != "OK")
            throw new UserError("Browser integration could not be changed. Check System → Health or use Recover/Repair to check the official Roblox player.");
    }
}
