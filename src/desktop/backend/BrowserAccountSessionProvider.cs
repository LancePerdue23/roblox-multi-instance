using System.Diagnostics;
using RobloxLaunchHelper;

namespace RobloxMultiInstance;

// The host keeps the isolated profile open for authorization, rotation and handoff.
// Cookies never pass through the desktop web bridge or an app-managed vault copy.
public interface IAccountLaunchSession : IAsyncDisposable
{
    string? Cookie { get; }
    Task PersistCookieAsync(System.Net.Cookie cookie,CancellationToken token);
}

public sealed class BrowserAccountSessionProvider : IPipelinedAccountSessionProvider
{
    readonly SemaphoreSlim discovery=new(1,1);
    InstanceSnapshot? cached;
    long collectedAt;
    (string Id,Task<IAccountLaunchSession> Session)? prepared;
    public Func<string,CancellationToken,Task<string?>>? ResolveUniverse {get;set;}
    public Action? PlayerHandoff {get;set;}
    public Func<AccountLaunchContext,AccountLaunchOutcome,CancellationToken,Task>? ManagedStarted {get;set;}
    internal Func<AccountLaunchContext,CancellationToken,Task<PerformanceProfile?>>? LaunchProfile {get;set;}
    public async Task Prepare(AccountLaunchContext account,CancellationToken token)
    {
        if(OpenSession is null||prepared is not null||token.IsCancellationRequested)return;
        var task=OpenSession(account,token);prepared=(account.Id,task);
        try{await task;}catch{/* The selected account reports its own failure when consumed. */}
    }
    public async Task Finish()
    {
        if(prepared is {} next){prepared=null;try{await (await next.Session).DisposeAsync();}catch{}}
        cached=null;
    }
    async Task<IAccountLaunchSession> Session(AccountLaunchContext account,CancellationToken token)
    {
        if(prepared is {} next&&next.Id==account.Id){prepared=null;return await next.Session;}
        return await OpenSession!(account,token);
    }
    async Task<InstanceSnapshot> Snapshot(CancellationToken token)
    {
        await discovery.WaitAsync(token);
        try
        {
            if(cached is null||Environment.TickCount64-collectedAt>=750)
            {cached=await Task.Run(()=>LaunchStartup.Collector().Collect(token),token);collectedAt=Environment.TickCount64;}
            return cached;
        }
        finally{discovery.Release();}
    }
    public PendingAccountLaunch Start(AccountLaunchContext account,LaunchTargetDto target,Action<string,long> progress,CancellationToken token)
    {
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return new(ready.Task,Run(account,target,progress,ready,token));
    }
    public Func<AccountLaunchContext,CancellationToken,Task<IAccountLaunchSession>>? OpenSession { get; set; }
    public bool LaunchAvailable=>OpenSession is not null;
    public string Notice=>LaunchAvailable
        ? string.Empty
        : "Account launching requires the desktop host. Open the complete desktop build.";

    public Task<AccountLaunchOutcome> LaunchAsync(AccountLaunchContext account,LaunchTargetDto target,CancellationToken token)
        =>Start(account,target,(_,_)=>{},token).Completion;
    async Task<AccountLaunchOutcome> Run(AccountLaunchContext account,LaunchTargetDto target,Action<string,long> progress,TaskCompletionSource<bool> ready,CancellationToken token)
    {
        var timer=Stopwatch.StartNew();
        void Stage(string stage){progress(stage,timer.ElapsedMilliseconds);Log.Write("account-launch-stage",stage,(int)timer.ElapsedMilliseconds);}
        bool handedOff=false;
        IAccountLaunchSession? activeSession=null;
        PerformanceLaunchScope? performanceScope=null;
        try
        {
            if(OpenSession is null)return new("Unavailable","ACCOUNT_HOST_UNAVAILABLE");
            Stage("Preparing");
            var session=activeSession=await Session(account,token);
            if(string.IsNullOrEmpty(session.Cookie))return new("NeedsSignIn","ACCOUNT_SESSION_EXPIRED");
            performanceScope=await PerformanceLaunchScope.EnterAsync(token);
            var profile=LaunchProfile is null?null:await LaunchProfile(account,token);
            using var auth=new AccountLaunchAuthorization(session.Cookie,session.PersistCookieAsync);
            var grant=await auth.AuthorizeAsync(account.RobloxUserId,token);
            if(grant.Ticket is null)return new(grant.Code is "ACCOUNT_SESSION_EXPIRED" or "ACCOUNT_IDENTITY_MISMATCH" or "ACCOUNT_CHALLENGE_REQUIRED"?"NeedsSignIn":"Failed",grant.Code);
            // Snapshot before handoff; never adopt an existing player or correlate by name.
            var existing=new HashSet<ProcessKey>();
            Guid attempt=Guid.NewGuid();
            long timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string uri=AccountLaunchAuthorization.BuildPlayerUri(target,grant.Ticket,attempt,timestamp);
            var arguments=AccountLaunchAuthorization.BuildPlayerArguments(target,grant.Ticket,attempt,timestamp);
            token.ThrowIfCancellationRequested();Stage("Starting");
            long startedAt=0;
            var launch=await Launch.RunResultAsync(uri,token,()=>{Stage("Joining");ready.TrySetResult(true);},baseline=>{
                if(!baseline.Available||baseline.Unavailable!=0)throw new InvalidOperationException("Discovery unavailable.");
                existing=baseline.Instances.Select(x=>x.Key).ToHashSet();
            },()=>{handedOff=true;startedAt=timer.ElapsedMilliseconds;PlayerHandoff?.Invoke();Stage("Waiting for helper");},arguments,performanceScope,profile);
            await activeSession.DisposeAsync();activeSession=null;
            if(!ready.Task.IsCompleted){ready.TrySetResult(!handedOff);return new(handedOff?"Unknown":"Failed",handedOff?"HELPER_READINESS_UNCONFIRMED":"ACCOUNT_START_FAILED");}
            if(launch.Outcome is OperationOutcome.Failed or OperationOutcome.Canceled)return new("Unknown","ACCOUNT_START_UNCONFIRMED");
            // Require a fresh, trusted player with one window and its own open log.
            // Current players omit joinAttemptId; their join record must then bind
            // both the immutable account identity and the requested place.
            var membership=new Dictionary<string,bool>();
            while(timer.ElapsedMilliseconds-startedAt<60000)
            {
                token.ThrowIfCancellationRequested();
                var snapshot=await Snapshot(token);
                if(snapshot.Available&&snapshot.Unavailable==0)
                {
                    var matches=new List<ProcessKey>();
                    var servers=new Dictionary<ProcessKey,string?>();
                    foreach(var candidate in snapshot.Instances.Where(x=>!existing.Contains(x.Key)&&x.Windows.Length==1))
                    {
                        foreach(string log in await Task.Run(()=>InstanceAccounts.ReadLogs(candidate.Key,token).ToArray(),token))
                        {
                            bool match=Matches(log,account.RobloxUserId,attempt)||MatchesJoin(log,account.RobloxUserId,target.PlaceId);
                            if(!match&&target.Game?.UniverseId is {} universe&&ResolveUniverse is not null)
                            {
                                var join=System.Text.RegularExpressions.Regex.Matches(log,@"\[FLog::GameJoinLoadTime\][^\r\n]*",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
                                if(join.Count>0&&InstanceAccounts.ParseUserId(join[^1].Value)?.ToString()==account.RobloxUserId)
                                {
                                    var place=System.Text.RegularExpressions.Regex.Match(join[^1].Value,@"\bplaceid:([1-9][0-9]{0,18})(?=\s*,|\s*$)");
                                    if(place.Success){string id=place.Groups[1].Value;if(!membership.TryGetValue(id,out match)){try{match=await ResolveUniverse(id,token)==universe;membership[id]=match;}catch{}}}
                                }
                            }
                            if(match){matches.Add(candidate.Key);servers[candidate.Key]=ObservedServer(log);break;}
                        }
                    }
                    if(matches.Count>1)return new("Unknown","ACCOUNT_PROCESS_AMBIGUOUS");
                    if(matches.Count==1)
                    {
                        // Revalidate liveness, ownership and creation time after reading logs.
                        var key=matches[0];
                        using var process=new InstanceProcess(key.Id);
                        var identity=process.Identity;
                        if(process.IsAlive&&identity.Created==key.Created&&identity.Sid==Program.Sid&&identity.Session==Process.GetCurrentProcess().SessionId&&process.Windows().Length==1)
                        {
                            var outcome=new AccountLaunchOutcome("Started","ACCOUNT_IDENTITY_CONFIRMED",key.Id,key.Created,servers[key]);
                            // Optimization failure cannot turn a confirmed game launch into a retryable launch failure.
                            if(ManagedStarted is not null)try {await ManagedStarted(account,outcome,token);}catch {Log.Write("optimization-registration-unavailable","");}
                            return outcome;
                        }
                    }
                }
                await Task.Delay(750,token);
            }
            return new("Unknown","ACCOUNT_JOIN_UNCONFIRMED");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested)
        { return new(handedOff?"Unknown":"Canceled",handedOff?"LAUNCH_OBSERVATION_CANCELED":"BATCH_CANCELED"); }
        catch { return new(handedOff?"Unknown":"Failed",handedOff?"ACCOUNT_START_UNCONFIRMED":"ACCOUNT_PROFILE_UNAVAILABLE"); }
        finally{try{performanceScope?.Dispose();}finally{ready.TrySetResult(!handedOff);if(activeSession is not null)try{await activeSession.DisposeAsync();}catch{}Log.Write("account-launch-elapsed","",(int)timer.ElapsedMilliseconds);}}
    }
    public static bool MatchesJoin(string ownedLog,string expectedUserId,string expectedPlaceId)
    {
        if(!LaunchTargetResolver.IsPositiveId(expectedUserId)||!LaunchTargetResolver.IsPositiveId(expectedPlaceId))return false;
        var records=System.Text.RegularExpressions.Regex.Matches(ownedLog,@"\[FLog::GameJoinLoadTime\][^\r\n]*",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(records.Count==0)return false;
        string latest=records[^1].Value;
        return InstanceAccounts.ParseUserId(latest)?.ToString(System.Globalization.CultureInfo.InvariantCulture)==expectedUserId
            && System.Text.RegularExpressions.Regex.IsMatch(latest,@"\bplaceid:"+expectedPlaceId+@"(?=\s*,|\s*$)",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    }
    public static string? ObservedServer(string ownedLog)
    {
        var matches=System.Text.RegularExpressions.Regex.Matches(ownedLog,@"\[FLog::Output\] ! Joining game '([0-9a-fA-F-]{36})' place [1-9][0-9]{0,18}\b",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        return matches.Count>0&&Guid.TryParseExact(matches[^1].Groups[1].Value,"D",out var id)&&id!=Guid.Empty?id.ToString("D"):null;
    }
    public static bool Matches(string ownedLog,string expectedUserId,Guid attempt)
    {
        if(attempt==Guid.Empty||!LaunchTargetResolver.IsPositiveId(expectedUserId))return false;
        // ParseUserId uses the last GameJoinLoadTime userid, never a username or
        // an arbitrary numeric string elsewhere in a historical log.
        return InstanceAccounts.ParseUserId(ownedLog)?.ToString(System.Globalization.CultureInfo.InvariantCulture)==expectedUserId
            && ownedLog.Contains(attempt.ToString("D"),StringComparison.OrdinalIgnoreCase);
    }
}
