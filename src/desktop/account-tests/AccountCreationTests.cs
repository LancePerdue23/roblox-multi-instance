using System.Text;
using System.Text.Json;
using RobloxLaunchHelper;
using RobloxMultiInstance;

internal static class AccountCreationTests
{
    internal static async Task<int> RunAsync(string root)
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
        void Reject(Action action,string label){try{action();throw new Exception(label);}catch(ArgumentException){Check(true,label);}}
        var options=new AccountCreationOptions("Fixture",8,3,3,new DateOnly(1990,2,3));
        Check(options.Preview(3).SequenceEqual(new[]{"Fixture008","Fixture009","Fixture010"}),"Creation preview preserves numbering and padding");
        Reject(()=>options.Preview(2),"Creation respects remaining account capacity");
        Reject(()=>(options with {Start=long.MaxValue,Count=2,Prefix=""}).Preview(100),"Creation rejects numeric overflow");
        Reject(()=>(options with {Prefix="TooLongForThisUsername"}).Preview(100),"Creation rejects overlong usernames");
        Reject(()=>(options with {Prefix="_Invalid"}).Preview(100),"Creation rejects invalid underscore placement");
        Reject(()=>(options with {Birthdate=DateOnly.FromDateTime(DateTime.Today).AddDays(1)}).Preview(100),"Creation rejects future birthdate");
        Reject(()=>(options with {SharedPassword="short"}).Preview(100),"Creation validates shared passwords");
        var generated=Enumerable.Range(0,100).Select(_=>AccountCreationOptions.GeneratePassword()).ToArray();
        Check(generated.Distinct().Count()==100&&generated.All(p=>p.Length==24&&p.Any(char.IsUpper)&&p.Any(char.IsLower)&&p.Any(char.IsDigit)),"Generated passwords have independent entropy and required character classes");
        var manager=new AccountManager(root);var progress=new List<AccountCreationProgressDto>();int calls=0;
        var coordinator=new AccountCreationCoordinator(manager);
        var state=await coordinator.RunAsync(options,async(request,credential,date,token)=>
        {
            Check(manager.Credentials.GetPending(request.Id)==credential,"Pending credentials are durable before signup opens");
            if(++calls==2)return null;
            manager.Credentials.SavePending(request.Id,credential with {Confirmed=true});
            if(calls==1)coordinator.LastNumber=15;
            await Task.Yield();return new AccountIdentity((100+calls).ToString(),credential.Username);
        },progress.Add,default);
        Check(state.Accounts.Select(a=>a.Username).SequenceEqual(new[]{"Fixture008","Fixture017"}),"Queue saves successes independently and consumes conflict numbers monotonically");
        Check(progress.Last() is {Completed:2,Skipped:1,Remaining:0,Status:"Finished"},"Queue reports completed and skipped items");
        Check(state.Accounts.All(a=>a.HasStoredCredentials),"Saved account DTO exposes credential availability only");
        var first=state.Accounts[0];var stored=manager.Credentials.Get(first.Id)!;
        Check(!stored.ToString().Contains(stored.Password)&&!options.ToString().Contains("1990"),"Native credential diagnostics redact passwords and birthdates");
        string bridge=JsonSerializer.Serialize(state);
        Check(!bridge.Contains(stored.Password)&&!bridge.Contains("Birthdate")&&!bridge.Contains("ProfileDirectory"),"Account DTO excludes credentials, birthdate and profile paths");
        var protectedFile=Path.Combine(root,"accounts","credentials","linked",first.Id+".bin");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(protectedFile)).Contains(stored.Password),"Credential files contain Windows-encrypted data");
        Check(new AccountManager(root).Credentials.Get(first.Id)==stored,"Stored credentials round trip after reopening");
        var pending=manager.Credentials.Pending().Single();Check(!pending.Credential.Confirmed,"Skipped signups retain unresolved supplied credentials");
        // Simulate a crash after metadata commit and before pending credential promotion.
        manager.Credentials.SavePending(first.Id,stored);manager.Credentials.Reconcile(state.Accounts);
        Check(manager.Credentials.GetPending(first.Id) is null&&manager.Credentials.Get(first.Id)==stored,"Startup reconciliation completes interrupted credential promotion");
        await manager.RemoveAsync(first.Id,default);
        Check(manager.Credentials.Get(first.Id) is null&&!manager.State().Accounts.Any(a=>a.Id==first.Id),"Account removal also removes stored credentials");
        using var canceled=new CancellationTokenSource();calls=0;
        try
        {
            await new AccountCreationCoordinator(manager).RunAsync(options with {Start=30},(request,credential,date,token)=>
            {
                calls++;manager.Credentials.SavePending(request.Id,credential with {Confirmed=true});canceled.Cancel();return Task.FromResult<AccountIdentity?>(new("999",credential.Username));
            },progress.Add,canceled.Token);
        }
        catch(OperationCanceledException){}
        Check(calls==1&&manager.State().Accounts.Any(a=>a.RobloxUserId=="999"),"Cancellation saves an already verified account before stopping subsequent items");
        Check(progress.Last().Status=="Canceled","Canceled queue reports cancellation rather than completion");
        int before=manager.State().Accounts.Length;
        try
        {
            await new AccountCreationCoordinator(manager).RunAsync(options with {Count=1},(request,credential,date,token)=>
            {
                manager.Credentials.SavePending(request.Id,credential with {Confirmed=true});return Task.FromResult<AccountIdentity?>(new("999",credential.Username));
            },progress.Add,default);
            throw new Exception("Duplicate accepted");
        }
        catch(InvalidOperationException){}
        Check(manager.State().Accounts.Length==before,"Duplicate identities do not discard previous successes or add another account");
        var operations=new OperationCoordinator();
        using(operations.Enter("accounts.create"))
        {
            using var read=operations.Enter("accounts.state");
            foreach(var command in new[]{"accounts.launchBatch","launch","accounts.remove","accounts.create","accounts.credentials.open"})
            {
                bool rejected=false;try{using var conflict=operations.Enter(command);}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"Creation blocks conflicting operation: "+command);
            }
        }
        using(operations.Enter("accounts.launchBatch"))
        {
            bool rejected=false;try{using var conflict=operations.Enter("accounts.create");}catch(InvalidOperationException){rejected=true;}Check(rejected,"Active launch blocks creation in reverse order");
        }
        return count;
    }
}
