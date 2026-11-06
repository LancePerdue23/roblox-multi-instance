using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobloxMultiInstance;

// All bridge-visible records deliberately exclude cookies, tickets, profile paths and tokens.
public sealed record AccountDto(string Id,string Nickname,string Username,string Status,string Group="",string Notes="",string Color="",bool Favorite=false,int Order=0,string? RobloxUserId=null,string? Avatar=null,bool BypassOptimizations=false);
public sealed record AccountsStateDto(bool Available,bool LaunchAvailable,string Notice,AccountDto[] Accounts);
public sealed record LaunchTargetDto(string PlaceId,string? JobId,GameCardDto? Game=null);
public sealed record LaunchBatchProgressDto(string BatchId,string AccountId,int Slot,string Status,string Code,long ElapsedMs=0,string? PlannedServer=null);
public sealed record LaunchBatchItemDto(string AccountId,int Slot,string Status,string Code,int? Pid,long? Created,string? PlannedServer=null,string? ObservedServer=null,long ElapsedMs=0);
public sealed record LaunchBatchResultDto(string BatchId,string Status,bool CanApplyLayout,string? PresetName,LaunchBatchItemDto[] Items);

// Native-only types: never serialize these through the page bridge.
public sealed record AccountIdentity(string RobloxUserId,string Username);
public sealed record AccountSignInRequest(string Id,string ProfileDirectory,string? ExpectedRobloxUserId,string[]? ExcludedRobloxUserIds=null);
public sealed record AccountLaunchContext(string Id,string ProfileDirectory,string RobloxUserId);
public sealed record AccountLaunchOutcome(string Status,string Code,int? Pid=null,long? Created=null,string? ObservedServer=null)
{
    public bool Verified => Status=="Started" && Pid is >0 && Created is >0;
}

public sealed record PendingAccountLaunch(Task<bool> Ready,Task<AccountLaunchOutcome> Completion);
public interface IPipelinedAccountSessionProvider : IAccountSessionProvider
{
    PendingAccountLaunch Start(AccountLaunchContext account,LaunchTargetDto target,Action<string,long> progress,CancellationToken token);
    Task Prepare(AccountLaunchContext account,CancellationToken token);
    Task Finish();
}

public interface IAccountSessionProvider
{
    bool LaunchAvailable { get; }
    string Notice { get; }
    Task<AccountLaunchOutcome> LaunchAsync(AccountLaunchContext account,LaunchTargetDto target,CancellationToken token);
}

public sealed class UnverifiedAccountSessionProvider : IAccountSessionProvider
{
    public bool LaunchAvailable => false;
    public string Notice => "Account sessions stay on this PC. Launching saved accounts is not implemented in this build. Verifying sign-in or checking a game target will not enable launching. Use Play on the Roblox website to start games.";
    public Task<AccountLaunchOutcome> LaunchAsync(AccountLaunchContext account,LaunchTargetDto target,CancellationToken token)
    { token.ThrowIfCancellationRequested();return Task.FromResult(new AccountLaunchOutcome("Unavailable","ACCOUNT_LAUNCH_UNVERIFIED")); }
}

public interface IAccountVault
{
    void Save(string accountId,ReadOnlySpan<byte> secret);
    byte[]? Read(string accountId);
    void Remove(string accountId);
}

/// <summary>Optional native session-secret storage; browser profiles retain their own cookies.</summary>
public sealed class WindowsAccountVault(string directory) : IAccountVault
{
    string FileFor(string id)
    {
        string path=Path.Combine(directory,AccountStore.ValidId(id)+".bin");AccountStorageSecurity.ValidatePath(path);return path;
    }
    public void Save(string accountId,ReadOnlySpan<byte> secret)
    {
        string path=FileFor(accountId);AccountStorageSecurity.EnsurePrivateDirectory(directory);
        byte[] clear=secret.ToArray();byte[] protectedBytes;
        try { protectedBytes=ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
        try { AccountStorageSecurity.ValidatePath(temporary);File.WriteAllBytes(temporary,protectedBytes);AccountStorageSecurity.EnsurePrivateFile(temporary);AccountStorageSecurity.ValidatePath(path);File.Move(temporary,path,true); }
        finally { AccountStorageSecurity.ValidatePath(temporary);if(File.Exists(temporary))File.Delete(temporary); }
    }
    public byte[]? Read(string accountId)
    { string path=FileFor(accountId);return File.Exists(path)?ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser):null; }
    public void Remove(string accountId) { string path=FileFor(accountId);if(File.Exists(path))File.Delete(path); }
}

internal sealed record SavedAccount(string Id,string Nickname,string RobloxUserId,string Username,string Group="",string Notes="",string Color="",bool Favorite=false,bool BypassOptimizations=false);
internal sealed record AccountFile(int Version,SavedAccount[] Accounts);

internal sealed class AccountStore(string directory)
{
    readonly string root=Path.GetFullPath(directory);
    string FilePath => Path.Combine(root,"accounts.json");
    internal static string ValidId(string id) => Regex.IsMatch(id??"",@"\A[a-f0-9]{32}\z",RegexOptions.CultureInvariant)?id!:throw new ArgumentException("Invalid account selection.");
    internal string Profile(string id)
    { string path=Path.Combine(root,"profiles",ValidId(id));AccountStorageSecurity.ValidatePath(path);return path; }
    internal async Task<FileStream> AcquireAsync(CancellationToken token)
    {
        AccountStorageSecurity.EnsurePrivateDirectory(root);
        string path=Path.Combine(root,"operation.lock");
        while(true)
        {
            token.ThrowIfCancellationRequested();AccountStorageSecurity.ValidatePath(path);
            try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException error) when((error.HResult&0xffff) is 32 or 33)
            { await Task.Delay(100,token); }
        }
    }
    internal SavedAccount[] Read()
    {
        AccountStorageSecurity.ValidatePath(FilePath);if(!File.Exists(FilePath))return [];
        if(new FileInfo(FilePath).Length>1024*1024)throw new InvalidDataException("Account metadata is unavailable.");
        var data=JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(FilePath));
        if(data is null || data.Version is not (1 or 2) || data.Accounts is null || data.Accounts.Length>100)throw new InvalidDataException("Account metadata is unavailable.");
        var ids=new HashSet<string>();var users=new HashSet<string>();
        foreach(var a in data.Accounts)
            if(a is null || !ids.Add(ValidId(a.Id)) || !users.Add(a.RobloxUserId) || !LaunchTargetResolver.IsPositiveId(a.RobloxUserId) || !ValidUsername(a.Username) || a.Nickname is null || a.Nickname.Length>60 || a.Nickname.Any(char.IsControl) || !ValidOrganization(a.Group,a.Notes,a.Color))throw new InvalidDataException("Account metadata is unavailable.");
        return data.Accounts;
    }
    internal static bool ValidOrganization(string? group,string? notes,string? color) =>
        group is not null && group.Length<=40 && !group.Any(char.IsControl) &&
        notes is not null && notes.Length<=1000 && !notes.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t')) &&
        color is not null && (color.Length==0 || Regex.IsMatch(color,@"\A#[0-9a-fA-F]{6}\z",RegexOptions.CultureInvariant));
    internal static bool ValidUsername(string? value) => value is not null && Regex.IsMatch(value,@"\A[A-Za-z0-9_]{3,20}\z",RegexOptions.CultureInvariant);
    internal void Write(SavedAccount[] accounts)
    {
        AccountStorageSecurity.EnsurePrivateDirectory(root);AccountStorageSecurity.ValidatePath(FilePath);string temporary=FilePath+".tmp-"+Guid.NewGuid().ToString("N");
        try { AccountStorageSecurity.ValidatePath(temporary);File.WriteAllText(temporary,JsonSerializer.Serialize(new AccountFile(2,accounts)));AccountStorageSecurity.EnsurePrivateFile(temporary);AccountStorageSecurity.ValidatePath(FilePath);
            // Keep the exact v1 original before the first v2 commit; readers never migrate.
            if(File.Exists(FilePath))
            {
                using var original=JsonDocument.Parse(File.ReadAllText(FilePath));
                if(original.RootElement.GetProperty("Version").GetInt32()==1)
                {
                    string backup=FilePath+".v1.bak";AccountStorageSecurity.ValidatePath(backup);
                    if(!File.Exists(backup)) { File.Copy(FilePath,backup);AccountStorageSecurity.EnsurePrivateFile(backup); }
                }
            }
            File.Move(temporary,FilePath,true); }
        finally { AccountStorageSecurity.ValidatePath(temporary);if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal void RemoveProfile(string id)
    {
        string path=Path.GetFullPath(Profile(id));string expectedRoot=Path.GetFullPath(Path.Combine(root,"profiles"))+Path.DirectorySeparatorChar;
        if(!path.StartsWith(expectedRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid profile location.");
        AccountStorageSecurity.ValidatePath(path);if(!Directory.Exists(path))return;
        ValidateTree(path);
        DeleteDirectory(path);
    }
    static void ValidateTree(string path)
    {
        AccountStorageSecurity.ValidatePath(path);
        foreach(var item in Directory.EnumerateFileSystemEntries(path))
        {
            AccountStorageSecurity.ValidatePath(item);
            if((File.GetAttributes(item)&FileAttributes.Directory)!=0)ValidateTree(item);
        }
    }
    static void DeleteDirectory(string path)
    {
        foreach(var item in Directory.EnumerateFileSystemEntries(path))
        {
            AccountStorageSecurity.ValidatePath(item);var attributes=File.GetAttributes(item);
            if((attributes&FileAttributes.Directory)!=0)DeleteDirectory(item);
            else File.Delete(item);
        }
        AccountStorageSecurity.ValidatePath(path);Directory.Delete(path);
    }
}

public static class LaunchTargetResolver
{
    internal static bool IsPositiveId(string? value) => value is not null && Regex.IsMatch(value,@"\A[1-9][0-9]{0,18}\z",RegexOptions.CultureInvariant) && long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out _);
    public static LaunchTargetDto Parse(string input,string? jobId=null)
    {
        string value=(input??"").Trim();
        if(value.Length>2048)throw new ArgumentException("Enter a Roblox game URL or positive Place ID.");
        string place=value;
        if(!IsPositiveId(place))
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Port!=443 || uri.UserInfo.Length!=0 || uri.Fragment.Length!=0 || uri.Host is not ("www.roblox.com" or "roblox.com") || value.Contains('\\'))
                throw new ArgumentException("Enter an HTTPS Roblox game URL or positive Place ID.");
            // v1 deliberately accepts game detail URLs only. Opaque share/private links
            // need separate server resolution and must never be blindly forwarded.
            var match=Regex.Match(uri.AbsolutePath,@"\A/games/([1-9][0-9]{0,18})(?:/[A-Za-z0-9_-]*)?/?\z",RegexOptions.CultureInvariant);
            if(!match.Success || !IsPositiveId(match.Groups[1].Value))throw new ArgumentException("Use the game's /games/PlaceId URL. Share and private server links are not supported yet.");
            if(uri.Query.Length>0)throw new ArgumentException("Remove URL query parameters. Enter an optional server Job ID separately; private/share links are not supported yet.");
            place=match.Groups[1].Value;
        }
        string? job=string.IsNullOrWhiteSpace(jobId)?null:jobId.Trim();
        if(job is not null && (!Guid.TryParseExact(job,"D",out var parsed)||parsed==Guid.Empty))throw new ArgumentException("A server Job ID must be a non-empty UUID. A Place ID is required as well.");
        return new(place,job?.ToLowerInvariant());
    }
}

public sealed class LaunchBatchCoordinator(IAccountSessionProvider provider)
{
    public Func<int,CancellationToken,Task<LaunchTargetDto>>? TargetForSlot {get;set;}
    readonly SemaphoreSlim gate=new(1,1);
    public async Task<LaunchBatchResultDto> RunAsync(AccountLaunchContext[] accounts,LaunchTargetDto target,string? presetName,Action<LaunchBatchProgressDto>? progress,CancellationToken token)
    {
        accounts=[..accounts]; // Freeze the caller's slot order before any asynchronous work.
        if(accounts.Length is <1 or >100 || accounts.Select(a=>a.Id).Distinct().Count()!=accounts.Length)throw new ArgumentException("Select distinct accounts to launch.");
        await gate.WaitAsync(token);
        try
        {
            string id=Guid.NewGuid().ToString("N");var results=new List<LaunchBatchItemDto>();var observing=new List<Task<LaunchBatchItemDto>>();bool blocked=false;
            for(int index=0;index<accounts.Length;index++)
            {
                var account=accounts[index];int slot=index+1;
                if(token.IsCancellationRequested)
                { results.Add(new(account.Id,slot,"Canceled","BATCH_CANCELED",null,null));continue; }
                if(blocked){results.Add(new(account.Id,slot,"Canceled","HELPER_READINESS_UNCONFIRMED",null,null));continue;}
                progress?.Invoke(new(id,account.Id,slot,"Launching",""));
                LaunchTargetDto slotTarget;
                try{slotTarget=TargetForSlot is null?target:await TargetForSlot(index,token);}
                catch(OperationCanceledException) when(token.IsCancellationRequested){results.Add(new(account.Id,slot,"Canceled","BATCH_CANCELED",null,null));continue;}
                if(provider is IPipelinedAccountSessionProvider pipeline)
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    var pending=pipeline.Start(account,slotTarget,(stage,ms)=>progress?.Invoke(new(id,account.Id,slot,stage,"",ms,slotTarget.JobId)),token);
                    Task prepare=index+1<accounts.Length?pipeline.Prepare(accounts[index+1],token):Task.CompletedTask;
                    async Task<LaunchBatchItemDto> Observe()
                    {
                        var value=await pending.Completion;
                        string status=value.Status=="Started"&&!value.Verified?"Unknown":value.Status;
                        progress?.Invoke(new(id,account.Id,slot,status=="Started"?"Joined":status=="Unknown"?"Unconfirmed":status,value.Code,timer.ElapsedMilliseconds,slotTarget.JobId));
                        return new(account.Id,slot,status,value.Code,value.Verified?value.Pid:null,value.Verified?value.Created:null,slotTarget.JobId,value.ObservedServer,timer.ElapsedMilliseconds);
                    }
                    observing.Add(Observe());
                    blocked=!await pending.Ready;
                    await prepare;
                    continue;
                }
                AccountLaunchOutcome outcome;
                try { outcome=await provider.LaunchAsync(account,slotTarget,token); }
                catch(OperationCanceledException) when(token.IsCancellationRequested) { outcome=new("Unknown","LAUNCH_OBSERVATION_CANCELED"); }
                catch { outcome=new("Failed","ACCOUNT_LAUNCH_FAILED"); }
                if(outcome.Status is not ("Started" or "Failed" or "NeedsSignIn" or "Unavailable" or "Unknown" or "Canceled") ||
                    outcome.Code is null || !Regex.IsMatch(outcome.Code,@"\A[A-Z][A-Z0-9_]{0,63}\z",RegexOptions.CultureInvariant))
                    outcome=new("Unknown","ACCOUNT_PROVIDER_RESULT_INVALID");
                // No automatic retry: an exception/cancellation might follow player start.
                // A provider cannot turn an uncorrelated process into layout permission.
                if(outcome.Status=="Started"&&!outcome.Verified)outcome=new("Unknown","ACCOUNT_PROCESS_UNCONFIRMED");
                results.Add(new(account.Id,slot,outcome.Status,outcome.Code,outcome.Verified?outcome.Pid:null,outcome.Verified?outcome.Created:null));
                progress?.Invoke(new(id,account.Id,slot,outcome.Status,outcome.Code));
            }
            results.AddRange(await Task.WhenAll(observing));results=results.OrderBy(x=>x.Slot).ToList();
            bool complete=!token.IsCancellationRequested && results.All(r=>r.Status=="Started") && results.Select(r=>(r.Pid,r.Created)).Distinct().Count()==results.Count;
            return new(id,token.IsCancellationRequested?"Canceled":complete?"Completed":"Partial",complete,presetName,results.ToArray());
        }
        finally { if(provider is IPipelinedAccountSessionProvider pipeline)await pipeline.Finish();gate.Release(); }
    }
}

public sealed class AccountManager
{
    public Func<int,CancellationToken,Task<LaunchTargetDto>>? TargetForSlot {set=>batches.TargetForSlot=value;}
    readonly AccountStore store;
    readonly IAccountVault vault;
    readonly IAccountSessionProvider provider;
    readonly LaunchBatchCoordinator batches;
    readonly SemaphoreSlim gate=new(1,1);
    public AccountManager(string storageRoot,IAccountSessionProvider? provider=null,IAccountVault? vault=null)
    {
        string root=Path.Combine(storageRoot,"accounts");store=new(root);this.vault=vault??new WindowsAccountVault(Path.Combine(root,"vault"));
        this.provider=provider??new UnverifiedAccountSessionProvider();batches=new(this.provider);
    }
    public AccountsStateDto State()
    {
        try { return new(true,provider.LaunchAvailable,provider.Notice,store.Read().Select((a,index)=>new AccountDto(a.Id,a.Nickname,a.Username,"Saved locally; identity verified when saved",a.Group,a.Notes,a.Color,a.Favorite,index,a.RobloxUserId,BypassOptimizations:a.BypassOptimizations)).ToArray()); }
        catch { return new(false,false,"Local account metadata could not be read. It has been preserved; account changes are unavailable.",[]); }
    }
    public LaunchTargetDto ValidateTarget(string input,string? jobId=null)=>LaunchTargetResolver.Parse(input,jobId);
    public async Task<AccountsStateDto> SetOptimizationBypassAsync(string id,bool bypass,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);var saved=store.Read();AccountStore.ValidId(id);
            if(!saved.Any(a=>a.Id==id))throw new ArgumentException("The account selection expired.");
            token.ThrowIfCancellationRequested();store.Write(saved.Select(a=>a.Id==id?a with {BypassOptimizations=bypass}:a).ToArray());return State();
        }
        finally {gate.Release();}
    }
    public async Task<AccountsStateDto> RenameAsync(string id,string nickname,CancellationToken token)
    {
        nickname=(nickname??"").Trim();if(nickname.Length is <1 or >60 || nickname.Any(char.IsControl))throw new ArgumentException("Use a nickname of 1 to 60 characters.");
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);
            var saved=store.Read();AccountStore.ValidId(id);if(!saved.Any(a=>a.Id==id))throw new ArgumentException("The account selection expired.");
            token.ThrowIfCancellationRequested();store.Write(saved.Select(a=>a.Id==id?a with { Nickname=nickname }:a).ToArray());return State();
        }
        finally { gate.Release(); }
    }
    public async Task<AccountsStateDto> OrganizeAsync(string id,string group,string notes,string color,bool favorite,CancellationToken token)
    {
        group=(group??"").Trim();notes=notes??"";color=color??"";
        if(!AccountStore.ValidOrganization(group,notes,color))throw new ArgumentException("Use a group up to 40 characters, notes up to 1000 characters, and an optional #RRGGBB color.");
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);var saved=store.Read();AccountStore.ValidId(id);
            if(!saved.Any(a=>a.Id==id))throw new ArgumentException("The account selection expired.");
            token.ThrowIfCancellationRequested();store.Write(saved.Select(a=>a.Id==id?a with { Group=group,Notes=notes,Color=color.ToUpperInvariant(),Favorite=favorite }:a).ToArray());return State();
        }
        finally { gate.Release(); }
    }
    public async Task<AccountsStateDto> OrderAsync(string[] ids,CancellationToken token)
    {
        if(ids is null || ids.Length>100 || ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Choose each saved account exactly once.");
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);var saved=store.Read();
            if(ids.Length!=saved.Length || ids.Any(id=>!saved.Any(a=>a.Id==id)))throw new ArgumentException("The saved account list changed. Refresh before ordering accounts.");
            token.ThrowIfCancellationRequested();store.Write(ids.Select(id=>saved.Single(a=>a.Id==id)).ToArray());return State();
        }
        finally { gate.Release(); }
    }
    public async Task<AccountsStateDto> ConnectAsync(string? accountId,string nickname,Func<AccountSignInRequest,CancellationToken,Task<AccountIdentity?>> signIn,CancellationToken token)
    {
        nickname=(nickname??"").Trim();if(nickname.Length>60 || nickname.Any(char.IsControl))throw new ArgumentException("Use a nickname of up to 60 characters.");
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);
            var saved=store.Read();var existing=accountId is null?null:saved.SingleOrDefault(a=>a.Id==AccountStore.ValidId(accountId))??throw new ArgumentException("The account selection expired.");
            if(existing is null&&saved.Length>=100)throw new ArgumentException("Remove an account before adding another.");
            string id=existing?.Id??Guid.NewGuid().ToString("N");
            bool committed=false;
            try
            {
                var identity=await signIn(new(id,store.Profile(id),existing?.RobloxUserId,saved.Where(a=>a.Id!=id).Select(a=>a.RobloxUserId).ToArray()),token);
                if(identity is null)return State();
                token.ThrowIfCancellationRequested();
                if(!LaunchTargetResolver.IsPositiveId(identity.RobloxUserId)||!AccountStore.ValidUsername(identity.Username))throw new InvalidOperationException("Roblox identity could not be verified.");
                if(existing is not null&&existing.RobloxUserId!=identity.RobloxUserId)throw new InvalidOperationException("A different Roblox account signed in. Remove this local account and add the new account separately.");
                if(saved.Any(a=>a.Id!=id&&a.RobloxUserId==identity.RobloxUserId))throw new InvalidOperationException("This Roblox account is already saved locally.");
                var item=existing is null?new SavedAccount(id,nickname.Length==0?identity.Username:nickname,identity.RobloxUserId,identity.Username):existing with { Nickname=nickname.Length==0?existing.Nickname:nickname,Username=identity.Username };
                store.Write(existing is null?[..saved,item]:saved.Select(a=>a.Id==id?item:a).ToArray());committed=true;return State();
            }
            finally
            {
                if(existing is null&&!committed)
                {
                    try { store.RemoveProfile(id); }
                    catch { throw new InvalidOperationException("The unsaved sign-in profile could not be removed. Close the app and clear the unsaved local profile before adding another account."); }
                }
            }
        }
        finally { gate.Release(); }
    }
    public async Task<AccountsStateDto> RemoveAsync(string id,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);
            var accounts=store.Read();AccountStore.ValidId(id);
            if(!accounts.Any(a=>a.Id==id))throw new ArgumentException("The account selection expired.");
            token.ThrowIfCancellationRequested();
            // Delete secret material first. Locked browser data leaves the account visible
            // and can be retried after its sign-in window/browser processes close.
            store.RemoveProfile(id);vault.Remove(id);store.Write(accounts.Where(a=>a.Id!=id).ToArray());return State();
        }
        finally { gate.Release(); }
    }
    public async Task<LaunchBatchResultDto> RunBatchAsync(string[] ids,string target,string? jobId,string? presetName,Action<LaunchBatchProgressDto>? progress,CancellationToken token)
    {
        var parsed=ValidateTarget(target,jobId);
        await gate.WaitAsync(token);
        try
        {
            using var lease=await store.AcquireAsync(token);
            var saved=store.Read();var selected=ids.Select(id=>saved.SingleOrDefault(a=>a.Id==id)??throw new ArgumentException("The account selection expired.")).Select(a=>new AccountLaunchContext(a.Id,store.Profile(a.Id),a.RobloxUserId)).ToArray();
            return await batches.RunAsync(selected,parsed,presetName,progress,token);
        }
        finally { gate.Release(); }
    }
}
