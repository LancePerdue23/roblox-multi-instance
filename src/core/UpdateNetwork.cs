using System.Net;
using System.Net.Http;
namespace RobloxLaunchHelper;
internal static class UpdateNetwork
{
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client,HttpRequestMessage request,CancellationToken token,CancellationToken caller)
    {
        try { return await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token); }
        catch(OperationCanceledException) when(!caller.IsCancellationRequested) { throw new UserError("The update request timed out. Check your connection and retry."); }
        catch(HttpRequestException) { throw new UserError("Could not reach GitHub for updates. Check your internet connection and retry, or open the release page."); }
    }
    internal static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden && (response.Headers.RetryAfter is not null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")))
            throw new UserError("GitHub's anonymous update rate limit was reached. Wait and retry later, or open the release page.");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new UserError("No public release or matching download is available. Retry later or open the release page.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new UserError("GitHub denied this anonymous update request. Retry later or open the public release page.");
        if(!response.IsSuccessStatusCode) throw new UserError("GitHub could not complete the update request. Retry later or open the release page.");
    }
}
