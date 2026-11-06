using System.Text.Json;
using RobloxLaunchHelper;
using RobloxMultiInstance;

internal static class AutomaticPerformanceTests
{
    internal static async Task<int> Run(string root)
    {
        int passed=0;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        string accountId=new('a',32);
        var account=new AccountDto(accountId,"Alt","FixtureAlt","Saved",RobloxUserId:"123");
        var accountState=new AccountsStateDto(true,true,"",[account]);
        var context=new AccountLaunchContext(accountId,"unused","123");
        var platform=new Platform();var scheduling=new Scheduling();double now=10;
        var balanced=PerformanceProfileStore.Defaults with {SelectedProfileId="balanced",Enabled=true};
        var key=new ProcessKey(42,platform.Created);
        string path=Path.Combine(root,"automatic-policy","sessions.json");
        using(var service=new AutomaticPerformancePolicy(path,platform,scheduling,()=>now))
        {
            service.Reconcile(balanced,accountState,default);
            Check(!File.Exists(path)&&scheduling.Applies==0,"Policy ignores unknown and externally launched clients");
            service.Register(context,new("Unknown","ambiguous",42,platform.Created));
            Check(!File.Exists(path),"Ambiguous launch outcomes never become managed accounts");
            service.Register(context,new("Started","verified",42,platform.Created));
            service.Reconcile(balanced,accountState,default);
            Check(scheduling.Applies==0,"Background scheduling waits for the focus debounce");
            now+=1.1;service.Reconcile(balanced,accountState,default);
            Check(scheduling.Applies==1&&scheduling.Priority=="belowNormal"&&scheduling.Eco=="off"&&service.Snapshot(true).Applied==1,"Balanced automatically applies and verifies real scheduling controls");
            service.Reconcile(balanced,accountState,default);
            Check(scheduling.Applies==1,"Unchanged policy does not repeatedly mutate a process");
            platform.Focused=true;service.Reconcile(balanced,accountState,default);
            Check(!scheduling.Owns&&service.Result(key)?.State=="Focused boost","Focused account restores owned scheduling");
            platform.Focused=false;service.Reconcile(balanced,accountState,default);now+=0.9;service.Reconcile(balanced,accountState,default);
            Check(!scheduling.Owns,"Focus transitions honor one second continuously in the background");
            now+=0.2;service.Reconcile(balanced,accountState,default);
            Check(scheduling.Owns,"Background scheduling resumes after debounce");
            service.Reconcile(balanced,accountState with {Accounts=[account with {BypassOptimizations=true}]},default);
            Check(!scheduling.Owns&&service.Snapshot(true).Bypassed==1,"Account bypass takes precedence over the global preset");
            service.Reconcile(balanced,accountState,default);
            Check(scheduling.Owns,"Removing bypass reapplies inherited policy");
            var maximum=balanced with {SelectedProfileId="many-accounts"};service.Retry();service.Reconcile(maximum,accountState,default);
            Check(platform.Window.Show==2&&platform.Window.Right-platform.Window.Left==800&&scheduling.Eco=="on","Maximum instances minimizes an owned small window and enables efficiency mode");
            service.Retry();service.Reconcile(maximum,accountState with {Accounts=[account with {BypassOptimizations=true}]},default);
            Check(platform.Window==platform.Original&&!scheduling.Owns,"Bypass restores original window placement and scheduling immediately");
            service.Retry();service.Reconcile(maximum,accountState,default);
            platform.Window=platform.Window with {Left=99};
            service.Reconcile(maximum with {Enabled=false},accountState,default);
            Check(platform.Window.Left==99,"Window reset preserves user changes made after optimization");
            service.Retry();service.Reconcile(balanced,accountState,default);scheduling.External=true;
            service.Reconcile(balanced,accountState,default);int writes=scheduling.Applies;
            service.Reconcile(balanced,accountState,default);
            Check(service.Snapshot(true).Failed==1&&scheduling.Applies==writes,"External scheduling changes fail visibly without repeated takeover");
            scheduling.External=false;scheduling.FailEco=true;service.Retry();service.Reconcile(balanced,accountState,default);
            Check(service.Snapshot(true).Failed==1,"Partial scheduling failure never reports successful application");
            scheduling.FailEco=false;scheduling.UnsupportedEco=true;service.Retry();service.Reconcile(balanced,accountState,default);
            Check(service.Result(key)?.State=="Partial"&&scheduling.Priority=="belowNormal"&&scheduling.Eco=="unchanged","Unsupported efficiency mode does not prevent supported CPU priority controls");
            scheduling.UnsupportedEco=false;service.Retry();service.Reconcile(balanced,accountState,default);
            platform.State="Unavailable";service.Reconcile(balanced,accountState,default);
            Check(!scheduling.Owns&&service.Snapshot(true).Unavailable==1,"Identity unavailability releases app-owned scheduling");
            platform.State="Verified";service.Retry();service.Reconcile(balanced,accountState,default);
        }
        Check(!scheduling.Owns,"Full coordinator shutdown restores owned scheduling");
        platform.VerifyIdentity=false;scheduling.Applies=0;
        using(var restarted=new AutomaticPerformancePolicy(path,platform,scheduling,()=>now))
        {
            restarted.Reconcile(balanced with {FocusBoost=false},accountState,default);
            Check(platform.IdentityChecks>0&&scheduling.Applies==0,"Restart requires account identity verification before reattaching managed sessions");
            platform.VerifyIdentity=true;restarted.Reconcile(balanced with {FocusBoost=false},accountState,default);
            Check(scheduling.Owns,"Verified managed session reattaches after restart");
            using var competing=new AutomaticPerformancePolicy(path,platform,scheduling,()=>now);
            try {competing.Reconcile(balanced,accountState,default);throw new Exception("Competing policy accepted");}catch(IOException){Check(true,"Automatic session ownership is exclusive across managers");}
            scheduling.FailEco=true;restarted.Retry();restarted.Reconcile(balanced with {FocusBoost=false},accountState,default);
            Check(restarted.Snapshot(true).Failed==1,"A live native failure remains visible until policy changes or explicit retry");
            platform.State="Exited";restarted.Reconcile(balanced with {FocusBoost=false},accountState,default);
            Check(restarted.Snapshot(true).Instances.Length==0&&!scheduling.Owns,"Exited or reused lifetimes are pruned without adopting another process");
        }
        string accountRoot=Path.Combine(root,"optimization-account-fixture");
        var store=new AccountStore(Path.Combine(accountRoot,"accounts"));
        store.Write([new(accountId,"Alt","123","FixtureAlt")]);
        var manager=new AccountManager(accountRoot);
        Check(!manager.State().Accounts.Single().BypassOptimizations,"Existing accounts inherit global optimizations by default");
        await manager.SetOptimizationBypassAsync(accountId,true,default);
        await manager.OrganizeAsync(accountId,"Group","Notes","",true,default);
        await manager.RenameAsync(accountId,"Main",default);
        Check(new AccountManager(accountRoot).State().Accounts.Single().BypassOptimizations,"Account bypass persists across restart, organization, and rename");
        using(var accountBackend=new DesktopBackend(accountRoot))
        {
            var toggled=(AccountsStateDto)(await accountBackend.ExecuteAsync("accounts.optimizationBypass",JsonSerializer.SerializeToElement(new {id=accountId,bypass=false}),default))!;
            Check(!toggled.Accounts.Single().BypassOptimizations,"Native bypass command persists an explicit Boolean and returns updated account state");
            try {await accountBackend.ExecuteAsync("accounts.optimizationBypass",JsonSerializer.SerializeToElement(new {id=accountId,bypass="false"}),default);throw new Exception("Invalid bypass accepted");}
            catch(ArgumentException){Check(true,"Native bypass command rejects ambiguous non-Boolean requests");}
        }
        using(var backend=new DesktopBackend(Path.Combine(root,"activation-fixture")))
        {
            var empty=JsonSerializer.SerializeToElement(new{});
            var state=(PerformanceProfilesDto)(await backend.ExecuteAsync("performance.profiles",empty,default))!;
            var payload=JsonSerializer.SerializeToElement(new {selectedProfileId="balanced",profiles=state.Profiles,expectedRevision=state.Revision,focusBoost=true});
            var activated=(PerformanceProfilesDto)(await backend.ExecuteAsync("performance.activate",payload,default))!;
            Check(activated.Enabled&&activated.Version==2,"Apply explicitly activates a versioned global policy");
            var exported=(PerformanceExportDto)(await backend.ExecuteAsync("performance.export",empty,default))!;
            var imported=(PerformanceProfilesDto)(await backend.ExecuteAsync("performance.import",JsonSerializer.SerializeToElement(new {json=exported.Json,confirmed=true,expectedRevision=activated.Revision}),default))!;
            Check(!imported.Enabled,"Importing even an active profile never implicitly activates automatic tuning");
            var result=(OptimizationStateDto)(await backend.ExecuteAsync("performance.optimizationState",empty,default))!;
            Check(result.LaunchCompatibility.Contains("next saved-account launch")&&PerformanceLaunchSettings.ClientIntegrationVerified,"Launch settings distinguish next-launch requests from live scheduling controls");
        }
        return passed;
    }
    sealed class Platform : IOptimizationPlatform
    {
        internal long Created=DateTime.UtcNow.AddMinutes(-1).ToFileTimeUtc();
        internal bool Focused,VerifyIdentity=true;internal int IdentityChecks;
        internal string State="Verified";
        internal readonly OptimizedWindow Original=new(7,0,1,-1,-1,-1,-1,20,20,1300,900);
        internal OptimizedWindow Window=new(7,0,1,-1,-1,-1,-1,20,20,1300,900);
        public ManagedProbe Probe(ManagedPerformanceSession session,bool verifyAccount,CancellationToken token)
        {if(verifyAccount)IdentityChecks++;return new(verifyAccount&&!VerifyIdentity?"Unavailable":State,Focused,7);}
        public OptimizedWindow ReadWindow(ManagedPerformanceSession session,long window)=>Window;
        public void WriteWindow(ManagedPerformanceSession session,OptimizedWindow value)=>Window=value;
    }
    sealed class Scheduling : IOptimizationScheduling
    {
        internal int Applies;internal bool Owns,External,FailEco,UnsupportedEco;internal string Priority="unchanged",Eco="unchanged";
        public ProcessPolicyStatus Status(ProcessKey key)=>new(true,Owns,false,new(0x20,UnsupportedEco?null:0u,UnsupportedEco?null:0u),Owns&&!External,Owns&&!External&&!UnsupportedEco,"CURRENT","");
        public ProcessPolicyStatus Apply(ProcessKey key,string priority,string eco,CancellationToken token)
        {Applies++;Owns=true;Priority=priority;Eco=eco;return Status(key) with {Code=FailEco?"APPLY_FAILED":"APPLIED",Message="Fixture scheduling failure"};}
        public ProcessPolicyRecovery Reset(ProcessKey key){Owns=false;return new(true,0,"Restored");}
    }
}
