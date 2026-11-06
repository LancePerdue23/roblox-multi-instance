using System.Net;
using System.Text.Json;
using RobloxMultiInstance;

static class AccountLaunchTests
{
    internal static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool ok,string label) {if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        const string session="PRIVATE_SESSION_CANARY",ticket="PRIVATE_TICKET_CANARY";
        HttpResponseMessage Response(int status,string body="{}")=>new((HttpStatusCode)status) {Content=new StringContent(body)};
        HttpResponseMessage Identity()=>Response(200,"{\"id\":101,\"name\":\"AccountOne\"}");
        HttpResponseMessage Grant() {var result=Response(200);result.Headers.Add("rbx-authentication-ticket",ticket);return result;}
        var persisted=new List<string>();
        using(var handler=new Steps(
            request=>{Check(request.Headers.GetValues("Cookie").Single().Contains(session),"Native validation uses selected profile session");var r=Identity();r.Headers.Add("Set-Cookie",".ROBLOSECURITY=ROTATED_SESSION; Domain=.roblox.com; Path=/; Secure; HttpOnly");return r;},
            request=>{Check(persisted.SequenceEqual(new[]{"ROTATED_SESSION"})&&request.Headers.GetValues("Cookie").Single().Contains("ROTATED_SESSION"),"Rotation is saved before the next request");return Response(200,"{\"clientAssertion\":\"ASSERTION_CANARY\"}");},
            request=>{Check(request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath=="/v1/authentication-ticket/","Authorization uses fixed ticket POST endpoint");Check(request.Content!.ReadAsStringAsync().Result.Contains("ASSERTION_CANARY"),"Client assertion supplied to ticket request");var r=Response(403);r.Headers.Add("x-csrf-token","CSRF_CANARY");return r;},
            request=>{Check(request.Headers.GetValues("x-csrf-token").Single()=="CSRF_CANARY","Only CSRF rejection retries authorization");return Grant();},
            request=>Identity()))
        using(var auth=new AccountLaunchAuthorization(session,(value,_)=>{persisted.Add(value.Value);return Task.CompletedTask;},handler))
        {
            var grant=await auth.AuthorizeAsync("101",default);
            Check(grant.Code=="AUTHORIZED"&&grant.Ticket==ticket&&handler.Calls==5,"Authorization revalidates immutable identity after ticket issuance");
            Check(!grant.ToString().Contains(ticket),"Grant diagnostic string excludes ticket");
        }
        foreach(var failure in new[]{(401,"ACCOUNT_SESSION_EXPIRED"),(429,"ACCOUNT_RATE_LIMITED"),(302,"ACCOUNT_REDIRECT_REFUSED")})
        {
            using var handler=new Steps(_=>Response(failure.Item1));
            using var auth=new AccountLaunchAuthorization(session,(_,_)=>Task.CompletedTask,handler);
            var grant=await auth.AuthorizeAsync("101",default);
            Check(grant.Code==failure.Item2&&grant.Ticket is null&&handler.Calls==1,"Authorization stops on "+failure.Item2);
        }
        foreach(var failure in new[]{"mismatch","challenge","oversize","missing","rotation-failed","post-ticket-mismatch","revoked"})
        {
            var steps=new List<Func<HttpRequestMessage,HttpResponseMessage>>();
            steps.Add(_=>{
                if(failure=="mismatch")return Response(200,"{\"id\":102}");
                if(failure=="oversize")return Response(200,new string('x',8193));
                var r=Identity();
                if(failure=="challenge")r.Headers.Add("rblx-challenge-id","CHALLENGE_CANARY");
                if(failure=="rotation-failed")r.Headers.Add("Set-Cookie",".ROBLOSECURITY=NEW_SESSION; Domain=.roblox.com; Path=/; Secure");
                if(failure=="revoked")r.Headers.Add("Set-Cookie",".ROBLOSECURITY=; Domain=.roblox.com; Path=/; Secure; Max-Age=0");
                return r;
            });
            steps.Add(_=>Response(200,"{\"clientAssertion\":\"assertion\"}"));
            steps.Add(_=>failure=="missing"?Response(200):Grant());
            steps.Add(_=>Response(200,"{\"id\":102}"));
            using var handler=new Steps(steps.ToArray());
            using var auth=new AccountLaunchAuthorization(session,(_,_)=>failure=="rotation-failed"?Task.FromException(new IOException("SECRET_CANARY")):Task.CompletedTask,handler);
            var grant=await auth.AuthorizeAsync("101",default);
            Check(grant.Ticket is null&&!JsonSerializer.Serialize(grant).Contains("SECRET_CANARY"),"Authorization refuses "+failure+" without secret errors");
        }
        using(var canceled=new CancellationTokenSource())
        using(var handler=new Steps(_=>throw new Exception("Unexpected network request")))
        using(var auth=new AccountLaunchAuthorization(session,(_,_)=>Task.CompletedTask,handler))
        {
            canceled.Cancel();bool stopped=false;
            try {await auth.AuthorizeAsync("101",canceled.Token);}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&handler.Calls==0,"Canceled authorization sends no network request");
        }
        Guid attempt=Guid.NewGuid();
        string uri=AccountLaunchAuthorization.BuildPlayerUri(new("123",null),"ticket+&=canary",attempt,1000);
        Check(uri.Contains("gameinfo:ticket%2B%26%3Dcanary")&&uri.Contains("RequestGame")&&!uri.Contains(session),"Player URI encodes ticket delimiters and excludes reusable session");
        string job=Guid.NewGuid().ToString("D");
        Check(Uri.UnescapeDataString(AccountLaunchAuthorization.BuildPlayerUri(new("123",job),ticket,attempt,1000)).Contains("RequestGameJob&browserTrackerId=0&placeId=123&gameId="+job),"Public server launch includes validated place and Job ID");
        var native=AccountLaunchAuthorization.BuildPlayerArguments(new("123",job),"ticket+&=canary",attempt,1000);
        Check(native[0]=="--app"&&native[1]=="-t"&&native[2]=="ticket+&=canary"&&native[3]=="-j"&&native[4].Contains("RequestGameJob")&&native[4].Contains("gameId="+job),"Native account launches use app mode and preserve the ticket and exact server as separate arguments");
        Check(native.Contains("--launchtime=1000")&&!native.Any(x=>x.StartsWith("roblox-player:"))&&!string.Join(" ",native).Contains(session),"Native launch bypasses protocol tray reuse without exporting the reusable session");
        Check(native[Array.IndexOf(native,"-a")+1]=="https://www.roblox.com/Login/Negotiate.ashx","Native app launches explicitly authenticate the selected account instead of inheriting a warm client's session");
        Check(native[Array.IndexOf(native,"-channel")+1]=="production","All saved accounts use the same production channel instead of initiating experimental-channel updates");
        string log="[FLog::GameJoinLoadTime] userid:101\njoinAttemptId="+attempt;
        Check(BrowserAccountSessionProvider.Matches(log,"101",attempt),"Binding requires immutable identity and unique launch attempt");
        Check(!BrowserAccountSessionProvider.Matches(log,"102",attempt)&&!BrowserAccountSessionProvider.Matches(log,"101",Guid.NewGuid())&&!BrowserAccountSessionProvider.Matches("userid:101 "+attempt,"101",attempt),"Wrong accounts, unrelated attempts and arbitrary text cannot bind a process");
        Check(!BrowserAccountSessionProvider.Matches(log+"\n[FLog::GameJoinLoadTime] userid:102","101",attempt),"A later account switch invalidates earlier identity evidence");
        string currentJoin="[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:123, join_time:1.5, userid:101, ";
        Check(BrowserAccountSessionProvider.MatchesJoin(currentJoin,"101","123"),"Current player join record binds account and place without an omitted attempt marker");
        Check(!BrowserAccountSessionProvider.MatchesJoin(currentJoin,"102","123")&&!BrowserAccountSessionProvider.MatchesJoin(currentJoin,"101","12"),"Join binding rejects wrong account and partial place matches");
        Check(!BrowserAccountSessionProvider.MatchesJoin(currentJoin+"\n[FLog::GameJoinLoadTime] placeid:456, userid:102,","101","123"),"Latest join supersedes earlier account and place evidence");
        Check(!BrowserAccountSessionProvider.MatchesJoin("placeid:123, userid:101,","101","123")&&!BrowserAccountSessionProvider.MatchesJoin("[FLog::GameJoinLoadTime] placeid:123,\n[FLog::GameJoinLoadTime] userid:101,","101","123"),"Join evidence must occur in the same structured record");
        var provider=new BrowserAccountSessionProvider();
        Check(!provider.LaunchAvailable,"Native account provider is unavailable without a host session factory");
        return checks;
    }
    sealed class Steps(params Func<HttpRequestMessage,HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {token.ThrowIfCancellationRequested();if(request.RequestUri!.Scheme!="https"||request.RequestUri.Host is not ("auth.roblox.com" or "users.roblox.com"))throw new Exception("Unexpected host");return Task.FromResult(steps[Calls++](request));}
    }
}
