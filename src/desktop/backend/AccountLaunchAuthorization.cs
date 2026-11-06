using System.Net;
using System.Text;
using System.Text.Json;

namespace RobloxMultiInstance;

// Native-only: neither this result nor the HTTP client may be serialized or logged.
public sealed record AccountLaunchGrant(string Code,[property:System.Text.Json.Serialization.JsonIgnore] string? Ticket=null)
{
    public override string ToString()=>Code;
}

public sealed class AccountLaunchAuthorization : IDisposable
{
    static readonly Uri Website=new("https://www.roblox.com/");
    readonly HttpClient client;
    readonly CookieContainer cookies=new();
    readonly Func<Cookie,CancellationToken,Task> persist;

    public AccountLaunchAuthorization(string session,Func<Cookie,CancellationToken,Task> persist,HttpMessageHandler? handler=null)
    {
        if(string.IsNullOrEmpty(session)||session.Length>16384||session.Any(c=>char.IsControl(c)||c==';'))throw new ArgumentException("Reconnect this account before launching.");
        cookies.Add(new Cookie(".ROBLOSECURITY",session,"/",".roblox.com") {Secure=true,HttpOnly=true});
        this.persist=persist;
        client=new(handler??new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(10)};
    }

    static string? Header(HttpResponseMessage response,string key)
    {
        if(!response.Headers.TryGetValues(key,out var values))return null;
        var items=values.ToArray();return items.Length==1&&items[0].Length<=16384?items[0]:null;
    }
    async Task<HttpResponseMessage> SendAsync(HttpMethod method,string url,string? body,string? csrf,CancellationToken token)
    {
        using var request=new HttpRequestMessage(method,url);
        request.Headers.Add("Cookie",cookies.GetCookieHeader(new Uri(url)));
        request.Headers.Referrer=Website;request.Headers.Add("Origin",Website.GetLeftPart(UriPartial.Authority));
        if(csrf is not null)request.Headers.Add("x-csrf-token",csrf);
        if(body is not null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
        var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        try
        {
            // Accept rotation only for the exact parent-domain session cookie. Persist
            // it in WebView2 before another request or any player handoff.
            if(response.Headers.TryGetValues("Set-Cookie",out var values))
            {
                var received=new CookieContainer();
                received.Add(new Cookie(".ROBLOSECURITY",cookies.GetCookies(Website)[".ROBLOSECURITY"]?.Value??"","/",".roblox.com") {Secure=true,HttpOnly=true});
                bool sessionHeader=false;
                foreach(string value in values)
                {
                    if(value.Length>32768)throw new IOException();
                    received.SetCookies(new Uri(url),value);
                    sessionHeader|=value.StartsWith(".ROBLOSECURITY=",StringComparison.Ordinal);
                }
                if(sessionHeader)
                {
                    var cookie=received.GetCookies(Website).Cast<Cookie>().SingleOrDefault(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.Secure);
                    if(cookie is null)
                    {
                        await persist(new Cookie(".ROBLOSECURITY","","/",".roblox.com") {Secure=true,Expired=true},token);
                        throw new IOException(); // Never continue with an invalidated session.
                    }
                    if(cookie.Value!=cookies.GetCookies(Website)[".ROBLOSECURITY"]?.Value)
                    {
                        await persist(cookie,token);
                        cookies.Add(cookie);
                    }
                }
            }
            return response;
        }
        catch { response.Dispose();throw; }
    }
    static string? Failure(HttpResponseMessage response)
    {
        if(response.Headers.Any(h=>h.Key.StartsWith("rblx-challenge-",StringComparison.OrdinalIgnoreCase)))return "ACCOUNT_CHALLENGE_REQUIRED";
        return (int)response.StatusCode switch {
            401=>"ACCOUNT_SESSION_EXPIRED",429=>"ACCOUNT_RATE_LIMITED",
            >=300 and <400=>"ACCOUNT_REDIRECT_REFUSED",_=>null
        };
    }
    static async Task<byte[]> ReadAsync(HttpResponseMessage response,CancellationToken token)
    {
        if(response.Content.Headers.ContentLength is >8192)throw new IOException();
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        byte[] bytes=new byte[8193];int count=0,read;
        while(count<bytes.Length&&(read=await stream.ReadAsync(bytes.AsMemory(count),token))>0)count+=read;
        if(count>8192)throw new IOException();return bytes[..count];
    }
    async Task<string?> VerifyAsync(string expected,CancellationToken token)
    {
        using var response=await SendAsync(HttpMethod.Get,"https://users.roblox.com/v1/users/authenticated",null,null,token);
        if(Failure(response) is string failure)return failure;
        if(response.StatusCode!=HttpStatusCode.OK)return "ACCOUNT_VALIDATION_FAILED";
        using var json=JsonDocument.Parse(await ReadAsync(response,token));
        if(json.RootElement.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)!=expected)return "ACCOUNT_IDENTITY_MISMATCH";
        return null;
    }
    public async Task<AccountLaunchGrant> AuthorizeAsync(string expectedUserId,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            if(!LaunchTargetResolver.IsPositiveId(expectedUserId))return new("ACCOUNT_IDENTITY_MISMATCH");
            if(await VerifyAsync(expectedUserId,timeout.Token) is string failure)return new(failure);
            string assertion="{}";
            using(var response=await SendAsync(HttpMethod.Get,"https://auth.roblox.com/v1/client-assertion/",null,null,timeout.Token))
            {
                if(Failure(response) is string error)return new(error);
                if(response.IsSuccessStatusCode)
                {
                    using var document=JsonDocument.Parse(await ReadAsync(response,timeout.Token));
                    if(document.RootElement.TryGetProperty("clientAssertion",out var value)&&value.ValueKind==JsonValueKind.String)
                        assertion=JsonSerializer.Serialize(new {clientAssertion=value.GetString()});
                }
                else if(response.StatusCode!=HttpStatusCode.NotFound)return new("ACCOUNT_ASSERTION_FAILED");
            }
            string? csrf=null;
            for(int attempt=0;attempt<2;attempt++)
            {
                using var response=await SendAsync(HttpMethod.Post,"https://auth.roblox.com/v1/authentication-ticket/",assertion,csrf,timeout.Token);
                if(Failure(response) is string error)return new(error);
                if(response.StatusCode==HttpStatusCode.Forbidden&&attempt==0&&Header(response,"x-csrf-token") is string next)
                { csrf=next;continue; }
                if(!response.IsSuccessStatusCode)return new(response.StatusCode==HttpStatusCode.Forbidden?"ACCOUNT_CHALLENGE_REQUIRED":"ACCOUNT_TICKET_FAILED");
                string? ticket=Header(response,"rbx-authentication-ticket");
                if(string.IsNullOrWhiteSpace(ticket)||ticket.Any(char.IsControl))return new("ACCOUNT_TICKET_MISSING");
                if(await VerifyAsync(expectedUserId,timeout.Token) is string mismatch)return new(mismatch);
                return new("AUTHORIZED",ticket);
            }
            return new("ACCOUNT_TICKET_FAILED");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch(OperationCanceledException) { return new("ACCOUNT_AUTH_TIMEOUT"); }
        catch { return new("ACCOUNT_AUTH_UNAVAILABLE"); }
    }
    public static string BuildPlayerUri(LaunchTargetDto target,string ticket,Guid attempt,long timestamp)
    {
        // Revalidate even for native callers. No executable, scheme or arbitrary URL input.
        target=LaunchTargetResolver.Parse(target.PlaceId,target.JobId);
        if(string.IsNullOrWhiteSpace(ticket)||ticket.Length>16384||ticket.Any(char.IsControl)||attempt==Guid.Empty)throw new ArgumentException("Invalid launch authorization.");
        string launcher="https://www.roblox.com/Game/PlaceLauncher.ashx?request="+(target.JobId is null?"RequestGame":"RequestGameJob")+
            "&browserTrackerId=0&placeId="+target.PlaceId+(target.JobId is null?"":"&gameId="+Uri.EscapeDataString(target.JobId))+
            "&isPlayTogetherGame=false&joinAttemptId="+attempt.ToString("D")+"&joinAttemptOrigin="+(target.JobId is null?"PlayButton":"JoinInstance");
        return "roblox-player:1+launchmode:play+gameinfo:"+Uri.EscapeDataString(ticket)+"+launchtime:"+timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            "+placelauncherurl:"+Uri.EscapeDataString(launcher)+"+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }
    // Native player mode avoids the protocol starter's NoReload handoff to an
    // existing tray process. ArgumentList preserves each value without shell parsing.
    public static string[] BuildPlayerArguments(LaunchTargetDto target,string ticket,Guid attempt,long timestamp)
    {
        string uri=BuildPlayerUri(target,ticket,attempt,timestamp);
        string launcher=uri.Split('+').Single(x=>x.StartsWith("placelauncherurl:",StringComparison.Ordinal))["placelauncherurl:".Length..];
        return ["--app","-t",ticket,"-j",Uri.UnescapeDataString(launcher),"-a","https://www.roblox.com/Login/Negotiate.ashx","-channel","production","-b","0","--launchtime="+timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),"--rloc","en_us","--gloc","en_us"];
    }
    public void Dispose()=>client.Dispose();
}
