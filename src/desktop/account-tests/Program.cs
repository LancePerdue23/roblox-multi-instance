using System.Text;
using System.Text.Json;
using RobloxMultiInstance;
using RobloxMultiInstance.Desktop;
using System.Security.AccessControl;
using System.Security.Principal;

if(args.Length==2&&args[0]=="--public-artwork")
{
    using var games=new GameLibrary(Path.Combine(Path.GetTempPath(),"RobloxPublicArtworkCheck"));
    var card=await games.Resolve(LaunchTargetResolver.Parse(args[1]).PlaceId,CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new {card.Name,card.ArtworkKind,card.ArtworkState,EncodedBytes=card.Artwork?.Length??0}));
    return card.Artwork is not null&&card.ArtworkKind=="Icon"?0:1;
}

int checks=0;
void Check(bool condition,string name) { if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
void Reject(Action action,string name) { try { action();throw new Exception("Accepted: "+name); } catch(ArgumentException) { Check(true,name); } }
string root=Path.Combine(Directory.GetCurrentDirectory(),".build","account-tests-"+Guid.NewGuid().ToString("N"));
try
{
    checks+=await AccountLaunchTests.RunAsync();
    checks+=await GameLaunchTests.RunAsync();
    Check(AccountSignInPolicy.AllowedNavigation("https://www.roblox.com/login"),"Sign-in permits exact Roblox HTTPS origin");
    foreach(string remote in new[]{"https://desktop.local/","https://evil.test/","https://roblox.com.evil.test/","file:///C:/test","data:text/html,secret","https://user@www.roblox.com/","https://www.roblox.com:444/","https://www.roblox.com\\@evil.test/"})
        Check(!AccountSignInPolicy.AllowedNavigation(remote),"Remote sign-in blocks privileged or unrelated navigation");
    Check(AccountSignInPolicy.AllowedFrame("https://client-api.arkoselabs.com/challenge")&&!AccountSignInPolicy.AllowedFrame("https://desktop.local/index.html")&&!AccountSignInPolicy.AllowedFrame("file:///C:/test"),"Challenge frames have HTTPS-only isolation from local app and file origins");
    var target=LaunchTargetResolver.Parse("https://www.roblox.com/games/123456/Example-game", "AA112233-4455-6677-8899-AABBCCDDEEFF");
    Check(target.PlaceId=="123456"&&target.JobId=="aa112233-4455-6677-8899-aabbccddeeff","Game target canonicalizes place and server IDs");
    Check(LaunchTargetResolver.Parse(" 123 ").PlaceId=="123","Numeric Place ID accepted");
    foreach(string unsafeValue in new[]{"0","-12","9223372036854775808","https://evil.test/games/123","https://www.roblox.com.evil.test/games/123","http://www.roblox.com/games/123","https://user@www.roblox.com/games/123","https://www.roblox.com:444/games/123","https://www.roblox.com/games/123?privateServerLinkCode=secret","https://www.roblox.com/games/123#fragment","https://www.roblox.com/share?code=secret","roblox-player:secret","https://www.roblox.com\\@evil.test/games/123"})Reject(()=>LaunchTargetResolver.Parse(unsafeValue),"Unsafe target rejected: "+unsafeValue.Split('?')[0]);
    Reject(()=>LaunchTargetResolver.Parse("123","00000000-0000-0000-0000-000000000000"),"Empty server UUID rejected");
    Reject(()=>LaunchTargetResolver.Parse("123","server-not-a-job"),"Malformed server UUID rejected");
    foreach(var fixture in new[]{(200,"{\"id\":101,\"name\":\"AccountOne\"}","Valid"),(401,"{}","Expired"),(403,"{}","Challenged"),(429,"{}","RateLimited"),(302,"{}","Unavailable"),(200,"not-json","Unavailable"),(200,new string('x',8193),"Unavailable")})
    {
        using var validator=new AccountSessionValidator(new ValidationHandler(fixture.Item1,fixture.Item2));
        var validation=await validator.ValidateAsync("SESSION_CANARY_FIXTURE","101",default);
        Check(validation.Status==fixture.Item3&&validation.CheckedAt<=DateTimeOffset.UtcNow,"Session validator classifies bounded HTTP fixture: "+fixture.Item3);
    }
    using(var validator=new AccountSessionValidator(new ValidationHandler(200,"{\"id\":102,\"name\":\"AccountTwo\"}")))
    {
        var wrong=await validator.ValidateAsync("SESSION_CANARY_FIXTURE","101",default);
        Check(wrong.Status=="WrongIdentity"&&wrong.Identity is null,"Wrong-identity validation never supplies an accepted native identity");
        using var alreadyCanceled=new CancellationTokenSource();alreadyCanceled.Cancel();bool canceledValidation=false;
        try { await validator.ValidateAsync("SESSION_CANARY_FIXTURE",null,alreadyCanceled.Token); } catch(OperationCanceledException) { canceledValidation=true; }
        Check(canceledValidation,"Session validation preserves caller cancellation");
    }
    var manager=new AccountManager(root);Check(manager.State().Accounts.Length==0&&!Directory.Exists(root),"Read-only account startup creates no storage");
    Check(!manager.State().LaunchAvailable&&manager.State().Notice.Contains("not implemented"),"Unverified live launcher explicitly disabled");
    checks+=await AccountCreationTests.RunAsync(Path.Combine(root,"creation"));
    var first=await manager.ConnectAsync(null,"First",(request,token)=>Task.FromResult<AccountIdentity?>(new("101","AccountOne")),default);
    Check(first.Accounts.Single().Status=="Saved locally; identity verified when saved","Successful sign-in reports saved identity verification");
    var reopened=new AccountManager(root).State();
    Check(reopened.Accounts.Single().Status==first.Accounts.Single().Status&&!reopened.LaunchAvailable&&reopened.Notice.Contains("not implemented"),"Reopening preserves historical identity status without implying launch support or session validity");
    var second=await manager.ConnectAsync(null,"Second",(request,token)=>Task.FromResult<AccountIdentity?>(new("102","AccountTwo")),default);
    var ids=second.Accounts.Select(a=>a.Id).ToArray();
    Check(ids.Length==2&&ids.Distinct().Count()==2&&ids.All(x=>x.Length==32),"Saved accounts use opaque distinct local IDs");
    Check(first.Accounts.Single().RobloxUserId=="101"&&!JsonSerializer.Serialize(first).Contains("ProfileDirectory"),"Bridge exposes the existing public avatar identity but excludes private browser profile paths");
    var renamed=await manager.RenameAsync(ids[0],"Renamed",default);Check(renamed.Accounts[0].Nickname=="Renamed","Local nickname can change");
    var organized=await manager.OrganizeAsync(ids[0]," Team ","Local notes\nSecond line","#a0B1c2",true,default);
    Check(organized.Accounts[0].Group=="Team"&&organized.Accounts[0].Notes.Contains("Second line")&&organized.Accounts[0].Color=="#A0B1C2"&&organized.Accounts[0].Favorite,"Account organization normalizes group/color and preserves notes");
    await manager.OrderAsync([ids[1],ids[0]],default);
    Check(new AccountManager(root).State().Accounts.Select(a=>a.Id).SequenceEqual(new[]{ids[1],ids[0]}),"Saved account order survives reopening");
    bool staleOrder=false;try { await manager.OrderAsync([ids[0]],default); } catch(ArgumentException) { staleOrder=true; }
    Check(staleOrder&&manager.State().Accounts[0].Id==ids[1],"Stale or incomplete account ordering cannot discard accounts");
    bool invalidNotes=false;try { await manager.OrganizeAsync(ids[0],"Group",new string('x',1001),"",false,default); } catch(ArgumentException) { invalidNotes=true; }
    Check(invalidNotes&&manager.State().Accounts.Single(a=>a.Id==ids[0]).Favorite,"Oversized organization input leaves committed details unchanged");
    await manager.ConnectAsync(ids[0],"Renamed",(r,t)=>Task.FromResult<AccountIdentity?>(new("101","AccountOne")),default);
    Check(manager.State().Accounts.Single(a=>a.Id==ids[0]).Favorite&&manager.State().Accounts.Single(a=>a.Id==ids[0]).Group=="Team","Reconnect preserves account organization");
    await manager.OrderAsync(ids,default);
    string migrationRoot=Path.Combine(root,"migration");string migrationDirectory=Path.Combine(migrationRoot,"accounts");Directory.CreateDirectory(migrationDirectory);
    string v1=JsonSerializer.Serialize(new { Version=1,Accounts=new[]{new {Id=ids[0],Nickname="Original",RobloxUserId="101",Username="AccountOne"}}});
    string migrationPath=Path.Combine(migrationDirectory,"accounts.json");File.WriteAllText(migrationPath,v1);
    var migrating=new AccountManager(migrationRoot);
    Check(migrating.State().Accounts.Single().Group==""&&File.ReadAllText(migrationPath)==v1&&!File.Exists(migrationPath+".v1.bak"),"Reading v1 metadata supplies defaults without writing or migrating");
    await migrating.OrganizeAsync(ids[0],"Migrated","Preserved","",true,default);
    Check(File.ReadAllText(migrationPath+".v1.bak")==v1&&JsonDocument.Parse(File.ReadAllText(migrationPath)).RootElement.GetProperty("Version").GetInt32()==2,"First mutation preserves exact v1 original before atomic v2 commit");
    bool duplicate=false;try { await manager.ConnectAsync(null,"Duplicate",(r,t)=>Task.FromResult<AccountIdentity?>(new("101","AccountOne")),default); } catch(InvalidOperationException) { duplicate=true; }Check(duplicate&&manager.State().Accounts.Length==2,"Duplicate Roblox accounts rejected");
    bool mismatch=false;try { await manager.ConnectAsync(ids[0],"Wrong",(r,t)=>Task.FromResult<AccountIdentity?>(new("103","AccountOther")),default); } catch(InvalidOperationException) { mismatch=true; }Check(mismatch&&manager.State().Accounts[0].Username=="AccountOne","Reconnect cannot silently switch identity");
    var offline=await manager.RunBatchAsync(ids,"123",null,"Layout",null,default);Check(offline.Status=="Partial"&&!offline.CanApplyLayout&&offline.Items.All(i=>i.Code=="ACCOUNT_LAUNCH_UNVERIFIED"),"Default batch never pretends to launch");
    var provider=new FakeProvider();var coordinator=new LaunchBatchCoordinator(provider);
    var contexts=ids.Select((id,i)=>new AccountLaunchContext(id,"unused-profile-"+i,(101+i).ToString())).ToArray();
    var progress=new List<LaunchBatchProgressDto>();
    provider.Outcomes.Enqueue(new("NeedsSignIn","ACCOUNT_SESSION_EXPIRED"));provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",222,333));
    var partial=await coordinator.RunAsync(contexts,new("123",null),"Saved",progress.Add,default);
    Check(!partial.CanApplyLayout&&partial.Items[0].Slot==1&&partial.Items[1].Slot==2&&partial.Items[1].AccountId==ids[1],"Partial batches preserve account-to-slot order");
    Check(provider.MaximumActive==1&&provider.Calls==2,"Launch queue is serialized and continues after account failure");
    provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",111,222));provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",222,333));
    var full=await coordinator.RunAsync(contexts,new("123",null),"Saved",null,default);Check(full.CanApplyLayout&&full.Status=="Completed"&&full.PresetName=="Saved","Only fully correlated batches permit later layout application");
    provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",111,222));provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",111,222));
    var duplicateProcess=await coordinator.RunAsync(contexts,new("123",null),null,null,default);Check(!duplicateProcess.CanApplyLayout,"Same process cannot satisfy two layout slots");
    provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED"));provider.Outcomes.Enqueue(new("Failed","ACCOUNT_LAUNCH_FAILED"));
    var unknown=await coordinator.RunAsync(contexts,new("123",null),null,null,default);Check(unknown.Items[0].Status=="Unknown"&&!unknown.CanApplyLayout,"Process-free success cannot claim a launch");
    using var cancellation=new CancellationTokenSource();provider.BeforeReturn=()=>cancellation.Cancel();provider.Outcomes.Enqueue(new("Started","ACCOUNT_IDENTITY_CONFIRMED",333,444));int calls=provider.Calls;
    var canceled=await coordinator.RunAsync(contexts,new("123",null),null,null,cancellation.Token);
    Check(canceled.Status=="Canceled"&&canceled.Items[0].Status=="Started"&&canceled.Items[1].Status=="Canceled"&&provider.Calls==calls+1&&!canceled.CanApplyLayout,"Cancellation preserves started game and cancels pending account without replay");
    provider.BeforeReturn=null;provider.Throw=true;calls=provider.Calls;
    var failed=await coordinator.RunAsync([contexts[0]],new("123",null),null,null,default);Check(failed.Items[0].Code=="ACCOUNT_LAUNCH_FAILED"&&provider.Calls==calls+1,"Provider failures are sanitized and never replayed");
    provider.Throw=false;provider.Outcomes.Enqueue(new("PRIVATE_COOKIE_BODY","unsafe detail with spaces"));
    var malformed=await coordinator.RunAsync([contexts[0]],new("123",null),null,null,default);Check(malformed.Items[0].Code=="ACCOUNT_PROVIDER_RESULT_INVALID"&&!JsonSerializer.Serialize(malformed).Contains("PRIVATE_COOKIE"),"Malformed provider output is sanitized before page events");
    var vault=new WindowsAccountVault(Path.Combine(root,"test-vault"));byte[] canary=Encoding.UTF8.GetBytes("COOKIE_CANARY_ONLY_LOCAL");vault.Save(ids[0],canary);
    Check(vault.Read(ids[0])!.SequenceEqual(canary)&&!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"test-vault",ids[0]+".bin"))).Contains("COOKIE_CANARY"),"DPAPI vault roundtrips without plaintext secret persistence");
    Reject(()=>vault.Read("../escape"),"Vault rejects path traversal account IDs");vault.Remove(ids[0]);Check(vault.Read(ids[0]) is null,"Removing a vault entry forgets local secret");
    var removed=await manager.RemoveAsync(ids[0],default);Check(removed.Accounts.Length==1&&removed.Accounts[0].Id==ids[1],"Remove account preserves other account");
    string? canceledProfile=null;
    await manager.ConnectAsync(null,"Canceled",(request,token)=>
    {
        canceledProfile=request.ProfileDirectory;Directory.CreateDirectory(canceledProfile);File.WriteAllText(Path.Combine(canceledProfile,"fixture-browser-data"),"local session fixture");
        return Task.FromResult<AccountIdentity?>(null);
    },default);
    Check(canceledProfile is not null&&!Directory.Exists(canceledProfile)&&manager.State().Accounts.Length==1,"Canceled new connection removes unsaved profile directory");
    string privateRoot=Path.Combine(root,"accounts");using var windowsIdentity=WindowsIdentity.GetCurrent();
    var rules=FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(privateRoot)).GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
    var allowedSids=new HashSet<string>{windowsIdentity.User!.Value,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null).Value,new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null).Value};
    Check(rules.All(rule=>allowedSids.Contains(rule.IdentityReference.Value))&&rules.Any(rule=>rule.IdentityReference.Value==windowsIdentity.User.Value),"Account root grants access only to owner Windows SID, SYSTEM and administrators");
    var leaseStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseLease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var appA=new AccountManager(root);var appB=new AccountManager(root);bool secondSignInStarted=false;
    var addA=appA.ConnectAsync(null,"App A",async (request,token)=>{leaseStarted.SetResult();await releaseLease.Task;return new AccountIdentity("201","CrossProcessOne");},default);
    await leaseStarted.Task;
    var addB=appB.ConnectAsync(null,"App B",(request,token)=>{secondSignInStarted=true;return Task.FromResult<AccountIdentity?>(new("202","CrossProcessTwo"));},default);
    await Task.Delay(150);Check(!secondSignInStarted,"Independent managers share a file lease across asynchronous sign-in");
    releaseLease.SetResult();await Task.WhenAll(addA,addB);
    Check(appA.State().Accounts.Any(a=>a.Username=="CrossProcessOne")&&appA.State().Accounts.Any(a=>a.Username=="CrossProcessTwo"),"Independent manager commits preserve both newly added accounts");
    var heldStart=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var heldRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var held=appA.ConnectAsync(null,"Canceled",(async (request,token)=>{heldStart.SetResult();await heldRelease.Task;return null;}),default);await heldStart.Task;
    using(var waiting=new CancellationTokenSource(150))
    {
        bool waitCanceled=false;try { await appB.RenameAsync(ids[1],"Blocked",waiting.Token); } catch(OperationCanceledException) { waitCanceled=true; }
        Check(waitCanceled,"Waiting for another app's account operation can be canceled without changes");
    }
    heldRelease.SetResult();await held;
    string outside=Path.Combine(root,"outside-profile");Directory.CreateDirectory(outside);string outsideCanary=Path.Combine(outside,"preserve.txt");File.WriteAllText(outsideCanary,"outside canary");
    string alias=Path.Combine(root,"redirected");
    bool symbolicLinks=false;
    try { Directory.CreateSymbolicLink(alias,outside);symbolicLinks=true; }
    catch(Exception e) when(e is UnauthorizedAccessException or IOException) { Console.WriteLine("SKIP reparse fixture: symbolic-link creation unavailable in this Windows account"); }
    if(symbolicLinks)
    {
        try
        {
            bool rootRejected=false;try { AccountStorageSecurity.ValidatePath(Path.Combine(alias,"child","new-file")); } catch(IOException) { rootRejected=true; }
            Check(rootRejected,"Storage validation rejects redirected ancestor even when final file is missing");
            var linkedManager=new AccountManager(alias);Check(!linkedManager.State().Available,"Redirected account root is unavailable before reads");
            bool vaultRejected=false;try { new WindowsAccountVault(alias).Save(ids[1],[1,2,3]); } catch(IOException) { vaultRejected=true; }
            Check(vaultRejected&&File.ReadAllText(outsideCanary)=="outside canary","Vault refuses redirected writes without changing external target");
        }
        finally { Directory.Delete(alias); }
        string profileParent=Path.Combine(privateRoot,"profiles");Directory.CreateDirectory(profileParent);string profile=Path.Combine(profileParent,ids[1]);
        if(Directory.Exists(profile))Directory.Delete(profile,true);
        Directory.CreateSymbolicLink(profile,outside);
        try
        {
            bool removalRejected=false;try { await manager.RemoveAsync(ids[1],default); } catch(IOException) { removalRejected=true; }
            Check(removalRejected&&File.Exists(outsideCanary)&&manager.State().Accounts.Any(a=>a.Id==ids[1]),"Removing redirected profile preserves outside files and account metadata");
        }
        finally { Directory.Delete(profile); }
        Directory.Delete(profileParent);Directory.CreateSymbolicLink(profileParent,outside);
        try
        {
            bool parentRejected=false;try { await manager.RemoveAsync(ids[1],default); } catch(IOException) { parentRejected=true; }
            Check(parentRejected&&File.ReadAllText(outsideCanary)=="outside canary","Profile-parent junction is rejected before recursive removal");
            bool signInCalled=false;bool reconnectRejected=false;
            try { await manager.ConnectAsync(ids[1],"Second",(request,token)=>{signInCalled=true;return Task.FromResult<AccountIdentity?>(null);},default); } catch(IOException) { reconnectRejected=true; }
            Check(reconnectRejected&&!signInCalled,"Profile-parent junction is rejected before opening native sign-in");
        }
        finally { Directory.Delete(profileParent); }
        string vaultFile=Path.Combine(root,"test-vault",ids[1]+".bin");File.CreateSymbolicLink(vaultFile,outsideCanary);
        try
        {
            bool readRejected=false;try { vault.Read(ids[1]); } catch(IOException) { readRejected=true; }
            bool deleteRejected=false;try { vault.Remove(ids[1]); } catch(IOException) { deleteRejected=true; }
            Check(readRejected&&deleteRejected&&File.Exists(outsideCanary),"Vault rejects final-file reparse points before read or delete");
        }
        finally { File.Delete(vaultFile); }
    }
    string metadata=Path.Combine(root,"accounts","accounts.json");File.WriteAllText(metadata,"corrupt");Check(!manager.State().Available,"Corrupt metadata becomes unavailable without overwriting");
    bool preserved=false;try { await manager.RemoveAsync(ids[1],default); } catch(JsonException) { preserved=File.ReadAllText(metadata)=="corrupt"; }Check(preserved,"Corrupt local metadata preserved during attempted mutation");
    Console.WriteLine($"{checks} account foundation checks passed.");return 0;
}
finally { if(Directory.Exists(root))Directory.Delete(root,true); }

sealed class FakeProvider : IAccountSessionProvider
{
    public bool LaunchAvailable=>true;
    public string Notice=>"Fixture provider only";
    public Queue<AccountLaunchOutcome> Outcomes {get;}=new();
    public int Calls,MaximumActive;int active;
    public bool Throw;
    public Action? BeforeReturn;
    public async Task<AccountLaunchOutcome> LaunchAsync(AccountLaunchContext account,LaunchTargetDto target,CancellationToken token)
    {
        Calls++;MaximumActive=Math.Max(MaximumActive,++active);
        try { await Task.Yield();if(Throw)throw new Exception("PRIVATE_COOKIE_CANARY");var outcome=Outcomes.Dequeue();BeforeReturn?.Invoke();return outcome; }
        finally { active--; }
    }
}

sealed class ValidationHandler(int status,string body) : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)
    {
        if(request.RequestUri?.AbsoluteUri!="https://users.roblox.com/v1/users/authenticated"||request.Method!=System.Net.Http.HttpMethod.Get)throw new Exception("Unexpected validation endpoint");
        return Task.FromResult(new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)status) { Content=new System.Net.Http.StringContent(body) });
    }
}
