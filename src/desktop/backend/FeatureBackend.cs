using System.Diagnostics;
using System.Text.Json;
using RobloxLaunchHelper;
using LegacyProgram = RobloxLaunchHelper.Program;

namespace RobloxMultiInstance;

public sealed partial class DesktopBackend
{
    TelemetryService telemetry = null!;
    SupportService support = null!;
    SpecialBadgeService specialBadges = null!;
    AccountManager accounts = null!;
    GameLibrary games = null!;
    readonly CancellationTokenSource featureLifetime = new();
    Task? telemetryLoop;
    public Func<AccountSignInRequest,CancellationToken,Task<AccountIdentity?>>? AccountSignIn { get; set; }
    public Func<IDisposable>? AccountLayoutFocusScope {get;set;}
    public Func<AccountManager,Action<AccountCreationProgressDto>,CancellationToken,Task<AccountsStateDto>>? AccountCreate {get;set;}
    public Func<AccountManager,string,CancellationToken,Task>? AccountCredentialsOpen {get;set;}
    readonly BrowserAccountSessionProvider accountLauncher=new();
    public Func<AccountLaunchContext,CancellationToken,Task<IAccountLaunchSession>>? AccountLaunchSession
    { get=>accountLauncher.OpenSession;set=>accountLauncher.OpenSession=value; }
    public void RecordTechnicalFailure(int nativeError) => telemetry.RecordOperation("OPERATION_FAILED",nativeError);

    void InitializeFeatures(bool production)
    {
        telemetry = production ? new TelemetryService() : new TelemetryService(Path.Combine(storageRoot,"privacy"));
        support = new SupportService(storageRoot);
        specialBadges = new SpecialBadgeService(storageRoot);
        accounts = new AccountManager(storageRoot,accountLauncher);
        accounts.Recognition=id=>specialBadges.State([id]).Badges;
        try { var state=accounts.State();if(state.Available)accounts.Credentials.Reconcile(state.Accounts); }
        catch { /* Preserve encrypted pending records for native recovery. */ }
        games=new GameLibrary(storageRoot);accountLauncher.ResolveUniverse=games.Universe;
        if(production) telemetryLoop = UploadErrorsAsync(featureLifetime.Token);
    }

    async Task UploadErrorsAsync(CancellationToken token)
    {
        try
        {
            int failures=0;
            while(!token.IsCancellationRequested)
            {
                try
                {
                    var state=await telemetry.SendAsync(token);
                    failures=state.Enabled&&state.Configured&&state.QueuedErrors>0?Math.Min(failures+1,4):0;
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested) { break; }
                catch { failures=Math.Min(failures+1,4); }
                await Task.Delay(TimeSpan.FromMinutes(Math.Min(60,5*Math.Pow(2,failures))),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }

    TelemetryDto PrivacyState()
    {
        var s=telemetry.Status();
        return new(s.Enabled,s.Available,s.Configured,s.QueuedErrors,s.LastResult);
    }
    SupportDto SupportState(SupportState? checkedState=null)
    {
        var s=checkedState??support.State();
        return new(s.Configured,s.Supported,s.ShowBadge,s.Pending,s.Message,s.RobloxConfigured,s.ReceiptAvailable);
    }

    SpecialBadgeStateDto BadgeState()=>specialBadges.State(accounts.State().Accounts.Select(a=>a.RobloxUserId??""));

    async Task<object?> ExecuteFeatureAsync(string command,JsonElement p,CancellationToken token)
    {
        switch(command)
        {
            case "telemetry.get": return PrivacyState();
            case "telemetry.set":
                if(!p.TryGetProperty("enabled",out var enabled)||enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("Choose whether reporting is enabled.");
                var saved=telemetry.SetEnabled(enabled.GetBoolean());
                if(!saved.Available)throw new DesktopOperationException("PRIVACY_SAVE_FAILED","The reporting choice could not be saved. Refresh reporting status before trying again.");
                return PrivacyState();
            case "telemetry.clear":
                if(!telemetry.Clear().Available)throw new DesktopOperationException("PRIVACY_CLEAR_FAILED","Queued reports could not be cleared. Check storage access and try again.");
                return PrivacyState();
            case "telemetry.send": await telemetry.SendAsync(token);return PrivacyState();
            case "support.badges": return BadgeState();
            // Paths never enter the page bridge; a native dialog supplies them.
            case "support.importBadgeFile":
            {
                var path=Text(p,"path");var receipt=specialBadges.ReadImport(path);var grant=specialBadges.Verify(receipt);
                if(AccountSignIn is null)throw new InvalidOperationException("Recognition claim requires the desktop sign-in window.");
                var user=await accounts.ClaimRecognitionAsync(id=>specialBadges.Eligible(grant,id),AccountSignIn,token);
                token.ThrowIfCancellationRequested();
                var message=specialBadges.Import(path,receipt,user);
                var state=BadgeState() with {Message=message,ClaimedBadgeId=grant.Badge};Event?.Invoke(requestId.Value??"recognition","badges.changed",state);return state;
            }
            case "support.get": return SupportState(await support.CheckRobloxAsync(accounts.State().Accounts.Select(a=>a.RobloxUserId??""),token:token));
            case "support.checkRoblox": return SupportState(await support.CheckRobloxAsync(accounts.State().Accounts.Select(a=>a.RobloxUserId??""),true,token));
            case "support.roblox":
                Process.Start(new ProcessStartInfo(support.RobloxDonationUri().AbsoluteUri){UseShellExecute=true})?.Dispose();
                return SupportState();
            case "support.start":
                var checkout=await support.StartAsync(token);
                Process.Start(new ProcessStartInfo(checkout.AbsoluteUri){UseShellExecute=true})?.Dispose();
                return SupportState();
            case "support.claim": await support.ClaimAsync(token);return SupportState();
            case "support.visibility": support.SetBadgeVisible(Bool(p,"visible"));return SupportState();
            case "support.remove": Confirm(p);support.RemoveReceipt();return SupportState();
            case "support.forgetPending": Confirm(p);support.ClearPending();return SupportState();
            // File paths are supplied by native dialogs; these commands are not bridge-allowlisted.
            case "support.importFile": support.ImportReceipt(Text(p,"path"));return SupportState();
            case "support.exportFile": support.ExportReceipt(Text(p,"path"));return SupportState();
            case "accounts.state":
            {
                var state=accounts.State();
                if(p.TryGetProperty("includeAvatars",out var includeAvatars))
                {
                    if(includeAvatars.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Choose whether to include account avatars.");
                    if(!includeAvatars.GetBoolean())return state;
                }
                var enriched=new List<AccountDto>();
                foreach(var account in state.Accounts)enriched.Add(account with {Avatar=account.RobloxUserId is {} userId?await games.Avatar(userId,token):null});
                return state with {Accounts=enriched.ToArray()};
            }
            case "accounts.create":
                if(AccountCreate is null)throw new DesktopOperationException("CREATION_UNAVAILABLE","Account creation requires the desktop host.");
                return await AccountCreate(accounts,value=>Event?.Invoke(requestId.Value??"creation","accounts.creationProgress",value),token);
            case "accounts.credentials.open":
                if(AccountCredentialsOpen is null)throw new DesktopOperationException("CREDENTIALS_UNAVAILABLE","Credentials require the desktop host.");
                await AccountCredentialsOpen(accounts,Text(p,"id"),token);return accounts.State();
            case "accounts.connect":
                if(AccountSignIn is null)throw new DesktopOperationException("SIGN_IN_UNAVAILABLE","Account sign-in requires the desktop host.");
                var connectedAccounts=await accounts.ConnectAsync(string.IsNullOrEmpty(Text(p,"id"))?null:Text(p,"id"),Text(p,"nickname"),AccountSignIn,token);
                var recognition=await support.CheckRobloxAsync(connectedAccounts.Accounts.Select(a=>a.RobloxUserId??""),true,token);
                Event?.Invoke(requestId.Value??"support","support.changed",SupportState(recognition));
                Event?.Invoke(requestId.Value??"recognition","badges.changed",BadgeState());
                return connectedAccounts;
            case "accounts.rename": return await accounts.RenameAsync(Text(p,"id"),Text(p,"nickname"),token);
            case "accounts.organize": return await accounts.OrganizeAsync(Text(p,"id"),Text(p,"group"),Text(p,"notes"),Text(p,"color"),Bool(p,"favorite"),token);
            case "accounts.optimizationBypass":
                if(!p.TryGetProperty("bypass",out var bypass)||bypass.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Choose whether to bypass optimizations.");
                var bypassState=await accounts.SetOptimizationBypassAsync(Text(p,"id"),bypass.GetBoolean(),token);
                await RefreshAutomaticPerformanceAsync(token);
                if(performanceWorkerError.Length>0||bypass.GetBoolean()&&automaticPerformance.Snapshot(true).Instances.Any(x=>x.AccountId==Text(p,"id")&&x.State!="Bypassed"))
                    return bypassState with {Notice="Bypass preference saved. Some live controls could not be verified or restored; open Performance to review the results and recovery status."};
                return bypassState;
            case "accounts.order": return await accounts.OrderAsync(p.GetProperty("ids").Deserialize<string[]>()??throw new ArgumentException("Choose accounts."),token);
            case "accounts.remove": Confirm(p);var remaining=await accounts.RemoveAsync(Text(p,"id"),token);Event?.Invoke(requestId.Value??"recognition","badges.changed",BadgeState());return remaining;
            case "accounts.target":
                var checkedTarget=accounts.ValidateTarget(Text(p,"target"),Text(p,"jobId"));
                return checkedTarget with {Game=await games.Resolve(checkedTarget.PlaceId,token)};
            case "games.recent": return await games.Recent(token);
            case "games.remove": return await games.Remove(Text(p,"placeId"),false,token);
            case "games.clear": return await games.Remove(null,true,token);
            case "accounts.launchBatch":
                RequireHelper();
                var selectedIds=p.GetProperty("ids").Deserialize<string[]>()??throw new ArgumentException("Select accounts.");
                var presetName=Text(p,"preset");
                var arrangement=BatchArrangement(Text(p,"arrangement"),presetName);
                WindowPreset? selectedPreset=null;
                if(presetName.Length>0)
                {
                    selectedPreset=settings.Presets.FirstOrDefault(x=>x.Name==presetName)??throw new ArgumentException("Choose an existing layout preset.");
                    if(selectedPreset.Slots.Length!=selectedIds.Length)throw new ArgumentException("Select one account for each preset slot.");
                }
                var parsed=accounts.ValidateTarget(Text(p,"target"),Text(p,"jobId"));
                var card=await games.Resolve(parsed.PlaceId,token);
                var targets=Enumerable.Repeat(parsed with {Game=card},selectedIds.Length).ToArray();
                string?[] assigned=parsed.JobId is null?GameLibrary.Assign(await games.Servers(parsed.PlaceId,token),selectedIds.Length):Enumerable.Repeat<string?>(parsed.JobId,selectedIds.Length).ToArray();
                var checkedGroups=new HashSet<string>();int handedOff=0;
                for(int index=0;index<selectedIds.Length;index++)Event?.Invoke(requestId.Value??"accounts","accounts.progress",new LaunchBatchProgressDto("planning",selectedIds[index],index+1,"Queued","",0,assigned[index]));
                if(assigned.FirstOrDefault() is {} firstGroup)checkedGroups.Add(firstGroup);
                accountLauncher.PlayerHandoff=()=>Interlocked.Increment(ref handedOff);
                accounts.TargetForSlot=async(index,ct)=>{
                    string? job=assigned[index];
                    if(parsed.JobId is null&&job is not null&&checkedGroups.Add(job))
                    {
                        var current=await games.Servers(parsed.PlaceId,ct);
                        // Reserve all earlier assignments conservatively: some clients
                        // may still be joining and not counted in the public snapshot.
                        var reserved=assigned.Take(index).Where(x=>x is not null).GroupBy(x=>x!).ToDictionary(g=>g.Key,g=>g.Count());
                        current=current.Select(s=>s with {Playing=s.Playing+reserved.GetValueOrDefault(s.Id)}).ToArray();
                        var replacement=GameLibrary.Assign(current,selectedIds.Length-index);
                        for(int j=index;j<assigned.Length;j++)assigned[j]=replacement[j-index];
                        job=assigned[index];if(job is not null)checkedGroups.Add(job);
                    }
                    return targets[index] with {JobId=job};
                };
                LaunchBatchResultDto batch;
                try {batch=await accounts.RunBatchAsync(selectedIds,Text(p,"target"),Text(p,"jobId"),presetName,
                    progress=>Event?.Invoke(requestId.Value??"accounts","accounts.progress",progress),token);}
                finally
                {
                    accounts.TargetForSlot=null;accountLauncher.PlayerHandoff=null;
                    if(handedOff>0)try{await games.Remember(card with {AccountIds=selectedIds,Preset=presetName,Arrangement=arrangement is null?null:Text(p,"arrangement")},CancellationToken.None);}catch{/* History must not change a launch outcome. */}
                }
                return new AccountBatchDto(batch,selectedPreset is null&&arrangement is null?"No automatic layout selected.":await ApplyBatchLayoutAsync(batch,selectedPreset,arrangement??ArrangementKind.Saved,token));
            default: throw new DesktopOperationException("INVALID_REQUEST","Unknown feature command.");
        }
    }

    // A later verified launch adapter must supply unique PID + creation-time pairs.
    // Fresh trusted discovery revalidates those exact processes and one window each.
    internal static InstanceTarget[]? BatchTargets(LaunchBatchResultDto batch,InstanceSnapshot snapshot)
    {
        if(!batch.CanApplyLayout||batch.Status!="Completed"||!snapshot.Available||snapshot.Unavailable>0||batch.Items.Length==0)return null;
        var ordered=batch.Items.OrderBy(x=>x.Slot).ToArray();
        if(ordered.Select(x=>x.Slot).Where((slot,index)=>slot!=index+1).Any()||ordered.Select(x=>(x.Pid,x.Created)).Distinct().Count()!=ordered.Length)return null;
        var result=new List<InstanceTarget>();
        foreach(var item in ordered)
        {
            if(item.Status!="Started"||item.Pid is not >0||item.Created is not >0)return null;
            var matches=snapshot.Instances.Where(x=>x.Key==new ProcessKey(item.Pid.Value,item.Created.Value)).ToArray();
            if(matches.Length!=1||matches[0].Windows.Length!=1)return null;
            result.Add(new(matches[0].Key,matches[0].Windows[0].Handle));
        }
        return result.ToArray();
    }

    internal static ArrangementKind? BatchArrangement(string arrangement,string preset)
    {
        if(arrangement.Length==0)return null;
        if(preset.Length>0||arrangement is not ("Grid" or "Columns"))throw new ArgumentException("Choose Grid, Columns, or one saved layout.");
        return arrangement=="Columns"?ArrangementKind.SideBySide:ArrangementKind.Grid;
    }

    async Task<string> ApplyBatchLayoutAsync(LaunchBatchResultDto batch,WindowPreset? preset,ArrangementKind arrangement,CancellationToken token)
    {
        using var focusScope=AccountLayoutFocusScope?.Invoke();
        if(!batch.CanApplyLayout)return "Layout was not applied: every account must have a verified window. Open Layouts to arrange confirmed windows manually.";
        try
        {
            using var current=Process.GetCurrentProcess();
            var snapshot=await Task.Run(()=>new InstanceCollector(platform,LegacyProgram.Sid,current.SessionId,collectMetrics:false).Collect(token),token);
            var batchTargets=BatchTargets(batch,snapshot);
            if(batchTargets is null||preset is not null&&batchTargets.Length!=preset.Slots.Length)return "Layout was not applied: account-to-window matching is incomplete or ambiguous.";
            var monitors=placement.Monitors();
            var windows=batchTargets.Select(t=>{using var session=placement.Open(t);return session.Read();}).ToArray();
            var proposed=BuildPlan(monitors,windows,windows.Select(_=>monitors.FirstOrDefault(m=>m.Primary)?.Id??"").ToArray(),arrangement,preset);
            if(!proposed.Valid)return "The layout could not be prepared. Open Layouts to review the available windows and monitors.";
            token.ThrowIfCancellationRequested();
            var results=await new PlacementCoordinator(placement).ApplyAsync(proposed,token);
            return results.Length==batchTargets.Length&&results.All(r=>r.Message=="Placed.")?(preset is null?(arrangement==ArrangementKind.SideBySide?"Columns":"Grid")+" layout applied in account selection order.":"Saved layout applied in account selection order."):"Some windows could not be placed. Open Layouts to review them; account slots were preserved.";
        }
        catch(OperationCanceledException) { return "Layout canceled. Already started games remain open."; }
        catch { return "Layout could not be applied. Already started games remain open; open Layouts to arrange them manually."; }
    }

    void DisposeFeatures()
    {
        featureLifetime.Cancel();
        telemetry.Dispose();
        support.Dispose();
        games.Dispose();
        // Observe the cooperative uploader before releasing its cancellation source.
        if(telemetryLoop is not null)_=telemetryLoop.ContinueWith(_=>featureLifetime.Dispose(),TaskScheduler.Default);
        else featureLifetime.Dispose();
    }
}
