using System.Net;
using System.Text.Json;

namespace RobloxMultiInstance;

// Native-only result. Identity and reusable session material never enter bridge DTOs.
public sealed record AccountSessionValidation(string Status,DateTimeOffset CheckedAt,AccountIdentity? Identity=null);

/// <summary>Bounded identity/session validation independent of the sign-in UI. No launch authorization is implied.</summary>
public sealed class AccountSessionValidator : IDisposable
{
    readonly HttpClient client;
    public AccountSessionValidator(HttpMessageHandler? handler=null)
    {
        client=new HttpClient(handler??new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(8)};
    }
    public async Task<AccountSessionValidation> ValidateAsync(string session,string? expectedUserId,CancellationToken token)
    {
        AccountSessionValidation Result(string status,AccountIdentity? identity=null)=>new(status,DateTimeOffset.UtcNow,identity);
        token.ThrowIfCancellationRequested();
        if(string.IsNullOrEmpty(session))return Result("Expired");
        if(session.Length>16384||session.Any(c=>char.IsControl(c)||c==';'))return Result("Unavailable");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://users.roblox.com/v1/users/authenticated");
            request.Headers.TryAddWithoutValidation("Cookie",".ROBLOSECURITY="+session);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests)return Result("RateLimited");
            if(response.StatusCode==HttpStatusCode.Unauthorized)return Result("Expired");
            if(response.StatusCode==HttpStatusCode.Forbidden||response.Headers.Contains("rblx-challenge-id"))return Result("Challenged");
            if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength is >8192)return Result("Unavailable");
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);byte[] bytes=new byte[8193];int count=0,read;
            while(count<bytes.Length&&(read=await stream.ReadAsync(bytes.AsMemory(count),deadline.Token))>0)count+=read;
            if(count>8192)return Result("Unavailable");
            using var document=JsonDocument.Parse(bytes.AsMemory(0,count));var root=document.RootElement;
            long id=root.GetProperty("id").GetInt64();string? name=root.GetProperty("name").GetString();
            if(id<=0||!AccountStore.ValidUsername(name))return Result("Unavailable");
            string immutableId=id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(expectedUserId is not null&&immutableId!=expectedUserId)return Result("WrongIdentity");
            return Result("Valid",new(immutableId,name!));
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch { return Result("Unavailable"); }
    }
    public void Dispose()=>client.Dispose();
}
